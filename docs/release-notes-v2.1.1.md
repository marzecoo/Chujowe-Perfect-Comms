This Perfect Comms release fixes an echo / doubled-voice bug that built up during a match whenever players' connections dropped and reconnected.

<p align="center">
  <img src="https://raw.githubusercontent.com/artriy/Perfect-Comms/v2.1.1/assets/brand/divider.svg" alt="divider" width="900">
</p>

### What's Changed

- Fixed an "echoey" / doubled-voice bug: when a player's connection rotated (a drop-and-reconnect), their previous voice channel - decoder, jitter buffer, and playback - was orphaned instead of being torn down. The same player could then be heard more than once across the reconnect, and unused "zombie" channels accumulated over the match. Each player now keeps exactly one live voice channel, and the superseded one is disposed on reconnect.
- Reduced the steady build-up of wasted voice processing those leftover channels caused, so longer games stay lighter on CPU and audio.
