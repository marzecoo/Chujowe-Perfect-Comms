using System;
using System.Collections.Generic;
using HarmonyLib;
using MiraAPI.Modifiers;
using UnityEngine;

namespace VoiceChatPlugin.VoiceChat;

internal static partial class VoiceRoleMuteState
{
    private const string BlackmailedModifierName = "TownOfUs.Modifiers.Impostor.BlackmailedModifier";
    private const string JailedModifierName = "TownOfUs.Modifiers.Crewmate.JailedModifier";
    private const string ParasiteInfectedModifierName = "TownOfUs.Modifiers.Impostor.ParasiteInfectedModifier";
    private const string PuppeteerControlModifierName = "TownOfUs.Modifiers.Impostor.PuppeteerControlModifier";
    private const string CrewpostorModifierName = "TownOfUs.Modifiers.Game.Alliance.CrewpostorModifier";
    private const string SwoopModifierName = "TownOfUs.Modifiers.Impostor.SwoopModifier";
    private const string JailorRoleName = "TownOfUs.Roles.Crewmate.JailorRole";
    private const float RoleStateRefreshInterval = 0.25f;

    private static readonly HashSet<byte> JailVoiceAllowed = new();
    private static readonly HashSet<byte> MeetingBlackmailedPlayers = new();
    private static readonly HashSet<byte> PostMeetingBlackmailedPlayers = new();
    private static readonly Dictionary<byte, CachedRoleState> RoleStateCache = new();

    private static Type? _blackmailedModifierType;
    private static Type? _jailedModifierType;
    private static Type? _parasiteInfectedModifierType;
    private static Type? _puppeteerControlModifierType;
    private static Type? _crewpostorModifierType;
    private static Type? _swoopModifierType;
    private static bool _supportedModTypesResolved;
    private static int _resolvedGameId = int.MinValue;
    private static VoiceGamePhase _resolvedPhase = VoiceGamePhase.Unknown;
    private static float _nextRoleStateRefreshTime;
    private static bool _wasInMeeting;

    private readonly record struct CachedRoleState(
        bool IsBlackmailed,
        bool IsJailed,
        byte JailorId,
        bool IsParasiteControlled,
        bool IsPuppeteerControlled,
        bool IsCrewpostor,
        bool IsBlackmailedNextRound,
        bool IsSwooped);

    internal static void Update()
    {
        var settings = VoiceRoomSettingsState.Current;
        bool inMeeting = MeetingHud.Instance != null;

        if (inMeeting && !_wasInMeeting)
        {
            PostMeetingBlackmailedPlayers.Clear();
            MeetingBlackmailedPlayers.Clear();
            InvalidateRoleStateCache();
        }

        RefreshRoleStateCacheIfNeeded();

        if (inMeeting)
        {
            _wasInMeeting = true;
            if (settings.MuteBlackmailedInMeetings)
                TrackMeetingBlackmailedPlayers();
            else
                MeetingBlackmailedPlayers.Clear();

            PruneJailVoiceAllowed(settings);
            return;
        }

        if (_wasInMeeting)
        {
            _wasInMeeting = false;
            JailVoiceAllowed.Clear();
            PostMeetingBlackmailedPlayers.Clear();

            if (settings.MuteBlackmailedInMeetings && settings.MuteBlackmailedNextRound)
            {
                foreach (byte playerId in MeetingBlackmailedPlayers)
                    PostMeetingBlackmailedPlayers.Add(playerId);
            }

            MeetingBlackmailedPlayers.Clear();
            InvalidateRoleStateCache();
        }
        else if (!settings.MuteBlackmailedNextRound && PostMeetingBlackmailedPlayers.Count > 0)
        {
            PostMeetingBlackmailedPlayers.Clear();
            InvalidateRoleStateCache();
        }

        var phase = VoiceSceneState.ResolvePhase();
        if (phase is VoiceGamePhase.Menu or VoiceGamePhase.Lobby or VoiceGamePhase.Intro or VoiceGamePhase.EndGame)
        {
            if (PostMeetingBlackmailedPlayers.Count > 0)
            {
                PostMeetingBlackmailedPlayers.Clear();
                InvalidateRoleStateCache();
            }
        }

        PrunePostMeetingBlackmailedPlayers();
    }

