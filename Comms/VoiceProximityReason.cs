namespace VoiceChatPlugin.VoiceChat;

internal enum VoiceProximityReason
{
    Lobby,
    Unmapped,
    NoListener,
    OnlyMeetingOrLobby,
    OnlyGhostsCanTalk,
    CommsSabotage,
    LocalDeadHearsGhost,
    LocalDeadHearsLiving,
    Blackmailed,
    Jailed,
    MeetingLiving,
    ImpostorRadio,
    ImpostorRadioMuted,
    ImpostorHearsGhost,
    TargetDeadMuted,
    VentMuted,
    VentPrivateMuted,
    SightBlocked,
    HardOcclusion,
    CameraProxy,
    Proximity,
}
