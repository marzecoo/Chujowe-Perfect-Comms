# Changelog

## Mega Chujowe Perfect Comms v1.0.6
- Merged with upstream Perfect Comms v2.1.7.
- Implemented Voodoo role voice mechanics (hidden muting, next-round persistence toggle, mute-in-meeting toggle).
- Maintained local optimizations (300% max volume, custom names/branding).

This Perfect Comms release is a big audio-quality pass: smoother voice with no clicks or pops, lower delay that adapts to each player's connection, fixed Bluetooth headsets going silent, better quiet-mic handling, and a Jailor unmute that always sticks.

### What's Changed

- **Voice is much smoother all around.**
  > <sub>Moving around, crossing walls, muting, and range edges no longer click or cut hard. Volume and stereo position glide instead of stepping.</sub>

- **Fixed short stutter/echo bursts during packet loss.**
  > <sub>A bug repeated the same split-second of audio when several packets were lost in a row.</sub>

- **Lower voice delay that adapts per player.**
  > <sub>Stable connections get snappier voice, laggy players automatically get more cushion, and their late-arriving audio is played instead of dropped.</sub>

- **Voice quality adapts to real packet loss.**
  > <sub>Loss protection is dialed up only for players who need it, instead of a fixed setting for everyone.</sub>

- **Bluetooth headsets no longer go deaf.**
  > <sub>Using one headset (e.g. AirPods) for mic and speaker could leave you unable to hear; the mod now recovers automatically, and switches back to full listening quality while you're muted.</sub>

- **Quiet microphones sound better.**
  > <sub>Mic boost survives mute cycles, rumble no longer trips the voice gate, and unmuting is instant.</sub>

- **The Jailor's unmute always sticks now.**
  > <sub>It re-confirms itself automatically so a missed unmute can't leave the jailee silent.</sub>

- **Speaking-bar names match in-game names.**
  > <sub>Same font and outline as player nameplates, readable over any background.</sub>

- **More self-healing playback.**
  > <sub>A stalled or erroring output device restarts itself instead of staying silent until you switch devices.</sub>

See `docs/release-notes-v2.1.7.md` for the full release notes.

## Perfect Comms v2.1.6

This Perfect Comms release reworks the per-player volume menu into a live mixer, adds an optional backdrop and steadier animation to the speaking bar, lets you switch between Open Mic and Push-to-Talk with a hotkey, fixes the volume menu hiding in the dark, and routes role-based spectators as voice ghosts.

### What's Changed

- **A live per-player volume mixer.** The volume menu now shows each player's avatar and a live voice meter that moves as they talk, you can scroll the list with the mouse wheel, and the roster refreshes as players join or leave — so it's much easier to find and set the right person's level.
- **The volume menu no longer hides in the dark.** During blackouts and low-vision moments the menu's rows could vanish; they now stay fully visible regardless of in-game vision.
- **Optional speaking-bar backdrop, steadier bar.** You can switch on a subtle backdrop behind the speaking bar for readability over busy scenes, the talking icons now hold a stable order instead of jumping around, and their level animation is smoothed.
- **Switch Open Mic ↔ Push-to-Talk on the fly.** A new keybind flips your microphone between Open Mic and Push-to-Talk and shows a quick confirmation. It's unbound by default — assign a key to it in the keybind settings to use it.
- **Spectators are handled as ghosts for voice.** Players the game marks dead through a role (e.g. Town of Us Spectator) are now routed like ghosts, so they hear and are heard the same as other dead players.
- **No more stray hover highlight on the voice buttons.** The mic/speaker buttons (cloned from the in-game map button) no longer show a leftover hover sprite.

See `docs/release-notes-v2.1.6.md` for the full release notes.

## Perfect Comms v2.1.5

This Perfect Comms release fixes the speaking bar showing the wrong player and voice being heard across the whole map, clears an end-game crash that could disconnect you, tones down the end-game group call so it isn't ear-splitting, and trims a few more audio drop-outs.

### What's Changed

