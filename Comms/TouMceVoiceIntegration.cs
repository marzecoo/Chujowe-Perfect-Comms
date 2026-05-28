using System;
using HarmonyLib;
using MiraAPI.Modifiers;

namespace VoiceChatPlugin.VoiceChat;

internal static class TouMceVoiceIntegration
{
    private const string PelicanSystemName = "TouMegaChujoweExtension.Modules.PelicanSystem";
    private const string JackalRoleName = "TouMegaChujoweExtension.Roles.Classic.Neutral.JackalRole";
    private const string SidekickModifierName = "TouMegaChujoweExtension.Modifiers.Neutral.SidekickModifier";
    private const string SpiritMasterRoleName = "TouMegaChujoweExtension.Roles.Classic.Crewmate.SpiritMasterRole";
    private const string SpiritMasterMediatedModifierName = "TouMegaChujoweExtension.Modifiers.Crewmate.SpiritMasterMediatedModifier";
    private const string LawyerRoleName = "TouMegaChujoweExtension.Roles.Classic.Neutral.LawyerRole";
    private const string LawyerTargetModifierName = "TouMegaChujoweExtension.Modifiers.Neutral.LawyerTargetModifier";

    private static Type? _pelicanSystemType;
    private static Type? _jackalRoleType;
    private static Type? _sidekickModifierType;
    private static Type? _spiritMasterRoleType;
    private static Type? _spiritMasterMediatedModifierType;
    private static Type? _lawyerRoleType;
    private static Type? _lawyerTargetModifierType;
    private static bool _resolved;

    public static void GetPlayerVoiceState(
        PlayerControl? player,
        out bool isPelicanSwallowed,
        out byte pelicanId,
        out byte jackalTeamId,
        out bool isSpiritMaster,
        out bool isSpiritMasterMediatedGhost,
        out byte spiritMasterId,
        out bool isLawyer,
        out byte lawyerClientId,
        out byte lawyerOwnerId)
    {
        isPelicanSwallowed = false;
        pelicanId = byte.MaxValue;
        jackalTeamId = byte.MaxValue;
        isSpiritMaster = false;
        isSpiritMasterMediatedGhost = false;
        spiritMasterId = byte.MaxValue;
        isLawyer = false;
        lawyerClientId = byte.MaxValue;
        lawyerOwnerId = byte.MaxValue;

        if (player == null)
            return;

        ResolveTypesIfNeeded();

        pelicanId = GetPelicanOf(player.PlayerId);
        isPelicanSwallowed = pelicanId != byte.MaxValue;
        jackalTeamId = GetJackalTeamId(player);
        isSpiritMaster = player.Data?.IsDead != true && IsRole(player, SpiritMasterRoleName, _spiritMasterRoleType);
        spiritMasterId = GetSpiritMasterId(player);
        isSpiritMasterMediatedGhost = player.Data?.IsDead == true && spiritMasterId != byte.MaxValue;
        isLawyer = player.Data?.IsDead != true && IsRole(player, LawyerRoleName, _lawyerRoleType);
        lawyerClientId = isLawyer ? GetLawyerClientId(player.PlayerId) : byte.MaxValue;
        lawyerOwnerId = player.Data?.IsDead != true ? GetLawyerOwnerId(player) : byte.MaxValue;
    }

    private static void ResolveTypesIfNeeded()
    {
        if (_resolved)
            return;

        _pelicanSystemType = ResolveType(PelicanSystemName);
        _jackalRoleType = ResolveType(JackalRoleName);
        _sidekickModifierType = ResolveType(SidekickModifierName);
        _spiritMasterRoleType = ResolveType(SpiritMasterRoleName);
        _spiritMasterMediatedModifierType = ResolveType(SpiritMasterMediatedModifierName);
        _lawyerRoleType = ResolveType(LawyerRoleName);
        _lawyerTargetModifierType = ResolveType(LawyerTargetModifierName);
        _resolved = true;
    }

