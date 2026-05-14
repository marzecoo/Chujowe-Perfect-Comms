using Concentus;
using Hazel;
using HarmonyLib;
using VoiceChatPlugin.Audio;
using VoiceChatPlugin.VoiceChat;
#if WINDOWS
using NAudio.Wave;
using NAudio.CoreAudioApi;
#endif
using MiraAPI.LocalSettings;
using UnityEngine;
using System.Collections.Generic;
using System;
using System.Linq;

namespace VoiceChatPlugin.VoiceChat;

/// <summary>
/// Manages the in-game voice chat session.
///
/// Audio transport runs entirely over the existing Among Us / Impostor game
/// server via the protocol-defined audio RPC.  No separate voice server is needed.
///
/// ── Bug fix: all clients were silent except the first ─────────────────────
///
/// Root cause: the original code declared _imager, _normalVolume, _ghostVolume,
/// _radioVolume, _clientVolume, _levelMeter and the per-client filter routers as
/// single instances shared across all VCPlayers.  AudioManager.AssignIds() visits
/// each router object exactly once (it is in one path), leaving HasMultipleInput=false
/// and _mixer=null on every node.
///
/// When AudioManager.Generate(clientB) ran, Build() called nodes[r.Id].AddInput(...)
/// on these already-built nodes, but AddInput delegates to _mixer?.AddInput(...)
/// which is a no-op when _mixer is null.  Client B's decoded audio was silently
/// dropped at the first shared router it hit — only the very first client made sound.
///
/// Fix: every router that lives in the per-client signal chain is now a "blueprint"
/// object.  All blueprints are connected in the graph once so AssignIds can assign
/// them stable IDs.  AudioManager.Generate() then creates fresh per-client
/// AudioRoutingInstanceNode copies for each non-global blueprint.  The shared
/// global chain (ghost reverbs, radio distortion, master volume, endpoint) is
/// unchanged and built only once.
///
/// The merge point _clientMixEntry is IsGlobalRouter=true so its HasMultipleInput
/// is forced true, guaranteeing a _mixer is always allocated to accumulate audio
/// from every client.
/// </summary>
public class VoiceChatRoom
{
    internal const byte AudioRpcId = VoiceProtocol.AudioRpcId;
    private const SendOption DefaultAudioSendOption = SendOption.None;
    private const int BroadcastTargetClientId = -1;
    private const float StateRefreshInterval = 0.05f;
    private const float CommsSabotageRefreshInterval = 0.10f;
    private const float BootstrapWindowSeconds = 6f;
    private const float BootstrapRefreshInterval = 0.50f;
    private const float TransitionTraceSeconds = 45f;
    private const float TransitionTraceStateInterval = 0.25f;
    private const int TransitionTraceAudioFrames = 64;
    private const int TransitionTracePerfEvents = 48;
    private const double SlowUpdateLogThresholdMs = 20.0;
    private const double SlowOperationLogThresholdMs = 2.0;

    // ── Singleton ─────────────────────────────────────────────────────────────
    public static VoiceChatRoom? Current { get; private set; }

    // ── Audio routing graph ────────────────────────────────────────────────────

    // Global merge point.  IsGlobalRouter=true forces HasMultipleInput=true,
    // so the internal _mixer is always constructed and can accumulate N clients.
    private readonly SimpleRouter _clientMixEntry;

    private readonly AudioManager          _audioManager;
    private readonly VolumeRouter.Property _masterVolumeProperty;

    // ── Per-client router blueprints ──────────────────────────────────────────
    // Wired into the graph once for ID assignment.
    // AudioManager.Generate() produces fresh per-client node instances keyed by
    // blueprint Id, so every client owns independent buffers and state.
    private readonly VolumeRouter     _clientVolumeBlueprint;
    private readonly StereoRouter     _imagerBlueprint;
    private readonly VolumeRouter     _normalVolumeBlueprint;
    private readonly LevelMeterRouter _levelMeterBlueprint;
    private readonly FilterRouter     _ghostLowpassBlueprint;
    private readonly VolumeRouter     _ghostVolumeBlueprint;
    private readonly FilterRouter     _radioHighpassBlueprint;
    private readonly FilterRouter     _radioLowpassBlueprint;
    private readonly VolumeRouter     _radioVolumeBlueprint;

    // ── Remote clients ─────────────────────────────────────────────────────────
    private readonly Dictionary<int, VCPlayer>     _clients    = new();
    private readonly Dictionary<int, IOpusDecoder> _decoders   = new();
    private readonly Dictionary<int, float[]>      _decodeBufs = new();
    private readonly Dictionary<int, int>          _lastSeq    = new();
    private readonly Dictionary<int, SenderJitterBuffer> _jitterBuffers = new();

    public IEnumerable<VCPlayer> AllClients => _clients.Values;
    internal IEnumerable<KeyValuePair<int, VCPlayer>> AllClientEntries => _clients;

    // ── Virtual components ─────────────────────────────────────────────────────
    private readonly List<IVoiceComponent> _virtualMics     = new();
    private readonly List<IVoiceComponent> _virtualSpeakers = new();
    public void AddVirtualMicrophone(IVoiceComponent c)    => _virtualMics.Add(c);
    public void AddVirtualSpeaker(IVoiceComponent c)       => _virtualSpeakers.Add(c);
    public void RemoveVirtualMicrophone(IVoiceComponent c) => _virtualMics.Remove(c);
    public void RemoveVirtualSpeaker(IVoiceComponent c)    => _virtualSpeakers.Remove(c);

    // ── Microphone ─────────────────────────────────────────────────────────────
    private IOpusEncoder? _encoder;
    private readonly object _micStateLock = new();
    private float[] _micAccumulator = new float[AudioHelpers.FrameSize * 8];
    private int     _micAccumCount  = 0;
    private int     _speechSendHangoverFrames;
    private readonly byte[] _encodeBuffer = new byte[VoiceProtocol.MaxEncodedAudioBytes];
    private float _micVolume = 1f;
    private float _noiseGateThreshold;
    private float _vadThreshold = 0.012f;
    private int _sendSeq = 0;
    private readonly VoiceTransport _transport = new();
    private readonly VoiceOutgoingFrame[] _sendBundleFrames = new VoiceOutgoingFrame[VoiceProtocol.MaxBundledAudioFrames];
    private readonly List<int> _jitterFlushIds = new(16);

#if WINDOWS
    private WaveInEvent? _waveIn;
    public bool UsingMicrophone => _waveIn != null;
#elif ANDROID
    private AndroidMicrophone? _androidMic;
    public bool UsingMicrophone => _androidMic != null;
#else
    public bool UsingMicrophone => false;
#endif

    private readonly VoiceActivityTracker _localMicActivity = new();
    public float LocalMicLevel => _localMicActivity.Level;
    public bool LocalMicSpeaking => _localMicActivity.IsSpeaking;
    public bool Mute  { get; private set; }
    public int  SampleRate => AudioHelpers.ClockRate;
    internal VoiceGameStateSnapshot? CurrentSnapshot { get; private set; }

    // ── Speaker ────────────────────────────────────────────────────────────────
#if WINDOWS
    private WasapiOut? _waveOut;
    private string _currentSpeakerDevice = "";
    private float _lastSpeakerCheckTime = 0f;
    public bool UsingSpeaker => _waveOut != null && _waveOut.PlaybackState == PlaybackState.Playing;
#elif ANDROID
    private AndroidSpeaker? _androidSpeaker;
    public bool UsingSpeaker => _androidSpeaker != null;
#else
    public bool UsingSpeaker => false;
#endif

    // ── Misc ───────────────────────────────────────────────────────────────────
    private bool  _commsSabActive;
    private float _commsSabCheckTimer;
    private byte   _lastId   = byte.MaxValue;
    private string _lastName = null!;
    private float  _handshakeTimer;
    private float  _lastCompatibilityRefreshTime = -999f;
    private float  _snapshotRefreshTimer;
    private float  _routeRefreshTimer;
    private int    _lastRouteClientCount = -1;
    private float  _bootstrapUntilTime = -999f;
    private float  _bootstrapRefreshTimer;
    private bool _haveTracePhase;
    private VoiceGamePhase _lastTracePhase = VoiceGamePhase.Unknown;
    private DateTime _transitionTraceUntilUtc = DateTime.MinValue;
    private float _transitionTraceStateTimer;
    private int _traceLocalEncodeFramesRemaining;
    private int _traceLocalSkipFramesRemaining;
    private int _traceSendFramesRemaining;
    private int _tracePerfEventsRemaining;
    private readonly Dictionary<int, int> _traceReceiveFramesRemaining = new();
    private readonly Dictionary<int, int> _traceDecodeFramesRemaining = new();
    private readonly Dictionary<int, int> _traceJitterFramesRemaining = new();
    private readonly Dictionary<int, string> _lastRouteTraceKey = new();
    private DateTime _lastTxFrameUtc = DateTime.MinValue;
    private readonly Dictionary<int, DateTime> _lastRxPacketUtc = new();
    private readonly Dictionary<int, DateTime> _lastDecodeUtc = new();
    private readonly Dictionary<int, float> _lastDecodeTailSample = new();
    private DateTime _lastDebugStateLogUtc = DateTime.MinValue;
    private DateTime _lastDiagnosticSummaryUtc = DateTime.MinValue;
    private long _diagMicCallbacks;
    private long _diagMicSamples;
    private long _diagMicMutedCallbacks;
    private long _diagMicDroppedSamples;
    private long _diagEncodedFrames;
    private long _diagEncodedBytes;
    private long _diagSendRpcs;
    private long _diagSendFrames;
    private long _diagSendBytes;
    private long _diagBundledRpcs;
    private long _diagBundledFrames;
    private long _diagNoTargets;
    private long _diagStaleDrops;
    private long _diagBadPackets;
    private long _diagReceivePackets;
    private long _diagReceiveBytes;
    private long _diagReceiveBundles;
    private long _diagReceiveBundleFrames;
    private long _diagJitterDrops;
    private long _diagSequenceGaps;
    private long _diagPlcFrames;
    private long _diagDecodeErrors;
    private long _diagDecodedFrames;
    private long _diagDecodedSamples;

    // ======================================================================
    // Factory
    // ======================================================================

    public static VoiceChatRoom Start()
    {
        Current?.Close();
        Current = new VoiceChatRoom();
        return Current;
    }

    public static void CloseCurrentRoom()
    {
        Current?.Close();
        Current = null;
    }

    // ======================================================================
    // Constructor
    // ======================================================================

    private VoiceChatRoom()
    {
        // ── Per-client blueprints ─────────────────────────────────────────────
        _clientVolumeBlueprint  = new VolumeRouter();
        _imagerBlueprint        = new StereoRouter();
        _normalVolumeBlueprint  = new VolumeRouter();
        _levelMeterBlueprint    = new LevelMeterRouter();
        _ghostLowpassBlueprint  = FilterRouter.CreateLowPassFilter(1900f, 2f);
        _ghostVolumeBlueprint   = new VolumeRouter();
        _radioHighpassBlueprint = FilterRouter.CreateHighPassFilter(650f, 3.2f);
        _radioLowpassBlueprint  = FilterRouter.CreateLowPassFilter(800f, 2.1f);
        _radioVolumeBlueprint   = new VolumeRouter();

        // ── Global merge point ────────────────────────────────────────────────
        _clientMixEntry = new SimpleRouter(isGlobal: true);

        // ── Shared global output ──────────────────────────────────────────────
        VolumeRouter   masterRouter = new() { IsGlobalRouter = true };
        SimpleEndpoint endpoint     = new();

        // ── Wire per-client blueprints ────────────────────────────────────────
        // Normal path: source → clientVolume → imager → normalVolume → levelMeter → clientMixEntry
        _clientVolumeBlueprint.Connect(_imagerBlueprint);
        _imagerBlueprint.Connect(_normalVolumeBlueprint);
        _normalVolumeBlueprint.Connect(_levelMeterBlueprint);
        _levelMeterBlueprint.Connect(_clientMixEntry);

        // Ghost path: imager → ghostLowpass → ghostVolume → clientMixEntry
        _imagerBlueprint.Connect(_ghostLowpassBlueprint);
        _ghostLowpassBlueprint.Connect(_ghostVolumeBlueprint);
        _ghostVolumeBlueprint.Connect(_clientMixEntry);

        // Radio path: clientVolume → radioHighpass → radioLowpass → radioVolume → clientMixEntry
        _clientVolumeBlueprint.Connect(_radioHighpassBlueprint);
        _radioHighpassBlueprint.Connect(_radioLowpassBlueprint);
        _radioLowpassBlueprint.Connect(_radioVolumeBlueprint);
        _radioVolumeBlueprint.Connect(_clientMixEntry);

        // ── Wire global output chain ──────────────────────────────────────────
        // Keep exactly one global path. Parallel global effects pull the same
        // per-client buffers multiple times per audio callback, which drains
        // voice 3x+ faster than real time and causes robotic/static playback.
        _clientMixEntry.Connect(masterRouter);
        masterRouter.Connect(endpoint);

        // ── AudioManager ──────────────────────────────────────────────────────
        // Keep playback bounded. Speaking UI updates on decode; stale buffered
        // audio here makes voices arrive long after the ring lights up.
        _audioManager = new AudioManager(_clientVolumeBlueprint,
            AudioHelpers.PlaybackPrebufferSamples,
            AudioHelpers.FrameSize * 8);
        _masterVolumeProperty = masterRouter.GetProperty(_audioManager);

        var localSettings = LocalSettingsTabSingleton<VoiceChatLocalSettings>.Instance;
        SetMasterVolume(localSettings.MasterVolume.Value);
        SetMicVolume(localSettings.MicVolume.Value);
        RefreshLocalAudioSettings();
        SetMicrophone(localSettings.MicrophoneDevice);
        VoiceDiagnostics.Log("room.construct", $"mic=\"{localSettings.MicrophoneDevice}\" master={localSettings.MasterVolume.Value:0.00} micVolume={localSettings.MicVolume.Value:0.00} noiseGate={_noiseGateThreshold:0.000}");

#if WINDOWS
        VoiceChatPluginMain.Logger.LogInfo("[VC] Room constructor: about to init audio output");
        try 
        { 
            SetSpeaker(localSettings?.SpeakerDevice ?? "");
            
            // VERIFY IT WORKED
            if (_waveOut == null)
            {
                VoiceChatPluginMain.Logger.LogError("[VC] CRITICAL: Speaker failed to initialize in constructor!");
            }
            else
            {
                VoiceChatPluginMain.Logger.LogInfo("[VC] Speaker initialized successfully in constructor");
            }
        }
        catch (Exception ex)
        {
            VoiceChatPluginMain.Logger.LogError($"[VC] Speaker init in constructor failed: {ex.Message}");
        }
#elif ANDROID
        if (VoiceChatPluginMain.ResidentObject != null)
        {
            try { InitAndroidSpeaker(); }
            catch (Exception ex)
            {
                VoiceChatPluginMain.Logger.LogError($"[VC] Android speaker init failed: {ex}");
            }
        }
        else
            VoiceChatPluginMain.Logger.LogWarning("[VC] Android: ResidentObject not available.");
#endif

        VoiceChatPluginMain.Logger.LogInfo("[VC] VoiceChatRoom constructed.");
        StartBootstrapWindow("room constructed");
        StartTransitionTrace("room constructed", CurrentSnapshot);
    }

