#if MACOS
namespace VoiceChatPlugin.Audio;

internal readonly record struct MacOsAudioDiagnosticsSnapshot(
    long CaptureCallbacks,
    long PlaybackCallbacks,
    long CaptureFrames,
    long PlaybackFrames,
    long Underruns,
    long Overruns,
    long CallbackErrors,
    long Restarts,
    string NativeStatsJson);
#endif
