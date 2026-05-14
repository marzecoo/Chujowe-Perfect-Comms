using System;
using System.Diagnostics;
using System.IO;
using BepInEx;
using UnityEngine;

namespace VoiceChatPlugin.VoiceChat;

internal static class VoiceDiagnostics
{
    private const double FlushIntervalSeconds = 5.0;
    private static readonly object Lock = new();
    private static readonly DateTime StartedUtc = DateTime.UtcNow;
    private static StreamWriter? _writer;
    private static string _path = "";
    private static DateTime _lastFlushUtc = StartedUtc;

    public static string Path => _path;

    public static void Init()
    {
        lock (Lock)
        {
            if (_writer != null) return;

            try
            {
                string root = System.IO.Path.Combine(Paths.BepInExRootPath, "VoiceChatDiagnostics");
                Directory.CreateDirectory(root);
                int pid = Process.GetCurrentProcess().Id;
                string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
                _path = System.IO.Path.Combine(root, $"voicechat_{stamp}_pid{pid}.log");
                _writer = new StreamWriter(new FileStream(_path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite))
                {
                    AutoFlush = false,
                };

                WriteLocked("diagnostics.start", $"path={_path} pid={pid} utc={StartedUtc:o} version=extreme-1");
            }
            catch (Exception ex)
            {
                VoiceChatPluginMain.Logger.LogError($"[VC] Diagnostics init failed: {ex.Message}");
            }
        }
    }

    public static void Log(string category, string message)
    {
        lock (Lock)
            WriteLocked(category, message);
    }

    private static void WriteLocked(string category, string message)
    {
        if (_writer == null) return;

        double elapsed = (DateTime.UtcNow - StartedUtc).TotalSeconds;
        _writer.WriteLine(
            $"{DateTime.UtcNow:o} +{elapsed:0.000}s frame={SafeFrame()} {SafeIdentity()} {category} {message}");
        MaybeFlushLocked(category);
    }

    private static void MaybeFlushLocked(string category)
    {
        if (_writer == null) return;

        var now = DateTime.UtcNow;
        if (category is "diagnostics.start" or "room.close" or "transition.perf.slowUpdate" or "transition.audio.clip" ||
            category.StartsWith("audio.buffer.", StringComparison.Ordinal) ||
            (now - _lastFlushUtc).TotalSeconds >= FlushIntervalSeconds)
        {
            _writer.Flush();
            _lastFlushUtc = now;
        }
    }

    private static int SafeFrame()
    {
        try { return Time.frameCount; }
        catch { return -1; }
    }

    private static string SafeIdentity()
    {
        try
        {
            int clientId = AmongUsClient.Instance?.ClientId ?? -1;
            var player = PlayerControl.LocalPlayer;
            int playerId = player != null ? player.PlayerId : -1;
            string name = player?.Data?.PlayerName ?? "unknown";
            return $"client={clientId} player={playerId} name=\"{Sanitize(name)}\"";
        }
        catch
        {
            return "client=-1 player=-1 name=\"unknown\"";
        }
    }

    private static string Sanitize(string value)
        => value.Replace("\r", " ").Replace("\n", " ").Replace("\"", "'");
}
