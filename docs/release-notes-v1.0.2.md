Mega Chujowe Perfect Comms v1.0.2 is the fork release matching upstream Perfect Comms v2.0.9 and made for Town Of Us Mega Chujowe Extension.

<p align="center">
  <img src="https://raw.githubusercontent.com/marzecoo/Chujowe-Perfect-Comms/v1.0.2/assets/brand/divider.svg" alt="divider" width="900">
</p>

## Download

Use this fork release, not the original Perfect Comms build:

https://github.com/marzecoo/Chujowe-Perfect-Comms/releases/latest

Discord and support:

https://discord.gg/qaQZAmAVh4

## Fork Changes

- Upgraded the plugin base to upstream **v2.0.9**.
- Added **Voice Falloff Softness** option for gentler near-vision volume fade.
- Added manual **speaking-bar layouts** with auto-fit and jail unmute button on jailee cards.
- Hardened WebRTC peer recovery, lobby join robustness, and jitter recovery.
- Hardened security by routing host settings authority only through authenticated RPCs.
- Maintained all TouMCE-specific custom voice rules and features:
  - Pelican belly voice with swallowed players.
  - Recruit-only voice for Infiltrator recruits.
  - Lawyer and Client private voice.
  - Spirit Master voice with mediated ghosts.
  - Voice safety for custom invisible roles (Astral, Burrower, Speedy, Vanisher, Wraith).
  - Hacker Jam voice muting.
  - Increased volume control up to 300%.

## Notes

Windows Defender or another antivirus may flag mod DLLs because this build embeds voice/network libraries inside one plugin DLL. Download only from this fork release page and do not mix it with the original Perfect Comms DLL.
