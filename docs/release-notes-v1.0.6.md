# Mega Chujowe Perfect Comms v1.0.6

- **Merged with upstream Perfect Comms v2.1.7.**
- **Implemented Voodoo role voice mechanics:** Adds support for hidden muting, next-round persistence toggle, and mute-in-meeting toggle, fully integrated with the TownOfUsMegaChujoweExtension.
- **Fixed Doppelganger visual bug:** Clears the active disguise modifier at meeting start so player avatars on meeting cards and speaking indicators correctly match their true identity.
- **Set BetterCrewLink as the default voice backend:** The default Voice Backend in both game options and room settings snapshots is now set to BetterCrewLink.
- **Maintained local optimizations:** Keeps local fork adjustments such as the 300% maximum volume scale, and custom names/branding.

---

## Upstream Release Notes (v2.1.4 to v2.1.7)

This release includes major audio-quality, performance, and stability improvements from upstream:

### Voice Quality & Playback
- **Smoother voice all around:** Volume and stereo panning now glide smoothly instead of stepping, removing clicks and pops when moving around, crossing walls, or nearing range edges.
- **Bluetooth headset recovery:** Mic and speaker usage on Bluetooth headsets (e.g. AirPods) is automatically recovered, switching back to full listening quality when muted.
- **Self-healing playback:** Stalled or erroring audio output devices will restart themselves automatically.
- **Quiet mic enhancements:** Microphone boost now survives mute cycles, low-frequency rumble is filtered from tripping the voice gate, and unmuting is instant.

### Network & Synchronization
- **Adaptive Voice Delay:** Stable connections get snappier voice response times, while laggy connections automatically get more cushion to prevent stutters.
- **Adaptive Packet Loss Protection:** Loss protection is dynamically adjusted per-player based on their network stability.
- **Stutter and Echo fixes:** Resolved a packet-loss bug that repeated the same split-second of audio during packet drops.
- **Jailor unmute persistence:** The Jailor's unmute in meetings now re-confirms itself automatically so no player gets stuck muted.

### UI & Mixer
- **Live per-player volume mixer:** The volume menu features a live roster showing player avatars and live voice level meters for easy adjustment.
- **Vision-independent menus:** The volume mixer and settings stay fully visible during map blackouts and low-vision phases.
- **On-the-fly voice mode hotkey:** Assign a key to toggle between Open Mic and Push-to-Talk instantly.
- **Speaking-bar names:** Indicators now match in-game nameplates (font and outline) for readability.
- **Spectator voice routing:** Role-based spectators are routed as voice ghosts so they hear and speak like other dead players.
