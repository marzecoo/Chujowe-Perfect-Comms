#if MACOS
namespace VoiceChatPlugin.Audio;

internal readonly record struct MacOsAudioStartupResult(
    bool Success,
    string Stage,
    string Code,
    string Message,
    string InputIdHash,
    string OutputIdHash,
    int ActualInputSampleRate,
    int ActualOutputSampleRate,
    int ActualInputChannels,
    int ActualOutputChannels)
{
    public static MacOsAudioStartupResult Ok(
        string inputIdHash,
        string outputIdHash,
        int sampleRate,
        int inputChannels,
        int outputChannels)
        => new(true, "ready", "PC_AUDIO_OK", string.Empty, inputIdHash, outputIdHash, sampleRate, sampleRate, inputChannels, outputChannels);

    public static MacOsAudioStartupResult Fail(string stage, string code, string message, string inputIdHash, string outputIdHash)
        => new(false, stage, code, message, inputIdHash, outputIdHash, 0, 0, 0, 0);
}
#endif
