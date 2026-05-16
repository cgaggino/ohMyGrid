# OhMyGrid

Valheim mod that lets you plant crops in **non-square grid patterns** — circular,
donut (inner + outer radius), and other shapes — as an alternative to the
default rectangular grid.

Status: **multi-plant + cursor/snap modes working**. While holding a plant on
the cultivator, a donut of ghost previews shows around the player; left-click
plants the whole donut, with per-point grow-space validation and inventory
cost. F7 cycles the center between Player / Fixed (lock) / Cursor / CursorSnap
(auto-aligns to the centroid of nearby plants for concentric placement). Mode
changes show as a TopLeft HUD toast.

## Stack

- C# / .NET Framework `net472`
- [BepInEx](https://github.com/BepInEx/BepInEx) 5.x + [HarmonyX](https://github.com/BepInEx/HarmonyX)
- No Jötunn (yet) — pure BepInEx + Harmony

## Building

The build requires the Valheim **client** managed DLLs (`assembly_valheim.dll`
and `UnityEngine.*.dll`). Those are NOT in this repo. Point MSBuild at your
local Steam install:

```bash
# Linux (native Steam)
export VALHEIM_INSTALL="$HOME/.steam/steam/steamapps/common/Valheim"

# macOS (Steam under Crossover/Proton-ish)
export VALHEIM_INSTALL="$HOME/Library/Application Support/Steam/steamapps/common/Valheim"

# Windows (PowerShell)
$env:VALHEIM_INSTALL = "C:\Program Files (x86)\Steam\steamapps\common\Valheim"
```

Then:

```bash
dotnet restore
dotnet build -c Release
```

Output: `bin/Release/net472/OhMyGrid.dll`.

Copy that DLL into `<Valheim>/BepInEx/plugins/` to test locally.

## Default hotkeys

All hotkeys are configurable via BepInEx config (`[Hotkeys]` section).

| Key | Action |
|---|---|
| `F7` | Cycle center mode: **Player → Fixed → Cursor → CursorSnap → Player** |
| `F8` | Dump every donut grid point to the BepInEx log |
| `]` | Outer radius +1 spacing step |
| `[` | Outer radius −1 spacing step |
| `Shift+]` | Inner radius +1 spacing step |
| `Shift+[` | Inner radius −1 spacing step |
| Left-click | Plant the entire donut (when holding a plant on the cultivator) |
| `Shift+E` | Mass-interact: pick up Pickables, fuel Fireplaces, feed Smelter switches within `MassInteract.Radius` (default 5m). Normal E unchanged. |

### Center modes (F7 cycles)

- **Player** — donut centered on you, moves as you walk.
- **Fixed** — pins the center where it currently is. Walk away freely; donut stays.
- **Cursor** — donut centered wherever the cultivator ghost is aiming.
- **CursorSnap** — like Cursor, but the center auto-snaps to the *centroid* of
  any existing Plants found within `AutoSnapRadius` (default 12 m). For
  concentric placement: stand inside an existing donut, F7 to CursorSnap,
  shrink outer radius (`[`) and plant the inner ring.

Default grid: `InnerRadius=2m`, `OuterRadius=6m`, `Spacing=1m` (configurable
under `[Grid]`).

## Roadmap

- [x] Project scaffold (BepInEx + Harmony Hello World)
- [x] Position generator — concentric rings between inner/outer radius, isolated + log-tested
- [x] Placement ghosts at generated positions
- [x] Multi-plant on click with `HaveGrowSpace()` validation + inventory cost
- [x] Config (inner/outer/spacing) + radius/lock hotkeys
- [x] CenterMode (Player/Fixed/Cursor/CursorSnap) via F7 cycle + HUD toast
- [x] Auto-snap: align donut center to centroid of nearby Plants in CursorSnap
- [x] Center mode persists across sessions (Fixed is downgraded to Player on load)
- [x] Max-placement-distance check: out-of-reach donut points tint red and are skipped on click
- [x] Cost/yield overlay: `OhMyGrid · valid 18/20\nNeed: Seed-onion × 18\nYield: ~18 Onion` (only counts green/valid points)
- [x] Shift+E mass interact: Pickables / Fireplaces / Smelter switches within MassInteract.Radius
- [ ] **Known limitations / next slices:**
  - [ ] Centroid snap is biased when the surrounding plant pattern is
        asymmetric (partial / half donuts, irregular clusters). Better
        approach: least-squares circle fit, or median-of-XZ, or detecting a
        ring and using its geometric center.
  - [ ] `Plant.Awake()` throws an NRE on each clone because the placement
        ghost has no `ZNetView`. The exception is benign (visual still
        renders, `HaveGrowSpace` works against the source ghost), but it
        spams the log. Workaround idea: SetActive(false) the source briefly
        so the clone instantiates inactive, attach a dummy ZNetView, then
        activate; or strip the Plant component from the clone before
        activation.
  - [ ] Out-of-game preference persistence for F7 mode (currently resets to
        Player each session).
- [ ] Thunderstore package (manifest + icon + CI/CD)

## Notes

- Client-only mod (probably). No `ServerSync` needed unless we change that.
- The build host is not the beelink — beelink-server has no Valheim install and
  no `dotnet` SDK. Develop here (cloud-dev), build on a machine that has both.
- Targets Valheim **0.221.x**. The relevant private API used:
  `Player.m_placementGhost`, `Plant.HaveGrowSpace()` — both accessed via
  `AccessTools` from HarmonyX. `Player.TryPlacePiece(Piece)` is the public
  prefix target; `Player.PlacePiece(Piece, Vector3, Quaternion, bool)` is the
  public per-point call.

## License

MIT — see [LICENSE](LICENSE).
