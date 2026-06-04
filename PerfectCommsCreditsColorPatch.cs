using System.Text.RegularExpressions;
using HarmonyLib;
using Reactor.Utilities;
using VoiceChatPlugin.VoiceChat;

namespace VoiceChatPlugin;

[HarmonyPatch(typeof(ReactorCredits), "GetText")]
public static class PerfectCommsCreditsColorPatch
{
    private const string CreditsColor = "#00E5FF";
    private static string CreditsLabel => VoiceChatLocalSettings.Censor("Mega Chujowe Perfect Comms") + " " + VoiceChatPluginMain.Version;

    private static void Postfix(ref string? __result)
    {
        if (string.IsNullOrEmpty(__result)) return;

        string label = CreditsLabel;
        var coloredLabel = $"<color={CreditsColor}><noparse>{label}</noparse></color>";
        var updated = Regex.Replace(
            __result,
            $@"<color=#[0-9A-Fa-f]{{3,8}}><noparse>{Regex.Escape(label)}</noparse></color>",
            coloredLabel);

        if (ReferenceEquals(updated, __result) || updated == __result)
            updated = __result.Replace($"<noparse>{label}</noparse>", coloredLabel);

        if (ReferenceEquals(updated, __result) || updated == __result)
            updated = __result.Replace(label, coloredLabel);

        __result = updated;
    }
}
