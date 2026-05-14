using System.Collections.Generic;
using VoiceChatPlugin.Audio;

using UnityEngine;
using System;

namespace VoiceChatPlugin.VoiceChat;

public class VCPlayer
{
    private readonly StereoRouter.Property     _imager;
    private readonly VolumeRouter.Property     _normalVolume, _ghostVolume, _radioVolume, _clientVolume;
    private readonly LevelMeterRouter.Property _levelMeter;
    private readonly SmoothedAudioValue _normalSmooth = new();
    private readonly SmoothedAudioValue _ghostSmooth = new();
    private readonly SmoothedAudioValue _radioSmooth = new();
    private readonly SmoothedAudioValue _panSmooth = new();
    private readonly VoiceActivityTracker _activity = new();

    private byte           _playerId   = byte.MaxValue;
    private string         _playerName = "Unknown";

    private readonly AudioRoutingInstance _instance;

    public string PlayerName => _playerName;
    public byte   PlayerId   => _playerId;
    public float  Volume     => _clientVolume.Volume;
    public float  Level      => _activity.Level;
    public bool   IsSpeaking => _activity.IsSpeaking;
    public bool   IsMapped   => _playerId != byte.MaxValue;
    public int    BufferedSamples => _instance.BufferedSamples;
    public string ConsumeAudioDebugStats() => _instance.ConsumeDebugStats();
    internal VoiceProximityResult CurrentRoute { get; private set; } =
        VoiceProximityResult.Muted(VoiceProximityReason.Unmapped);

    public VCPlayer(
        VoiceChatRoom        room,
        AudioRoutingInstance instance,
        StereoRouter         imager,
        VolumeRouter         normalVolume,
        VolumeRouter         ghostVolume,
        VolumeRouter         radioVolume,
        VolumeRouter         clientVolume,
        LevelMeterRouter     levelMeter)
    {
        _instance     = instance;
        _imager       = imager.GetProperty(instance);
        _normalVolume = normalVolume.GetProperty(instance);
        _ghostVolume  = ghostVolume.GetProperty(instance);
        _radioVolume  = radioVolume.GetProperty(instance);
        _clientVolume = clientVolume.GetProperty(instance);
        _levelMeter   = levelMeter.GetProperty(instance);
        _clientVolume.Volume = 1f;
    }

    internal void AddSamples(float[] samples, int count)
    {
        _activity.PushSamples(samples, count);
        _instance.AddSamples(samples, 0, count);
    }

    internal void TickVoiceActivity() => _activity.TickSilence();

    internal void ResetVoiceActivity() => _activity.Reset();

    internal void TickRouteSmoothing() => ApplyResult(CurrentRoute);

    internal void SetVadThreshold(float threshold) => _activity.VadThreshold = threshold;

    public void UpdateProfile(byte playerId, string playerName)
    {
        if (_playerId == playerId && _playerName == playerName)
            return;

        _playerId     = playerId;
        _playerName   = playerName;
        VoiceVolumeMenu.ApplySavedVolume(this);
        MuteAll();
    }

    public void ResetMapping()
    {
        _playerId = byte.MaxValue;
        MuteAll();
    }

    public void ResetMappingNoMute()
    {
        _playerId = byte.MaxValue;
    }

    private bool TryResolveFromSnapshot(VoiceGameStateSnapshot? snapshot, int clientId, out VoicePlayerSnapshot target)
    {
        if (snapshot != null)
        {
            if (clientId >= 0 && snapshot.TryGetClient(clientId, out target))
            {
                _playerId = target.PlayerId;
                _playerName = string.IsNullOrWhiteSpace(target.PlayerName) ? _playerName : target.PlayerName;
                VoiceVolumeMenu.ApplySavedVolume(this);
                return true;
            }

            int fallbackPlayerId = clientId - 1000;
            if (fallbackPlayerId >= 0 && fallbackPlayerId <= byte.MaxValue &&
                snapshot.TryGetPlayer((byte)fallbackPlayerId, out target))
            {
                _playerId = target.PlayerId;
                _playerName = string.IsNullOrWhiteSpace(target.PlayerName) ? _playerName : target.PlayerName;
                VoiceVolumeMenu.ApplySavedVolume(this);
                return true;
            }

            if (_playerId != byte.MaxValue && snapshot.TryGetPlayer(_playerId, out target))
            {
                _playerName = string.IsNullOrWhiteSpace(target.PlayerName) ? _playerName : target.PlayerName;
                VoiceVolumeMenu.ApplySavedVolume(this);
                return true;
            }
        }

        target = default;
        return false;
    }