    // ======================================================================
    // Volume / mute
    // ======================================================================

    public void SetMasterVolume(float v)
    {
        _masterVolumeProperty.Volume = v;
        if (v <= 0f)
            ClearClientBuffers();
#if ANDROID
        _androidSpeaker?.SetMasterVolume(v);
#endif
    }

    public void SetMicVolume(float v)
    {
        lock (_micStateLock)
            _micVolume = Math.Clamp(v, 0f, 2f);
    }

    public void RefreshLocalAudioSettings()
    {
        var settings = LocalSettingsTabSingleton<VoiceChatLocalSettings>.Instance;
        _noiseGateThreshold = settings?.NoiseGateThreshold.Value ?? 0f;
        _vadThreshold = settings?.VadThreshold.Value ?? 0.012f;
        _localMicActivity.VadThreshold = _vadThreshold;
        foreach (var client in _clients.Values)
            client.SetVadThreshold(_vadThreshold);
    }

    public void SetMute(bool mute)
    {
        lock (_micStateLock)
        {
            bool wasMuted = Mute;
            Mute = mute;
            if (mute)
            {
                _localMicActivity.Reset();
                _micAccumCount = 0;
                _speechSendHangoverFrames = 0;
            }
            else if (wasMuted)
            {
                StartBootstrapWindow("local unmuted");
            }
        }
    }
    public void ToggleMute() => SetMute(!Mute);
    public void SetLoopBack(bool lb) { }

    // ======================================================================
    // Microphone
    // ======================================================================

    public void SetMicrophone(string deviceName)
    {
#if WINDOWS
        SetMicrophoneWindows(deviceName);
#elif ANDROID
        SetMicrophoneAndroid(deviceName);
#endif
    }

#if WINDOWS
    private void SetMicrophoneWindows(string deviceName)
    {
        try
        {
            _waveIn?.StopRecording();
            _waveIn?.Dispose();
            _waveIn  = null;

            lock (_micStateLock)
            {
                _encoder?.Dispose();
                _encoder = AudioHelpers.GetOpusEncoder();
                _micAccumCount = 0;
                _speechSendHangoverFrames = 0;
            }

            int deviceNum = 0;
            int total = WaveInEvent.DeviceCount;
            for (int i = 0; i < total; i++)
            {
                if (WaveInEvent.GetCapabilities(i).ProductName == deviceName)
                { deviceNum = i; break; }
            }

            _waveIn = new WaveInEvent
            {
                DeviceNumber       = deviceNum,
                WaveFormat         = new WaveFormat(AudioHelpers.ClockRate, 16, 1),
                BufferMilliseconds = 20,
                NumberOfBuffers    = 3,
            };
            _waveIn.DataAvailable += OnMicDataAvailableWindows;
            _waveIn.StartRecording();

            VoiceChatPluginMain.Logger.LogInfo(
                $"[VC] Windows mic: '{(string.IsNullOrEmpty(deviceName) ? "default" : deviceName)}'");
            VoiceDiagnostics.Log("mic.windows.start", $"device=\"{(string.IsNullOrEmpty(deviceName) ? "default" : deviceName)}\" format=pcm16 rate={AudioHelpers.ClockRate} frame={AudioHelpers.FrameSize}");
        }
        catch (Exception ex)
        {
            VoiceChatPluginMain.Logger.LogError($"[VC] Windows mic init failed: {ex.Message}");
            _waveIn  = null;
            lock (_micStateLock)
            {
                _encoder?.Dispose();
                _encoder = null;
                _micAccumCount = 0;
                _speechSendHangoverFrames = 0;
            }
        }
    }
#endif

#if ANDROID
    private void SetMicrophoneAndroid(string deviceName)
    {
        if (VoiceChatPluginMain.ResidentObject != null)
        {
            var behaviour = VoiceChatPluginMain.ResidentObject.GetComponent<PermissionHelper>()
                ?? VoiceChatPluginMain.ResidentObject.AddComponent<PermissionHelper>();
            behaviour.RequestMicAndStart(this, deviceName ?? "");
        }
        else
        {
            StartMicNow(deviceName ?? "");
        }
    }

    internal void StartMicNow(string deviceName)
    {
        try
        {
            _androidMic?.Stop();
            _androidMic?.Dispose();

            lock (_micStateLock)
            {
                _encoder?.Dispose();
                _encoder = AudioHelpers.GetOpusEncoder();
                _micAccumCount = 0;
                _speechSendHangoverFrames = 0;
            }
            _androidMic    = new AndroidMicrophone();
            _androidMic.DataAvailable += OnAndroidMicData;
            _androidMic.Start(deviceName);
            VoiceDiagnostics.Log("mic.android.start", $"device=\"{(string.IsNullOrEmpty(deviceName) ? "default" : deviceName)}\" rate={AudioHelpers.ClockRate} frame={AudioHelpers.FrameSize}");
        }
        catch (Exception ex)
        {
            VoiceChatPluginMain.Logger.LogError($"[VC] Android mic init failed: {ex.Message}");
            _androidMic = null;
            lock (_micStateLock)
            {
                _encoder?.Dispose();
                _encoder = null;
                _micAccumCount = 0;
                _speechSendHangoverFrames = 0;
            }
        }
    }
#endif

