namespace VoiceChatPlugin.VoiceChat;

internal readonly record struct VoiceFrameHeader(int Sequence, VoiceFrameFlags Flags)
{
    public bool IsImpostorRadioActive => Flags.HasFlag(VoiceFrameFlags.ImpostorRadio);

    public static bool TryRead(byte[] payload, out VoiceFrameHeader header, out int encodedOffset, out int encodedCount)
    {
        header = default;
        encodedOffset = VoiceProtocol.AudioHeaderBytes;
        encodedCount = 0;

        if (!VoiceProtocol.IsValidAudioPayloadLength(payload.Length))
            return false;

        int seq = (payload[0] << 24) |
                  (payload[1] << 16) |
                  (payload[2] << 8) |
                  payload[3];
        var flags = (VoiceFrameFlags)payload[4];
        if ((flags & ~VoiceProtocol.AllowedFrameFlags) != 0)
            return false;

        header = new VoiceFrameHeader(seq, flags);
        encodedCount = payload.Length - VoiceProtocol.AudioHeaderBytes;
        return true;
    }
}
