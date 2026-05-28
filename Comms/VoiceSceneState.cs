using UnityEngine;
using Object = UnityEngine.Object;

namespace VoiceChatPlugin.VoiceChat;

internal static class VoiceSceneState
{
    private const float EndGameProbeInterval = 0.25f;

    private static int _lastFrame = -1;
    private static float _nextEndGameProbeTime;
    private static bool _endGameActive;

    public static bool IsEndGameActive
    {
        get
        {
            int frame = Time.frameCount;
            if (_lastFrame == frame)
                return _endGameActive;

            _lastFrame = frame;
            if (_endGameActive || Time.time >= _nextEndGameProbeTime)
            {
                _endGameActive = Object.FindObjectOfType<EndGameManager>() != null;
                _nextEndGameProbeTime = Time.time + EndGameProbeInterval;
            }

            return _endGameActive;
        }
    }

    public static VoiceGamePhase ResolvePhase()
    {
        if (IsEndGameActive) return VoiceGamePhase.EndGame;
        if (ExileController.Instance != null) return VoiceGamePhase.Exile;
        if (MeetingHud.Instance != null) return VoiceGamePhase.Meeting;
        if (IntroCutscene.Instance != null) return VoiceGamePhase.Intro;
        if (LobbyBehaviour.Instance != null) return VoiceGamePhase.Lobby;
        if (ShipStatus.Instance != null) return VoiceGamePhase.Tasks;
        if (AmongUsClient.Instance == null) return VoiceGamePhase.Menu;
        return VoiceGamePhase.Unknown;
    }

    public static bool IsLobbyVoicePhase(VoiceGamePhase phase)
        => phase is VoiceGamePhase.Menu
            or VoiceGamePhase.Lobby
            or VoiceGamePhase.Intro
            or VoiceGamePhase.EndGame
            or VoiceGamePhase.Unknown;

    public static bool IsMeetingVoicePhase(VoiceGamePhase phase)
        => phase is VoiceGamePhase.Meeting or VoiceGamePhase.Exile;

    public static bool IsTaskVoicePhase(VoiceGamePhase phase)
        => phase == VoiceGamePhase.Tasks;

    public static void Reset()
    {
        _lastFrame = -1;
        _nextEndGameProbeTime = 0f;
        _endGameActive = false;
    }
}
