using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace VoiceChatPlugin;

internal sealed class PerfectCommsUpdateInfo
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("test")] public bool Test { get; set; }
    [JsonPropertyName("latestVersion")] public string LatestVersion { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "Perfect Comms update available";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("releaseUrl")] public string ReleaseUrl { get; set; } = "";
    [JsonPropertyName("showEveryMainMenu")] public bool ShowEveryMainMenu { get; set; }
}

internal static class PerfectCommsUpdateClient
{
    private const string DefaultUrl = "https://perfect-comms-lobbies.edgetel.workers.dev/updates/latest";

    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    internal static async Task<PerfectCommsUpdateInfo?> GetLatestAsync(string configuredUrl)
    {
        var url = BuildUrl(configuredUrl);
        using var response = await Client.GetAsync(url).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<PerfectCommsUpdateInfo>(text, JsonOptions);
    }

    internal static bool IsNewerThanCurrent(string latestVersion)
        => CompareVersions(latestVersion, VoiceChatPluginMain.Version) > 0;

    private static string BuildUrl(string configuredUrl)
    {
        var baseUrl = string.IsNullOrWhiteSpace(configuredUrl) ? DefaultUrl : configuredUrl.Trim();
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            baseUrl = DefaultUrl;

        var separator = baseUrl.Contains('?') ? '&' : '?';
        return baseUrl + separator + "current=" + Uri.EscapeDataString(VoiceChatPluginMain.Version);
    }

    private static int CompareVersions(string left, string right)
    {
        var leftParts = SplitVersion(left);
        var rightParts = SplitVersion(right);
        var count = Math.Max(leftParts.Length, rightParts.Length);
        for (var i = 0; i < count; i++)
        {
            var a = i < leftParts.Length ? leftParts[i] : 0;
            var b = i < rightParts.Length ? rightParts[i] : 0;
            if (a != b) return a.CompareTo(b);
        }
        return 0;
    }

    private static int[] SplitVersion(string value)
    {
        value = (value ?? "").Trim();
        if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase)) value = value[1..];
        var dash = value.IndexOfAny(new[] { '-', '+' });
        if (dash >= 0) value = value[..dash];
        var parts = value.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var numbers = new int[Math.Min(parts.Length, 4)];
        for (var i = 0; i < numbers.Length; i++)
            numbers[i] = int.TryParse(parts[i], out var parsed) ? parsed : 0;
        return numbers;
    }
}