    private static Type? ResolveType(string fullName)
    {
        var type = AccessTools.TypeByName(fullName);
        if (type != null)
            return type;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = assembly.GetType(fullName, false);
            if (type != null)
                return type;
        }

        return null;
    }

    private static byte GetPelicanOf(byte playerId)
    {
        if (_pelicanSystemType == null)
            return byte.MaxValue;

        try
        {
            var method = AccessTools.Method(_pelicanSystemType, "GetPelicanOf");
            var value = method?.Invoke(null, [playerId]);
            return value is byte id ? id : byte.MaxValue;
        }
        catch
        {
            return byte.MaxValue;
        }
    }

    private static byte GetJackalTeamId(PlayerControl player)
    {
        if (player.Data?.IsDead == true)
            return byte.MaxValue;

        var modifier = GetSidekickModifier(player);
        if (modifier == null)
            return byte.MaxValue;

        try
        {
            var value = modifier.GetType().GetProperty("JackalId")?.GetValue(modifier);
            return value is byte id && id != 255 ? id : byte.MaxValue;
        }
        catch
        {
            return byte.MaxValue;
        }
    }

    private static bool IsRole(PlayerControl player, string roleName, Type? resolvedRoleType)
    {
        var role = player.Data?.Role;
        if (role == null)
            return false;

        try
        {
            var roleType = role.GetType();
            return roleType.FullName == roleName ||
                   (resolvedRoleType != null && (resolvedRoleType == roleType || resolvedRoleType.IsAssignableFrom(roleType)));
        }
        catch
        {
            return false;
        }
    }

    private static BaseModifier? GetSidekickModifier(PlayerControl player)
    {
        if (_sidekickModifierType == null)
            return null;

        try
        {
            return player.GetModifier(_sidekickModifierType);
        }
        catch
        {
            return null;
        }
    }

    private static byte GetSpiritMasterId(PlayerControl player)
    {
        var modifier = GetSpiritMasterMediatedModifier(player);
        if (modifier == null)
            return byte.MaxValue;

        try
        {
            var value = modifier.GetType().GetProperty("SpiritMasterId")?.GetValue(modifier);
            return value is byte id && id != 255 ? id : byte.MaxValue;
        }
        catch
        {
            return byte.MaxValue;
        }
    }

    private static BaseModifier? GetSpiritMasterMediatedModifier(PlayerControl player)
    {
        if (_spiritMasterMediatedModifierType == null)
            return null;

        try
        {
            return player.GetModifier(_spiritMasterMediatedModifierType);
        }
        catch
        {
            return null;
        }
    }

    private static byte GetLawyerClientId(byte lawyerId)
    {
        if (_lawyerTargetModifierType == null)
            return byte.MaxValue;

        try
        {
            foreach (var player in PlayerControl.AllPlayerControls)
            {
                if (player == null || player.Data?.IsDead == true)
                    continue;

                var modifier = GetLawyerTargetModifier(player);
                if (modifier == null)
                    continue;

                var value = modifier.GetType().GetProperty("OwnerId")?.GetValue(modifier);
                if (value is byte ownerId && ownerId == lawyerId)
                    return player.PlayerId;
            }
        }
        catch
        {
            return byte.MaxValue;
        }

        return byte.MaxValue;
    }

    private static byte GetLawyerOwnerId(PlayerControl player)
    {
        var modifier = GetLawyerTargetModifier(player);
        if (modifier == null)
            return byte.MaxValue;

        try
        {
            var value = modifier.GetType().GetProperty("OwnerId")?.GetValue(modifier);
            return value is byte id && id != 255 ? id : byte.MaxValue;
        }
        catch
        {
            return byte.MaxValue;
        }
    }

    private static BaseModifier? GetLawyerTargetModifier(PlayerControl player)
    {
        if (_lawyerTargetModifierType == null)
            return null;

        try
        {
            return player.GetModifier(_lawyerTargetModifierType);
        }
        catch
        {
            return null;
        }
    }
}
