# OhMyGrid

Valheim mod that lets you plant crops in **non-square grid patterns** — circular,
donut (inner + outer radius), and other shapes — as an alternative to the
default rectangular grid.

Status: **early scaffold**. The plugin loads and prints a Hello World; the
placement Harmony patches and per-plant validation are not wired up yet.

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

## Roadmap

Tracks the executive plan:

- [x] Project scaffold (BepInEx + Harmony Hello World)
- [ ] Position generator — concentric rings between inner/outer radius, isolated + log-tested
- [ ] Placement ghosts at generated positions
- [ ] Multi-plant on click with `Plant.HaveGrowSpace()` validation + inventory cost
- [ ] Config (ConfigurationManager): inner/outer radius, spacing, pattern toggle key
- [ ] Thunderstore package (manifest + icon + CI/CD)

## Notes

- Client-only mod (probably). No `ServerSync` needed unless we change that.
- The build host is not the beelink — beelink-server has no Valheim install and
  no `dotnet` SDK. Develop here (cloud-dev), build on a machine that has both.

## License

MIT — see [LICENSE](LICENSE).