    // ======================================================================
    // Speaker
    // ======================================================================

#if WINDOWS
    public void SetSpeaker(string deviceName)
    {
        try
        {
            // Skip if already playing the same device
            if (_waveOut != null 
                && _waveOut.PlaybackState == PlaybackState.Playing
                && _currentSpeakerDevice == deviceName)
            {
                VoiceChatPluginMain.Logger.LogInfo("[VC] Speaker already initialized, skipping.");
                return;
            }

            _waveOut?.Stop();
            _waveOut?.Dispose();
            _waveOut = null;
            _currentSpeakerDevice = "";

            var ep = _audioManager.Endpoint;
            if (ep == null)
            {
                VoiceChatPluginMain.Logger.LogError("[VC] Audio graph has no endpoint.");
                return;
            }

            var enumerator = new MMDeviceEnumerator();
            MMDevice? device = null;
            if (!string.IsNullOrEmpty(deviceName))
            {
                foreach (var d in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                {
                    if (d.FriendlyName == deviceName) { device = d; break; }
                }
            }
            device ??= enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

            _waveOut = new WasapiOut(device, AudioClientShareMode.Shared, false, 20);
            _waveOut.Init(ep);
            _waveOut.Play();
            _currentSpeakerDevice = deviceName ?? "";

            VoiceChatPluginMain.Logger.LogInfo(
                $"[VC] Windows speaker: '{(string.IsNullOrEmpty(deviceName) ? "default" : deviceName)}'");
            VoiceDiagnostics.Log("speaker.windows.start", $"device=\"{(string.IsNullOrEmpty(deviceName) ? "default" : deviceName)}\" latencyMs=20");
        }
        catch (Exception ex)
        {
            VoiceChatPluginMain.Logger.LogError($"[VC] Windows speaker init failed: {ex.Message}");
            _waveOut = null;
            _currentSpeakerDevice = "";
        }
    }
#endif

#if ANDROID
    private void InitAndroidSpeaker()
    {
        try
        {
            _androidSpeaker?.Dispose();
            if (_audioManager.Endpoint == null)
                throw new InvalidOperationException("AudioManager endpoint is null");
            _androidSpeaker = new AndroidSpeaker(_audioManager.Endpoint);
        }
        catch (Exception ex)
        {
            VoiceChatPluginMain.Logger.LogError($"[VC] Android speaker init failed: {ex.Message}");
            _androidSpeaker = null;
        }
    }
#endif

    // ======================================================================
    // Microphone data callbacks
    // ======================================================================

#if WINDOWS
    private void OnMicDataAvailableWindows(object? sender, WaveInEventArgs e)
    {
        lock (_micStateLock)
        {
            if (Mute || _encoder == null)
            {
                DiagInc(ref _diagMicMutedCallbacks);
                _localMicActivity.Reset();
                _speechSendHangoverFrames = 0;
                return;
            }

            int newSamples = e.BytesRecorded / 2;
            DiagInc(ref _diagMicCallbacks);
            DiagAdd(ref _diagMicSamples, newSamples);
            float level = 0f;
            for (int i = 0; i < newSamples; i++)
            {
                float s   = (BitConverter.ToInt16(e.Buffer, i * 2) / 32768f) * _micVolume;
                float abs = s < 0 ? -s : s;
                if (abs < _noiseGateThreshold)
                {
                    s = 0f;
                    abs = 0f;
                }
                else
                {
                    s = Math.Clamp(s, -1f, 1f);
                    abs = Math.Abs(s);
                }
                if (abs > level) level = abs;
                AppendMicSample(s);
            }
            _localMicActivity.PushLevel(level);
            FlushAccumulator();
        }
    }
#endif

#if ANDROID
    private void OnAndroidMicData(float[] buf, int length)
    {
        lock (_micStateLock)
        {
            if (Mute || _encoder == null)
            {
                DiagInc(ref _diagMicMutedCallbacks);
                _localMicActivity.Reset();
                _speechSendHangoverFrames = 0;
                return;
            }

            DiagInc(ref _diagMicCallbacks);
            DiagAdd(ref _diagMicSamples, length);
            float level = 0f;
            for (int i = 0; i < length; i++)
            {
                float s   = buf[i] * _micVolume;
                float abs = s < 0 ? -s : s;
                if (abs < _noiseGateThreshold)
                {
                    s = 0f;
                    abs = 0f;
                }
                else
                {
                    s = Math.Clamp(s, -1f, 1f);
                    abs = Math.Abs(s);
                }
                if (abs > level) level = abs;
                AppendMicSample(s);
            }
            _localMicActivity.PushLevel(level);
            FlushAccumulator();
        }
    }
#endif

    private void AppendMicSample(float sample)
    {
        if (_micAccumCount >= _micAccumulator.Length)
        {
            int keep = Math.Max(0, _micAccumulator.Length - AudioHelpers.FrameSize);
            if (keep > 0)
                Array.Copy(_micAccumulator, AudioHelpers.FrameSize, _micAccumulator, 0, keep);
            _micAccumCount = keep;
            DiagAdd(ref _diagMicDroppedSamples, AudioHelpers.FrameSize);
        }

        _micAccumulator[_micAccumCount++] = sample;
    }

    private void FlushAccumulator()
    {
        while (_micAccumCount >= AudioHelpers.FrameSize)
        {
            EncodeAndEnqueue(_micAccumulator, AudioHelpers.FrameSize);
            int remaining = _micAccumCount - AudioHelpers.FrameSize;
            if (remaining > 0)
                Array.Copy(_micAccumulator, AudioHelpers.FrameSize, _micAccumulator, 0, remaining);
            _micAccumCount = remaining;
        }
    }

    private void EncodeAndEnqueue(float[] pcm, int sampleCount)
    {
        if (_encoder == null) return;
        if (!ShouldTransmitFrame(pcm, sampleCount, out float peak, out float threshold, out string transmitReason))
        {
            TraceLocalAudioDecision(false, 0, 0, sampleCount, peak, threshold, transmitReason);
            return;
        }
        try
        {
            int encoded = _encoder.Encode(pcm, sampleCount, _encodeBuffer, _encodeBuffer.Length);
            if (encoded <= 0) return;

            DiagInc(ref _diagEncodedFrames);
            DiagAdd(ref _diagEncodedBytes, encoded);

            int seq = System.Threading.Interlocked.Increment(ref _sendSeq);
            var payload = new byte[VoiceProtocol.AudioHeaderBytes + encoded];
            payload[0] = (byte)(seq >> 24);
            payload[1] = (byte)(seq >> 16);
            payload[2] = (byte)(seq >> 8);
            payload[3] = (byte)(seq);
            payload[4] = (byte)(VoiceChatHudState.IsInImpostorRadioMode()
                ? VoiceFrameFlags.ImpostorRadio
                : VoiceFrameFlags.None);
            Array.Copy(_encodeBuffer, 0, payload, VoiceProtocol.AudioHeaderBytes, encoded);

            TraceLocalAudioDecision(true, seq, encoded, sampleCount, peak, threshold, transmitReason);
            _transport.EnqueueOutgoing(payload);
        }
        catch (Exception ex)
        {
            VoiceChatPluginMain.Logger.LogError($"[VC] Encode error: {ex.Message}");
        }
    }

    private bool ShouldTransmitFrame(float[] pcm, int sampleCount, out float peak, out float transmitThreshold, out string reason)
    {
        const int hangoverFrames = 10; // 200 ms tail, avoids choppy word endings.

        peak = 0f;
        int limit = Math.Min(sampleCount, pcm.Length);
        for (int i = 0; i < limit; i++)
        {
            float abs = Math.Abs(pcm[i]);
            if (abs > peak) peak = abs;
        }

        transmitThreshold = Math.Max(_noiseGateThreshold, _vadThreshold * 0.75f);
        if (peak >= transmitThreshold)
        {
            _speechSendHangoverFrames = hangoverFrames;
            reason = "voice";
            return true;
        }

        if (_speechSendHangoverFrames <= 0)
        {
            reason = "below-threshold";
            return false;
        }

        _speechSendHangoverFrames--;
        reason = "hangover";
        return true;
    }

    // ======================================================================
    // Packet queuing (Hazel network thread → main thread)
    // ======================================================================

    internal static void EnqueueAudioPacket(int senderId, byte[] encoded)
        => Current?._transport.QueueIncomingAudio(senderId, encoded);

    internal static void EnqueueAudioPacket(int senderId, byte playerId, string playerName, byte[] encoded)
        => Current?._transport.QueueIncomingAudio(senderId, playerId, playerName, encoded);

    internal static void EnqueueProfilePacket(int senderId, byte playerId, string playerName)
        => Current?._transport.QueueIncomingProfile(senderId, playerId, playerName);

    // ======================================================================
    // Main update loop (WITH AGGRESSIVE SPEAKER RECOVERY - FIXED!)
    // ======================================================================

    public void Update()
    {
        long updateStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        string updateStep = "speaker-check";
#if ANDROID
        _androidMic?.Tick();
#elif WINDOWS
        // ── AGGRESSIVE speaker recovery: Reinitialize if lost during scene transition ──
        _lastSpeakerCheckTime -= Time.deltaTime;
        if (_lastSpeakerCheckTime <= 0f)
        {
            _lastSpeakerCheckTime = 1f;  // Check every second
            
            // Log audio manager state
            if (_audioManager?.Endpoint == null)
            {
                VoiceChatPluginMain.Logger.LogError("[VC] CRITICAL: Audio endpoint is NULL!");
            }
            
            if (_waveOut == null)
            {
                VoiceChatPluginMain.Logger.LogWarning("[VC] Speaker is NULL! Attempting recovery...");
                var settings = LocalSettingsTabSingleton<VoiceChatLocalSettings>.Instance;
                if (settings != null)
                    SetSpeaker(settings.SpeakerDevice);
            }
            else if (_waveOut.PlaybackState != PlaybackState.Playing)
            {
                VoiceChatPluginMain.Logger.LogWarning($"[VC] Speaker not playing (state: {_waveOut.PlaybackState}). Restarting...");
                try 
                { 
                    _waveOut.Play();
                }
                catch (Exception ex)
                {
                    VoiceChatPluginMain.Logger.LogError($"[VC] Failed to restart speaker: {ex.Message}");
                    _waveOut = null;
                }
            }
        }
#endif

        updateStep = "transport";
        PruneDisconnectedClients();
        TryUpdateLocalProfile();
        TrySendCompatibilityHandshake();
        TryRunBootstrapRefresh();
        TickVoiceActivity();
        DrainSendQueue();
        DrainReceiveQueue();
        MaybeLogNetworkStats();

        updateStep = "snapshot";
        _commsSabCheckTimer -= Time.deltaTime;
        if (_commsSabCheckTimer <= 0f)
        {
            _commsSabCheckTimer = CommsSabotageRefreshInterval;
            _commsSabActive     = CheckCommsSabotage();
        }

        _snapshotRefreshTimer -= Time.deltaTime;
        bool refreshSnapshot = CurrentSnapshot == null || _snapshotRefreshTimer <= 0f;
        if (refreshSnapshot)
        {
            _snapshotRefreshTimer = StateRefreshInterval;
            CurrentSnapshot = VoiceSnapshotBuilder.Build(_commsSabActive);
        }

        var snapshot = CurrentSnapshot;
        Vector2? listenerPos = snapshot?.LocalPosition;
        TrackTransitionPhase(snapshot);
        if (snapshot != null)
            PrewarmSnapshotClients(snapshot);
        bool localInVent = snapshot != null &&
                            snapshot.TryGetLocalPlayer(out var localSnapshot) &&
                            localSnapshot.InVent;

        IReadOnlyList<SpeakerCache> speakerCache = Array.Empty<SpeakerCache>();
        updateStep = "speaker-cache";
        if (listenerPos.HasValue && _virtualSpeakers.Count > 0)
        {
            float maxRange = VoiceChatGameOptions.Instance.MaxChatDistance.Value;
            var list = new List<SpeakerCache>(_virtualSpeakers.Count);
            foreach (var speaker in _virtualSpeakers)
            {
                float d = Vector2.Distance(speaker.Position, listenerPos.Value);
                float volume = VoiceAudioOcclusion.ApplyFalloff(d, maxRange, (VoiceFalloffMode)VoiceChatGameOptions.Instance.FalloffMode.Value);
                if (volume > 0f)
                    list.Add(new(speaker, volume, GetPan(listenerPos.Value.x, speaker.Position.x)));
            }
            speakerCache = list;
        }

        var phase = snapshot?.Phase ?? VoiceGamePhase.Unknown;
        bool inLobby = phase is VoiceGamePhase.Lobby
            or VoiceGamePhase.Menu
            or VoiceGamePhase.Intro
            or VoiceGamePhase.EndGame
            or VoiceGamePhase.Unknown;
        bool inMeeting = phase is VoiceGamePhase.Meeting or VoiceGamePhase.Exile;

        _routeRefreshTimer -= Time.deltaTime;
        bool refreshRoutes = snapshot != null &&
                             (_routeRefreshTimer <= 0f || _lastRouteClientCount != _clients.Count);

        updateStep = "routes";
        if (refreshRoutes)
        {
            _routeRefreshTimer = StateRefreshInterval;
            _lastRouteClientCount = _clients.Count;

            foreach (var kv in _clients)
            {
                int senderId = kv.Key;
                var client = kv.Value;
                bool remoteRadioActive = VoiceClientRegistry.IsRadioActive(senderId);

                if (inLobby)
                    client.UpdateLobby(snapshot, senderId);
                else if (inMeeting)
                    client.UpdateMeeting(snapshot, senderId, remoteRadioActive);
                else
                    client.UpdateTaskPhase(snapshot, senderId, listenerPos, speakerCache, _virtualMics, localInVent, remoteRadioActive, _commsSabActive);

                TraceRouteChange(senderId, client, snapshot!);
            }
        }

        foreach (var client in _clients.Values)
            client.TickRouteSmoothing();

        updateStep = "diagnostics";
        MaybeLogTransitionTraceState(snapshot);

        if (_masterVolumeProperty.Volume <= 0f)
            ClearClientBuffers();

        TraceUpdateCost(updateStartTicks, updateStep, snapshot);
    }

    private void TickVoiceActivity()
    {
        lock (_micStateLock)
            _localMicActivity.TickSilence();
        foreach (var client in _clients.Values)
            client.TickVoiceActivity();
    }

    private void ClearClientBuffers()
    {
        foreach (var client in _clients.Values)
            client.ClearBufferedSamples();
    }

    private void DrainSendQueue()
    {
        if (AmongUsClient.Instance == null || PlayerControl.LocalPlayer == null) return;

        bool broadcastAudio = VoiceClientRegistry.AreAllLiveRemoteClientsCompatible();
        if (!broadcastAudio && !VoiceClientRegistry.HasKnownIncompatibleLiveRemoteClients())
        {
            broadcastAudio = true;
            RefreshCompatibilityNow("audio bootstrap", throttle: true);
        }

        var compatibleClients = broadcastAudio ? Array.Empty<int>() : VoiceClientRegistry.GetCompatibleClientIds();
        if (!broadcastAudio && compatibleClients.Length == 0)
        {
            DiagInc(ref _diagNoTargets);
            TraceNoAudioTargets();
            _transport.DropStaleOutgoing(GetMaxQueuedFrameAgeSeconds());
            RefreshCompatibilityNow("no compatible audio targets", throttle: true);
            return;
        }

        var sendOption = GetAudioSendOption();
        bool bundleAudio = VoiceProtocol.MaxBundledAudioFrames > 1;
        while (true)
        {
            if (bundleAudio &&
                _transport.OutgoingCount < VoiceProtocol.MaxBundledAudioFrames &&
                (!_transport.TryPeekOutgoing(out var oldestFrame) ||
                 GetFrameAgeSeconds(oldestFrame.CreatedAtUtc) < VoiceProtocol.MaxAudioBundleWaitSeconds))
                return;

            if (!TryDequeueFreshOutgoing(out var payload))
                return;

            if (!bundleAudio)
            {
                SendAudioPayload(payload.Payload, compatibleClients, broadcastAudio, sendOption, 1);
                continue;
            }

            _sendBundleFrames[0] = payload;
            int frameCount = 1;
            while (frameCount < _sendBundleFrames.Length &&
                   TryDequeueFreshOutgoing(out var nextPayload))
                _sendBundleFrames[frameCount++] = nextPayload;

            if (frameCount > 1 && TryBuildAudioBundlePayload(_sendBundleFrames, frameCount, out var rpcPayload))
            {
                SendAudioPayload(rpcPayload, compatibleClients, broadcastAudio, sendOption, frameCount);
                continue;
            }

            for (int i = 0; i < frameCount; i++)
                SendAudioPayload(_sendBundleFrames[i].Payload, compatibleClients, broadcastAudio, sendOption, 1);
        }
    }

    private void SendAudioPayload(byte[] rpcPayload, int[] compatibleClients, bool broadcastAudio, SendOption sendOption, int frameCount)
    {
        if (broadcastAudio)
        {
            // All live clients are compatible with this mod/protocol, so one
            // broadcast replaces N targeted sends at 15-player scale.
            SendAudioPayload(BroadcastTargetClientId, rpcPayload, sendOption, frameCount);
            return;
        }

        foreach (int targetClientId in compatibleClients)
            SendAudioPayload(targetClientId, rpcPayload, sendOption, frameCount);
    }

    private void SendAudioPayload(int targetClientId, byte[] rpcPayload, SendOption sendOption, int frameCount)
    {
        try
        {
            SendAudioRpc(targetClientId, rpcPayload, sendOption);
            TraceSendAudioPayload(targetClientId, rpcPayload, sendOption, frameCount);
            DiagInc(ref _diagSendRpcs);
            DiagAdd(ref _diagSendFrames, frameCount);
            DiagAdd(ref _diagSendBytes, rpcPayload.Length);
            if (frameCount > 1)
            {
                DiagInc(ref _diagBundledRpcs);
                DiagAdd(ref _diagBundledFrames, frameCount);
            }
            _transport.CountSent(rpcPayload.Length);
        }
        catch (Exception ex)
        {
            VoiceChatPluginMain.Logger.LogError($"[VC] RPC send error to {targetClientId}: {ex.Message}");
        }
    }

    private bool TryDequeueFreshOutgoing(out VoiceOutgoingFrame frame)
    {
        while (_transport.TryDequeueOutgoing(out frame))
        {
            if (GetFrameAgeSeconds(frame.CreatedAtUtc) <= GetMaxQueuedFrameAgeSeconds())
                return true;

            _transport.CountStaleDrop();
            DiagInc(ref _diagStaleDrops);
        }

        return false;
    }

    private static void SendAudioRpc(int targetClientId, byte[] payload, SendOption sendOption)
    {
        var w = AmongUsClient.Instance.StartRpcImmediately(
            PlayerControl.LocalPlayer.NetId, AudioRpcId, sendOption, targetClientId);
        w.Write((byte)VoicePacketType.Audio);
        w.WriteBytesAndSize(payload);
        AmongUsClient.Instance.FinishRpcImmediately(w);
    }

    private static SendOption GetAudioSendOption()
        => DefaultAudioSendOption;

    private static double GetMaxQueuedFrameAgeSeconds()
        => IsHttpRegion()
            ? VoiceProtocol.MaxHttpQueuedFrameAgeSeconds
            : VoiceProtocol.MaxQueuedFrameAgeSeconds;

    private static double GetFrameAgeSeconds(DateTime createdAtUtc)
        => (DateTime.UtcNow - createdAtUtc).TotalSeconds;

    private static bool IsHttpRegion()
    {
        try
        {
            var manager = DestroyableSingleton<ServerManager>.Instance;
            if (manager == null || manager.CurrentRegion == null) return false;

            var servers = manager.CurrentRegion.Servers;
            if (servers == null || servers.Length == 0) return false;

            var server = servers[0];
            string ip = server.Ip ?? string.Empty;
            return ip.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                   ip.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                   !server.UseDtls;
        }
        catch
        {
            return false;
        }
    }

    internal static bool TryBuildAudioBundlePayload(VoiceOutgoingFrame[] frames, int frameCount, out byte[] payload)
    {
        payload = Array.Empty<byte>();
        if (frameCount <= 1) return false;

        int size = VoiceProtocol.AudioHeaderBytes + VoiceProtocol.AudioBundleCountBytes;
        VoiceFrameHeader first = default;
        for (int i = 0; i < frameCount; i++)
        {
            var frame = frames[i];
            if (!VoiceFrameHeader.TryRead(frame.Payload, out var header, out int encodedOffset, out int encodedCount) ||
                header.Flags.HasFlag(VoiceFrameFlags.BundledAudio) ||
                encodedCount > ushort.MaxValue)
                return false;

            int entrySize = VoiceProtocol.AudioHeaderBytes + VoiceProtocol.AudioBundleLengthBytes + encodedCount;
            if (size + entrySize > VoiceProtocol.MaxAudioPayloadBytes)
                return false;

            if (i == 0) first = header;
            size += entrySize;
        }

        payload = new byte[size];
        WriteSequence(payload, 0, first.Sequence);
        payload[VoiceProtocol.AudioSequenceBytes] = (byte)(first.Flags | VoiceFrameFlags.BundledAudio);
        int offset = VoiceProtocol.AudioHeaderBytes;
        payload[offset++] = (byte)frameCount;

        for (int i = 0; i < frameCount; i++)
        {
            var frame = frames[i];
            VoiceFrameHeader.TryRead(frame.Payload, out var header, out int encodedOffset, out int encodedCount);

            WriteSequence(payload, offset, header.Sequence);
            offset += VoiceProtocol.AudioSequenceBytes;
            payload[offset++] = (byte)(header.Flags & ~VoiceFrameFlags.BundledAudio);
            payload[offset++] = (byte)(encodedCount >> 8);
            payload[offset++] = (byte)encodedCount;
            Array.Copy(frame.Payload, encodedOffset, payload, offset, encodedCount);
            offset += encodedCount;
        }

        return true;
    }

    private static void WriteSequence(byte[] payload, int offset, int sequence)
    {
        payload[offset]     = (byte)(sequence >> 24);
        payload[offset + 1] = (byte)(sequence >> 16);
        payload[offset + 2] = (byte)(sequence >> 8);
        payload[offset + 3] = (byte)sequence;
    }

    private void DrainReceiveQueue()
    {
        while (_transport.TryDequeueIncoming(out var pkt))
        {
            if (GetFrameAgeSeconds(pkt.ReceivedAtUtc) > GetMaxQueuedFrameAgeSeconds())
            {
                _transport.CountStaleDrop();
                DiagInc(ref _diagStaleDrops);
                TraceReceiveDrop(pkt, "stale-receive-queue");
                continue;
            }

            if (pkt.PacketType == (byte)VoicePacketType.Audio)
                ProcessAudioFrame(pkt.SenderId, pkt.PlayerId, pkt.PlayerName, pkt.Data);
            else if (pkt.PacketType == (byte)VoicePacketType.Profile)
                ProcessProfileUpdate(pkt.SenderId, pkt.PlayerId, pkt.PlayerName);
        }

        int decodeBudget = VoiceProtocol.MaxDecodedAudioFramesPerUpdate;
        _jitterFlushIds.Clear();
        foreach (int senderId in _jitterBuffers.Keys)
            _jitterFlushIds.Add(senderId);
        foreach (int senderId in _jitterFlushIds)
        {
            if (decodeBudget <= 0) break;
            FlushJitterBuffer(senderId, ref decodeBudget);
        }
    }

    // ======================================================================
    // Client creation
    // ======================================================================

    private VCPlayer CreateClient(int senderId)
    {
        long startTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        // Generate() creates fresh per-client AudioRoutingInstanceNodes for every
        // blueprint router (clientVolume, imager, normalVolume, levelMeter,
        // ghostLowpass, ghostVolume, radioHighpass, radioLowpass, radioVolume).
        // Clients never share node state or buffers.
        long graphStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        var instance = _audioManager.Generate(senderId);
        double graphMs = ElapsedMilliseconds(graphStartTicks);
        long playerStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        var player = new VCPlayer(
            this, instance,
            _imagerBlueprint,
            _normalVolumeBlueprint,
            _ghostVolumeBlueprint,
            _radioVolumeBlueprint,
            _clientVolumeBlueprint,
            _levelMeterBlueprint);
        double playerMs = ElapsedMilliseconds(playerStartTicks);
        player.SetVadThreshold(_vadThreshold);
        TraceOperationCost("transition.perf.clientCreate",
            $"sender={senderId} graphMs={graphMs:0.000} playerMs={playerMs:0.000} totalMs={ElapsedMilliseconds(startTicks):0.000} clientsBefore={_clients.Count}",
            Math.Max(graphMs, playerMs));
        return player;
    }

    private VCPlayer EnsureClient(int senderId, string reason)
    {
        if (_clients.TryGetValue(senderId, out var player))
            return player;

        player = CreateClient(senderId);
        _clients[senderId] = player;
        VoiceChatPluginMain.Logger.LogInfo($"[VC] New client {senderId} ({reason}).");
        VoiceDiagnostics.Log("client.new", $"sender={senderId} reason=\"{LogSafe(reason)}\"");

        if (_pendingProfiles.TryGetValue(senderId, out var pending))
        {
            player.UpdateProfile(pending.PlayerId, pending.PlayerName);
            _pendingProfiles.Remove(senderId);
            VoiceChatPluginMain.Logger.LogInfo(
                $"[VC] Applied buffered profile to new client {senderId}: id={pending.PlayerId} name={pending.PlayerName}");
        }

        return player;
    }

    private void PrewarmSnapshotClients(VoiceGameStateSnapshot snapshot)
    {
        long startTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        int created = 0;
        int scanned = 0;
        int existing = 0;
        foreach (var remote in snapshot.Players)
        {
            scanned++;
            if (remote.IsLocal || remote.Disconnected || remote.IsDummy || remote.ClientId < 0)
                continue;

            bool existed = _clients.ContainsKey(remote.ClientId);
            var player = EnsureClient(remote.ClientId, "snapshot prewarm");
            player.UpdateProfile(remote.PlayerId, remote.PlayerName);
            EnsureDecoderState(remote.ClientId);

            if (existed)
                existing++;
            else if (++created >= 2)
                break;
        }

        double elapsedMs = ElapsedMilliseconds(startTicks);
        if (IsTransitionTraceActive && (created > 0 || elapsedMs >= SlowOperationLogThresholdMs))
            VoiceDiagnostics.Log("transition.perf.prewarm",
                $"phase={snapshot.Phase} scanned={scanned} created={created} existing={existing} clients={_clients.Count} elapsedMs={elapsedMs:0.000} snapshot=\"{LogSafe(DescribeTransitionSnapshot(snapshot))}\"");
    }

    private IOpusDecoder EnsureDecoderState(int senderId)
    {
        long startTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        bool decoderCreated = false;
        bool bufferCreated = false;
        bool jitterCreated = false;
        if (!_decoders.TryGetValue(senderId, out var decoder))
        {
            long decoderStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            decoder = AudioHelpers.GetOpusDecoder();
            _decoders[senderId] = decoder;
            decoderCreated = true;
            TraceOperationCost("transition.perf.decoderCreate",
                $"sender={senderId} elapsedMs={ElapsedMilliseconds(decoderStartTicks):0.000}",
                ElapsedMilliseconds(decoderStartTicks));
        }

        if (!_decodeBufs.ContainsKey(senderId))
        {
            _decodeBufs[senderId] = new float[AudioHelpers.FrameSize * 2];
            bufferCreated = true;
        }

        if (!_jitterBuffers.ContainsKey(senderId))
        {
            _jitterBuffers[senderId] = new SenderJitterBuffer();
            jitterCreated = true;
        }

        double elapsedMs = ElapsedMilliseconds(startTicks);
        if (IsTransitionTraceActive && (decoderCreated || bufferCreated || jitterCreated || elapsedMs >= SlowOperationLogThresholdMs))
            VoiceDiagnostics.Log("transition.perf.decoderState",
                $"sender={senderId} decoderCreated={decoderCreated} bufferCreated={bufferCreated} jitterCreated={jitterCreated} elapsedMs={elapsedMs:0.000}");

        return decoder;
    }

    // ======================================================================
    // Audio frame processing
    // ======================================================================

    private void ProcessAudioFrame(int senderId, byte observedPlayerId, string observedPlayerName, byte[] payload)
    {
        if (!VoiceFrameHeader.TryRead(payload, out var header, out int encodedOffset, out int encodedCount))
        {
            _transport.CountBadPacket();
            DiagInc(ref _diagBadPackets);
            return;
        }

        if (VoiceClientRegistry.IsKnownIncompatible(senderId)) return;
        if (!VoiceClientRegistry.IsCompatible(senderId))
            VoiceClientRegistry.MarkAudioBootstrap(senderId);
        ApplyObservedProfile(senderId, observedPlayerId, observedPlayerName);

        TraceReceiveAudioFrame(senderId, observedPlayerId, observedPlayerName, header, payload.Length, encodedCount, bundled: false);

        DiagInc(ref _diagReceivePackets);
        DiagAdd(ref _diagReceiveBytes, payload.Length);
        _transport.CountReceived(payload.Length);

        if (header.Flags.HasFlag(VoiceFrameFlags.BundledAudio))
        {
            ProcessAudioBundle(senderId, payload, encodedOffset);
            return;
        }

        VoiceClientRegistry.MarkRadioActive(senderId, header.IsImpostorRadioActive);

        QueueAudioFrameForDecode(senderId, header, payload, encodedOffset, encodedCount);
    }

    private void ProcessAudioBundle(int senderId, byte[] payload, int offset)
    {
        if (offset >= payload.Length)
        {
            _transport.CountBadPacket();
            DiagInc(ref _diagBadPackets);
            return;
        }

        int frameCount = payload[offset++];
        if (IsTransitionTraceActive)
            VoiceDiagnostics.Log("transition.rx.bundle",
                $"sender={senderId} frames={frameCount} payloadBytes={payload.Length} offset={offset - 1} registry=\"{LogSafe(VoiceClientRegistry.Describe(senderId))}\"");
        if (frameCount <= 0 || frameCount > VoiceProtocol.MaxBundledAudioFrames)
        {
            _transport.CountBadPacket();
            DiagInc(ref _diagBadPackets);
            return;
        }

        DiagInc(ref _diagReceiveBundles);
        DiagAdd(ref _diagReceiveBundleFrames, frameCount);

        int framesOffset = offset;
        for (int i = 0; i < frameCount; i++)
        {
            if (offset + VoiceProtocol.AudioHeaderBytes + VoiceProtocol.AudioBundleLengthBytes > payload.Length)
            {
                _transport.CountBadPacket();
                DiagInc(ref _diagBadPackets);
                return;
            }

            int sequence = ReadSequence(payload, offset);
            offset += VoiceProtocol.AudioSequenceBytes;
            var flags = (VoiceFrameFlags)payload[offset++];
            if ((flags & ~VoiceProtocol.AllowedFrameFlags) != 0 ||
                flags.HasFlag(VoiceFrameFlags.BundledAudio))
            {
                _transport.CountBadPacket();
                DiagInc(ref _diagBadPackets);
                return;
            }
            int encodedCount = (payload[offset++] << 8) | payload[offset++];
            if (encodedCount <= 0 || offset + encodedCount > payload.Length)
            {
                _transport.CountBadPacket();
                DiagInc(ref _diagBadPackets);
                return;
            }

            offset += encodedCount;
        }

        if (offset != payload.Length)
        {
            _transport.CountBadPacket();
            DiagInc(ref _diagBadPackets);
            return;
        }

        offset = framesOffset;
        DateTime receivedAtUtc = DateTime.UtcNow;
        for (int i = 0; i < frameCount; i++)
        {
            int sequence = ReadSequence(payload, offset);
            offset += VoiceProtocol.AudioSequenceBytes;
            var flags = (VoiceFrameFlags)payload[offset++];
            int encodedCount = (payload[offset++] << 8) | payload[offset++];

            var header = new VoiceFrameHeader(sequence, flags);
            VoiceClientRegistry.MarkRadioActive(senderId, header.IsImpostorRadioActive);
            TraceReceiveAudioFrame(senderId, 0, "", header, payload.Length, encodedCount, bundled: true);
            QueueAudioFrameForDecode(senderId, new BufferedVoiceFrame(header, payload, offset, encodedCount, receivedAtUtc));
            offset += encodedCount;
        }
    }

    private void QueueAudioFrameForDecode(int senderId, VoiceFrameHeader header, byte[] payload, int encodedOffset, int encodedCount)
        => QueueAudioFrameForDecode(senderId, new BufferedVoiceFrame(header, payload, encodedOffset, encodedCount, DateTime.UtcNow));

    private void QueueAudioFrameForDecode(int senderId, BufferedVoiceFrame frame)
    {
        if (!_jitterBuffers.TryGetValue(senderId, out var jitterBuffer))
        {
            jitterBuffer = new SenderJitterBuffer();
            _jitterBuffers[senderId] = jitterBuffer;
            if (IsTransitionTraceActive)
                VoiceDiagnostics.Log("transition.jitter.new", $"sender={senderId} seq={frame.Header.Sequence}");
        }

        if (!jitterBuffer.Enqueue(frame))
        {
            _transport.CountStaleDrop();
            DiagInc(ref _diagJitterDrops);
            if (IsTransitionTraceActive)
                VoiceDiagnostics.Log("transition.jitter.drop", $"sender={senderId} seq={frame.Header.Sequence} reason=enqueue-rejected");
            return;
        }

        TraceJitterState(senderId, frame, jitterBuffer, "enqueue");
    }

    private static int ReadSequence(byte[] payload, int offset)
        => (payload[offset] << 24) |
           (payload[offset + 1] << 16) |
           (payload[offset + 2] << 8) |
           payload[offset + 3];

    private void FlushJitterBuffer(int senderId, ref int decodeBudget)
    {
        if (!_jitterBuffers.TryGetValue(senderId, out var jitterBuffer)) return;

        var now = DateTime.UtcNow;
        int decodedThisUpdate = 0;
        while (decodeBudget > 0 &&
               decodedThisUpdate < VoiceProtocol.MaxJitterFramesPerUpdate &&
               jitterBuffer.TryDequeueReady(now, out var frame))
        {
            TraceJitterState(senderId, frame, jitterBuffer, "dequeue");
            DecodeAudioFrame(senderId, frame);
            decodedThisUpdate++;
            decodeBudget--;
        }

        if (decodedThisUpdate == 0 && jitterBuffer.PendingCount > 0 && IsTransitionTraceActive)
            TraceJitterWait(senderId, jitterBuffer);
    }

    private void DecodeAudioFrame(int senderId, BufferedVoiceFrame frame)
    {
        long totalStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        var header = frame.Header;
        long setupStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        var decoder = EnsureDecoderState(senderId);
        var buf = _decodeBufs[senderId];
        var player = EnsureClient(senderId, "decode");
        double setupMs = ElapsedMilliseconds(setupStartTicks);

        int lastOutputSeq = header.Sequence - 1;
        bool hasLastOutputSeq = false;

        // Detect packet gaps. Do not synthesize PLC/FEC frames here: in practice
        // the game RPC transport produced rare concealment bursts that sounded
        // like static. A tiny dropout is less noticeable than synthetic noise.
        if (_lastSeq.TryGetValue(senderId, out int prevSeq))
        {
            lastOutputSeq = prevSeq;
            hasLastOutputSeq = true;
            uint gapDistance = GetForwardSequenceGap(prevSeq, header.Sequence);
            if (gapDistance > int.MaxValue)
            {
                _transport.CountStaleDrop();
                DiagInc(ref _diagStaleDrops);
                if (IsTransitionTraceActive)
                    VoiceDiagnostics.Log("transition.sequence.drop", $"sender={senderId} prevSeq={prevSeq} seq={header.Sequence} reason=backward-or-huge-gap");
                return;
            }

            if (gapDistance > 0)
            {
                _transport.CountSequenceGap((int)gapDistance);
                DiagAdd(ref _diagSequenceGaps, gapDistance);
                if (IsTransitionTraceActive)
                    VoiceDiagnostics.Log("transition.sequence.gap", $"sender={senderId} prevSeq={prevSeq} seq={header.Sequence} gap={gapDistance}");
            }
        }

        int decoded;
        long decodeStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        try   { decoded = decoder.Decode(frame.EncodedSpan, buf, AudioHelpers.FrameSize, false); }
        catch (Exception ex)
        {
            if (hasLastOutputSeq)
                _lastSeq[senderId] = lastOutputSeq;
            _transport.CountDecodeError();
            DiagInc(ref _diagDecodeErrors);
            _transport.LogThrottled($"decode:{senderId}", $"[VC] Decode error client {senderId}: {ex.Message}");
            if (IsTransitionTraceActive)
                VoiceDiagnostics.Log("transition.decode.error", $"sender={senderId} seq={header.Sequence} bytes={frame.EncodedCount} error=\"{LogSafe(ex.Message)}\"");
            return;
        }
        double decodeMs = ElapsedMilliseconds(decodeStartTicks);
        if (decoded <= 0)
        {
            if (hasLastOutputSeq)
                _lastSeq[senderId] = lastOutputSeq;
            if (IsTransitionTraceActive)
                VoiceDiagnostics.Log("transition.decode.empty", $"sender={senderId} seq={header.Sequence} bytes={frame.EncodedCount}");
            return;
        }

        var stats = InspectPcm(buf, decoded);
        double decodeDeltaMs = -1;
        if (_lastDecodeUtc.TryGetValue(senderId, out var lastDecodeUtc))
            decodeDeltaMs = (DateTime.UtcNow - lastDecodeUtc).TotalMilliseconds;
        float discontinuity = 0f;
        if (_lastDecodeTailSample.TryGetValue(senderId, out float previousTail))
            discontinuity = Math.Abs(stats.FirstSample - previousTail);
        int bufferBefore = player.BufferedSamples;
        long addStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        player.AddSamples(buf, decoded);
        double addMs = ElapsedMilliseconds(addStartTicks);
        int bufferAfter = player.BufferedSamples;
        _lastDecodeUtc[senderId] = DateTime.UtcNow;
        _lastDecodeTailSample[senderId] = stats.LastSample;
        TraceDecodeAudio(senderId, player, header, frame, decoded, setupMs, decodeMs, addMs, ElapsedMilliseconds(totalStartTicks), bufferBefore, bufferAfter, stats, decodeDeltaMs, discontinuity);
        if (IsTransitionTraceActive && stats.Peak >= 0.98f)
            VoiceDiagnostics.Log("transition.audio.clip",
                $"sender={senderId} seq={header.Sequence} peak={stats.Peak:0.00000} rms={stats.Rms:0.00000} zeroRatio={stats.ZeroRatio:0.000} decodedSamples={decoded}");
        if (IsTransitionTraceActive && discontinuity >= 0.35f)
            VoiceDiagnostics.Log("transition.audio.discontinuity",
                $"sender={senderId} seq={header.Sequence} delta={discontinuity:0.00000} prevTail={previousTail:0.00000} first={stats.FirstSample:0.00000} last={stats.LastSample:0.00000} peak={stats.Peak:0.00000} rms={stats.Rms:0.00000} decodeDeltaMs={decodeDeltaMs:0.000} bufferBefore={bufferBefore} bufferAfter={bufferAfter}");
        DiagInc(ref _diagDecodedFrames);
        DiagAdd(ref _diagDecodedSamples, decoded);
        _lastSeq[senderId] = header.Sequence;
    }

    private static uint GetForwardSequenceGap(int previousSequence, int currentSequence)
        => unchecked((uint)currentSequence - (uint)previousSequence - 1u);

    private readonly record struct BufferedVoiceFrame(
        VoiceFrameHeader Header,
        byte[] Payload,
        int EncodedOffset,
        int EncodedCount,
        DateTime ReceivedAtUtc)
    {
        public ReadOnlySpan<byte> EncodedSpan => new(Payload, EncodedOffset, EncodedCount);
    }

    private sealed class SenderJitterBuffer
    {
        private readonly List<BufferedVoiceFrame> _pending = new(VoiceProtocol.MaxJitterBufferFrames);
        private int? _nextSequence;
        private int _highestSequence;

        public int PendingCount => _pending.Count;
        public int? NextSequence => _nextSequence;
        public int HighestSequence => _highestSequence;

        public bool Enqueue(BufferedVoiceFrame frame)
        {
            int sequence = frame.Header.Sequence;
            if (_nextSequence.HasValue && IsSequenceBehind(sequence, _nextSequence.Value))
                return false;
            for (int i = 0; i < _pending.Count; i++)
                if (_pending[i].Header.Sequence == sequence)
                    return false;

            _pending.Add(frame);
            if (!_nextSequence.HasValue)
            {
                _nextSequence = sequence;
                _highestSequence = sequence;
            }
            else if (IsSequenceAhead(sequence, _highestSequence))
            {
                _highestSequence = sequence;
            }

            while (_pending.Count > VoiceProtocol.MaxJitterBufferFrames)
            {
                int oldestIndex = GetOldestPendingIndex();
                int oldestSequence = _pending[oldestIndex].Header.Sequence;
                _pending.RemoveAt(oldestIndex);
                if (_nextSequence.HasValue && oldestSequence == _nextSequence.Value)
                    _nextSequence = unchecked(oldestSequence + 1);
            }

            return true;
        }

        public bool TryDequeueReady(DateTime now, out BufferedVoiceFrame frame)
        {
            frame = default;
            if (!_nextSequence.HasValue || _pending.Count == 0)
                return false;

            while (_pending.Count > 0)
            {
                int expected = _nextSequence.Value;
                int expectedIndex = IndexOfSequence(expected);
                if (expectedIndex >= 0)
                {
                    var expectedFrame = _pending[expectedIndex];
                    if (!IsReady(expectedFrame, now))
                        return false;

                    _pending.RemoveAt(expectedIndex);
                    _nextSequence = unchecked(expected + 1);
                    frame = expectedFrame;
                    return true;
                }

                int nextAvailableIndex = GetOldestPendingIndex();
                var nextAvailable = _pending[nextAvailableIndex];
                int nextSequence = nextAvailable.Header.Sequence;
                if (IsSequenceBehind(nextSequence, expected))
                {
                    _pending.RemoveAt(nextAvailableIndex);
                    continue;
                }

                bool missingFrameExpired = ForwardSequenceDistance(expected, _highestSequence) >= VoiceProtocol.JitterBufferDelayFrames;
                if (!missingFrameExpired && !IsReady(nextAvailable, now))
                    return false;

                _pending.RemoveAt(nextAvailableIndex);
                _nextSequence = unchecked(nextSequence + 1);
                frame = nextAvailable;
                return true;
            }

            return false;
        }

        private bool IsReady(BufferedVoiceFrame frame, DateTime now)
            => ForwardSequenceDistance(frame.Header.Sequence, _highestSequence) >= VoiceProtocol.JitterBufferDelayFrames ||
               (now - frame.ReceivedAtUtc).TotalMilliseconds >= VoiceProtocol.MaxJitterBufferDelayMilliseconds;

        private int IndexOfSequence(int sequence)
        {
            for (int i = 0; i < _pending.Count; i++)
                if (_pending[i].Header.Sequence == sequence)
                    return i;
            return -1;
        }

        private int GetOldestPendingIndex()
        {
            if (_pending.Count == 0) return -1;
            if (!_nextSequence.HasValue) return 0;

            int bestIndex = 0;
            uint bestDistance = uint.MaxValue;
            for (int i = 0; i < _pending.Count; i++)
            {
                int key = _pending[i].Header.Sequence;
                uint distance = ForwardSequenceDistance(_nextSequence.Value, key);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                bestIndex = i;
            }

            return bestIndex;
        }

        private static bool IsSequenceAhead(int sequence, int reference)
            => ForwardSequenceDistance(reference, sequence) < int.MaxValue;

        private static bool IsSequenceBehind(int sequence, int reference)
            => ForwardSequenceDistance(reference, sequence) > int.MaxValue;

        private static uint ForwardSequenceDistance(int from, int to)
            => unchecked((uint)to - (uint)from);
    }

    private readonly Dictionary<int, (byte PlayerId, string PlayerName)> _pendingProfiles = new();

    private void ProcessProfileUpdate(int senderId, byte playerId, string playerName)
        => ApplyObservedProfile(senderId, playerId, playerName, log: true);

    private void ApplyObservedProfile(int senderId, byte playerId, string playerName, bool log = false)
    {
        if (playerId == byte.MaxValue) return;

        string oldProfile = _clients.TryGetValue(senderId, out var existingPlayer)
            ? $"player={existingPlayer.PlayerId} name=\"{LogSafe(existingPlayer.PlayerName)}\" mapped={existingPlayer.IsMapped}"
            : _pendingProfiles.TryGetValue(senderId, out var existingPending)
                ? $"pendingPlayer={existingPending.PlayerId} pendingName=\"{LogSafe(existingPending.PlayerName)}\""
                : "none";

        VoiceClientRegistry.MarkProfile(senderId, playerId, playerName);

        if (_clients.TryGetValue(senderId, out var player))
        {
            if (!log && player.PlayerId == playerId && player.PlayerName == playerName)
                return;

            player.UpdateProfile(playerId, playerName);
            if (log)
            {
                VoiceChatPluginMain.Logger.LogInfo($"[VC] Client {senderId}: id={playerId} name={playerName}");
                VoiceDiagnostics.Log("profile.apply", $"sender={senderId} player={playerId} name=\"{playerName}\"");
            }
        }
        else
        {
            if (!log && _pendingProfiles.TryGetValue(senderId, out var pending) &&
                pending.PlayerId == playerId && pending.PlayerName == playerName)
                return;

            _pendingProfiles[senderId] = (playerId, playerName);
            if (log)
            {
                VoiceChatPluginMain.Logger.LogInfo(
                    $"[VC] Buffered profile for future client {senderId}: id={playerId} name={playerName}");
                VoiceDiagnostics.Log("profile.buffer", $"sender={senderId} player={playerId} name=\"{playerName}\"");
            }
        }

        if (IsTransitionTraceActive)
            VoiceDiagnostics.Log("transition.profile.observed",
                $"sender={senderId} observedPlayer={playerId} observedName=\"{LogSafe(playerName)}\" old={oldProfile} logSource={(log ? "profile-rpc" : "audio-rpc")} registry=\"{LogSafe(VoiceClientRegistry.Describe(senderId))}\"");
    }

    private void PruneDisconnectedClients()
    {
        if (AmongUsClient.Instance == null) return;
        List<int>? toRemove = null;
        foreach (var id in _clients.Keys)
        {
            bool alive = false;
            foreach (var cl in AmongUsClient.Instance.allClients)
                if (cl.Id == id) { alive = true; break; }
            if (!alive) (toRemove ??= new()).Add(id);
        }
        if (toRemove == null) return;
        foreach (var id in toRemove)
        {
            _clients.Remove(id);
            _decoders.Remove(id);
            _decodeBufs.Remove(id);
            _lastSeq.Remove(id);
            _jitterBuffers.Remove(id);
            _pendingProfiles.Remove(id);
            _transport.PruneSender(id);
            _audioManager.Remove(id);
            VoiceChatPluginMain.Logger.LogInfo($"[VC] Client {id} pruned.");
        }
    }

    private static bool CheckCommsSabotage()
    {
        if (ShipStatus.Instance == null) return false;
        foreach (var sys in ShipStatus.Instance.Systems.Values)
        {
            var hud = sys.TryCast<HudOverrideSystemType>();
            if (hud != null && hud.IsActive) return true;
        }
        return false;
    }

    public void Rejoin()
    {
        _transport.Clear();
        foreach (var id in _clients.Keys.ToList())
        {
            _audioManager.Remove(id);
            _decoders.Remove(id);
            _decodeBufs.Remove(id);
            _lastSeq.Remove(id);
            _jitterBuffers.Remove(id);
        }
        _clients.Clear();
        _decoders.Clear();
        _decodeBufs.Clear();
        _lastSeq.Clear();
        _jitterBuffers.Clear();
        _jitterFlushIds.Clear();
        _pendingProfiles.Clear();
        lock (_micStateLock)
        {
            _micAccumCount = 0;
            _speechSendHangoverFrames = 0;
        }
        _lastId   = byte.MaxValue;
        _lastName = null!;
        _handshakeTimer = 0f;
        _lastCompatibilityRefreshTime = -999f;
        CurrentSnapshot = null;
        _snapshotRefreshTimer = 0f;
        _routeRefreshTimer = 0f;
        _lastRouteClientCount = -1;
        _bootstrapUntilTime = -999f;
        _bootstrapRefreshTimer = 0f;
        ResetTransitionTraceState();
        VoiceChatPluginMain.Logger.LogInfo("[VC] Rejoin: state cleared.");
        VoiceDiagnostics.Log("room.rejoin", "state cleared");
        VoiceClientRegistry.Reset();
    }

    public void Close()
    {
#if WINDOWS
        try { _waveIn?.StopRecording(); } catch { }
        try { _waveIn?.Dispose(); } catch { }
        _waveIn = null;
#elif ANDROID
        try { _androidMic?.Stop(); } catch { }
        try { _androidMic?.Dispose(); } catch { }
        _androidMic = null;
        try { _androidSpeaker?.Dispose(); } catch { }
        _androidSpeaker = null;
#endif
        _transport.Clear();
#if WINDOWS
        try { _waveOut?.Stop(); _waveOut?.Dispose(); } catch { }
        _waveOut = null;
        _currentSpeakerDevice = "";
#endif
        lock (_micStateLock)
        {
            _encoder?.Dispose();
            _encoder = null;
            _micAccumCount = 0;
            _speechSendHangoverFrames = 0;
        }
        _clients.Clear();
        _decoders.Clear();
        _decodeBufs.Clear();
        _lastSeq.Clear();
        _jitterBuffers.Clear();
        _jitterFlushIds.Clear();
        _pendingProfiles.Clear();
        _handshakeTimer = 0f;
        _lastCompatibilityRefreshTime = -999f;
        CurrentSnapshot = null;
        _snapshotRefreshTimer = 0f;
        _routeRefreshTimer = 0f;
        _lastRouteClientCount = -1;
        _bootstrapUntilTime = -999f;
        _bootstrapRefreshTimer = 0f;
        ResetTransitionTraceState();
        VoiceDiagnostics.Log("room.close", "state cleared");
        VoiceClientRegistry.Reset();
    }

    public bool TryGetPlayer(byte playerId, out VCPlayer? player)
    {
        foreach (var c in _clients.Values)
            if (c.PlayerId == playerId) { player = c; return true; }
        player = null;
        return false;
    }

    private void TryUpdateLocalProfile()  => UpdateLocalProfile(false);
    internal void ForceUpdateLocalProfile() => UpdateLocalProfile(true);

    private void UpdateLocalProfile(bool always)
    {
        var lp = PlayerControl.LocalPlayer;
        if (!lp) return;
        if (!always && lp.PlayerId == _lastId && lp.name == _lastName) return;

        _lastId   = lp.PlayerId;
        _lastName = lp.name;

        try
        {
            if (AmongUsClient.Instance == null) return;
            var w = AmongUsClient.Instance.StartRpcImmediately(
                PlayerControl.LocalPlayer.NetId, AudioRpcId, SendOption.Reliable, -1);
            w.Write((byte)VoicePacketType.Profile);
            w.Write(_lastId);
            w.Write(TrimProfileName(_lastName));
            AmongUsClient.Instance.FinishRpcImmediately(w);
        }
        catch (Exception ex)
        {
            VoiceChatPluginMain.Logger.LogError($"[VC] Profile broadcast error: {ex.Message}");
        }
    }

    private void TrySendCompatibilityHandshake()
    {
        _handshakeTimer -= Time.deltaTime;
        if (_handshakeTimer > 0f) return;

        SendCompatibilityHandshake("periodic");
    }

    internal void ForceCompatibilityRefresh(string reason)
    {
        StartBootstrapWindow(reason);
        StartTransitionTrace($"compat refresh: {reason}", CurrentSnapshot);
        RefreshCompatibilityNow(reason, throttle: false);
    }

    private void StartBootstrapWindow(string reason)
    {
        _bootstrapUntilTime = Math.Max(_bootstrapUntilTime, Time.time + BootstrapWindowSeconds);
        _bootstrapRefreshTimer = 0f;
        VoiceDiagnostics.Log("bootstrap.start", reason);
    }

    private void TryRunBootstrapRefresh()
    {
        if (Time.time > _bootstrapUntilTime) return;

        _bootstrapRefreshTimer -= Time.deltaTime;
        if (_bootstrapRefreshTimer > 0f) return;

        _bootstrapRefreshTimer = BootstrapRefreshInterval;
        ForceUpdateLocalProfile();
        SendCompatibilityHandshake("bootstrap refresh");
    }

    private void RefreshCompatibilityNow(string reason, bool throttle)
    {
        if (throttle && Time.time - _lastCompatibilityRefreshTime < 0.25f)
            return;

        _lastCompatibilityRefreshTime = Time.time;
        ForceUpdateLocalProfile();
        SendCompatibilityHandshake(reason);
    }

    private void SendCompatibilityHandshake(string reason)
    {
        _handshakeTimer = 3f;
        _lastCompatibilityRefreshTime = Time.time;
        VoiceClientRegistry.PruneDisconnectedClients();
        ModdedRoomManager.SendHandshake();
        VoiceDiagnostics.Log("handshake.send", reason);
    }

    private bool IsTransitionTraceActive => DateTime.UtcNow <= _transitionTraceUntilUtc;

    private void StartTransitionTrace(string reason, VoiceGameStateSnapshot? snapshot)
    {
        _transitionTraceUntilUtc = DateTime.UtcNow.AddSeconds(TransitionTraceSeconds);
        _transitionTraceStateTimer = 0f;
        _traceLocalEncodeFramesRemaining = TransitionTraceAudioFrames;
        _traceLocalSkipFramesRemaining = TransitionTraceAudioFrames;
        _traceSendFramesRemaining = TransitionTraceAudioFrames;
        _tracePerfEventsRemaining = TransitionTracePerfEvents;
        _traceReceiveFramesRemaining.Clear();
        _traceDecodeFramesRemaining.Clear();
        _traceJitterFramesRemaining.Clear();
        _lastRouteTraceKey.Clear();
        _lastTxFrameUtc = DateTime.MinValue;
        _lastRxPacketUtc.Clear();
        _lastDecodeUtc.Clear();
        _lastDecodeTailSample.Clear();

        VoiceDiagnostics.Log("transition.trace.start",
            $"reason=\"{LogSafe(reason)}\" duration={TransitionTraceSeconds:0.0}s liveClients=[{DescribeLiveClients()}] snapshot=\"{LogSafe(DescribeTransitionSnapshot(snapshot))}\"");
        LogDetailedGameState(snapshot);
    }

    private void ResetTransitionTraceState()
    {
        _transitionTraceUntilUtc = DateTime.MinValue;
        _transitionTraceStateTimer = 0f;
        _traceLocalEncodeFramesRemaining = 0;
        _traceLocalSkipFramesRemaining = 0;
        _traceSendFramesRemaining = 0;
        _tracePerfEventsRemaining = 0;
        _traceReceiveFramesRemaining.Clear();
        _traceDecodeFramesRemaining.Clear();
        _traceJitterFramesRemaining.Clear();
        _lastRouteTraceKey.Clear();
        _lastTxFrameUtc = DateTime.MinValue;
        _lastRxPacketUtc.Clear();
        _lastDecodeUtc.Clear();
        _lastDecodeTailSample.Clear();
        _haveTracePhase = false;
        _lastTracePhase = VoiceGamePhase.Unknown;
    }

    private void TrackTransitionPhase(VoiceGameStateSnapshot? snapshot)
    {
        var phase = snapshot?.Phase ?? VoiceGamePhase.Unknown;
        if (!_haveTracePhase)
        {
            _haveTracePhase = true;
            _lastTracePhase = phase;
            VoiceDiagnostics.Log("transition.phase.initial", $"phase={phase} snapshot=\"{LogSafe(DescribeTransitionSnapshot(snapshot))}\"");
            return;
        }

        if (phase == _lastTracePhase) return;

        var previous = _lastTracePhase;
        _lastTracePhase = phase;
        StartTransitionTrace($"phase {previous}->{phase}", snapshot);
    }

    private void MaybeLogTransitionTraceState(VoiceGameStateSnapshot? snapshot)
    {
        if (!IsTransitionTraceActive) return;

        _transitionTraceStateTimer -= Time.deltaTime;
        if (_transitionTraceStateTimer > 0f) return;

        _transitionTraceStateTimer = TransitionTraceStateInterval;
        VoiceDiagnostics.Log("transition.state",
            $"remaining={(_transitionTraceUntilUtc - DateTime.UtcNow).TotalSeconds:0.000}s " +
            $"liveClients=[{DescribeLiveClients()}] registry=[{DescribeRegistryState()}] " +
            $"queues=send:{_transport.OutgoingCount} recvClients={_clients.Count} jitter={_jitterBuffers.Count} pendingProfiles={_pendingProfiles.Count} " +
            $"micLevel={LocalMicLevel:0.000} micSpeaking={LocalMicSpeaking} mute={Mute} speakerMuted={VoiceChatHudState.IsSpeakerMuted} " +
            $"snapshot=\"{LogSafe(DescribeTransitionSnapshot(snapshot))}\"");
        LogDetailedGameState(snapshot);
    }

    private void TraceUpdateCost(long startTicks, string completedStep, VoiceGameStateSnapshot? snapshot)
    {
        if (!IsTransitionTraceActive) return;

        double elapsedMs = ElapsedMilliseconds(startTicks);
        bool slow = elapsedMs >= SlowUpdateLogThresholdMs;
        if (!slow && _tracePerfEventsRemaining <= 0) return;
        if (!slow) _tracePerfEventsRemaining--;

        VoiceDiagnostics.Log(slow ? "transition.perf.slowUpdate" : "transition.perf.update",
            $"elapsedMs={elapsedMs:0.000} completedStep={completedStep} phase={snapshot?.Phase.ToString() ?? "none"} " +
            $"clients={_clients.Count} jitter={_jitterBuffers.Count} sendQueue={_transport.OutgoingCount} pendingProfiles={_pendingProfiles.Count} " +
            $"micLevel={LocalMicLevel:0.000} speaking={LocalMicSpeaking} mute={Mute}");
    }

    private void TraceOperationCost(string category, string message, double elapsedMs)
    {
        if (!IsTransitionTraceActive) return;
        if (elapsedMs < SlowOperationLogThresholdMs && _tracePerfEventsRemaining <= 0) return;
        if (elapsedMs < SlowOperationLogThresholdMs) _tracePerfEventsRemaining--;

        VoiceDiagnostics.Log(category, message);
    }

    private static double ElapsedMilliseconds(long startTicks)
        => (System.Diagnostics.Stopwatch.GetTimestamp() - startTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

    private static PcmStats InspectPcm(float[] samples, int count)
    {
        int limit = Math.Min(count, samples.Length);
        if (limit <= 0) return default;

        double sumSquares = 0;
        float peak = 0f;
        int zeroSamples = 0;
        int clippedSamples = 0;
        for (int i = 0; i < limit; i++)
        {
            float sample = samples[i];
            float abs = sample < 0f ? -sample : sample;
            if (abs > peak) peak = abs;
            if (abs <= 0.00001f) zeroSamples++;
            if (abs >= 0.98f) clippedSamples++;
            sumSquares += sample * sample;
        }

        return new PcmStats(
            peak,
            (float)Math.Sqrt(sumSquares / limit),
            zeroSamples / (float)limit,
            clippedSamples,
            samples[0],
            samples[limit - 1]);
    }

    private readonly record struct PcmStats(float Peak, float Rms, float ZeroRatio, int ClippedSamples, float FirstSample, float LastSample);

    private void TraceLocalAudioDecision(bool transmit, int sequence, int encodedBytes, int samples, float peak, float threshold, string reason)
    {
        if (!IsTransitionTraceActive) return;

        if (transmit)
        {
            if (_traceLocalEncodeFramesRemaining <= 0) return;
            _traceLocalEncodeFramesRemaining--;
        }
        else
        {
            if (_traceLocalSkipFramesRemaining <= 0) return;
            _traceLocalSkipFramesRemaining--;
        }

        double txDeltaMs = -1;
        if (transmit)
        {
            var now = DateTime.UtcNow;
            if (_lastTxFrameUtc != DateTime.MinValue)
                txDeltaMs = (now - _lastTxFrameUtc).TotalMilliseconds;
            _lastTxFrameUtc = now;
        }

        VoiceDiagnostics.Log(transmit ? "transition.tx.encode" : "transition.tx.skip",
            $"seq={sequence} transmit={transmit} reason={reason} samples={samples} peak={peak:0.00000} threshold={threshold:0.00000} " +
            $"encodedBytes={encodedBytes} txDeltaMs={txDeltaMs:0.000} sendQueue={_transport.OutgoingCount} mute={Mute} micLevel={LocalMicLevel:0.000} speaking={LocalMicSpeaking} phase={CurrentSnapshot?.Phase.ToString() ?? "none"}");
    }

    private void TraceNoAudioTargets()
    {
        if (!IsTransitionTraceActive) return;

        VoiceDiagnostics.Log("transition.tx.noTargets",
            $"sendQueue={_transport.OutgoingCount} liveClients=[{DescribeLiveClients()}] registry=[{DescribeRegistryState()}] " +
            $"allCompatible={VoiceClientRegistry.AreAllLiveRemoteClientsCompatible()} hasKnownIncompatible={VoiceClientRegistry.HasKnownIncompatibleLiveRemoteClients()}");
    }

    private void TraceSendAudioPayload(int targetClientId, byte[] payload, SendOption sendOption, int frameCount)
    {
        if (!IsTransitionTraceActive || _traceSendFramesRemaining <= 0) return;
        _traceSendFramesRemaining = Math.Max(0, _traceSendFramesRemaining - frameCount);

        VoiceFrameHeader.TryRead(payload, out var header, out _, out int encodedCount);
        VoiceDiagnostics.Log("transition.tx.rpc",
            $"target={targetClientId} option={sendOption} frameCount={frameCount} payloadBytes={payload.Length} firstSeq={header.Sequence} flags={header.Flags} encodedBytes={encodedCount} " +
            $"sendQueue={_transport.OutgoingCount} liveClients=[{DescribeLiveClients()}] registry=[{DescribeRegistryState()}]");
    }

    private void TraceReceiveDrop(VoiceIncomingPacket packet, string reason)
    {
        if (!IsTransitionTraceActive) return;

        VoiceDiagnostics.Log("transition.rx.drop",
            $"reason={reason} sender={packet.SenderId} type={packet.PacketType} observedPlayer={packet.PlayerId} observedName=\"{LogSafe(packet.PlayerName)}\" age={GetFrameAgeSeconds(packet.ReceivedAtUtc):0.000}s bytes={packet.Data.Length}");
    }

    private void TraceReceiveAudioFrame(int senderId, byte observedPlayerId, string observedPlayerName, VoiceFrameHeader header, int payloadBytes, int encodedBytes, bool bundled)
    {
        if (!ShouldTraceSender(_traceReceiveFramesRemaining, senderId)) return;

        var now = DateTime.UtcNow;
        double rxDeltaMs = -1;
        if (_lastRxPacketUtc.TryGetValue(senderId, out var lastRxUtc))
            rxDeltaMs = (now - lastRxUtc).TotalMilliseconds;
        _lastRxPacketUtc[senderId] = now;

        VoiceDiagnostics.Log("transition.rx.frame",
            $"sender={senderId} observedPlayer={observedPlayerId} observedName=\"{LogSafe(observedPlayerName)}\" seq={header.Sequence} flags={header.Flags} bundled={bundled} " +
            $"rxDeltaMs={rxDeltaMs:0.000} payloadBytes={payloadBytes} encodedBytes={encodedBytes} registry=\"{LogSafe(VoiceClientRegistry.Describe(senderId))}\" pendingProfile={_pendingProfiles.ContainsKey(senderId)} hasClient={_clients.ContainsKey(senderId)}");
    }

    private void TraceJitterState(int senderId, BufferedVoiceFrame frame, SenderJitterBuffer jitterBuffer, string action)
    {
        if (!ShouldTraceSender(_traceJitterFramesRemaining, senderId)) return;

        VoiceDiagnostics.Log("transition.jitter.state",
            $"sender={senderId} action={action} seq={frame.Header.Sequence} pending={jitterBuffer.PendingCount} next={jitterBuffer.NextSequence?.ToString() ?? "none"} highest={jitterBuffer.HighestSequence} age={GetFrameAgeSeconds(frame.ReceivedAtUtc):0.000}s");
    }

    private void TraceJitterWait(int senderId, SenderJitterBuffer jitterBuffer)
    {
        if (!ShouldTraceSender(_traceJitterFramesRemaining, senderId)) return;

        VoiceDiagnostics.Log("transition.jitter.wait",
            $"sender={senderId} pending={jitterBuffer.PendingCount} next={jitterBuffer.NextSequence?.ToString() ?? "none"} highest={jitterBuffer.HighestSequence}");
    }

    private void TraceDecodeAudio(
        int senderId,
        VCPlayer player,
        VoiceFrameHeader header,
        BufferedVoiceFrame frame,
        int decodedSamples,
        double setupMs,
        double decodeMs,
        double addMs,
        double totalMs,
        int bufferBefore,
        int bufferAfter,
        PcmStats stats,
        double decodeDeltaMs,
        float discontinuity)
    {
        if (!ShouldTraceSender(_traceDecodeFramesRemaining, senderId)) return;

        var route = player.CurrentRoute;
        VoiceDiagnostics.Log("transition.decode.ok",
            $"sender={senderId} seq={header.Sequence} encodedBytes={frame.EncodedCount} decodedSamples={decodedSamples} age={GetFrameAgeSeconds(frame.ReceivedAtUtc):0.000}s " +
            $"setupMs={setupMs:0.000} decodeMs={decodeMs:0.000} addMs={addMs:0.000} totalMs={totalMs:0.000} decodeDeltaMs={decodeDeltaMs:0.000} " +
            $"pcmPeak={stats.Peak:0.00000} pcmRms={stats.Rms:0.00000} zeroRatio={stats.ZeroRatio:0.000} clipped={stats.ClippedSamples} first={stats.FirstSample:0.00000} last={stats.LastSample:0.00000} discontinuity={discontinuity:0.00000} " +
            $"mapped={player.IsMapped} player={player.PlayerId} name=\"{LogSafe(player.PlayerName)}\" bufferBefore={bufferBefore} bufferAfter={bufferAfter} speaking={player.IsSpeaking} level={player.Level:0.000} " +
            $"routeAudible={route.Audible} routeReason={route.Reason} normal={route.NormalVolume:0.000} ghost={route.GhostVolume:0.000} radio={route.RadioVolume:0.000} registry=\"{LogSafe(VoiceClientRegistry.Describe(senderId))}\"");
    }

    private bool ShouldTraceSender(Dictionary<int, int> remainingBySender, int senderId)
    {
        if (!IsTransitionTraceActive) return false;
        if (!remainingBySender.TryGetValue(senderId, out int remaining))
            remaining = TransitionTraceAudioFrames;
        if (remaining <= 0) return false;
        remainingBySender[senderId] = remaining - 1;
        return true;
    }

    private void TraceRouteChange(int senderId, VCPlayer client, VoiceGameStateSnapshot snapshot)
    {
        if (!IsTransitionTraceActive) return;

        var route = client.CurrentRoute;
        string key = $"{client.PlayerId}|{client.IsMapped}|{route.Audible}|{route.Reason}|{route.NormalVolume:0.000}|{route.GhostVolume:0.000}|{route.RadioVolume:0.000}|{client.BufferedSamples > 0}|{client.IsSpeaking}";
        if (_lastRouteTraceKey.TryGetValue(senderId, out var previous) && previous == key)
            return;

        _lastRouteTraceKey[senderId] = key;

        VoicePlayerSnapshot target = default;
        bool byClient = senderId >= 0 && snapshot.TryGetClient(senderId, out target);
        bool byFallbackPlayer = false;
        int fallbackPlayerId = senderId - 1000;
        if (!byClient && fallbackPlayerId >= 0 && fallbackPlayerId <= byte.MaxValue)
            byFallbackPlayer = snapshot.TryGetPlayer((byte)fallbackPlayerId, out target);
        bool byProfilePlayer = false;
        if (!byClient && !byFallbackPlayer && client.PlayerId != byte.MaxValue)
            byProfilePlayer = snapshot.TryGetPlayer(client.PlayerId, out target);

        VoiceDiagnostics.Log("transition.route.change",
            $"sender={senderId} resolvedBy={(byClient ? "client" : byFallbackPlayer ? "fallback-player" : byProfilePlayer ? "profile-player" : "none")} " +
            $"mapped={client.IsMapped} profilePlayer={client.PlayerId} profileName=\"{LogSafe(client.PlayerName)}\" target={DescribePlayer((byClient || byFallbackPlayer || byProfilePlayer) ? target : null)} " +
            $"audible={route.Audible} reason={route.Reason} filter={route.FilterMode} normal={route.NormalVolume:0.000} ghost={route.GhostVolume:0.000} radio={route.RadioVolume:0.000} pan={route.Pan:0.000} " +
            $"buffer={client.BufferedSamples} speaking={client.IsSpeaking} level={client.Level:0.000} registry=\"{LogSafe(VoiceClientRegistry.Describe(senderId))}\"");
    }

    private string DescribeTransitionSnapshot(VoiceGameStateSnapshot? snapshot)
    {
        if (snapshot == null) return "snapshot=none";

        string players = snapshot.Players.Count == 0
            ? "none"
            : string.Join(";", snapshot.Players.Select(p =>
                $"p={p.PlayerId}/c={p.ClientId}/name={LogSafe(p.PlayerName)}/pos={FormatVector(p.Position)}/local={p.IsLocal}/dead={p.IsDead}/disc={p.Disconnected}/vis={p.IsVisible}"));

        return $"phase={snapshot.Phase} map={snapshot.MapId} localClient={snapshot.LocalClientId} localPlayer={snapshot.LocalPlayerId} localPos={FormatVector(snapshot.LocalPosition)} meeting={snapshot.MeetingActive} comms={snapshot.CommsSabotageActive} players=[{players}]";
    }

    private string DescribeRegistryState()
    {
        var ids = new HashSet<int>();
        try
        {
            if (AmongUsClient.Instance != null)
            {
                foreach (var client in AmongUsClient.Instance.allClients)
                    ids.Add(client.Id);
            }
        }
        catch { /* diagnostics only */ }

        foreach (var id in _clients.Keys)
            ids.Add(id);
        foreach (var id in _pendingProfiles.Keys)
            ids.Add(id);

        return ids.Count == 0
            ? "none"
            : string.Join("; ", ids.OrderBy(id => id).Select(id => LogSafe(VoiceClientRegistry.Describe(id))));
    }

    private static string DescribeLiveClients()
    {
        try
        {
            if (AmongUsClient.Instance == null) return "none";
            var ids = new List<int>();
            foreach (var client in AmongUsClient.Instance.allClients)
                ids.Add(client.Id);
            ids.Sort();
            return string.Join(",", ids);
        }
        catch (Exception ex)
        {
            return $"error:{ex.GetType().Name}";
        }
    }

    private void MaybeLogNetworkStats()
    {
        var settings = LocalSettingsTabSingleton<VoiceChatLocalSettings>.Instance;
        bool debugEnabled = settings?.DebugVoiceStats.Value == true;
        _transport.MaybeLogNetworkStats(debugEnabled);
        if (debugEnabled)
        {
            MaybeLogVoiceDiagnostics();
            MaybeLogDebugState();
        }
    }

    private void MaybeLogVoiceDiagnostics()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastDiagnosticSummaryUtc).TotalSeconds < 1.0)
            return;

