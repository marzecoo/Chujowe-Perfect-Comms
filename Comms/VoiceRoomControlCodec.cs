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
    private const byte Version = 13;
    private const byte LegacyVersion12 = 12;
    private const byte LegacyVersion11 = 11;
    private const byte LegacyVersion10 = 10;
    private const byte LegacyVersion9 = 9;
    private const byte LegacyVersion8 = 8;
    private const byte LegacyVersion7 = 7;
    private const byte LegacyVersion6 = 6;
    private const byte LegacyVersion5 = 5;
    private const byte LegacyVersion4 = 4;
    private const int HeaderBytes = 4;
    private const int LegacyFixedSettingsBytesV4 = 4 + 4 + 4 + 4 + 17;
    private const int LegacyFixedSettingsBytesV5 = 4 + 4 + 4 + 4 + 18;
    private const int LegacyFixedSettingsBytesV6 = 4 + 4 + 4 + 4 + 21;
    private const int LegacyFixedSettingsBytesV7 = 4 + 4 + 4 + 4 + 21 + 4;
    private const int LegacyFixedSettingsBytesV8 = 4 + 4 + 4 + 4 + 21 + 4 + 1;
    private const int LegacyFixedSettingsBytesV9 = 4 + 4 + 4 + 4 + 21 + 4 + 3;
    private const int LegacyFixedSettingsBytesV10 = 4 + 4 + 4 + 4 + 21 + 4 + 7;
    private const int LegacyFixedSettingsBytesV11 = 4 + 4 + 4 + 4 + 23 + 4 + 11;
    private const int LegacyFixedSettingsBytesV12 = 4 + 4 + 4 + 4 + 24 + 4 + 11;
    private const int FixedSettingsBytes = 4 + 4 + 4 + 4 + 24 + 4 + 12;
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
        if (payload[0] != Magic0 || payload[1] != Magic1 || !IsSupportedVersion(version)) return false;

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
        buffer[23] = ToByte(settings.TeamRadio);
        buffer[24] = ToByte(settings.TeamRadioImpostors);
        buffer[25] = ToByte(settings.TeamRadioVampires);
        buffer[26] = ToByte(settings.TeamRadioLovers);
        buffer[27] = ToByte(settings.TeamRadioRecruits);
        buffer[28] = ToByte(settings.TeamRadioLawyer);
        buffer[29] = ToByte(settings.OnlyGhostsCanTalk);
        buffer[30] = ToByte(settings.OnlyMeetingOrLobby);
        buffer[31] = ToByte(settings.OnlyMeetingOrLobbyAffectsGhosts);
        buffer[32] = ToByte(settings.MuteBlackmailedInMeetings);
        buffer[33] = ToByte(settings.MuteBlackmailedNextRound);
        buffer[34] = ToByte(settings.MuteJailedInMeetings);
        buffer[35] = ToByte(settings.JailorCanUnmuteJailed);
        buffer[36] = ToByte(settings.MuteParasiteControlled);
        buffer[37] = ToByte(settings.MutePuppeteerControlled);
        buffer[38] = ToByte(settings.CrewpostorUsesImpostorVoice);
        buffer[39] = ToByte(settings.MuteSwooperWhileSwooped);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[40..], settings.MediumGhostVoice);
        buffer[44] = ToByte(settings.TouMceHackerJamMutesVoice);
        buffer[45] = ToByte(settings.MuteGlitchHacked);
        buffer[46] = ToByte(settings.MuffleBlindedOrFlashedHearing);
        buffer[47] = ToByte(settings.MuffleHypnotizedDuringHysteria);
        buffer[48] = ToByte(settings.TeamRadioInMeetings);
        buffer[49] = ToByte(settings.TouMcePelicanBellyVoice);
        buffer[50] = ToByte(settings.TouMceRecruitVoice);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[51..], settings.TouMceSpiritMasterGhostVoice);
        buffer[55] = ToByte(settings.TouMceLawyerClientVoice);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[FixedSettingsBytes..], checked((ushort)serverUrlBytes.Length));
        serverUrlBytes.CopyTo(buffer[(FixedSettingsBytes + 2)..]);
    }

    private static bool TryReadSettings(ReadOnlySpan<byte> buffer, byte version, out VoiceRoomSettingsSnapshot settings)
    {
        settings = default;
        var fixedSettingsBytes = FixedSettingsBytesForVersion(version);
        if (fixedSettingsBytes < 0) return false; // fail closed: unknown/unmapped version
        if (buffer.Length < fixedSettingsBytes + 2) return false;
        var serverUrlLength = BinaryPrimitives.ReadUInt16LittleEndian(buffer[fixedSettingsBytes..]);
        if (serverUrlLength > MaxServerUrlBytes || buffer.Length != fixedSettingsBytes + 2 + serverUrlLength) return false;
        var serverUrl = System.Text.Encoding.UTF8.GetString(buffer.Slice(fixedSettingsBytes + 2, serverUrlLength));
        bool isCurrent = version == Version;
        bool hasTouMceTeamRadioSettings = isCurrent || version is LegacyVersion12 or LegacyVersion11;
        bool hasTeamRadioSubSettings = hasTouMceTeamRadioSettings || version is LegacyVersion10 or LegacyVersion9 or LegacyVersion8 or LegacyVersion7 or LegacyVersion6;
        int tailOffset = hasTouMceTeamRadioSettings ? 29 : hasTeamRadioSubSettings ? 27 : 24;
        bool hasMediumGhostVoice = isCurrent || version is LegacyVersion12 or LegacyVersion11 or LegacyVersion10 or LegacyVersion9 or LegacyVersion8 or LegacyVersion7;
        bool hasMuteGlitchHacked = isCurrent || version is LegacyVersion12 or LegacyVersion11 or LegacyVersion10 or LegacyVersion9 or LegacyVersion8;
        bool hasListenerMuffleSettings = isCurrent || version is LegacyVersion12 or LegacyVersion11 or LegacyVersion10 or LegacyVersion9;
        bool hasTouMceVoiceSettings = isCurrent || version is LegacyVersion12 or LegacyVersion11 or LegacyVersion10;
        bool hasHackerJamSetting = isCurrent || version is LegacyVersion12 or LegacyVersion11;
        bool hasMeetingLobbyGhostSetting = isCurrent || version == LegacyVersion12;
        bool hasTeamRadioInMeetings = isCurrent;
        int roleTailOffset = tailOffset + (hasMeetingLobbyGhostSetting ? 1 : 0);
        int touMceVoiceOffset = roleTailOffset + (hasHackerJamSetting ? 18 : 17) + (hasTeamRadioInMeetings ? 1 : 0);

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
            !hasTeamRadioSubSettings || buffer[24] != 0,
            hasTeamRadioSubSettings && buffer[25] != 0,
            hasTeamRadioSubSettings && buffer[26] != 0,
            hasTouMceTeamRadioSettings ? buffer[27] != 0 : true,
            hasTouMceTeamRadioSettings ? buffer[28] != 0 : true,
            buffer[tailOffset] != 0,
            buffer[tailOffset + 1] != 0,
            hasMeetingLobbyGhostSetting ? buffer[tailOffset + 2] != 0 : false,
            buffer[roleTailOffset + 2] != 0,
            buffer[roleTailOffset + 3] != 0,
            buffer[roleTailOffset + 4] != 0,
            buffer[roleTailOffset + 5] != 0,
            buffer[roleTailOffset + 6] != 0,
            buffer[roleTailOffset + 7] != 0,
            buffer[roleTailOffset + 8] != 0,
            version == LegacyVersion4 || buffer[roleTailOffset + 9] != 0,
            hasMediumGhostVoice
                ? BinaryPrimitives.ReadInt32LittleEndian(buffer[(roleTailOffset + 10)..])
                : (int)MediumGhostVoiceMode.None,
            hasHackerJamSetting ? buffer[roleTailOffset + 14] != 0 : true,
            hasMuteGlitchHacked ? buffer[roleTailOffset + (hasHackerJamSetting ? 15 : 14)] != 0 : true,
            hasListenerMuffleSettings ? buffer[roleTailOffset + (hasHackerJamSetting ? 16 : 15)] != 0 : true,
            hasListenerMuffleSettings ? buffer[roleTailOffset + (hasHackerJamSetting ? 17 : 16)] != 0 : true,
            hasTeamRadioInMeetings && buffer[roleTailOffset + 18] != 0,
            hasTouMceVoiceSettings ? buffer[touMceVoiceOffset] != 0 : true,
            hasTouMceVoiceSettings ? buffer[touMceVoiceOffset + 1] != 0 : true,
            hasHackerJamSetting
                ? BinaryPrimitives.ReadInt32LittleEndian(buffer[(touMceVoiceOffset + 2)..])
                : hasTouMceVoiceSettings && buffer[touMceVoiceOffset + 1] != 0 ? (int)MediumGhostVoiceMode.Both : (int)MediumGhostVoiceMode.None,
            hasHackerJamSetting
                ? buffer[touMceVoiceOffset + 6] != 0
                : hasTouMceVoiceSettings && buffer[touMceVoiceOffset + 2] != 0).Clamp();
        return true;
    }

    private static bool IsSupportedVersion(byte version)
        => version is Version or LegacyVersion12 or LegacyVersion11 or LegacyVersion10 or LegacyVersion9 or LegacyVersion8 or LegacyVersion7 or LegacyVersion6 or LegacyVersion5 or LegacyVersion4;

    private static int FixedSettingsBytesForVersion(byte version)
        => version switch
        {
            LegacyVersion4 => LegacyFixedSettingsBytesV4,
            LegacyVersion5 => LegacyFixedSettingsBytesV5,
            LegacyVersion6 => LegacyFixedSettingsBytesV6,
            LegacyVersion7 => LegacyFixedSettingsBytesV7,
            LegacyVersion8 => LegacyFixedSettingsBytesV8,
            LegacyVersion9 => LegacyFixedSettingsBytesV9,
            LegacyVersion10 => LegacyFixedSettingsBytesV10,
            LegacyVersion11 => LegacyFixedSettingsBytesV11,
            LegacyVersion12 => LegacyFixedSettingsBytesV12,
            Version => FixedSettingsBytes,
            _ => -1, // fail closed: reject unknown versions instead of guessing the current layout
        };

    private static byte ToByte(bool value) => value ? (byte)1 : (byte)0;
}