    internal static bool IsLocalVoiceBlocked()
        => TryGetLocalVoiceBlockReason(out _);

    internal static bool IsLocalMeetingVoiceBlocked()
        => TryGetLocalMeetingVoiceBlockReason(out _);

    internal static bool TryGetLocalVoiceBlockReason(out string reason)
    {
        reason = string.Empty;
        Update();

        var local = PlayerControl.LocalPlayer;
        if (local == null || local.Data?.IsDead == true)
            return false;

        GetPlayerRoleState(local, out bool isBlackmailed, out bool isJailed, out byte jailorId,
            out bool isParasiteControlled, out bool isPuppeteerControlled, out bool isCrewpostor,
            out bool isBlackmailedNextRound, out bool isSwooped);

        var state = new CachedRoleState(
            isBlackmailed,
            isJailed,
            jailorId,
            isParasiteControlled,
            isPuppeteerControlled,
            isCrewpostor,
            isBlackmailedNextRound,
            isSwooped);

        var settings = VoiceRoomSettingsState.Current;
        if (MeetingHud.Instance != null && IsMeetingVoiceBlocked(local.PlayerId, state, settings, out var meetingReason))
        {
            reason = ToDisplayReason(meetingReason);
            return true;
        }

        if (IsTaskVoiceBlocked(local.PlayerId, state, settings, out var taskReason))
        {
            reason = ToDisplayReason(taskReason);
            return true;
        }

        return false;
    }

    internal static bool TryGetLocalMeetingVoiceBlockReason(out string reason)
    {
        reason = string.Empty;
        Update();

        var local = PlayerControl.LocalPlayer;
        if (local == null || MeetingHud.Instance == null || local.Data?.IsDead == true)
            return false;

        GetPlayerRoleState(local, out bool isBlackmailed, out bool isJailed, out byte jailorId,
            out _, out _, out _, out _, out bool isSwooped);

        var state = new CachedRoleState(isBlackmailed, isJailed, jailorId, false, false, false, false, isSwooped);
        if (!IsMeetingVoiceBlocked(local.PlayerId, state, VoiceRoomSettingsState.Current, out var blockReason))
            return false;

        reason = ToDisplayReason(blockReason);
        return true;
    }

    internal static bool IsMeetingVoiceBlocked(VoicePlayerSnapshot player)
    {
        if (MeetingHud.Instance == null || player.IsDead)
            return false;

        var state = new CachedRoleState(
            player.IsBlackmailed,
            player.IsJailed,
            player.JailorId,
            player.IsParasiteControlled,
            player.IsPuppeteerControlled,
            false,
            player.IsBlackmailedNextRound,
            player.IsSwooped);

        return IsMeetingVoiceBlocked(player.PlayerId, state, VoiceRoomSettingsState.Current, out _);
    }

    internal static VoiceProximityReason GetMeetingBlockReason(VoicePlayerSnapshot player)
    {
        var state = new CachedRoleState(
            player.IsBlackmailed,
            player.IsJailed,
            player.JailorId,
            player.IsParasiteControlled,
            player.IsPuppeteerControlled,
            false,
            player.IsBlackmailedNextRound,
            player.IsSwooped);

        return IsMeetingVoiceBlocked(player.PlayerId, state, VoiceRoomSettingsState.Current, out var reason)
            ? reason
            : VoiceProximityReason.MeetingLiving;
    }

    internal static bool IsTaskVoiceBlocked(VoicePlayerSnapshot player)
    {
        if (player.IsDead)
            return false;

        var state = new CachedRoleState(
            player.IsBlackmailed,
            player.IsJailed,
            player.JailorId,
            player.IsParasiteControlled,
            player.IsPuppeteerControlled,
            false,
            player.IsBlackmailedNextRound,
            player.IsSwooped);

        return IsTaskVoiceBlocked(player.PlayerId, state, VoiceRoomSettingsState.Current, out _);
    }

