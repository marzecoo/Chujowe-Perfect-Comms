using UnityEngine;

namespace VoiceChatPlugin.VoiceChat;

internal readonly record struct VoicePlayerSnapshot(
    byte PlayerId,
    int ClientId,
    string PlayerName,
    Vector2 Position,
    bool IsLocal,
    bool IsDead,
    bool IsImpostor,
    bool InVent,
    bool Disconnected,
    bool IsDummy,
    bool IsVisible,
    bool IsBlackmailed,
    bool IsJailed,
    byte JailorId);
