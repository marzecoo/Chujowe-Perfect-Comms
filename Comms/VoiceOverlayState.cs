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

    private static VoiceOverlayState Build(VoiceChatRoom? room)
    {
        if (room == null)
            return Empty;

        var snapshot = room.CurrentSnapshot;
        var remotePlayers = new List<VoiceRemoteOverlayState>(16);
        foreach (var entry in room.AllClientEntries)
        {
            int senderId = entry.Key;
            var client = entry.Value;
            byte playerId = client.PlayerId;
            string playerName = client.PlayerName;
            if (snapshot != null &&
                TryResolvePlayer(snapshot, senderId, playerId, out var player) &&
                !player.Disconnected &&
                !player.IsDummy)
            {
                playerId = player.PlayerId;
                playerName = string.IsNullOrWhiteSpace(player.PlayerName)
                    ? playerName
                    : player.PlayerName;
            }

            if (playerId == byte.MaxValue) continue;

            bool isSpeaking = client.IsSpeaking || client.Level >= 0.004f;

            remotePlayers.Add(new VoiceRemoteOverlayState(
                playerId,
                playerName,
                client.Level,
                isSpeaking,
                client.CurrentRoute.Audible,
                client.CurrentRoute.Reason));
        }

        var local = new VoiceLocalOverlayState(
            VoiceChatHudState.IsMuted,
            VoiceChatHudState.IsSpeakerMuted,
            room.LocalMicLevel,
            room.LocalMicSpeaking && !VoiceChatHudState.IsMuted,
            room.UsingMicrophone,
            room.UsingSpeaker);

        return new VoiceOverlayState(local, remotePlayers);
    }

    private static bool TryResolvePlayer(
        VoiceGameStateSnapshot snapshot,
        int senderId,
        byte playerId,
        out VoicePlayerSnapshot player)
    {
        if (playerId != byte.MaxValue && snapshot.TryGetPlayer(playerId, out player))
            return true;

        if (senderId >= 0 && snapshot.TryGetClient(senderId, out player))
            return true;

        int fallbackPlayerId = senderId - 1000;
        if (fallbackPlayerId >= 0 && fallbackPlayerId <= byte.MaxValue &&
            snapshot.TryGetPlayer((byte)fallbackPlayerId, out player))
            return true;

        player = default;
        return false;
    }
}