    internal static VoiceProximityReason GetTaskBlockReason(VoicePlayerSnapshot player)
    {
        var state = new CachedRoleState(
            player.IsBlackmailed,
            player.IsJailed,
            player.JailorId,
            player.IsParasiteControlled,
            player.IsPuppeteerControlled,
            false,
            player.IsBlackmailedNextRound,
            player.IsSwooped);

        return IsTaskVoiceBlocked(player.PlayerId, state, VoiceRoomSettingsState.Current, out var reason)
            ? reason
            : VoiceProximityReason.Proximity;
    }

    internal static bool IsBlackmailed(PlayerControl? player)
    {
        GetPlayerRoleState(player, out bool isBlackmailed, out _, out _, out _, out _, out _, out _, out _);
        return isBlackmailed;
    }

    internal static bool TryGetJailorId(PlayerControl? player, out byte jailorId)
    {
        GetPlayerRoleState(player, out _, out bool isJailed, out jailorId, out _, out _, out _, out _, out _);
        return isJailed;
    }

    internal static bool IsVoiceImpostor(PlayerControl? player)
    {
        if (player?.Data?.Role?.IsImpostor == true)
            return true;
        if (!VoiceRoomSettingsState.Current.CrewpostorUsesImpostorVoice)
            return false;

        GetPlayerRoleState(player, out _, out _, out _, out _, out _, out bool isCrewpostor, out _, out _);
        return isCrewpostor;
    }

    internal static void GetPlayerRoleState(
        PlayerControl? player,
        out bool isBlackmailed,
        out bool isJailed,
        out byte jailorId,
        out bool isParasiteControlled,
        out bool isPuppeteerControlled,
        out bool isCrewpostor,
        out bool isBlackmailedNextRound,
        out bool isSwooped)
    {
        isBlackmailed = false;
        isJailed = false;
        jailorId = byte.MaxValue;
        isParasiteControlled = false;
        isPuppeteerControlled = false;
        isCrewpostor = false;
        isBlackmailedNextRound = false;
        isSwooped = false;
        if (player == null) return;

        RefreshRoleStateCacheIfNeeded();
        if (!RoleStateCache.TryGetValue(player.PlayerId, out var state)) return;

        isBlackmailed = state.IsBlackmailed;
        isJailed = state.IsJailed;
        jailorId = state.JailorId;
        isParasiteControlled = state.IsParasiteControlled;
        isPuppeteerControlled = state.IsPuppeteerControlled;
        isCrewpostor = state.IsCrewpostor;
        isBlackmailedNextRound = state.IsBlackmailedNextRound;
        isSwooped = state.IsSwooped;
    }

    internal static bool CanLocalJailorUnmute(out byte jailedPlayerId)
    {
        jailedPlayerId = byte.MaxValue;
        Update();

        var settings = VoiceRoomSettingsState.Current;
        if (!settings.MuteJailedInMeetings || !settings.JailorCanUnmuteJailed)
            return false;

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
        RefreshRoleStateCacheIfNeeded(force: true);
        if (!CanLocalJailorUnmute(out byte jailedPlayerId)) return;
        SetJailVoiceAllowed(jailedPlayerId, true);
        SendJailVoiceAllowed(jailedPlayerId, true);
        VoiceChatHudState.ApplyMicState();
    }

