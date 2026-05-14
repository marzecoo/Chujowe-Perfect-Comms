using System;

namespace VoiceChatPlugin.VoiceChat;

[Flags]
public enum VoiceFeatureFlags : uint
{
    None = 0,
    ReliableAudio = 1 << 0,
    ProfilePackets = 1 << 1,
    CompatibilityHandshake = 1 << 2,
    AudioFrameFlags = 1 << 3,
    UnreliableAudio = 1 << 4,
    BundledAudio = 1 << 5,
}
