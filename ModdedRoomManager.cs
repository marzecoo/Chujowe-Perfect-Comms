using System;
using AmongUs.GameOptions;
using HarmonyLib;
using Hazel;
using InnerNet;
using VoiceChatPlugin.VoiceChat;

namespace VoiceChatPlugin;

public static class ModdedRoomManager
{
    public static readonly Guid ModGuid = new("a3f7c821-4b9e-4d62-bc50-1e2f83a97d04");

    internal const byte ModHandshakeRpcId = VoiceProtocol.HandshakeRpcId;

    [HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.HostGame))]
    public static class HostGamePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(InnerNetClient __instance,
            IGameOptions settings,
            GameFilterOptions filterOpts)
        {
            try
            {
                // Build the host-modded-game message ourselves.
                // Tags.HostModdedGame == 25 (byte).
                var msg = MessageWriter.Get(SendOption.Reliable);
                msg.StartMessage(25); // Tags.HostModdedGame

                // Standard HostGame body: serialized options + crossplay flags + filter
                msg.WriteBytesAndSize(GameOptionsManager.Instance.gameOptionsFactory.ToBytes(settings, false));
                msg.Write(CrossplayMode.GetCrossplayFlags());
                filterOpts.Serialize(msg);

                // Append our mod GUID so Innersloth can group us in their matchmaker
                msg.Write(ModGuid.ToByteArray());

                msg.EndMessage();
                __instance.SendOrDisconnect(msg);
                msg.Recycle();

                VoiceChatPluginMain.Logger.LogInfo(
                    $"[VC] HostModdedGame sent with GUID {ModGuid}");
            }
            catch (Exception ex)
            {
                VoiceChatPluginMain.Logger.LogError(
                    $"[VC] HostGamePatch failed, falling back to vanilla host: {ex.Message}");
                // Return true to let the original method run if our patch failed.
                return true;
            }

            // Return false = skip the original HostGame implementation.
            return false;
        }
    }

    // ── FindGame / matchmaking filter patch ──────────────────────────────────
    [HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.RequestGameList))]
    public static class FindGamePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(InnerNetClient __instance, IGameOptions settings,
            GameFilterOptions filterOpts)
        {
            // Let vanilla/Reactor HTTP matchmaking run. Reactor adds Client-Mods and records
            // Client-Mods-Processed, which is what unlocks public lobbies on compatible servers.
            VanillaLobbyDiagnostics.Limited("request-game-list", "request", $"InnerNetClient.RequestGameList passthrough state={__instance.GameState} netMode={__instance.NetworkMode} gameId={__instance.GameId}", first: 12, every: 60);
            return true;
        }
    }

    internal static void SendHandshake()
    {
        if (AmongUsClient.Instance == null || PlayerControl.LocalPlayer == null) return;
        try
        {
            byte[] guidBytes = ModGuid.ToByteArray();
            var w = AmongUsClient.Instance.StartRpcImmediately(
                PlayerControl.LocalPlayer.NetId,
                ModHandshakeRpcId,
                SendOption.Reliable, -1);
            w.WriteBytesAndSize(guidBytes);
            w.Write(VoiceProtocol.ProtocolVersion);
            w.Write(VoiceProtocol.MinCompatibleVersion);
            w.Write((uint)VoiceProtocol.CurrentFeatures);
            AmongUsClient.Instance.FinishRpcImmediately(w);
            VoiceDiagnostics.Log("handshake.rpc", "sent");
        }
        catch (Exception ex)
        {
            VoiceChatPluginMain.Logger.LogError($"[VC] Handshake send failed: {ex.Message}");
        }
    }

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.HandleRpc))]
    public static class HandshakeRpcPatch
    {
        [HarmonyPostfix]
        public static void Postfix(PlayerControl __instance, byte callId, MessageReader reader)
        {
            if (callId != ModHandshakeRpcId) return;
            try
            {
                int senderId = ResolveSenderId(__instance);
                byte[] guidBytes = reader.ReadBytesAndSize();
                if (guidBytes != null && guidBytes.Length == 16)
                {
                    var theirGuid = new Guid(guidBytes);
                    int protocolVersion = 0;
                    int minCompatibleVersion = 0;
                    VoiceFeatureFlags features = VoiceFeatureFlags.None;

                    if (reader.BytesRemaining >= 4)
                        protocolVersion = reader.ReadInt32();
                    if (reader.BytesRemaining >= 4)
                        minCompatibleVersion = reader.ReadInt32();
                    if (reader.BytesRemaining >= 4)
                        features = (VoiceFeatureFlags)reader.ReadUInt32();

                    bool changed = VoiceClientRegistry.MarkHandshake(
                        senderId,
                        __instance.PlayerId,
                        __instance.Data?.PlayerName ?? __instance.name,
                        ModGuid,
                        theirGuid,
                        protocolVersion,
                        minCompatibleVersion,
                        features);

                    if (changed)
                        VoiceChatPluginMain.Logger.LogInfo("[VC] Handshake: " + VoiceClientRegistry.Describe(senderId));
                    VoiceDiagnostics.Log("handshake.recv", VoiceClientRegistry.Describe(senderId));
                }
            }
            catch (Exception ex)
            {
                VoiceChatPluginMain.Logger.LogError($"[VC] Handshake parse error: {ex.Message}");
            }
        }

        private static int ResolveSenderId(PlayerControl player)
        {
            if (AmongUsClient.Instance != null)
            {
                var cl = AmongUsClient.Instance.GetClientFromCharacter(player);
                if (cl != null) return cl.Id;
            }

            return player.PlayerId + 1000;
        }
    }
}
