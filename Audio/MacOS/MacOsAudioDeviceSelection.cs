#if MACOS
using System;

namespace VoiceChatPlugin.Audio;

internal static class MacOsAudioDeviceSelection
{
    private const string Prefix = "macos:";
    private const char Separator = '\t';

    public static string Default => string.Empty;

    public static string Encode(MacOsAudioDeviceInfo device)
        => Prefix + device.Id + Separator + device.Name;

    public static string EncodeMissing(string id, string name)
        => Prefix + id + Separator + (string.IsNullOrWhiteSpace(name) ? "Missing macOS device" : name);

    public static bool TryDecode(string selection, out string id, out string name)
    {
        id = string.Empty;
        name = selection ?? string.Empty;
        if (string.IsNullOrWhiteSpace(selection) || !selection.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        var body = selection.Substring(Prefix.Length);
        var sep = body.IndexOf(Separator);
        if (sep < 0)
        {
            id = body;
            name = body;
            return !string.IsNullOrWhiteSpace(id);
        }

        id = body.Substring(0, sep);
        name = body.Substring(sep + 1);
        return !string.IsNullOrWhiteSpace(id);
    }

    public static string Describe(string selection)
        => TryDecode(selection, out _, out var name) ? name : string.IsNullOrWhiteSpace(selection) ? "Default" : selection;
}
#endif
