using System.Text.RegularExpressions;
using HarmonyLib;
using Reactor.Utilities;

namespace VoiceChatPlugin;

[HarmonyPatch(typeof(ReactorCredits), "GetText")]
public static class PerfectCommsCreditsColorPatch
{
    private const string CreditsColor = "#00E5FF";
    private const string CreditsLabel = "Mega Chujowe Perfect Comms " + VoiceChatPluginMain.Version;

    private static void Postfix(ref string? __result)
    {
        if (string.IsNullOrEmpty(__result)) return;

        var coloredLabel = $"<color={CreditsColor}><noparse>{CreditsLabel}</noparse></color>";
        var updated = Regex.Replace(
            __result,
            $@"<color=#[0-9A-Fa-f]{{3,8}}><noparse>{Regex.Escape(CreditsLabel)}</noparse></color>",
            coloredLabel);

        if (ReferenceEquals(updated, __result) || updated == __result)
            updated = __result.Replace($"<noparse>{CreditsLabel}</noparse>", coloredLabel);

        if (ReferenceEquals(updated, __result) || updated == __result)
            updated = __result.Replace(CreditsLabel, coloredLabel);

        __result = updated;
    }
}