- **The speaking bar shows the right person again.** Fixed a bug where the talking indicator — in meetings and on the in-game bar — could light up the wrong player, or two players' voices could collapse onto a single name, especially after rounds where player slots got reshuffled. Each voice now reliably follows its own player.
- **No more being heard from across the map.** Caused by the same mix-up: a player's voice could ignore distance and come through from anywhere (and, the flip side, a player who was actually nearby could go unheard). Voice now tracks each player's real position again, so proximity works as intended.
- **Fixed a crash/kick around the end-game screen.** Dropping to the end-game results and rejoining the lobby could spam errors and get you disconnected. The voice HUD now steps aside cleanly during that transition, so the freeze-and-kick is gone.
- **The end-game group call is no longer ear-splitting.** On the results screen everyone was played back at full blast; levels are now dialed to a comfortable volume while staying clearly audible.
- **Fewer audio drop-outs and a little less stutter.** Tiny/odd audio frames are now handled cleanly instead of being dropped, and the playback buffer carries a touch more cushion so brief network hiccups don't turn into gaps.

See `docs/release-notes-v2.1.5.md` for the full release notes.

## Perfect Comms v2.1.4

This Perfect Comms release gives the voice settings menu a cleaner, easier-to-read makeover, keeps voice and avatars from dropping out on the end-game screen, and rounds out another batch of fixes for one-way and patchy voice.

### What's Changed

- **A cleaner voice settings menu.** The settings page has a tidier two-column layout, easier-on-the-eyes colors, and shorter labels that no longer wrap onto two lines. The microphone and speaker pickers now clearly show which is which, instead of both just reading "Default".
- **Voice now stays put through the end-game screen.** Voice keeps working through the end screen and between rounds, and player voice and speaker icons no longer vanish the moment a game ends.
- **A heads-up when you refresh voice.** Pressing a voice-refresh key now shows a quick on-screen message so you know it worked. Refreshing as the host lets everyone know; refreshing just for yourself shows only to you.
- **No more freeze when switching microphone or speaker mid-game.** Changing your mic or speaker quickly no longer risks a crash, and leaving the speaker on "Default" now follows whatever your computer's current default speaker is.
- **Fixed some players' audio going flat (pure mono, non-directional).** In some cases a player's voice lost its proximity and direction and came through evenly no matter where they were on the map. Their audio now plays back with the correct distance and direction again.
- **More "I can hear you but you can't hear me" fixes.** Several cases where a voice link got stuck on one side — so one player could be heard but couldn't hear back, or a connected player made no sound at all — now sort themselves out automatically instead of needing a rejoin.
- **Voice is a touch smoother still.** Continued behind-the-scenes tuning keeps speech steady through uneven connections, with a little less stutter.

See `docs/release-notes-v2.1.4.md` for the full release notes.

## Perfect Comms v2.1.3
>>>>>>> upstream/main