    internal static void ApplyRemoteJailVoice(byte jailorId, byte jailedPlayerId, bool allowed)
    {
        RefreshRoleStateCacheIfNeeded(force: true);
        var settings = VoiceRoomSettingsState.Current;
        if (!settings.MuteJailedInMeetings || !settings.JailorCanUnmuteJailed)
            return;

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

    private static bool IsMeetingVoiceBlocked(
        byte playerId,
        CachedRoleState state,
        VoiceRoomSettingsSnapshot settings,
        out VoiceProximityReason reason)
    {
        reason = VoiceProximityReason.MeetingLiving;

        if (state.IsSwooped)
        {
            reason = VoiceProximityReason.Swooped;
            return true;
        }

        if (settings.MuteBlackmailedInMeetings && state.IsBlackmailed)
        {
            reason = VoiceProximityReason.Blackmailed;
            return true;
        }

        if (settings.MuteJailedInMeetings &&
            state.IsJailed &&
            IsJailorValid(state.JailorId) &&
            (!settings.JailorCanUnmuteJailed || !JailVoiceAllowed.Contains(playerId)))
        {
            reason = VoiceProximityReason.Jailed;
            return true;
        }

        return false;
    }

    private static bool IsTaskVoiceBlocked(
        byte playerId,
        CachedRoleState state,
        VoiceRoomSettingsSnapshot settings,
        out VoiceProximityReason reason)
    {
        _ = playerId;
        reason = VoiceProximityReason.Proximity;

        if (state.IsSwooped)
        {
            reason = VoiceProximityReason.Swooped;
            return true;
        }

        if (settings.MuteBlackmailedNextRound && state.IsBlackmailedNextRound)
        {
            reason = VoiceProximityReason.BlackmailedNextRound;
            return true;
        }

        if (settings.MuteParasiteControlled && state.IsParasiteControlled)
        {
            reason = VoiceProximityReason.ParasiteControlled;
            return true;
        }

        if (settings.MutePuppeteerControlled && state.IsPuppeteerControlled)
        {
            reason = VoiceProximityReason.PuppeteerControlled;
            return true;
        }

        return false;
    }

    private static string ToDisplayReason(VoiceProximityReason reason)
        => reason switch
        {
            VoiceProximityReason.Blackmailed => "Blackmailed",
            VoiceProximityReason.BlackmailedNextRound => "Blackmailed",
            VoiceProximityReason.Jailed => "Jailed",
            VoiceProximityReason.ParasiteControlled => "Parasite Controlled",
            VoiceProximityReason.PuppeteerControlled => "Puppeteer Controlled",
            VoiceProximityReason.Swooped => "Swooped",
            _ => "Role Muted",
        };

    private static void TrackMeetingBlackmailedPlayers()
    {
        foreach (var pair in RoleStateCache)
        {
            if (pair.Value.IsBlackmailed)
                MeetingBlackmailedPlayers.Add(pair.Key);
        }
    }

    private static void SetJailVoiceAllowed(byte playerId, bool allowed)
    {
        if (allowed) JailVoiceAllowed.Add(playerId);
        else JailVoiceAllowed.Remove(playerId);
    }

    private static void SendJailVoiceAllowed(byte jailedPlayerId, bool allowed)
    {
        try
        {
            VoiceChatRoom.SendJailVoicePacket(jailedPlayerId, allowed);
        }
        catch (Exception ex)
        {
            VoiceDiagnostics.DebugError($"[VC] Jail voice send failed: {ex.Message}");
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
        return roleName == JailorRoleName;
    }

    private static void PruneJailVoiceAllowed(VoiceRoomSettingsSnapshot settings)
    {
        if (JailVoiceAllowed.Count == 0) return;
        if (!settings.MuteJailedInMeetings || !settings.JailorCanUnmuteJailed)
        {
            JailVoiceAllowed.Clear();
            return;
        }

        foreach (byte playerId in new List<byte>(JailVoiceAllowed))
        {
            var player = FindPlayer(playerId);
            if (player == null || player.Data?.IsDead == true || !TryGetJailorId(player, out byte jailorId) || !IsJailorValid(jailorId))
                JailVoiceAllowed.Remove(playerId);
        }
    }

    private static void PrunePostMeetingBlackmailedPlayers()
    {
        if (PostMeetingBlackmailedPlayers.Count == 0) return;
        bool removed = false;

        foreach (byte playerId in new List<byte>(PostMeetingBlackmailedPlayers))
        {
            var player = FindPlayer(playerId);
            if (player == null || player.Data?.IsDead == true || player.Data?.Disconnected == true)
            {
                PostMeetingBlackmailedPlayers.Remove(playerId);
                removed = true;
            }
        }

        if (removed)
            InvalidateRoleStateCache();
    }

}
