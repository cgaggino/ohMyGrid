# OhMyGrid

Valheim mod that lets you plant crops in **non-square grid patterns** — circular,
donut (inner + outer radius), and other shapes — as an alternative to the
default rectangular grid.

Status: **multi-plant working**. While holding a plant on the cultivator, a
donut of ghost previews shows around the player; left-click plants the whole
donut, with per-point grow-space validation and inventory cost. Center can be
locked (F7) to chain concentric donuts of different crops.

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
| `F7` | Toggle lock/unlock of the donut center (anchors at current position) |
| `F8` | Dump every donut grid point to the BepInEx log |
| `]` | Outer radius +1 spacing step |
| `[` | Outer radius −1 spacing step |
| `Shift+]` | Inner radius +1 spacing step |
| `Shift+[` | Inner radius −1 spacing step |
| Left-click | Plant the entire donut (when holding a plant on the cultivator) |

Default grid: `InnerRadius=2m`, `OuterRadius=6m`, `Spacing=1m` (configurable
under `[Grid]`).

## Roadmap

- [x] Project scaffold (BepInEx + Harmony Hello World)
- [x] Position generator — concentric rings between inner/outer radius, isolated + log-tested
- [x] Placement ghosts at generated positions
- [x] Multi-plant on click with `HaveGrowSpace()` validation + inventory cost
- [x] Config (inner/outer/spacing) + radius/lock hotkeys
- [ ] **Next slice:**
  - [ ] `CenterMode = Player | Cursor` — alternative center anchored at the
        cursor (PlantEasily-style) instead of the player.
  - [ ] Auto-snap: detect a nearby existing plant cluster and align the donut
        center to it automatically (F7 lock is the manual version).
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
