This Perfect Comms release targets choppy / robotic voice (the kind where a player's speech breaks up or cuts in and out, especially at random and in fuller lobbies) and makes voice connections noticeably more reliable.

<p align="center">
  <img src="https://raw.githubusercontent.com/artriy/Perfect-Comms/v2.1.3/assets/brand/divider.svg" alt="divider" width="900">
</p>

### What's Changed

- **Much less choppy/robotic voice.** The playback buffer now adapts to each player's connection, deepening when their audio is arriving unevenly and easing back down between sentences, so brief network hiccups get smoothed over instead of turning into stutter.
- **Fewer voice drop-outs from bad packets.** A player who sends the occasional undecodable audio frame is no longer muted for several seconds; only a genuinely broken stream is parked, and short gaps are concealed instead of cutting out.
- **Players not hearing each other right after joining a fresh lobby is fixed.** When two players joined a brand-new lobby together, their voice link could get stuck so neither could hear the other until someone left and rejoined. The connection now repairs itself automatically.
- **Steadier voice connections.** Fixed a recovery loop that could repeatedly reset all of your voice connections when one player couldn't be reached, and tightened per-peer reconnection so a stuck connection re-establishes on its own. This reduces cases where you suddenly stop hearing someone mid-game while still being heard.
- **Smoother framerate in big lobbies.** Voice-related per-frame work that grew with the player count was trimmed, reducing hitches at 12+ players.
