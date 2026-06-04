This Perfect Comms release fixes voice routing during exile and other transitional phases.

<p align="center">
  <img src="https://raw.githubusercontent.com/artriy/Perfect-Comms/v2.0.7/assets/brand/divider.svg" alt="divider" width="900">
</p>

### What's Changed

- Exile now uses meeting-style voice routing instead of accidental lobby proximity routing.
- Meeting mutes now carry through Exile, so blackmailed and jailed players remain muted until the meeting-like sequence ends.
- Intro, EndGame, Menu, and Unknown phases now use an explicit lobby-like voice policy instead of inheriting task-phase role mutes.
- BetterCrewLink and Interstellar now share the same phase routing policy, keeping backend behavior consistent.
