# OhMyGrid

Plant crops in **donut and other non-square patterns** in Valheim. Lay down a
big onion ring, drop a smaller carrot donut concentric inside it, and stop
fighting the rectangular grid. Also: mass-pick / mass-refuel with `Shift+E`.

![icon](icon.png)

**Install:** [Thunderstore page](https://thunderstore.io/c/valheim/p/suspicious_geet/OhMyGrid/)
(direct download
[OhMyGrid-1.0.2.zip](https://thunderstore.io/package/download/suspicious_geet/OhMyGrid/1.0.2/)).
Use r2modman or Thunderstore Mod Manager to drop it into a Valheim profile.

## Features

- **Donut planting.** While holding the cultivator with a seed selected, a
  ring of ghost previews shows around your chosen center. Left-click plants
  the whole donut in one go.
- **Per-point validity.** Each ghost turns red when its spot is invalid —
  blocked by rocks/plants, out of reach, or on uncultivated soil. Green ones
  are the only ones that get planted, charged, and counted.
- **Cost + yield overlay** centered at the top of the screen: shows the
  current plant, valid/total point count, total seed cost, and expected yield
  — all live as you tune the radii.
- **Four center modes**, cycled with `F7` (top-left HUD toast on change):
  - **Player** — donut follows you.
  - **Fixed** — pins the donut at your current spot. Walk away, donut stays.
  - **Cursor** — donut follows where the cultivator ghost aims.
  - **CursorSnap** — like Cursor, but the center auto-snaps to the centroid
    of nearby existing plants. Perfect for concentric placement.
- **Geometry toggle.** `F6` cycles `Off → Donut → Off`. In `Off`, the mod
  stays out of the way and you place one plant per click like vanilla.
- **Shift+E mass-interact** (hover-scoped, same-type only):
  - Hover a **Pickable** → harvest every Pickable of the same item type in
    range. Carrots don't sweep nearby strawberries.
  - Hover a **Fireplace** → fuel every Fireplace in range.
  - Hover a **Smelter / Charcoal Kiln** → feed every smelter in range.
- **Persists across sessions.** Center mode, geometry mode, radii, hotkeys,
  and toggles are all stored under `cgaggino.OhMyGrid.cfg`.

## Default hotkeys

All hotkeys are configurable under `[Hotkeys]` in the config file.

| Key | Action |
|---|---|
| `F6` | Cycle geometry: `Off ↔ Donut` |
| `F7` | Cycle center mode: `Player → Fixed → Cursor → CursorSnap → Player` |
| `F8` | Dump every donut grid point to the BepInEx log |
| `]` | Outer radius +1 spacing step |
| `[` | Outer radius −1 spacing step |
| `Shift+]` | Inner radius +1 spacing step |
| `Shift+[` | Inner radius −1 spacing step |
| `Left-click` | Plant the entire donut (when holding a Plant ghost) |
| `Shift+E` | Mass-interact with the hovered object's type in range |

Defaults: `InnerRadius=2m`, `OuterRadius=6m`, `Spacing=1m`, `AutoSnapRadius=12m`,
`MassInteract.Radius=5m`. All editable in the cfg file (or via BepInEx
ConfigurationManager if you have it installed).

## Concentric donut workflow

1. Stand inside an existing donut of one crop (or anywhere if it's the first).
2. Tap `F7` until the HUD shows `Mode: CursorSnap`.
3. Aim near the center — the donut snaps to the centroid of the existing
   plants automatically.
4. Bring `[` until the outer radius matches the inside of the previous donut.
5. Swap seed type and left-click. The new ring lands concentric to the old one.

## Building from source

You need a local Valheim install with BepInEx (the build references
`assembly_valheim.dll` and friends; they're not redistributable):

```powershell
$env:VALHEIM_INSTALL = "C:\Program Files (x86)\Steam\steamapps\common\Valheim"
dotnet restore
dotnet build -c Release
```

The output is `bin/Release/OhMyGrid.dll`. Drop it in your r2modman /
Thunderstore Mod Manager profile under `BepInEx/plugins/`.

## Known limitations

- **Centroid snap is biased on asymmetric clusters.** If the existing plants
  are a half-donut or irregular, the snapped center won't be the geometric
  center. A least-squares circle fit is on the roadmap.
- **Plant.Awake NRE per clone.** The placement ghost has no `ZNetView` and
  `Plant.Awake` dereferences it unconditionally. The exception is caught by
  Unity and the visual still renders correctly; `HaveGrowSpace` is validated
  against the source ghost (not the clones), so behavior is correct, but the
  log gets one NRE entry per donut clone created.
- **Fixed-mode center doesn't persist across sessions.** The center *mode*
  saves and reloads as `Player` on the next launch (no anchor to restore).
  Other modes restore exactly as you left them.

## Planned (v1.1+)

- More geometries on the `F6` cycle: square grid, hex grid, spiral, and a
  user-drawn polygon.
- Least-squares circle fit for `CursorSnap` so half/quarter donuts also
  resolve to the geometric center.
- Workaround for the `Plant.Awake` NRE (instantiate inactive → attach a
  dummy `ZNetView` → activate, or strip the `Plant` component before the
  first frame).
- `Shift+E` support for more interactables (Beehive, Cauldron, Workbench
  repair).
- A toggle to **mass-fill** a single Smelter / Fireplace with one press,
  not just one tick per press.
- Optional ground ring rendered at the AutoSnap centroid so the snap target
  is visually obvious before you click.

## Compatibility

- Targets Valheim **1.0** (1.0.1+; 1.0.0 was built for 0.221.x and does not work on 1.0).
- BepInEx 5.x via `denikson-BepInExPack_Valheim` (5.4.2350+).
- **Tested on Valheim 1.0.15 (network version 40)** with BepInExPack 5.4.2350, Windows,
  1.0.2 build — play session with `BepInEx/LogOutput.log` review: 0 exceptions, seed
  accounting exact (`planted=18 … paidHere=17 paidByVanilla=1`, 18 seeds in → 0 left),
  ghosts render green/red with the cost overlay, Shift+E feeds only the aimed
  kiln/smelter slot, F7 cycles all four center modes.

### Known issues

- Valheim's own requirement panel (bottom of the build UI) still shows the cost of
  **one** plant while the donut is active; the mod's overlay at the top shows the real
  batch cost (`Need: … × N (have M)`) and that is what gets charged. Cosmetic; planned
  for 1.0.3.
- Client-only — no `ServerSync`. Use freely on dedicated/multiplayer servers
  without forcing other players to install.

## Contributing / releasing

PRs welcome. The dev release workflow (version bump, zip build, Thunderstore
upload via `tcli`) is documented in [`RELEASING.md`](RELEASING.md).

## License

MIT — see [LICENSE](LICENSE).
