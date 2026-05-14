using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace VoiceChatPlugin.VoiceChat;

internal readonly record struct VoiceOutgoingFrame(byte[] Payload, DateTime CreatedAtUtc);

internal readonly record struct VoiceIncomingPacket(
    int SenderId,
    byte PacketType,
    byte[] Data,
    int Sequence,
    byte PlayerId,
    string PlayerName,
    DateTime ReceivedAtUtc);

internal sealed class VoiceTransport
{
    private readonly ConcurrentQueue<VoiceOutgoingFrame> _sendQueue = new();
    private readonly ConcurrentQueue<VoiceIncomingPacket> _receiveQueue = new();
    private readonly object _stateLock = new();
    private readonly Dictionary<int, SenderTransportState> _senderTransport = new();
    private readonly Dictionary<string, DateTime> _lastThrottledLogUtc = new();
    private readonly VoiceNetworkStats _networkStats = new();

    public void EnqueueOutgoing(byte[] payload)
    {
        while (_sendQueue.Count >= VoiceProtocol.MaxQueuedSendFrames &&
               _sendQueue.TryDequeue(out _))
        {
            _networkStats.CountSendQueueDrop();
            VoiceDiagnostics.Log("drop.sendQueue", $"depth={_sendQueue.Count} max={VoiceProtocol.MaxQueuedSendFrames}");
        }

        _sendQueue.Enqueue(new VoiceOutgoingFrame(payload, DateTime.UtcNow));
    }

    public bool TryDequeueOutgoing(out VoiceOutgoingFrame frame)
        => _sendQueue.TryDequeue(out frame);

    public bool TryPeekOutgoing(out VoiceOutgoingFrame frame)
        => _sendQueue.TryPeek(out frame);

    public int OutgoingCount => _sendQueue.Count;

    public void DropQueuedOutgoingAsSendDrops()
    {
        while (_sendQueue.TryDequeue(out _))
        {
            _networkStats.CountSendQueueDrop();
            VoiceDiagnostics.Log("drop.noTargets", $"depth={_sendQueue.Count}");
        }
    }

    public int DropStaleOutgoing(double maxAgeSeconds)
    {
        int dropped = 0;
        var now = DateTime.UtcNow;
        while (_sendQueue.TryPeek(out var frame) &&
               (now - frame.CreatedAtUtc).TotalSeconds > maxAgeSeconds &&
               _sendQueue.TryDequeue(out _))
        {
            dropped++;
            _networkStats.CountStaleDrop();
            VoiceDiagnostics.Log("drop.staleSendQueue", $"depth={_sendQueue.Count}");
        }

        return dropped;
    }

    public void QueueIncomingAudio(int senderId, byte[] encoded)
        => QueueIncomingAudio(senderId, 0, "", encoded);

    public void QueueIncomingAudio(int senderId, byte playerId, string playerName, byte[] encoded)
        => QueueIncomingPacket(new VoiceIncomingPacket(
            senderId,
            (byte)VoicePacketType.Audio,
            encoded,
            0,
            playerId,
            playerName,
            DateTime.UtcNow));

    public void QueueIncomingProfile(int senderId, byte playerId, string playerName)
        => QueueIncomingPacket(new VoiceIncomingPacket(
            senderId,
            (byte)VoicePacketType.Profile,
            Array.Empty<byte>(),
            0,
            playerId,
            playerName,
            DateTime.UtcNow));

    public bool TryDequeueIncoming(out VoiceIncomingPacket packet)
        => _receiveQueue.TryDequeue(out packet);

    public void PruneSender(int senderId)
    {
        lock (_stateLock)
            _senderTransport.Remove(senderId);
    }

    public void Clear()
    {
        while (_sendQueue.TryDequeue(out _)) { }
        while (_receiveQueue.TryDequeue(out _)) { }
        lock (_stateLock)
        {
            _senderTransport.Clear();
            _lastThrottledLogUtc.Clear();
        }
    }