Fork release for [Town Of Us Mega Chujowe Extension](https://github.com/HekerB/TownOfUsMegaChujoweExtension).

- Upgraded and merged with upstream Perfect Comms v2.1.3 (includes adaptive jitter buffer, decode guard, reliable peer connections, and performance optimizations).
- Preserved all TouMCE voice integration, rebranding, and custom tweaks.

See `docs/release-notes-v1.0.4.md` for the full fork release notes.

## Mega Chujowe Perfect Comms v1.0.3

Fork release for [Town Of Us Mega Chujowe Extension](https://github.com/HekerB/TownOfUsMegaChujoweExtension).

- Upgraded fork changes to include upstream Perfect Comms v2.1.0 voice improvements.
- Added configurable speaking-bar name position.
- Added host options for Parasite and Puppeteer to hear from controlled victims.
- Preserved all Mega Chujowe Extension voice rules while porting the upstream changes.

See `docs/release-notes-v1.0.3.md` for the full fork release notes.

## Mega Chujowe Perfect Comms v1.0.2

Fork release for [Town Of Us Mega Chujowe Extension](https://github.com/HekerB/TownOfUsMegaChujoweExtension).

- Upgraded base plugin to upstream v2.0.9.
- Added Voice Falloff Softness option for gentler near-vision volume fade.
- Added manual speaking-bar layouts with auto-fit and jail unmute button.
- Hardened WebRTC peer recovery, lobby join robustness, and jitter recovery.
- Hardened security and host settings authority checks.
- Kept and validated all TouMCE-specific custom voice rules and features.

See `docs/release-notes-v1.0.2.md` for the full fork release notes.

## Mega Chujowe Perfect Comms v1.0.1

Fork release for [Town Of Us Mega Chujowe Extension](https://github.com/HekerB/TownOfUsMegaChujoweExtension).

- Built as `MegaChujowePerfectComms.dll`.
- Update popups and patch notes now point to the fork releases at [marzecoo/Chujowe-Perfect-Comms](https://github.com/marzecoo/Chujowe-Perfect-Comms/releases/latest).
- Added TouMCE voice support for Pelican belly voice, Infiltrator Recruits, Lawyer/Client, Spirit Master, hidden role mutes, and Hacker Jam.
- Added Team Radio channels for Recruits and Lawyer/Client.
- Increased speaker and per-player volume controls up to 300%.
- Ported upstream 2.0.7 and 2.0.8 routing, camera, and volume fixes.

See `docs/release-notes-v1.0.1.md` for the full fork release notes.

## Perfect Comms v2.0.6

This Perfect Comms release expands role-aware voice behavior and adds team radio channels.

### What's Changed

- Added **Crewpostor: Use Impostor Voice**.
  - Crewpostor can use impostor radio, talk with impostors in vents, and inherit the other impostor-only voice behavior when this option is enabled.
- Added **Swooper: Mute While Swooped**.
- Added **Glitch: Mute Hacked Players**.
- Added **Medium: Ghost Voice**.
  - Choose **Medium -> Ghost**, **Ghost -> Medium**, or **Both** for private Medium spirit communication.
- Added **Eclipsal/Grenadier: Muffle Blinded/Flashed Hearing**.
- Added **Hypnotist: Muffle Hypnotized During Hysteria**.
- Changed **Impostor Radio** to **Team Radio** with:
  - **Team Radio - Impostors**
  - **Team Radio - Vampires**
  - **Team Radio - Lovers**
  - Players with more than one radio can cycle between channels with a keybind.
- Improved Role Voice Rules labels with role-first wording, role-matched colors thanks to @idkimneil in [#6](https://github.com/artriy/Perfect-Comms/pull/6), and cleaner ordering.

See `docs/release-notes-v2.0.6.md` for the full release notes.

## Perfect Comms v2.0.5

This Perfect Comms release adds role-based voice controls and improves voice button placement.

### What's Changed

- Added a new *Perfect Comms: Role Voice Rules* settings section for role-specific voice behavior.
- Added *Mute Blackmailed Next Round* so blackmail can optionally continue after the meeting.
- Added options to mute parasite-controlled and puppeteer-controlled players.
- Improved voice button placement so mic, speaker, and jail controls stay visible and easier to position near the screen edge.

See `docs/release-notes-v2.0.5.md` for the full release notes.

## Perfect Comms v2.0.4

This Perfect Comms release focuses on BetterCrewLink audio stability and chat input safety.

### Fixed

- Fixed intermittent fuzzy/static audio in BetterCrewLink lobbies when multiple voices overlap.
- Fixed hot BetterCrewLink mic frames after RNNoise so clipped capture peaks are limited before Opus encoding.
- Fixed chat input handling so Perfect Comms no longer intercepts textbox typing, preventing crashes while typing in chat.

See `docs/release-notes-v2.0.4.md` for the full release notes.

## Perfect Comms v2.0.3

This Perfect Comms release focuses on voice stability, speaking ring accuracy, noise suppression, and cleaner in-game controls.

### What's Changed

- Added volume controls sliders by @idkimneil in [#4](https://github.com/artriy/Perfect-Comms/pull/4).
- Reduced mute/unmute spam crashes with serialized mic capture transitions.
- Added RNNoise noise suppression, enabled by default.
- Added host and local voice refresh keybinds.
- Added MB4/MB5 mouse button bind support (in local settings).
- Fixed comms sabotage voice blocking detection.
- Fixed rainbow, idle-pose, and morph/mimic speaking ring portraits.
- Added Middle Left and Middle Right speaking bar positions.
- Improved voice overlay positioning, layout, scale, and defaults.
- Improved the player volume menu and slider behavior.
- Deafened players now stop transmitting voice too.
- Removed `.DS_Store`.

See `docs/release-notes-v2.0.3.md` for the full release notes.

## Perfect Comms v2.0.2

### What's Changed

- Fixed voice keybind behavior while chat is open so toggle shortcuts stay blocked during typing, while push-to-talk and impostor radio can still activate after a short hold.
- Fixed printable push-to-talk and radio keys in chat so quick taps type normally, but held voice keys do not spam characters into the message field.
- Fixed push-to-talk chat handling so it only applies when Mic Mode is set to Push To Talk.
- Fixed impostor radio chat handling so it only applies when the local player can actually use the radio, including blackmailer and jailor voice-block rules.
- Changed Push To Talk mode so the mute toggle no longer creates a redundant manual mute state; released PTT remains muted and holding PTT still transmits.
- Fixed the `VC: mic unavailable` warning appearing during normal Push To Talk idle mute.

See `docs/release-notes-v2.0.2.md` for the full release notes.

## Perfect Comms v2.0.1

### What's Changed

- Changed the assets to a cleaner version by @AtonyGit in #1.
- Fixed the Voice Lobbies close X so it sits in a clearer top-right position and is easier to see.
- Fixed bottom-positioned speaking indicators so player names no longer clip off the bottom of the screen while staying close to the edge.

See `docs/release-notes-v2.0.1.md` for the full release notes.

## Perfect Comms v2.0.0

This is the backend rewrite release. Perfect Comms no longer depends on Among Us RPCs for voice audio transport. Voice now runs through selectable voice backends, with BetterCrewLink live voice as the default path and Interstellar available as an alternate backend.

### Most Notable Changes

- BetterCrewLink backend + lobby browser is now fully built in.
- Interstellar backend is now fully supported.
- Voice transport no longer depends on Among Us RPCs.
- Public lobby browsing and publishing are much better.
- Directional and proximity audio are more reliable.
- Host settings now sync across the lobby automatically.
- Debug logs are quiet by default unless you turn them on.

See `docs/release-notes-v2.0.0.md` for the full technical changelog.

## Perfect Comms v1.0.2

### Changed

- Reposition Perfect Comms as its own Among Us proximity chat mod.
- Document TOU-Mira as a supported mod behaviour instead of a requirement.
- Add `PerfectComms+dependencies.zip` with BepInEx, MiraAPI, Reactor, and Perfect Comms.
- Cache supported-mod role state for 0.25 seconds instead of checking every voice snapshot.

### Fixed

- Missing TOU-Mira role/modifier types now no-op and retry instead of being cached as unavailable.
- Default voice lobby title now uses `Perfect Comms` instead of TOU-Mira-specific wording.

## Perfect Comms v1.0.1

### Changed

- Update notifications now check GitHub releases directly for future releases.

### Fixed

- Speaking indicators now render above the Among Us game UI instead of being hidden behind menus or scene elements.
- Speaking-ring player icons now show a stable recolored crewmate body with loaded cosmetics, without cloning live player objects.

## Perfect Comms v1.0.0

**PUBLIC BETA Ă˘â‚¬â€ť expect bugs.**
This is the first public Perfect Comms release. It is ready for real lobbies, but still needs wider testing.

### What makes this release special

- **Fully integrated proximity chat** Ă˘â‚¬â€ť voice runs inside Among Us, no separate voice app needed.
- **Supported mod behaviours** Ă˘â‚¬â€ť TOU-Mira blackmailed players stay muted, and Jailor can unmute jailed players.
- **No more hackers messing with the voice** Ă˘â‚¬â€ť voice stays tied to compatible Perfect Comms clients.
- **Built-in Voice Lobbies** Ă˘â‚¬â€ť find compatible voice lobbies from the main menu and join with one click.
- **In-game update prompt** Ă˘â‚¬â€ť players can be sent straight to the newest release when an update drops.

### Added

- Native Perfect Comms BepInEx plugin.
- Windows release build: `PerfectComms.dll`.
- Dependency bundle: `PerfectComms+dependencies.zip` with BepInEx, MiraAPI, Reactor, and Perfect Comms.
- Proximity voice during gameplay.
- Meeting voice behavior for modded role states.
- Jailor-controlled jailed voice support.
- Blackmailer voice blocking support.
- Voice Lobby button and lobby browser.
- Compatible-client voice checks.
- Reactor mod list entry for Perfect Comms.
- Clickable main-menu update notification.

### Beta notes

- **Expect bugs in some lobbies.** Report anything weird to me.
- 15-player support is designed and hardened, but still needs more real public-lobby testing.
- Everyone in the lobby should use the same Perfect Comms release.

### Install

Download `PerfectComms.dll` and place it here:

```text
BepInEx/plugins/PerfectComms.dll
```

Requires MiraAPI and Reactor from your mod pack. Supported mod behaviours activate only when matching mods are installed.
