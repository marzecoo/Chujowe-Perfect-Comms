using System;
using System.Buffers.Binary;

namespace VoiceChatPlugin.VoiceChat;

public enum VoiceRoomControlMessageKind : byte
{
    HostSettingsSnapshot = 1,
    HostSettingsRequest = 2,
}

public readonly record struct VoiceRoomControlMessage(
    VoiceRoomControlMessageKind Kind,
    VoiceRoomSettingsSnapshot Settings);

public static class VoiceRoomControlCodec
{
    private const byte Magic0 = (byte)'P';
    private const byte Magic1 = (byte)'C';
    private const byte Version = 5;
    private const byte LegacyVersion = 4;
    private const int HeaderBytes = 4;
    private const int LegacyFixedSettingsBytes = 4 + 4 + 4 + 4 + 17;
    private const int FixedSettingsBytes = 4 + 4 + 4 + 4 + 18;
    private const int MaxServerUrlBytes = 512;

    public static byte[] EncodeHostSettingsSnapshot(VoiceRoomSettingsSnapshot settings)
    {
        var serverUrlBytes = System.Text.Encoding.UTF8.GetBytes(settings.BackendServerUrl ?? string.Empty);
        if (serverUrlBytes.Length > MaxServerUrlBytes)
            serverUrlBytes = serverUrlBytes.AsSpan(0, MaxServerUrlBytes).ToArray();

        var buffer = new byte[HeaderBytes + FixedSettingsBytes + 2 + serverUrlBytes.Length];
        WriteHeader(buffer, VoiceRoomControlMessageKind.HostSettingsSnapshot);
        WriteSettings(buffer.AsSpan(HeaderBytes), settings.Clamp(), serverUrlBytes);
        return buffer;
    }

    public static byte[] EncodeHostSettingsRequest()
    {
        var buffer = new byte[HeaderBytes];
        WriteHeader(buffer, VoiceRoomControlMessageKind.HostSettingsRequest);
        return buffer;
    }

    public static bool TryDecode(ReadOnlySpan<byte> payload, out VoiceRoomControlMessage message)
    {
        message = default;
        if (payload.Length < HeaderBytes) return false;
        var version = payload[2];
        if (payload[0] != Magic0 || payload[1] != Magic1 || version is not (Version or LegacyVersion)) return false;

        var kind = (VoiceRoomControlMessageKind)payload[3];
        if (kind == VoiceRoomControlMessageKind.HostSettingsRequest)
        {
            if (payload.Length != HeaderBytes) return false;
            message = new VoiceRoomControlMessage(kind, default);
            return true;
        }

        if (kind == VoiceRoomControlMessageKind.HostSettingsSnapshot)
        {
            if (!TryReadSettings(payload[HeaderBytes..], version, out var settings)) return false;
            message = new VoiceRoomControlMessage(kind, settings);
            return true;
        }

        return false;
    }

    private static void WriteHeader(Span<byte> buffer, VoiceRoomControlMessageKind kind)
    {
        buffer[0] = Magic0;
        buffer[1] = Magic1;
        buffer[2] = Version;
        buffer[3] = (byte)kind;
    }

    private static void WriteSettings(Span<byte> buffer, VoiceRoomSettingsSnapshot settings, ReadOnlySpan<byte> serverUrlBytes)
    {
        BinaryPrimitives.WriteInt32LittleEndian(buffer, settings.Backend);
        BinaryPrimitives.WriteSingleLittleEndian(buffer[4..], settings.MaxChatDistance);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[8..], settings.FalloffMode);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[12..], settings.OcclusionMode);
        buffer[16] = ToByte(settings.WallsBlockSound);
        buffer[17] = ToByte(settings.OnlyHearInSight);
        buffer[18] = ToByte(settings.ImpostorHearGhosts);
        buffer[19] = ToByte(settings.HearInVent);
        buffer[20] = ToByte(settings.VentPrivateChat);
        buffer[21] = ToByte(settings.CommsSabDisables);
        buffer[22] = ToByte(settings.CameraCanHear);
        buffer[23] = ToByte(settings.ImpostorPrivateRadio);
        buffer[24] = ToByte(settings.OnlyGhostsCanTalk);
        buffer[25] = ToByte(settings.OnlyMeetingOrLobby);
        buffer[26] = ToByte(settings.MuteBlackmailedInMeetings);
        buffer[27] = ToByte(settings.MuteBlackmailedNextRound);
        buffer[28] = ToByte(settings.MuteJailedInMeetings);
        buffer[29] = ToByte(settings.JailorCanUnmuteJailed);
        buffer[30] = ToByte(settings.MuteParasiteControlled);
        buffer[31] = ToByte(settings.MutePuppeteerControlled);
        buffer[32] = ToByte(settings.CrewpostorUsesImpostorVoice);
        buffer[33] = ToByte(settings.MuteSwooperWhileSwooped);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[FixedSettingsBytes..], checked((ushort)serverUrlBytes.Length));
        serverUrlBytes.CopyTo(buffer[(FixedSettingsBytes + 2)..]);
    }

    private static bool TryReadSettings(ReadOnlySpan<byte> buffer, byte version, out VoiceRoomSettingsSnapshot settings)
    {
        settings = default;
        var fixedSettingsBytes = version == LegacyVersion ? LegacyFixedSettingsBytes : FixedSettingsBytes;
        if (buffer.Length < fixedSettingsBytes + 2) return false;
        var serverUrlLength = BinaryPrimitives.ReadUInt16LittleEndian(buffer[fixedSettingsBytes..]);
        if (serverUrlLength > MaxServerUrlBytes || buffer.Length != fixedSettingsBytes + 2 + serverUrlLength) return false;
        var serverUrl = System.Text.Encoding.UTF8.GetString(buffer.Slice(fixedSettingsBytes + 2, serverUrlLength));
        settings = new VoiceRoomSettingsSnapshot(
            BinaryPrimitives.ReadInt32LittleEndian(buffer),
            serverUrl,
            BinaryPrimitives.ReadSingleLittleEndian(buffer[4..]),
            BinaryPrimitives.ReadInt32LittleEndian(buffer[8..]),
            BinaryPrimitives.ReadInt32LittleEndian(buffer[12..]),
            buffer[16] != 0,
            buffer[17] != 0,
            buffer[18] != 0,
            buffer[19] != 0,
            buffer[20] != 0,
            buffer[21] != 0,
            buffer[22] != 0,
            buffer[23] != 0,
            buffer[24] != 0,
            buffer[25] != 0,
            buffer[26] != 0,
            buffer[27] != 0,
            buffer[28] != 0,
            buffer[29] != 0,
            buffer[30] != 0,
            buffer[31] != 0,
            buffer[32] != 0,
            version == LegacyVersion || buffer[33] != 0).Clamp();
        return true;
    }

    private static byte ToByte(bool value) => value ? (byte)1 : (byte)0;
}
