<h1 align="center">Mega Chujowe Perfect Comms</h1>

<p align="center">
  <strong>Immersive voice chat built directly inside Among Us.</strong><br>
  Proximity audio, voice lobbies, role-specific behavior, and simple plug-and-play setup.
</p>

<p align="center">
  <img src="assets/brand/divider.svg" alt="divider" width="900">
</p>

Mega Chujowe Perfect Comms makes voice chat feel like part of the match. Players can talk in-game, hear voices around them, find voice-ready lobbies, and play with voice rules that fit the way Among Us is actually played.

> [!IMPORTANT]
> This fork is made specifically for [Town Of Us Mega Chujowe Extension](https://github.com/HekerB/TownOfUsMegaChujoweExtension) and works with [Town of Us: Mira](https://github.com/AU-Avengers/TOU-Mira). If you play with that extension, use this fork instead of the original Perfect Comms build so extension-specific roles do not leak or break voice behavior.

> [!NOTE]
> This project is a fork of [artriy/Perfect-Comms](https://github.com/artriy/Perfect-Comms). Huge thanks to the original Perfect Comms creator and contributors for building the base voice chat mod.

> [!TIP]
> Need help, want to report bugs, or looking for lobbies? Join the TouMCE Discord: [https://discord.gg/qaQZAmAVh4](https://discord.gg/qaQZAmAVh4)

## Why Players Use It

- **Voice built directly inside Among Us**
- **Extremely immersive audio**
- **Supports role-specific behaviors**
- **Plug and play setup**
- **Built-in voice lobby discovery**
- **Simple in-game voice controls**
- **Made for public and private lobbies**

## What It Feels Like

| Without Mega Chujowe Perfect Comms | With Mega Chujowe Perfect Comms |
|---|---|
| Voice is outside the game | Voice feels built into the game |
| Everyone sounds the same distance away | Nearby players feel close and distant players fade out |
| Lobby setup is manual | Voice-ready lobbies are easier to find |
| Roles and special situations feel disconnected | Voice can follow the rules of the match |
| Players need extra setup | Players can install, join, and play |

## Voice Lobbies

Mega Chujowe Perfect Comms includes in-game voice lobby discovery, making it easier to find compatible rooms and easier for hosts to create games that other voice players can join.

## Role And Match Behavior

Mega Chujowe Perfect Comms can support special voice behavior for roles, meetings, ghosts, and other match situations. The goal is simple: voice should feel like it belongs to the round, not like a separate call running in the background.

## Supported Mod Behaviors

Mega Chujowe Perfect Comms works as its own Among Us proximity voice chat mod.

Some mods get extra voice behavior when installed:

| Mod | Supported behavior |
|---|---|
| TOU-Mira | Blackmailer, Jailor, Parasite/Puppeteer, Swooper, and Glitch voice mutes.<br>Crewpostor impostor voice rules.<br>Medium ghost voice modes.<br>Muffled hearing for Eclipsal, Grenadier, and Hypnotist effects.<br>Team Radio for Impostors, Vampires, and Lovers, with keybind cycling. |
| Town Of Us Mega Chujowe Extension | Pelican belly voice with swallowed players.<br>Team Radio for Infiltrator Recruits and Lawyer/Client, with keybind cycling.<br>Spirit Master voice with mediated ghosts.<br>Hacker Jam can mute voice globally.<br>Voice safety for custom hidden roles like Astral, Burrower, Speedy, Vanisher, and Wraith. |

TouMCE-specific voice channels can be toggled in the Team Radio and Role Voice Rules settings. If Pelican belly voice is disabled, swallowed players are muted instead of being able to talk and reveal the Pelican.

## Install

1. Download `MegaChujowePerfectComms.dll` from the fork releases: [marzecoo/Chujowe-Perfect-Comms](https://github.com/marzecoo/Chujowe-Perfect-Comms/releases/latest).
2. Put `MegaChujowePerfectComms.dll` in `BepInEx/plugins`.
3. Start Among Us and open the Mega Chujowe Perfect Comms settings in-game.

If you are using Town Of Us Mega Chujowe Extension, do not install the original Perfect Comms build instead of this fork. Mixing the wrong build can cause role voice bugs, especially around Pelican, Recruits, Lawyer, and Spirit Master.

DLL-only install:

```text
BepInEx/plugins/MegaChujowePerfectComms.dll
```

DLL-only installs require your mod pack to already include MiraAPI and Reactor.

## Updates And Patch Notes

In-game update popups and patch notes are meant to point to this fork, not the original Perfect Comms releases. Latest fork release:

[https://github.com/marzecoo/Chujowe-Perfect-Comms/releases/latest](https://github.com/marzecoo/Chujowe-Perfect-Comms/releases/latest)

## Notice

Mega Chujowe Perfect Comms is an unofficial mod. It is not affiliated with Innersloth, Among Us, BepInEx, MiraAPI, Reactor, BetterCrewLink, Interstellar, or any supported mods.

Windows Defender or another antivirus may flag mod DLLs because this build embeds voice and network libraries inside one plugin DLL. Download only from this fork release page and do not mix it with the original Perfect Comms DLL.

Parts of this fork are AI-assisted. Expect bugs, test in real lobbies, and report anything weird on the Discord.

## Credits

- Original Perfect Comms fork source: [artriy/Perfect-Comms](https://github.com/artriy/Perfect-Comms)
- Original repo: [FangkuaiYa/AmongUs-VoiceChat](https://github.com/FangkuaiYa/AmongUs-VoiceChat)
- BetterCrewLink: [OhMyGuus/BetterCrewLink](https://github.com/OhMyGuus/BetterCrewLink)
- Interstellar: [Dolly1016/Interstellar](https://github.com/Dolly1016/Interstellar)
- Special thanks to [idkimneil](https://github.com/idkimneil) - the reason I made this.

<p align="center">
  <img src="assets/brand/divider.svg" alt="divider" width="900">
</p>
