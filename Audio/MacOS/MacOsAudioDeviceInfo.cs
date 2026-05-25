#if MACOS
namespace VoiceChatPlugin.Audio;

internal readonly record struct MacOsAudioDeviceInfo(
    string Id,
    string Name,
    bool IsDefault,
    int Channels,
    int MinSampleRate,
    int MaxSampleRate)
{
    public string DisplayName => IsDefault ? $"{Name} (Default)" : Name;
}
#endif
