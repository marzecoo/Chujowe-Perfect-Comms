using System;

namespace VoiceChatPlugin.VoiceChat;

internal sealed class VoiceNetworkStats
{
    private readonly object _lock = new();
    private DateTime _windowStartedUtc = DateTime.UtcNow;

    private int _sentPackets;
    private int _sentBytes;
    private int _receivedPackets;
    private int _receivedBytes;
    private int _sendQueueDrops;
    private int _receiveQueueDrops;
    private int _staleDrops;
    private int _badPackets;
    private int _sequenceGaps;
    private int _decodeErrors;

    public int SendQueueDepth { get; private set; }
    public int ReceiveQueueDepth { get; private set; }

    public void SetQueueDepths(int sendQueueDepth, int receiveQueueDepth)
    {
        lock (_lock)
        {
            SendQueueDepth = sendQueueDepth;
            ReceiveQueueDepth = receiveQueueDepth;
        }
    }

    public void CountSent(int bytes)
    {
        lock (_lock)
        {
            _sentPackets++;
            _sentBytes += Math.Max(0, bytes);
        }
    }

    public void CountReceived(int bytes)
    {
        lock (_lock)
        {
            _receivedPackets++;
            _receivedBytes += Math.Max(0, bytes);
        }
    }

    public void CountSendQueueDrop() { lock (_lock) _sendQueueDrops++; }
    public void CountReceiveQueueDrop() { lock (_lock) _receiveQueueDrops++; }
    public void CountStaleDrop() { lock (_lock) _staleDrops++; }
    public void CountBadPacket() { lock (_lock) _badPackets++; }
    public void CountSequenceGap(int gap) { lock (_lock) _sequenceGaps += Math.Max(1, gap); }
    public void CountDecodeError() { lock (_lock) _decodeErrors++; }

    public string? ConsumeSnapshotIfDue(TimeSpan interval)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            var elapsed = now - _windowStartedUtc;
            if (elapsed < interval) return null;

            double seconds = Math.Max(0.001, elapsed.TotalSeconds);
            bool hasActivity = _sentPackets != 0 ||
                               _receivedPackets != 0 ||
                               _sendQueueDrops != 0 ||
                               _receiveQueueDrops != 0 ||
                               _staleDrops != 0 ||
                               _badPackets != 0 ||
                               _sequenceGaps != 0 ||
                               _decodeErrors != 0;

            if (!hasActivity)
            {
                Reset(now);
                return null;
            }

            string snapshot =
                $"sent={_sentPackets / seconds:0.0}pkt/s {_sentBytes / seconds:0}B/s " +
                $"recv={_receivedPackets / seconds:0.0}pkt/s {_receivedBytes / seconds:0}B/s " +
                $"queues=send:{SendQueueDepth} recv:{ReceiveQueueDepth} " +
                $"drops=send:{_sendQueueDrops} recv:{_receiveQueueDrops} stale:{_staleDrops} " +
                $"gaps={_sequenceGaps} bad={_badPackets} decodeErrors={_decodeErrors}";

            Reset(now);
            return snapshot;
        }
    }

    private void Reset(DateTime now)
    {
        _windowStartedUtc = now;
        _sentPackets = 0;
        _sentBytes = 0;
        _receivedPackets = 0;
        _receivedBytes = 0;
        _sendQueueDrops = 0;
        _receiveQueueDrops = 0;
        _staleDrops = 0;
        _badPackets = 0;
        _sequenceGaps = 0;
        _decodeErrors = 0;
    }
}
