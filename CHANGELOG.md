# Changelog

## Perfect Comms v1.0.0

**PUBLIC BETA — expect bugs.**  
This is the first public Perfect Comms release. It is ready for real lobbies, but still needs wider testing across different TOU-Mira setups.

### What makes this release special

- **Fully integrated proximity chat** — voice runs inside Among Us, no separate voice app needed.
- **Full Jailor + Blackmailer support** — voice respects TOU-Mira role rules instead of ignoring them.
- **No more hackers messing with the voice** — voice stays tied to compatible Perfect Comms clients.
- **Built-in Voice Lobbies** — find compatible voice lobbies from the main menu and join with one click.
- **Made for 15-player TOU-Mira lobbies** — designed around bigger modded games, not vanilla-only matches.
- **In-game update prompt** — players can be sent straight to the newest release when an update drops.

### Added

- Native Perfect Comms BepInEx plugin.
- Windows release build: `PerfectComms.dll`.
- Android release build: `PerfectCommsAndroid.dll`.
- Proximity voice during gameplay.
- Meeting voice behavior for modded role states.
- Jailor-controlled jailed voice support.
- Blackmailer voice blocking support.
- Voice Lobby button and lobby browser.
- Compatible-client voice checks.
- Reactor mod list entry for Perfect Comms.
- Clickable main-menu update notification.

### Beta notes

- **Expect bugs in some lobbies.** Report anything weird with player count, mod list, and what happened.
- 15-player support is designed and hardened, but still needs more real public-lobby testing.
- Everyone in the lobby should use the same Perfect Comms release.

### Install

Download `PerfectComms.dll` and place it here:

```text
BepInEx/plugins/PerfectComms.dll
```

Requires TOU-Mira, MiraAPI, and Reactor from your mod pack.
