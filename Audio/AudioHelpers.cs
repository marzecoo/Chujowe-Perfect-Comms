using Concentus;
using Concentus.Enums;
using Concentus.Structs;
using System;

namespace VoiceChatPlugin.Audio;

internal static class AudioHelpers
{
    public const int ClockRate  = 48000;
    public const int FrameSize  = 960;   // 20 ms @ 48 kHz
    public const int Channels   = 1;     // mono capture
    public const int PlaybackPrebufferSamples = FrameSize * 5; // 100 ms jitter cushion for HTTP/modded RPC jitter
    public const int PlaybackRecoveryPrebufferSamples = FrameSize * 2; // 40 ms after stream already started
    public const int PlaybackMaxPrebufferWaitMilliseconds = 120; // do not strand short utterances forever
    public const int PlaybackEdgeFadeSamples = FrameSize / 2; // 10 ms, removes start/end clicks without eating words

    private static readonly object WarmupLock = new();
    private static readonly float[] SilenceFrame = new float[FrameSize];
    private static byte[]? _decoderWarmupPacket;

#pragma warning disable CS0618
    public static IOpusEncoder GetOpusEncoder()
    {
        var enc = CreateConfiguredEncoder();
        WarmEncoder(enc);
        return enc;
    }

    private static OpusEncoder CreateConfiguredEncoder()
    {
        var enc = new OpusEncoder(ClockRate, Channels, OpusApplication.OPUS_APPLICATION_VOIP);

        // Keep voice packets small enough for real-time game RPC transport.
        enc.Bitrate    = 32_000;

        // Mid-high complexity keeps quality good without starving capture/update
        // threads on weaker clients.
        enc.Complexity = 6;

        // Keep VBR so quiet passages still compress well.
        enc.UseVBR     = true;

        // DTX (discontinuous transmission) OFF — it caused brief cut-ins at the
        // start of each utterance which felt like the mic was "muting itself".
        enc.UseDTX     = false;

        // FEC/PLC concealment caused occasional static bursts on packet gaps in
        // the game RPC transport. Prefer a tiny dropout over synthetic noise.
        enc.UseInbandFEC     = false;
        enc.PacketLossPercent = 0;

        return enc;
    }

    public static IOpusDecoder GetOpusDecoder()
    {
        var decoder = new OpusDecoder(ClockRate, Channels);
        WarmDecoder(decoder);
        return decoder;
    }

    private static void WarmEncoder(IOpusEncoder encoder)
    {
        try
        {
            Span<byte> scratch = stackalloc byte[64];
            encoder.Encode(SilenceFrame, FrameSize, scratch, scratch.Length);
        }
        catch
        {
            // Warmup is best-effort; actual encode path still handles errors.
        }
    }

    private static void WarmDecoder(IOpusDecoder decoder)
    {
        try
        {
            var packet = GetDecoderWarmupPacket();
            if (packet.Length == 0) return;

            var scratch = new float[FrameSize];
            decoder.Decode(packet, scratch, FrameSize, false);
        }
        catch
        {
            // Warmup is best-effort; actual decode path still handles errors.
        }
    }

    private static byte[] GetDecoderWarmupPacket()
    {
        if (_decoderWarmupPacket != null) return _decoderWarmupPacket;

        lock (WarmupLock)
        {
            if (_decoderWarmupPacket != null) return _decoderWarmupPacket;

            var encoder = CreateConfiguredEncoder();
            var buffer = new byte[64];
            int bytes = encoder.Encode(SilenceFrame, FrameSize, buffer, buffer.Length);
            if (bytes <= 0)
                return _decoderWarmupPacket = Array.Empty<byte>();

            var packet = new byte[bytes];
            Array.Copy(buffer, packet, bytes);
            _decoderWarmupPacket = packet;
            return packet;
        }
    }
#pragma warning restore CS0618
}
