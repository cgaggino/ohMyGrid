# Changelog

## 1.1.0 — 2026-11-08

New geometry: **Wedge** (a slice of the donut), built to complete a circle one
crop at a time. Play-tested on Valheim 1.0.21 (network version 41) + BepInExPack
5.4.2360: a half circle built from four wedges of different crops, no shared
edge plants, 0 OhMyGrid exceptions in `LogOutput.log`.

- **Wedge geometry.** `F6` now cycles `Off → Donut → Wedge → Off`. A wedge uses
  the same inner/outer radius and spacing as the donut, plus a heading and a
  sweep. Rotate it with the **mouse wheel** or `.` / `,`, and widen or narrow it
  with `Shift+.` / `Shift+,`. Steps are 15°, so you can reach 1/2, 1/3, 1/4,
  1/6, 1/8 and 1/12 of a circle. The overlay shows `Heading 345° · sweep 45° (1/8)`.
- **Wedges tile the circle exactly.** Every wedge is cut from the donut's own
  point lattice with half-open edges. Wedges around one center add up to
  exactly the donut: no doubled edge rows, no crowded tip, no gaps. The center
  point belongs to the wedge that contains north.
- **Auto-advance.** After you plant a wedge, the heading rotates by one sweep,
  so the next click continues the circle (`[Wedge] AutoAdvance`).
- **Snap to remembered centers.** Each planting saves its center as an anchor
  (the last 16, persisted). `CursorSnap` snaps to the nearest anchor first and
  falls back to the plant centroid only when there is no anchor. Partial
  circles now resolve to their true center, so concentric donuts also snap
  exactly.
- **Out-of-reach is explained.** The overlay shows `out of reach N (get closer)`
  when points are red only because they are beyond your build reach. A new
  `[Grid] IgnoreReach` option (off by default) plants them anyway.
- **Fix:** ghost previews created while the vanilla cursor ghost was red (for
  example while aiming at an existing plant) stayed red forever.

## 1.0.2 — 2026-09-20

Runtime fixes found while play-testing 1.0.1 on Valheim 1.0.15. This build was play-tested
on Valheim 1.0.15 (network version 40) + BepInExPack 5.4.2350: 0 exceptions in
`LogOutput.log`, seed accounting exact, ghosts + overlay rendering, Shift+E targeting OK.

Known issue: vanilla's requirement panel still shows the per-plant cost (1) while the
donut is active; the top overlay shows the real batch cost. Cosmetic, planned for 1.0.3.

- **Seeds are now charged for every plant.** Since 1.0, vanilla charges one piece
  *after* `TryPlacePiece` returns, so a donut of N plants cost 1 seed. The mod now
  pays for N-1 itself and leaves the last one to vanilla (stamina/skill intact),
  checks stock before each plant, and never plants what you can't pay for.
- **Ghost previews render again.** Ghost clones are created inactive and stripped
  of `Plant`/`ZNetView` before activation. Previously `Plant.Awake` ran on the clone
  and either NRE'd every frame (after the first placement recreated the ghost) or,
  worse, gave the clone a real ZDO — a phantom plant in the world simulation.
  If cloning ever fails, the preview turns itself off for that ghost with one
  error line instead of one exception per frame.
- **Overlay shows what you can afford**: `can afford N`, `Need: item × N (have M)`,
  and ghosts beyond your stock are tinted red.
- **Shift+E on a smelter/kiln/fire feeds only the station you aim at**, until it is
  full or you run out — no more radius sweep feeding the furnace when you aim at
  the kiln. Aiming at a specific slot (ore vs fuel) feeds just that slot. Fires use
  the refill path (no accidental toggle-off).
- Log lines for seed cost (stock before/after, paid here/by vanilla), Shift+E
  target + amounts, geometry cycling, and ghost clone creation — so a run can be
  verified from `LogOutput.log`.

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
