using System.Collections.Generic;
using UnityEngine;

namespace VoiceChatPlugin.VoiceChat;

internal readonly record struct VoiceLocalOverlayState(
    bool IsMuted,
    bool IsDeafened,
    float Level,
    bool IsSpeaking,
    bool MicrophoneAvailable,
    bool SpeakerAvailable);

internal readonly record struct VoiceRemoteOverlayState(
    byte PlayerId,
    string PlayerName,
    float Level,
    bool IsSpeaking,
    bool IsAudible,
    VoiceProximityReason Reason);

internal sealed class VoiceOverlayState
{
    public static readonly VoiceOverlayState Empty = new(
        new VoiceLocalOverlayState(false, false, 0f, false, false, false),
        []);

    public VoiceLocalOverlayState Local { get; }
    public IReadOnlyList<VoiceRemoteOverlayState> RemotePlayers { get; }
    private static int _cachedFrame = -1;
    private static VoiceChatRoom? _cachedRoom;
    private static VoiceOverlayState _cachedState = Empty;

    private VoiceOverlayState(VoiceLocalOverlayState local, IReadOnlyList<VoiceRemoteOverlayState> remotePlayers)
    {
        Local = local;
        RemotePlayers = remotePlayers;
    }

    public static VoiceOverlayState Current(VoiceChatRoom? room)
    {
        int frame = Time.frameCount;
        if (_cachedFrame == frame && ReferenceEquals(_cachedRoom, room))
            return _cachedState;

        _cachedFrame = frame;
        _cachedRoom = room;
        _cachedState = Build(room);
        return _cachedState;
    }

    internal static void InvalidateCache()
    {
        _cachedFrame = -1;
        _cachedRoom = null;
        _cachedState = Empty;
    }

    private static VoiceOverlayState Build(VoiceChatRoom? room)
    {
        if (room == null)
            return Empty;

        var snapshot = room.CurrentSnapshot;
        var remotePlayers = new List<VoiceRemoteOverlayState>(16);
        foreach (var remote in room.InterstellarRemoteOverlayStates)
        {
            if (!IsLiveRemoteSpeaker(remote.PlayerId, snapshot))
                continue;
            remotePlayers.Add(remote);
        }

        var local = new VoiceLocalOverlayState(
            VoiceChatHudState.IsMuted,
            VoiceChatHudState.IsSpeakerMuted,
            room.LocalMicLevel,
            room.LocalMicSpeaking && !VoiceChatHudState.IsMuted && !VoiceChatHudState.IsLocalTransmitBlocked,
            room.UsingMicrophone,
            room.UsingSpeaker);

        return new VoiceOverlayState(local, remotePlayers);
    }

    private static bool IsLiveRemoteSpeaker(byte playerId, VoiceGameStateSnapshot? snapshot)
    {
        if (snapshot == null || playerId == byte.MaxValue)
            return false;

        if (!snapshot.TryGetPlayer(playerId, out var player))
            return false;

        return !player.IsLocal
               && !player.Disconnected
               && !player.IsDummy
               && player.ClientId >= 0;
    }
}