    public void CountSent(int bytes) => _networkStats.CountSent(bytes);
    public void CountReceived(int bytes) => _networkStats.CountReceived(bytes);
    public void CountStaleDrop() => _networkStats.CountStaleDrop();
    public void CountBadPacket() => _networkStats.CountBadPacket();
    public void CountSequenceGap(int gap) => _networkStats.CountSequenceGap(gap);
    public void CountDecodeError() => _networkStats.CountDecodeError();

    public void MaybeLogNetworkStats(bool debugEnabled)
    {
        _networkStats.SetQueueDepths(_sendQueue.Count, _receiveQueue.Count);

        var snapshot = _networkStats.ConsumeSnapshotIfDue(VoiceProtocol.StatsLogInterval);
        if (snapshot != null)
        {
            if (debugEnabled)
            {
                VoiceDiagnostics.Log("network", snapshot);
                VoiceChatPluginMain.Logger.LogInfo("[VC] Network stats: " + snapshot);
            }
        }
    }

    public void LogThrottled(string key, string message)
    {
        var now = DateTime.UtcNow;
        lock (_stateLock)
        {
            if (_lastThrottledLogUtc.TryGetValue(key, out var last) &&
                (now - last).TotalSeconds < 5)
                return;

            _lastThrottledLogUtc[key] = now;
        }
        try { VoiceDiagnostics.Log("warning", message); }
        catch { /* diagnostics only */ }
    }

    private void QueueIncomingPacket(VoiceIncomingPacket packet)
    {
        if (packet.PacketType == (byte)VoicePacketType.Audio &&
            !AcceptIncomingAudioPacket(packet.SenderId, packet.Data.Length))
            return;

        while (_receiveQueue.Count >= VoiceProtocol.MaxQueuedReceivePackets &&
               _receiveQueue.TryDequeue(out _))
        {
            _networkStats.CountReceiveQueueDrop();
            VoiceDiagnostics.Log("drop.receiveQueue", $"depth={_receiveQueue.Count} max={VoiceProtocol.MaxQueuedReceivePackets}");
        }

        _receiveQueue.Enqueue(packet);
    }

    private bool AcceptIncomingAudioPacket(int senderId, int payloadLength)
    {
        if (!VoiceProtocol.IsValidAudioPayloadLength(payloadLength))
        {
            _networkStats.CountBadPacket();
            VoiceDiagnostics.Log("drop.badSize", $"sender={senderId} bytes={payloadLength} max={VoiceProtocol.MaxAudioPayloadBytes}");
            LogThrottled($"bad-size:{senderId}",
                $"[VC] Dropped invalid audio packet from {senderId}: {payloadLength} bytes.");
            return false;
        }

        lock (_stateLock)
        {
            if (!_senderTransport.TryGetValue(senderId, out var state))
            {
                state = new SenderTransportState();
                _senderTransport[senderId] = state;
            }

            var now = DateTime.UtcNow;
            if ((now - state.WindowStartUtc).TotalSeconds >= 1)
            {
                state.WindowStartUtc = now;
                state.Packets = 0;
                state.Bytes = 0;
            }

            state.Packets++;
            state.Bytes += payloadLength;

            if (state.Packets > VoiceProtocol.MaxSenderPacketsPerSecond ||
                state.Bytes > VoiceProtocol.MaxSenderBytesPerSecond)
            {
                _networkStats.CountBadPacket();
                VoiceDiagnostics.Log("drop.rate", $"sender={senderId} packets={state.Packets} bytes={state.Bytes}");
                LogThrottled($"rate:{senderId}",
                    $"[VC] Dropped audio packet from {senderId}: rate {state.Packets} pkt/s {state.Bytes} B/s.");
                return false;
            }
        }

        return true;
    }

    private sealed class SenderTransportState
    {
        public DateTime WindowStartUtc = DateTime.UtcNow;
        public int Packets;
        public int Bytes;
    }
}
