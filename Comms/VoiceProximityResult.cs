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
    public static VoiceProximityResult Muted(VoiceProximityReason reason, float wallCoefficient = 1f)
        => new(0f, 0f, 0f, 0f, VoiceAudioFilterMode.None, false, reason, wallCoefficient);
}
