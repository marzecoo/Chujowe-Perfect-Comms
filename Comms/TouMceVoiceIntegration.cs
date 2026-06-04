using System;
using HarmonyLib;
using BepInEx.Configuration;
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
    private const string HackerSystemName = "TouMegaChujoweExtension.Modules.HackerSystem";

    private static Type? _pelicanSystemType;
    private static Type? _jackalRoleType;
    private static Type? _sidekickModifierType;
    private static Type? _spiritMasterRoleType;
    private static Type? _spiritMasterMediatedModifierType;
    private static Type? _lawyerRoleType;
    private static Type? _lawyerTargetModifierType;
    private static Type? _hackerSystemType;
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
        _hackerSystemType = ResolveType(HackerSystemName);
        _resolved = true;
    }

    internal static bool IsHackerJammed()
    {
        ResolveTypesIfNeeded();
        if (_hackerSystemType == null)
            return false;

        try
        {
            object? value = _hackerSystemType.GetProperty("IsJammed")?.GetValue(null);
            return value is bool jammed && jammed;
        }
        catch
        {
            return false;
        }
    }

    internal static bool HasRecruitVoiceChannel(PlayerControl? player)
    {
        if (player == null || player.Data?.IsDead == true)
            return false;

        ResolveTypesIfNeeded();
        return GetJackalTeamId(player) != byte.MaxValue;
    }

    internal static bool HasLawyerVoiceChannel(PlayerControl? player)
    {
        if (player == null || player.Data?.IsDead == true)
            return false;

        ResolveTypesIfNeeded();
        return IsRole(player, LawyerRoleName, _lawyerRoleType) ||
               GetLawyerOwnerId(player) != byte.MaxValue;
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

    internal static bool HasApocalypseVoiceChannel(PlayerControl? player)
    {
        if (player == null || player.Data?.IsDead == true)
            return false;

        var role = player.Data?.Role;
        if (role == null)
            return false;

        try
        {
            string fullName = role.GetType().FullName ?? "";
            if (fullName.Contains("Apocalypse", StringComparison.OrdinalIgnoreCase) ||
                fullName.Contains("Plaguebearer", StringComparison.OrdinalIgnoreCase) ||
                fullName.Contains("Pestilence", StringComparison.OrdinalIgnoreCase) ||
                fullName.Contains("Famine", StringComparison.OrdinalIgnoreCase) ||
                fullName.Contains("DeathRole", StringComparison.OrdinalIgnoreCase) ||
                fullName.Contains("WarRole", StringComparison.OrdinalIgnoreCase) ||
                fullName.Contains("BakerRole", StringComparison.OrdinalIgnoreCase) ||
                fullName.Contains("Baker", StringComparison.OrdinalIgnoreCase) ||
                fullName.Contains("Berserker", StringComparison.OrdinalIgnoreCase) ||
                fullName.Contains("SoulCollector", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Also check properties for "Faction" or "Team" containing "Apocalypse"
            var teamProp = role.GetType().GetProperty("Team", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (teamProp != null)
            {
                var teamValue = teamProp.GetValue(role);
                if (teamValue != null && teamValue.ToString()?.Contains("Apocalypse", StringComparison.OrdinalIgnoreCase) == true)
                    return true;
            }

            var factionProp = role.GetType().GetProperty("Faction", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (factionProp != null)
            {
                var factionValue = factionProp.GetValue(role);
                if (factionValue != null && factionValue.ToString()?.Contains("Apocalypse", StringComparison.OrdinalIgnoreCase) == true)
                    return true;
            }
        }
        catch (Exception ex)
        {
            VoiceDiagnostics.DebugWarning($"[VC] Error checking Apocalypse role: {ex.Message}");
        }

        return false;
    }

    internal static bool IsCensureActive()
    {
        try
        {
            var chainloaderType = Type.GetType("BepInEx.Unity.IL2CPP.IL2CPPChainloader, BepInEx.Unity.IL2CPP");
            if (chainloaderType != null)
            {
                var instanceProp = chainloaderType.GetProperty("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                var instance = instanceProp?.GetValue(null);
                if (instance != null)
                {
                    var pluginsProp = instance.GetType().GetProperty("Plugins", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    var plugins = pluginsProp?.GetValue(instance) as System.Collections.IDictionary;
                    if (plugins != null)
                    {
                        foreach (System.Collections.DictionaryEntry entry in plugins)
                        {
                            var pluginInfo = entry.Value;
                            if (pluginInfo != null)
                            {
                                var instanceField = pluginInfo.GetType().GetProperty("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                                    ?? pluginInfo.GetType().GetField("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance) as System.Reflection.MemberInfo;
                                object? pluginInstance = null;
                                if (instanceField is System.Reflection.PropertyInfo pi) pluginInstance = pi.GetValue(pluginInfo);
                                else if (instanceField is System.Reflection.FieldInfo fi) pluginInstance = fi.GetValue(pluginInfo);

                                if (pluginInstance is BepInEx.Unity.IL2CPP.BasePlugin bp)
                                {
                                    var config = bp.Config;
                                    if (config != null)
                                    {
                                        foreach (var configKey in config.Keys)
                                        {
                                            if (configKey.Key.Contains("Censure", StringComparison.OrdinalIgnoreCase) ||
                                                configKey.Key.Contains("Censor", StringComparison.OrdinalIgnoreCase))
                                            {
                                                var dict = config as System.Collections.Generic.IDictionary<ConfigDefinition, ConfigEntryBase>;
                                                if (dict != null && dict.TryGetValue(configKey, out var configEntry))
                                                {
                                                    if (configEntry != null && configEntry.BoxedValue is bool val)
                                                    {
                                                        return val;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        catch { }
        return false;
    }
}