    public void SetVolume(float v)
    {
        _clientVolume.Volume = v;
        if (v <= 0f)
            ClearBufferedSamples();
    }

    internal void ClearBufferedSamples() => _instance.ClearBufferedSamples();

    private void MuteAll()
    {
        ApplyResult(VoiceProximityResult.Muted(VoiceProximityReason.Unmapped, _wallCoeff));
    }

    private void ApplyResult(VoiceProximityResult result)
    {
        CurrentRoute = result;
        if (!result.Audible || _clientVolume.Volume <= 0f)
        {
            ClearBufferedSamples();
            _activity.Reset();
        }

        if (ShouldMuteImmediately(result))
        {
            _normalSmooth.SetImmediate(0f);
            _ghostSmooth.SetImmediate(0f);
            _radioSmooth.SetImmediate(0f);
            _normalVolume.Volume = 0f;
            _ghostVolume.Volume = 0f;
            _radioVolume.Volume = 0f;
        }
        else
        {
            _normalVolume.Volume = _normalSmooth.Step(result.NormalVolume, 12f);
            _ghostVolume.Volume = _ghostSmooth.Step(result.GhostVolume, 10f);
            _radioVolume.Volume = _radioSmooth.Step(result.RadioVolume, 14f);
        }

        _imager.Pan = _panSmooth.Step(result.Pan, 8f);
        _wallCoeff = result.WallCoefficient;
    }

    private static bool ShouldMuteImmediately(VoiceProximityResult result)
    {
        if (result.Audible) return false;

        return result.Reason is VoiceProximityReason.OnlyGhostsCanTalk
            or VoiceProximityReason.CommsSabotage
            or VoiceProximityReason.Blackmailed
            or VoiceProximityReason.Jailed
            or VoiceProximityReason.TargetDeadMuted
            or VoiceProximityReason.ImpostorRadioMuted
            or VoiceProximityReason.VentMuted
            or VoiceProximityReason.VentPrivateMuted
            or VoiceProximityReason.HardOcclusion
            or VoiceProximityReason.Unmapped
            or VoiceProximityReason.NoListener;
    }

    internal void UpdateLobby(VoiceGameStateSnapshot? snapshot, int clientId)
    {
        if (!TryResolveFromSnapshot(snapshot, clientId, out var target)) { MuteAll(); return; }
        ApplyResult(VoiceProximityCalculator.CalculateLobby(target, snapshot?.LocalPosition));
    }

    internal void UpdateMeeting(VoiceGameStateSnapshot? snapshot, int clientId, bool remoteRadioActive)
    {
        if (!TryResolveFromSnapshot(snapshot, clientId, out var target)) { MuteAll(); return; }
        VoicePlayerSnapshot? local = snapshot != null && snapshot.TryGetLocalPlayer(out var localPlayer)
            ? localPlayer
            : null;

        ApplyResult(VoiceProximityCalculator.CalculateMeeting(local, target, remoteRadioActive));
    }

    private float _wallCoeff = 1f;

    internal void UpdateTaskPhase(
        VoiceGameStateSnapshot? snapshot,
        int clientId,
        Vector2? listenerPos,
        IEnumerable<VoiceChatRoom.SpeakerCache> speakers,
        IEnumerable<IVoiceComponent> virtualMics,
        bool localInVent,
        bool remoteRadioActive,
        bool commsSabActive)
    {
        if (!TryResolveFromSnapshot(snapshot, clientId, out var target) || !listenerPos.HasValue) { MuteAll(); return; }
        VoicePlayerSnapshot? local = snapshot != null && snapshot.TryGetLocalPlayer(out var localPlayer)
            ? localPlayer
            : null;

        ApplyResult(VoiceProximityCalculator.CalculateTaskPhase(
            local,
            target,
            listenerPos,
            snapshot?.LocalLightRadius ?? -1f,
            speakers,
            virtualMics,
            localInVent,
            remoteRadioActive,
            commsSabActive,
            _wallCoeff));
    }
}