        _lastDiagnosticSummaryUtc = now;
        string clients = _clients.Count == 0
            ? "none"
            : string.Join("; ", _clients.Select(kv =>
                $"sender={kv.Key} pid={kv.Value.PlayerId} name=\"{kv.Value.PlayerName}\" " +
                $"mapped={kv.Value.IsMapped} speaking={kv.Value.IsSpeaking} " +
                $"level={kv.Value.Level:0.000} vol={kv.Value.Volume:0.00} " +
                $"buffer={kv.Value.BufferedSamples} route={kv.Value.CurrentRoute.Reason} " +
                $"audioStats=\"{kv.Value.ConsumeAudioDebugStats()}\""));

        VoiceDiagnostics.Log("summary",
            $"region=\"{DescribeCurrentRegion()}\" sendOption={GetAudioSendOption()} " +
            $"queues=send:{_transport.OutgoingCount} " +
            $"micCallbacks={DiagTake(ref _diagMicCallbacks)} micSamples={DiagTake(ref _diagMicSamples)} " +
            $"micMutedCallbacks={DiagTake(ref _diagMicMutedCallbacks)} micDroppedSamples={DiagTake(ref _diagMicDroppedSamples)} " +
            $"encodedFrames={DiagTake(ref _diagEncodedFrames)} encodedBytes={DiagTake(ref _diagEncodedBytes)} " +
            $"sendRpcs={DiagTake(ref _diagSendRpcs)} sendFrames={DiagTake(ref _diagSendFrames)} sendBytes={DiagTake(ref _diagSendBytes)} " +
            $"bundledRpcs={DiagTake(ref _diagBundledRpcs)} bundledFrames={DiagTake(ref _diagBundledFrames)} noTargets={DiagTake(ref _diagNoTargets)} " +
            $"recvPackets={DiagTake(ref _diagReceivePackets)} recvBytes={DiagTake(ref _diagReceiveBytes)} " +
            $"recvBundles={DiagTake(ref _diagReceiveBundles)} recvBundleFrames={DiagTake(ref _diagReceiveBundleFrames)} " +
            $"staleDrops={DiagTake(ref _diagStaleDrops)} badPackets={DiagTake(ref _diagBadPackets)} jitterDrops={DiagTake(ref _diagJitterDrops)} " +
            $"sequenceGaps={DiagTake(ref _diagSequenceGaps)} plcFrames={DiagTake(ref _diagPlcFrames)} " +
            $"decodeErrors={DiagTake(ref _diagDecodeErrors)} decodedFrames={DiagTake(ref _diagDecodedFrames)} decodedSamples={DiagTake(ref _diagDecodedSamples)} " +
            $"localMicLevel={LocalMicLevel:0.000} localSpeaking={LocalMicSpeaking} mute={Mute} speakerMuted={VoiceChatHudState.IsSpeakerMuted} " +
            $"clients=[{clients}]");

