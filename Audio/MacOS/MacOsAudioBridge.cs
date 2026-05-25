#if MACOS
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using BepInEx;
using VoiceChatPlugin.VoiceChat;

namespace VoiceChatPlugin.Audio;

internal static class MacOsAudioBridge
{
    private const string NativeFileName = "libperfectcomms_audio_macos.dylib";
    private const string ResourceName = "Lib.libperfectcomms_audio_macos.dylib";
    private static readonly object LoadLock = new();
    private static NativeApi? _api;
    private static IntPtr _nativeHandle;
    private static string _nativePath = string.Empty;
    private static string _loadError = string.Empty;

    public static bool IsLoaded => _api != null;
    public static string NativePath => _nativePath;
    public static string LastLoadError => _loadError;

    public static IReadOnlyList<MacOsAudioDeviceInfo> EnumerateInputs()
        => EnumerateDevices(input: true);

    public static IReadOnlyList<MacOsAudioDeviceInfo> EnumerateOutputs()
        => EnumerateDevices(input: false);

    public static MacOsAudioStartupResult StartFullDuplex(
        string backend,
        string inputSelection,
        string outputSelection,
        int sampleRate,
        int inputChannels,
        int outputChannels,
        CaptureCallback captureCallback,
        PlaybackCallback playbackCallback,
        IntPtr userData)
    {
        var inputId = ResolveDeviceId(inputSelection, input: true, out var inputError);
        var outputId = ResolveDeviceId(outputSelection, input: false, out var outputError);
        var inputHash = MacOsAudioDiagnostics.HashId(inputId);
        var outputHash = MacOsAudioDiagnostics.HashId(outputId);

        if (!string.IsNullOrEmpty(inputError))
            return MacOsAudioStartupResult.Fail("input-selection", "PC_AUDIO_ERROR_INPUT_NOT_FOUND", inputError, inputHash, outputHash);
        if (!string.IsNullOrEmpty(outputError))
            return MacOsAudioStartupResult.Fail("output-selection", "PC_AUDIO_ERROR_OUTPUT_NOT_FOUND", outputError, inputHash, outputHash);

        if (!TryLoad(out var api, out var loadError))
            return MacOsAudioStartupResult.Fail("native-load", "PC_AUDIO_ERROR_NATIVE_INIT", loadError, inputHash, outputHash);

        VoiceDiagnostics.Log("mac.audio.start",
            $"backend={backend} fullDuplexRequired=true sampleRate={sampleRate} inputChannels={inputChannels} outputChannels={outputChannels} inputIdHash={inputHash} outputIdHash={outputHash}");

        var result = api.StartFullDuplex(inputId, outputId, sampleRate, inputChannels, outputChannels, captureCallback, playbackCallback, userData);
        if (result != MacOsNativeResult.Ok)
            return MacOsAudioStartupResult.Fail(NativeStage(result), result.ToString(), GetLastError(), inputHash, outputHash);

        return MacOsAudioStartupResult.Ok(inputHash, outputHash, sampleRate, inputChannels, outputChannels);
    }

    public static void StopFullDuplex()
    {
        if (_api == null) return;
        try { _api.StopFullDuplex(); }
        catch (Exception ex) { VoiceDiagnostics.Log("mac.audio.stop", $"nativeStop=false error=\"{Sanitize(ex.Message)}\""); }
    }

    public static string GetLastError()
    {
        if (_api == null) return _loadError;
        var buffer = new byte[1024];
        var count = _api.GetLastError(buffer, buffer.Length);
        return DecodeUtf8(buffer, count);
    }

    public static string GetStatsJson()
    {
        if (_api == null) return "{}";
        var buffer = new byte[2048];
        var count = _api.GetStats(buffer, buffer.Length);
        return DecodeUtf8(buffer, count);
    }

    private static IReadOnlyList<MacOsAudioDeviceInfo> EnumerateDevices(bool input)
    {
        if (!TryLoad(out var api, out var error))
        {
            VoiceDiagnostics.Log("mac.devices", $"{(input ? "input" : "output")}Count=0 error=\"{Sanitize(error)}\"");
            return Array.Empty<MacOsAudioDeviceInfo>();
        }

        var count = input ? api.GetInputCount() : api.GetOutputCount();
        var devices = new List<MacOsAudioDeviceInfo>(Math.Max(0, count));
        for (var i = 0; i < count; i++)
        {
            devices.Add(new MacOsAudioDeviceInfo(
                ReadString((buffer, length) => input ? api.GetInputId(i, buffer, length) : api.GetOutputId(i, buffer, length)),
                ReadString((buffer, length) => input ? api.GetInputName(i, buffer, length) : api.GetOutputName(i, buffer, length)),
                i == 0,
                input ? 1 : 2,
                AudioHelpers.ClockRate,
                AudioHelpers.ClockRate));
        }

        MacOsAudioDiagnostics.LogDevices(input ? "input" : "output", devices);
        return devices;
    }

    private static string ResolveDeviceId(string selection, bool input, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(selection))
            return string.Empty;

        if (MacOsAudioDeviceSelection.TryDecode(selection, out var encodedId, out var encodedName))
        {
            var devices = input ? EnumerateInputs() : EnumerateOutputs();
            foreach (var device in devices)
            {
                if (string.Equals(device.Id, encodedId, StringComparison.Ordinal))
                    return encodedId;
            }

            error = $"{(input ? "Selected microphone" : "Selected speaker")} was not found: {encodedName}";
            return encodedId;
        }

