# Perfect Comms

<p align="center">
  <strong>Proximity chat that feels built into TOU-Mira.</strong><br>
  Clean in-game voice, role-aware mute rules, protected lobbies, and one-click voice lobby discovery.
</p>

Perfect Comms is a native Among Us voice-chat mod made for serious TOU-Mira lobbies. No separate app. No awkward setup. No voice rules getting ignored. Just launch, join, and play with voice that matches the game.

## Why Perfect Comms

- **Fully integrated proximity chat** — voice runs directly inside Among Us with in-game controls.
- **Full Jailor + Blackmailer support** — blackmailed players stay silent, jailed players follow Jailor voice rules.
- **No more hackers messing with the voice** — voice stays tied to compatible Perfect Comms clients, so lobby audio is harder to disrupt or fake.
- **Voice Lobbies built in** — find compatible voice lobbies from the main menu and join with one click.
- **Made for TOU-Mira** — designed around modded meetings, roles, lobbies, and 15-player games.
- **Update-ready** — players can be sent straight to the newest release when a new build drops.

## What makes it must-use

Perfect Comms turns voice chat into part of the match instead of a side tool.

| Old way | Perfect Comms |
| --- | --- |
| Separate voice app | Voice inside Among Us |
| Manual lobby sharing | Built-in Voice Lobbies |
| Role rules ignored | Jailor + Blackmailer handled |
| Hackers messing with the voice | Voice stays tied to compatible Perfect Comms clients |
| Players miss updates | In-game update prompt |

If your lobby plays TOU-Mira with voice, this is the clean setup: install once, keep everyone on the same build, and play.

## Install

1. Download `PerfectComms.dll`.
2. Put it in `BepInEx/plugins`.
3. Your install should look like this:

```text
BepInEx/plugins/PerfectComms.dll
```

Requires your mod pack to already include MiraAPI, Reactor, and TOU-Mira.

## Build

```bash
dotnet build PerfectComms.csproj -c Release --nologo
```

Output:

```text
bin/Release/net6.0/PerfectComms.dll
```

For GitHub release uploads, use `PerfectComms.dll` for Windows and `PerfectCommsAndroid.dll` for Android.

## Notice

Perfect Comms is an unofficial mod. It is not affiliated with Innersloth, Among Us, BepInEx, MiraAPI, Reactor, or TOU-Mira.
