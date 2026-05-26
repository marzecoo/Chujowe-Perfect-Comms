This Perfect Comms release focuses on BetterCrewLink audio stability and chat input safety.

<p align="center">
  <img src="https://raw.githubusercontent.com/artriy/Perfect-Comms/1959e95ba5c9fa42cdd46a3340d13245e5614a6e/assets/brand/divider.svg" alt="divider" width="900">
</p>

### Fixed

- Fixed intermittent fuzzy/static audio in BetterCrewLink lobbies when multiple voices overlap.
- Fixed hot BetterCrewLink mic frames after RNNoise so clipped capture peaks are limited before Opus encoding.
- Fixed chat input handling so Perfect Comms no longer intercepts textbox typing, preventing crashes while typing in chat.
