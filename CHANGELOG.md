# Changelog

## 1.0.1 — 2026-09-20

Compatibility rebuild for **Valheim 1.0** (tested against `l-1.0.12`). No functional changes.

- Rebuilt against the 1.0 assemblies. 1.0 added optional parameters to
  `Player.PlacePiece` and `MessageHud.ShowMessage`, so the 1.0.0 DLL threw
  `MissingMethodException` when planting a donut or showing any HUD toast.
- Dependency bumped to `denikson-BepInExPack_Valheim-5.4.2350`.

## 1.0.0 — 2026-05-17

Initial Thunderstore release.

### Planting

- Donut placement geometry (configurable inner radius / outer radius / spacing).
- Live ghost previews around the resolved donut center, each tinted per-point:
  red when blocked by the grow-space check, by max placement distance, or when
  the soil isn't cultivated for plants that need it.
- Left-click plants every green ghost in one click, charged from inventory.
  Out-of-resource / out-of-space / out-of-reach / uncultivated points are
  skipped silently and reported in the log summary.

### Center & snap

- `F7` cycles `Player → Fixed → Cursor → CursorSnap → Player` with a HUD toast
  on every change. `Fixed` pins the donut where it currently is; walk away
  freely. `CursorSnap` auto-snaps the donut center to the centroid of nearby
  existing plants (`AutoSnapRadius`, default 12 m), so concentric placement
  is a matter of "stand inside, F7, plant".

### Overlay

- Centered top-of-screen overlay shows the active plant, valid/total point
  count, total seed cost, expected yield, and the current donut radii.
  Updates live as you nudge the radii or change mode.
- Toggle with `[Overlay] ShowCostOverlay`.

### Geometry toggle

- `F6` cycles `Off ↔ Donut`. `Off` makes the mod a no-op (vanilla 1-plant
  click), so you can quickly drop into single-plant precision mode without
  uninstalling.

### Mass interact (`Shift+E`)

- Hover-scoped: looks at what you're aiming at and only mass-interacts with
  the same kind.
- Pickables additionally filter by drop type, so harvesting carrots doesn't
  sweep neighboring strawberries or mushrooms.
- Supports Pickable, Fireplace, and Smelter (both ore and fuel switches).

### Other

- All hotkeys, radii, snap distance, mass-interact radius, and the on/off
  toggles persist across sessions via the BepInEx config.
