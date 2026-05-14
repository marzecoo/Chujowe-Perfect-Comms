using System;
using System.Collections.Generic;
using HarmonyLib;
using MiraAPI.Modifiers;
using UnityEngine;

namespace VoiceChatPlugin.VoiceChat;

internal static class VoiceRoleMuteState
{
    private const string BlackmailedModifierName = "TownOfUs.Modifiers.Impostor.BlackmailedModifier";
    private const string JailedModifierName = "TownOfUs.Modifiers.Crewmate.JailedModifier";
    private const string JailorRoleName = "TownOfUs.Roles.Crewmate.JailorRole";

    private static readonly Dictionary<string, Type?> TypeCache = new();
    private static readonly HashSet<byte> JailVoiceAllowed = new();
    private static bool _wasInMeeting;

    internal static void Update()
    {
        bool inMeeting = MeetingHud.Instance != null;
        if (!inMeeting)
        {
            if (_wasInMeeting)
                JailVoiceAllowed.Clear();
            _wasInMeeting = false;
            return;
        }

        _wasInMeeting = true;
        PruneJailVoiceAllowed();
    }

    internal static bool IsLocalMeetingVoiceBlocked()
        => TryGetLocalMeetingVoiceBlockReason(out _);

    internal static bool TryGetLocalMeetingVoiceBlockReason(out string reason)
    {
        reason = string.Empty;
        Update();

        var local = PlayerControl.LocalPlayer;
        if (local == null || MeetingHud.Instance == null || local.Data?.IsDead == true)
            return false;

        if (HasModifier(local, BlackmailedModifierName))
        {
            reason = "Blackmailed";
            return true;
        }

        if (TryGetJailorId(local, out byte jailorId) && IsJailorValid(jailorId) && !JailVoiceAllowed.Contains(local.PlayerId))
        {
            reason = "Jailed";
            return true;
        }

        return false;
    }

    internal static bool IsMeetingVoiceBlocked(VoicePlayerSnapshot player)
    {
        if (MeetingHud.Instance == null || player.IsDead)
            return false;
        if (player.IsBlackmailed)
            return true;
        if (player.IsJailed && IsJailorValid(player.JailorId) && !JailVoiceAllowed.Contains(player.PlayerId))
            return true;
        return false;
    }

    internal static VoiceProximityReason GetMeetingBlockReason(VoicePlayerSnapshot player)
        => player.IsBlackmailed ? VoiceProximityReason.Blackmailed : VoiceProximityReason.Jailed;

    internal static bool IsBlackmailed(PlayerControl? player)
        => HasModifier(player, BlackmailedModifierName);

    internal static bool TryGetJailorId(PlayerControl? player, out byte jailorId)
    {
        jailorId = byte.MaxValue;
        var modifier = GetModifier(player, JailedModifierName);
        if (modifier == null) return false;

        try
        {
            object? value = modifier.GetType().GetProperty("JailorId")?.GetValue(modifier);
            if (value is byte id)
            {
                jailorId = id;
                return true;
            }
        }
        catch
        {
            // ignored; role integration should fail closed per player, not per frame
        }

        return false;
    }

    internal static bool CanLocalJailorUnmute(out byte jailedPlayerId)
    {
        jailedPlayerId = byte.MaxValue;
        Update();

        var local = PlayerControl.LocalPlayer;
        if (local == null || MeetingHud.Instance == null || local.Data?.IsDead == true || !IsJailor(local))
            return false;

        foreach (var player in PlayerControl.AllPlayerControls)
        {
            if (player == null || player.Data?.IsDead == true) continue;
            if (!TryGetJailorId(player, out byte jailorId) || jailorId != local.PlayerId) continue;
            if (!IsJailorValid(jailorId) || JailVoiceAllowed.Contains(player.PlayerId)) continue;

            jailedPlayerId = player.PlayerId;
            return true;
        }

        return false;
    }

    internal static void LocalJailorAllowVoice()
    {
        if (!CanLocalJailorUnmute(out byte jailedPlayerId)) return;
        SetJailVoiceAllowed(jailedPlayerId, true);
        SendJailVoiceAllowed(jailedPlayerId, true);
        VoiceChatHudState.ApplyMicState();
    }

    internal static void ApplyRemoteJailVoice(byte jailorId, byte jailedPlayerId, bool allowed)
    {
        var jailed = FindPlayer(jailedPlayerId);
        if (jailed == null || !TryGetJailorId(jailed, out byte actualJailorId) || actualJailorId != jailorId)
            return;
        if (!IsJailorValid(jailorId))
            return;

        SetJailVoiceAllowed(jailedPlayerId, allowed);
        VoiceChatHudState.ApplyMicState();
    }

    internal static bool IsJailVoiceAllowed(byte playerId)
        => JailVoiceAllowed.Contains(playerId);

    private static void SetJailVoiceAllowed(byte playerId, bool allowed)
    {
        if (allowed) JailVoiceAllowed.Add(playerId);
        else JailVoiceAllowed.Remove(playerId);
    }

    private static void SendJailVoiceAllowed(byte jailedPlayerId, bool allowed)
    {
        try
        {
            if (AmongUsClient.Instance == null || PlayerControl.LocalPlayer == null) return;
            var w = AmongUsClient.Instance.StartRpcImmediately(
                PlayerControl.LocalPlayer.NetId,
                VoiceProtocol.AudioRpcId,
                Hazel.SendOption.Reliable,
                -1);
            w.Write((byte)VoicePacketType.JailVoice);
            w.Write(jailedPlayerId);
            w.Write(allowed);
            AmongUsClient.Instance.FinishRpcImmediately(w);
        }
        catch (Exception ex)
        {
            VoiceChatPluginMain.Logger.LogError($"[VC] Jail voice RPC send failed: {ex.Message}");
        }
    }

    internal static bool IsJailorValid(byte jailorId)
    {
        var jailor = FindPlayer(jailorId);
        return jailor != null && jailor.Data?.IsDead != true && IsJailor(jailor);
    }

    private static bool IsJailor(PlayerControl? player)
    {
        string? roleName = player?.Data?.Role?.GetType().FullName;
        return roleName == JailorRoleName || roleName?.EndsWith(".JailorRole", StringComparison.Ordinal) == true;
    }

    private static void PruneJailVoiceAllowed()
    {
        if (JailVoiceAllowed.Count == 0) return;

        foreach (byte playerId in new List<byte>(JailVoiceAllowed))
        {
            var player = FindPlayer(playerId);
            if (player == null || player.Data?.IsDead == true || !TryGetJailorId(player, out byte jailorId) || !IsJailorValid(jailorId))
                JailVoiceAllowed.Remove(playerId);
        }
    }

    private static bool HasModifier(PlayerControl? player, string typeName)
        => GetModifier(player, typeName) != null;

    private static BaseModifier? GetModifier(PlayerControl? player, string typeName)
    {
        if (player == null) return null;
        var type = ResolveType(typeName);
        if (type == null) return null;

        try
        {
            return player.GetModifier(type);
        }
        catch
        {
            return null;
        }
    }

    private static Type? ResolveType(string fullName)
    {
        if (TypeCache.TryGetValue(fullName, out var cached))
            return cached;

        Type? type = AccessTools.TypeByName(fullName);
        if (type == null)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = asm.GetType(fullName, false);
                if (type != null) break;
            }
        }

        TypeCache[fullName] = type;
        return type;
    }

    private static PlayerControl? FindPlayer(byte playerId)
    {
        foreach (var player in PlayerControl.AllPlayerControls)
            if (player != null && player.PlayerId == playerId)
                return player;
        return null;
    }
}
