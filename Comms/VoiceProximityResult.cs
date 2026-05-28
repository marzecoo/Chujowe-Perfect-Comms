namespace VoiceChatPlugin.VoiceChat;

internal readonly record struct VoiceProximityResult(
    float NormalVolume,
    float GhostVolume,
    float RadioVolume,
    float Pan,
    VoiceAudioFilterMode FilterMode,
    bool Audible,
    VoiceProximityReason Reason,
    float WallCoefficient)
{
    private const float PlaybackBoost = 1.5f;
    private const float MaxPlaybackVolume = 3f;

    public static VoiceProximityResult Muted(VoiceProximityReason reason, float wallCoefficient = 1f)
        => new(0f, 0f, 0f, 0f, VoiceAudioFilterMode.None, false, reason, wallCoefficient);

    public static float BoostPlaybackVolume(float volume, float gain = 1f)
        => System.Math.Clamp(volume * gain * PlaybackBoost, 0f, MaxPlaybackVolume);
}