        LogDetailedGameState(CurrentSnapshot);
    }

    private void LogDetailedGameState(VoiceGameStateSnapshot? snapshot)
    {
        if (snapshot == null)
        {
            VoiceDiagnostics.Log("state.local", "snapshot=none");
            return;
        }

        snapshot.TryGetLocalPlayer(out var local);
        bool localFound = snapshot.LocalPlayerId != byte.MaxValue;
        VoiceDiagnostics.Log("state.local",
            $"phase={snapshot.Phase} map={snapshot.MapId} localClient={snapshot.LocalClientId} localPlayer={snapshot.LocalPlayerId} " +
            $"localFound={localFound} local={DescribePlayer(localFound ? local : null)} " +
            $"localPos={FormatVector(snapshot.LocalPosition)} localLight={snapshot.LocalLightRadius:0.000} meeting={snapshot.MeetingActive} comms={snapshot.CommsSabotageActive} " +
            $"cameras={snapshot.CameraCount} closedDoors={snapshot.ClosedDoorCount} virtualMics={_virtualMics.Count} virtualSpeakers={_virtualSpeakers.Count} " +
            $"micMuted={Mute} speakerMuted={VoiceChatHudState.IsSpeakerMuted} radioHeld={VoiceChatHudState.IsImpostorRadio}");

        VoiceDiagnostics.Log("state.options", DescribeGameOptions());

        foreach (var kv in _clients)
            LogClientGameState(snapshot, localFound ? local : null, kv.Key, kv.Value);
    }

    private void LogClientGameState(
        VoiceGameStateSnapshot snapshot,
        VoicePlayerSnapshot? local,
        int senderId,
        VCPlayer client)
    {
        VoicePlayerSnapshot target = default;
        bool byClient = senderId >= 0 && snapshot.TryGetClient(senderId, out target);
        bool byPlayer = false;
        if (!byClient && client.PlayerId != byte.MaxValue)
            byPlayer = snapshot.TryGetPlayer(client.PlayerId, out target);

        bool targetFound = byClient || byPlayer;
        var route = client.CurrentRoute;

        var options = VoiceChatGameOptions.Instance;
        Vector2 cameraPos = default;
        bool cameraActive = targetFound
                            && options.CameraCanHear.Value
                            && VoiceAudioOcclusion.TryGetCameraListenerPosition(target.Position, out cameraPos);
        Vector2? listenerPos = cameraActive ? cameraPos : snapshot.LocalPosition;
        float rangeMultiplier = cameraActive ? 0.65f : 1f;
        float maxDistance = options.MaxChatDistance.Value * rangeMultiplier;
        if (options.OnlyHearInSight.Value && !cameraActive && snapshot.LocalLightRadius > 0f)
            maxDistance = Math.Min(maxDistance, snapshot.LocalLightRadius);

        string relation = "targetFound=false";
        if (targetFound && listenerPos.HasValue)
        {
            float distance = Vector2.Distance(listenerPos.Value, target.Position);
            float baseVolume = VoiceAudioOcclusion.ApplyFalloff(distance, maxDistance, (VoiceFalloffMode)options.FalloffMode.Value);
            var rawOcclusion = VoiceAudioOcclusion.Inspect(listenerPos.Value, target.Position);
            var optionOcclusion = VoiceAudioOcclusion.Evaluate(listenerPos.Value, target.Position, (VoiceOcclusionMode)options.OcclusionMode.Value);
            bool onlySightBlocked = options.OnlyHearInSight.Value && !rawOcclusion.InSight;
            bool wallOptionActive = options.WallsBlockSound.Value && optionOcclusion.IsOccluded;
            float pan = GetPan(listenerPos.Value.x, target.Position.x);

            relation =
                $"targetFound=true resolvedBy={(byClient ? "client" : "player")} distance={distance:0.000} maxDistance={maxDistance:0.000} baseVolume={baseVolume:0.000} panCalc={pan:0.000} " +
                $"listenerPos={FormatVector(listenerPos)} localLight={snapshot.LocalLightRadius:0.000} cameraActive={cameraActive} cameraPos={(cameraActive ? FormatVector(cameraPos) : "none")} " +
                $"inSight={rawOcclusion.InSight} wallRaw={rawOcclusion.HasWall} closedDoorRaw={rawOcclusion.HasClosedDoor} " +
                $"occludedByOption={optionOcclusion.IsOccluded} optionWall={optionOcclusion.HasWall} optionDoor={optionOcclusion.HasClosedDoor} " +
                $"optionVolumeMultiplier={optionOcclusion.TargetVolumeMultiplier:0.000} optionFilter={optionOcclusion.FilterMode} " +
                $"onlySightBlocked={onlySightBlocked} wallOptionActive={wallOptionActive}";
        }

        VoiceDiagnostics.Log("state.client",
            $"sender={senderId} mapped={client.IsMapped} registry=\"{LogSafe(VoiceClientRegistry.Describe(senderId))}\" " +
            $"profilePlayer={client.PlayerId} profileName=\"{LogSafe(client.PlayerName)}\" target={DescribePlayer(targetFound ? target : null)} " +
            $"local={DescribePlayer(local)} {relation} remoteRadioActive={VoiceClientRegistry.IsRadioActive(senderId)} " +
            $"routeAudible={route.Audible} routeReason={route.Reason} routeFilter={route.FilterMode} " +
            $"normal={route.NormalVolume:0.000} ghost={route.GhostVolume:0.000} radio={route.RadioVolume:0.000} routePan={route.Pan:0.000} wallCoeff={route.WallCoefficient:0.000} " +
            $"speaking={client.IsSpeaking} level={client.Level:0.000} playerVolume={client.Volume:0.000} buffer={client.BufferedSamples}");
    }

    private static string DescribeGameOptions()
    {
        var o = VoiceChatGameOptions.Instance;
        return
            $"publicLobby={o.PublicVoiceLobby.Value} maxDistance={o.MaxChatDistance.Value:0.000} falloff={(VoiceFalloffMode)o.FalloffMode.Value} occlusion={(VoiceOcclusionMode)o.OcclusionMode.Value} " +
            $"wallsBlock={o.WallsBlockSound.Value} onlySight={o.OnlyHearInSight.Value} cameraCanHear={o.CameraCanHear.Value} " +
            $"hearInVent={o.HearInVent.Value} ventPrivate={o.VentPrivateChat.Value} commsDisable={o.CommsSabDisables.Value} " +
            $"impHearGhosts={o.ImpostorHearGhosts.Value} impRadio={o.ImpostorPrivateRadio.Value} onlyGhosts={o.OnlyGhostsCanTalk.Value} onlyMeetingLobby={o.OnlyMeetingOrLobby.Value}";
    }

    private static string DescribePlayer(VoicePlayerSnapshot? player)
    {
        if (!player.HasValue) return "none";
        var p = player.Value;
        return
            $"id={p.PlayerId} client={p.ClientId} name=\"{LogSafe(p.PlayerName)}\" pos={FormatVector(p.Position)} local={p.IsLocal} dead={p.IsDead} imp={p.IsImpostor} " +
            $"vent={p.InVent} disconnected={p.Disconnected} dummy={p.IsDummy} visible={p.IsVisible}";
    }

    private static string LogSafe(string value)
        => (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Replace("\"", "'");

    private static string FormatVector(Vector2? value)
        => value.HasValue ? FormatVector(value.Value) : "none";

    private static string FormatVector(Vector2 value)
        => $"({value.x:0.000},{value.y:0.000})";

    private static void DiagInc(ref long value)
        => System.Threading.Interlocked.Increment(ref value);

    private static void DiagAdd(ref long value, long amount)
        => System.Threading.Interlocked.Add(ref value, amount);

    private static long DiagTake(ref long value)
        => System.Threading.Interlocked.Exchange(ref value, 0);

    private static string DescribeCurrentRegion()
    {
        try
        {
            var manager = DestroyableSingleton<ServerManager>.Instance;
            var region = manager?.CurrentRegion;
            if (region == null) return "none";
            var servers = region.Servers;
            if (servers == null || servers.Length == 0) return region.Name;
            var server = servers[0];
            return $"{region.Name} {server.Name} {server.Ip}:{server.Port} dtls={server.UseDtls}";
        }
        catch (Exception ex)
        {
            return $"region-error:{ex.GetType().Name}";
        }
    }

    private void MaybeLogDebugState()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastDebugStateLogUtc) < VoiceProtocol.StatsLogInterval)
            return;

        _lastDebugStateLogUtc = now;
        var snapshot = CurrentSnapshot;
        if (snapshot == null) return;

        int livePlayers = 0;
        int deadPlayers = 0;
        int ventPlayers = 0;
        foreach (var player in snapshot.Players)
        {
            if (player.Disconnected || player.IsDummy) continue;
            livePlayers++;
            if (player.IsDead) deadPlayers++;
            if (player.InVent) ventPlayers++;
        }

        var routeCounts = new Dictionary<VoiceProximityReason, int>();
        foreach (var client in _clients.Values)
        {
            var reason = client.CurrentRoute.Reason;
            routeCounts.TryGetValue(reason, out int count);
            routeCounts[reason] = count + 1;
        }

        string routes = routeCounts.Count == 0
            ? "none"
            : string.Join(", ", routeCounts.Select(kv => $"{kv.Key}:{kv.Value}"));

        VoiceChatPluginMain.Logger.LogInfo(
            $"[VC] State: phase={snapshot.Phase} map={snapshot.MapId} " +
            $"players={livePlayers} dead={deadPlayers} vent={ventPlayers} " +
            $"meeting={snapshot.MeetingActive} comms={snapshot.CommsSabotageActive} " +
            $"cameras={snapshot.CameraCount} closedDoors={snapshot.ClosedDoorCount} " +
            $"routes={routes}");
    }

    private static string TrimProfileName(string name)
    {
        if (string.IsNullOrEmpty(name)) return string.Empty;
        return name.Length <= VoiceProtocol.MaxProfileNameChars
            ? name
            : name[..VoiceProtocol.MaxProfileNameChars];
    }

    internal static float GetVolume(float dist, float maxDist)
        => Math.Clamp(1f - dist / maxDist, 0f, 1f);

    internal static float GetPan(float micX, float spkX)
        => Math.Clamp((spkX - micX) / 3f, -1f, 1f);

    internal record SpeakerCache(IVoiceComponent Speaker, float Volume, float Pan);

    // ======================================================================
    // RPC patch
    // ======================================================================

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.HandleRpc))]
    public static class AudioRpcPatch
    {
        [HarmonyPostfix]
        public static void Postfix(PlayerControl __instance, byte callId, MessageReader reader)
        {
            if (callId != AudioRpcId) return;
            if (Current == null) return;
            var localPlayer = PlayerControl.LocalPlayer;
            if (localPlayer != null && __instance.NetId == localPlayer.NetId) return;

            int senderId = -1;
            if (AmongUsClient.Instance != null)
            {
                var cl = AmongUsClient.Instance.GetClientFromCharacter(__instance);
                if (cl != null) senderId = cl.Id;
            }
            if (senderId < 0)
                senderId = __instance.PlayerId + 1000;

            try
            {
                if (reader.BytesRemaining < 1) return;
                byte packetType = reader.ReadByte();
                if (packetType == (byte)VoicePacketType.Audio)
                {
                    if (reader.BytesRemaining <= 0 ||
                        reader.BytesRemaining > VoiceProtocol.MaxAudioPayloadBytes + 16)
                    {
                        Current._transport.CountBadPacket();
                        return;
                    }

                    byte[] payload = reader.ReadBytesAndSize();
                    if (payload != null && VoiceProtocol.IsValidAudioPayloadLength(payload.Length))
                    {
                        string senderName = __instance.Data?.PlayerName ?? __instance.name ?? "";
                        if (Current.IsTransitionTraceActive)
                            VoiceDiagnostics.Log("transition.rx.rpc",
                                $"sender={senderId} observedPlayer={__instance.PlayerId} observedName=\"{LogSafe(senderName)}\" netId={__instance.NetId} payloadBytes={payload.Length} bytesRemaining={reader.BytesRemaining} registry=\"{LogSafe(VoiceClientRegistry.Describe(senderId))}\"");
                        EnqueueAudioPacket(senderId, __instance.PlayerId, senderName, payload);
                    }
                    else
                        Current._transport.CountBadPacket();
                }
                else if (packetType == (byte)VoicePacketType.Profile)
                {
                    if (reader.BytesRemaining < 1 ||
                        reader.BytesRemaining > VoiceProtocol.MaxProfileNameChars * 4 + 8)
                    {
                        Current._transport.CountBadPacket();
                        return;
                    }

                    byte   pid  = reader.ReadByte();
                    string name = reader.ReadString();
                    if (name.Length > VoiceProtocol.MaxProfileNameChars)
                        name = name[..VoiceProtocol.MaxProfileNameChars];
                    EnqueueProfilePacket(senderId, pid, name);
                }
                else if (packetType == (byte)VoicePacketType.JailVoice)
                {
                    if (reader.BytesRemaining < 2) return;
                    byte jailedPlayerId = reader.ReadByte();
                    bool allowed = reader.ReadBoolean();
                    VoiceRoleMuteState.ApplyRemoteJailVoice(__instance.PlayerId, jailedPlayerId, allowed);
                }
            }
            catch (Exception ex)
            {
                VoiceChatPluginMain.Logger.LogError($"[VC] RPC parse error: {ex.Message}");
            }
        }
    }
}
