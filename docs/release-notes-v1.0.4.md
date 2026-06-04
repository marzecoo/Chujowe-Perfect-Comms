This release merges the fork with the original Perfect Comms v2.1.3 while maintaining all Mega Chujowe / TouMCE modifications.

### What's Changed

- **Merged with upstream Perfect Comms v2.1.3:**
  - **Much less choppy/robotic voice:** The playback buffer now adapts to each player's connection (adaptive jitter buffer), deepening when their audio is arriving unevenly and easing back down between sentences.
  - **Fewer voice drop-outs from bad packets:** Short gaps are concealed instead of cutting out.
  - **Auto-repairing voice links:** Fixed connection hangs when two players join a new lobby together.
  - **Steadier voice connections:** Fixed a recovery loop that could repeatedly reset all voice connections and tightened reconnection.
  - **Smoother framerate in large lobbies:** Trimmed voice-related per-frame work to reduce hitches at 12+ players.
- **Preserved Fork Features:**
  - **TouMCE Voice Rules:** Pelican belly voice, Infiltrator Recruits, Lawyer/Client, Spirit Master ghost-mediation, hidden role mutes, and Hacker Jam.
  - **Team Radio channels:** Impostors, Vampires, Lovers, Recruits, and Lawyer/Client.
  - **Increased Volume Controls:** Up to 300% volume boost.
  - **Windows Only Optimization:** Kept the PC-only build pipeline (Android code omitted).
