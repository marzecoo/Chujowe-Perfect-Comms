#if MACOS
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using VoiceChatPlugin.VoiceChat;

namespace VoiceChatPlugin.Audio;

internal static class MacOsAudioDiagnostics
{
    public static void LogPlatform(string pluginVersion)
    {
        VoiceDiagnostics.Log("mac.platform",
            $"os=\"{Sanitize(Environment.OSVersion.VersionString)}\" arch={System.Runtime.InteropServices.RuntimeInformation.OSArchitecture} processArch={System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture} runtime=\"{Sanitize(System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription)}\" plugin=PerfectCommsmacOS.dll version={pluginVersion}");
    }

    public static void LogNativeLoad(string resource, string path, bool loaded, string error)
    {
        VoiceDiagnostics.Log("mac.native",
            $"resource={resource} extractPath=\"{Sanitize(path)}\" load={loaded.ToString().ToLowerInvariant()} error=\"{Sanitize(error)}\"");
    }

    public static void LogDevices(string kind, IReadOnlyList<MacOsAudioDeviceInfo> devices)
    {
        VoiceDiagnostics.Log("mac.devices", $"{kind}Count={devices.Count}");
        for (var i = 0; i < devices.Count; i++)
        {
            var device = devices[i];
            VoiceDiagnostics.Log("mac.device",
                $"{kind} index={i} idHash={HashId(device.Id)} name=\"{Sanitize(device.Name)}\" isDefault={device.IsDefault.ToString().ToLowerInvariant()} channels={device.Channels} sampleRates={device.MinSampleRate}-{device.MaxSampleRate}");
        }
    }

    public static void LogSelection(string backend, string inputSelection, string outputSelection)
    {
        var inputHash = MacOsAudioDeviceSelection.TryDecode(inputSelection, out var inputId, out var inputName) ? HashId(inputId) : "default";
        var outputHash = MacOsAudioDeviceSelection.TryDecode(outputSelection, out var outputId, out var outputName) ? HashId(outputId) : "default";
        VoiceDiagnostics.Log("mac.selection",
            $"backend={backend} inputName=\"{Sanitize(inputName)}\" inputIdHash={inputHash} outputName=\"{Sanitize(outputName)}\" outputIdHash={outputHash}");
    }

    public static void LogStartup(MacOsAudioStartupResult result, string backend)
    {
        if (result.Success)
        {
            VoiceDiagnostics.Log("mac.audio.start",
                $"backend={backend} nativeResult={result.Code} actualInputRate={result.ActualInputSampleRate} actualOutputRate={result.ActualOutputSampleRate} actualInputChannels={result.ActualInputChannels} actualOutputChannels={result.ActualOutputChannels} inputIdHash={result.InputIdHash} outputIdHash={result.OutputIdHash}");
        }
        else
        {
            VoiceDiagnostics.Log("mac.audio.fail",
                $"backend={backend} stage={result.Stage} code={result.Code} inputIdHash={result.InputIdHash} outputIdHash={result.OutputIdHash} nativeError=\"{Sanitize(result.Message)}\"");
        }
    }

    public static void LogStats(string backend, MacOsAudioDiagnosticsSnapshot snapshot)
    {
        VoiceDiagnostics.Log("mac.audio.stats",
            $"backend={backend} captureCallbacks={snapshot.CaptureCallbacks} playbackCallbacks={snapshot.PlaybackCallbacks} captureFrames={snapshot.CaptureFrames} playbackFrames={snapshot.PlaybackFrames} underruns={snapshot.Underruns} overruns={snapshot.Overruns} callbackErrors={snapshot.CallbackErrors} restarts={snapshot.Restarts} nativeStats=\"{Sanitize(snapshot.NativeStatsJson)}\"");
    }

    public static string HashId(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "default";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(id));
        return Convert.ToHexString(bytes, 0, 6).ToLowerInvariant();
    }

    private static string Sanitize(string? value)
        => (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Replace("\"", "'");
}
#endif
