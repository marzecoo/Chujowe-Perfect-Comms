namespace VoiceChatPlugin.VoiceChat;

internal readonly record struct VoiceOcclusionResult(
    bool HasWall,
    bool HasClosedDoor,
    float TargetVolumeMultiplier,
    VoiceAudioFilterMode FilterMode)
{
    public bool IsOccluded => HasWall || HasClosedDoor;
}
