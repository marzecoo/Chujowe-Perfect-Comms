#if MACOS
namespace VoiceChatPlugin.Audio;

internal readonly record struct MacOsVoiceAudioConfig(
    string Backend,
    string InputSelection,
    string OutputSelection,
    int SampleRate,
    int InputChannels,
    int OutputChannels);
#endif