        foreach (var device in input ? EnumerateInputs() : EnumerateOutputs())
        {
            if (string.Equals(device.Name, selection, StringComparison.OrdinalIgnoreCase))
                return device.Id;
        }

        error = $"{(input ? "Selected microphone" : "Selected speaker")} was not found: {selection}";
        return selection;
    }

    private static bool TryLoad(out NativeApi api, out string error)
    {
        lock (LoadLock)
        {
            if (_api != null)
            {
                api = _api;
                error = string.Empty;
                return true;
            }

            if (!string.IsNullOrEmpty(_loadError))
            {
                api = default!;
                error = _loadError;
                return false;
            }

            try
            {
                _nativePath = ExtractNativeLibrary();
                _nativeHandle = NativeLibrary.Load(_nativePath);
                _api = new NativeApi(
                    GetExport<AudioInit>("pc_audio_init"),
                    GetExport<AudioShutdown>("pc_audio_shutdown"),
                    GetExport<GetCount>("pc_audio_get_input_count"),
                    GetExport<GetCount>("pc_audio_get_output_count"),
                    GetExport<GetDeviceString>("pc_audio_get_input_name"),
                    GetExport<GetDeviceString>("pc_audio_get_output_name"),
                    GetExport<GetDeviceString>("pc_audio_get_input_id"),
                    GetExport<GetDeviceString>("pc_audio_get_output_id"),
                    GetExport<StartFullDuplexNative>("pc_audio_start_full_duplex"),
                    GetExport<StopFullDuplexNative>("pc_audio_stop_full_duplex"),
                    GetExport<GetStringNative>("pc_audio_get_last_error"),
                    GetExport<GetStringNative>("pc_audio_get_stats"));

                var init = _api.Init();
                if (init != MacOsNativeResult.Ok)
                    throw new InvalidOperationException($"pc_audio_init failed: {init}");

                api = _api;
                error = string.Empty;
                MacOsAudioDiagnostics.LogNativeLoad(ResourceName, _nativePath, loaded: true, error: string.Empty);
                return true;
            }
            catch (Exception ex)
            {
                _loadError = ex.Message;
                api = default!;
                error = _loadError;
                MacOsAudioDiagnostics.LogNativeLoad(ResourceName, _nativePath, loaded: false, error: _loadError);
                return false;
            }
        }
    }

    private static string ExtractNativeLibrary()
    {
        var root = Paths.BepInExRootPath;
        if (string.IsNullOrWhiteSpace(root))
            root = AppContext.BaseDirectory;

        var dir = Path.Combine(root, "cache", "PerfectComms", "native", "macos");
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, NativeFileName);

        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new FileNotFoundException($"Missing embedded resource {ResourceName}");

        if (File.Exists(target) && new FileInfo(target).Length == stream.Length)
            return target;

        var temp = target + ".tmp";
        using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            stream.CopyTo(output);

        File.Move(temp, target, true);
        return target;
    }

    private static T GetExport<T>(string name) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_nativeHandle, name));

    private static string ReadString(Func<byte[], int, int> reader)
    {
        var buffer = new byte[512];
        var count = reader(buffer, buffer.Length);
        return DecodeUtf8(buffer, count);
    }

    private static string DecodeUtf8(byte[] buffer, int count)
    {
        if (count <= 0) return string.Empty;
        count = Math.Min(count, buffer.Length);
        if (count > 0 && buffer[count - 1] == 0) count--;
        return Encoding.UTF8.GetString(buffer, 0, count);
    }

    private static string NativeStage(MacOsNativeResult result)
        => result switch
        {
            MacOsNativeResult.Permission => "permission",
            MacOsNativeResult.InputNotFound => "input-start",
            MacOsNativeResult.OutputNotFound => "output-start",
            MacOsNativeResult.InputStartFailed => "input-start",
            MacOsNativeResult.OutputStartFailed => "output-start",
            MacOsNativeResult.FormatUnsupported => "format",
            _ => "native-start"
        };

    private static string Sanitize(string? value)
        => (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Replace("\"", "'");

    public delegate void CaptureCallback(IntPtr samples, int frameCount, int channels, IntPtr userData);
    public delegate int PlaybackCallback(IntPtr samples, int frameCount, int channels, IntPtr userData);

    private enum MacOsNativeResult
    {
        Ok = 0,
        NativeInit = 1,
        Permission = 2,
        InputNotFound = 3,
        OutputNotFound = 4,
        InputStartFailed = 5,
        OutputStartFailed = 6,
        FormatUnsupported = 7
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate MacOsNativeResult AudioInit();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void AudioShutdown();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetCount();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetDeviceString(int index, byte[] buffer, int bufferLength);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate MacOsNativeResult StartFullDuplexNative(
        string inputId,
        string outputId,
        int sampleRate,
        int inputChannels,
        int outputChannels,
        CaptureCallback captureCallback,
        PlaybackCallback playbackCallback,
        IntPtr userData);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void StopFullDuplexNative();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetStringNative(byte[] buffer, int bufferLength);

    private sealed record NativeApi(
        AudioInit Init,
        AudioShutdown Shutdown,
        GetCount GetInputCount,
        GetCount GetOutputCount,
        GetDeviceString GetInputName,
        GetDeviceString GetOutputName,
        GetDeviceString GetInputId,
        GetDeviceString GetOutputId,
        StartFullDuplexNative StartFullDuplex,
        StopFullDuplexNative StopFullDuplex,
        GetStringNative GetLastError,
        GetStringNative GetStats);
}
#endif
