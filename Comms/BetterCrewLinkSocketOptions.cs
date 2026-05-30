using System;
using System.Collections.Generic;
using SocketIOClient;

namespace VoiceChatPlugin.VoiceChat;

internal static class BetterCrewLinkSocketOptions
{
    internal const string UserAgentHeader = "User-Agent";

    internal static string UserAgent => $"PerfectComms/{VoiceChatPluginMain.Version}";

    internal static SocketIOOptions Create()
    {
        return new SocketIOOptions
        {
            Reconnection = true,
            ReconnectionAttempts = int.MaxValue,
            // Bound connect/backoff so a dead server can't hang or tight-loop; reconnection stays unbounded for recovery.
            ConnectionTimeout = TimeSpan.FromSeconds(10),
            ReconnectionDelay = 1000,
            ReconnectionDelayMax = 10000,
            EIO = SocketIO.Core.EngineIO.V3,
            Transport = SocketIOClient.Transport.TransportProtocol.WebSocket,
            ExtraHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [UserAgentHeader] = UserAgent,
            },
        };
    }
}
