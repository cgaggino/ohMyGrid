# Releasing OhMyGrid

Internal notes for cutting a Thunderstore release. Aimed at the maintainer; not
shipped with the package.

## Prerequisites

- `.NET 8 SDK` (or any modern `dotnet`) installed.
- `tcli` global tool: `dotnet tool install -g tcli`.
- `$env:VALHEIM_INSTALL` pointing at a Valheim install with the right
  managed DLLs (the build references them; details in the main README).
- A Thunderstore **service-account token** for the `suspicious_geet` team,
  exported as a User-scoped env var:
  ```powershell
  [Environment]::SetEnvironmentVariable('TCLI_AUTH_TOKEN', '<token>', 'User')
  ```
  Once set this persists across sessions — no need to recreate per release.

## Version bump

Bump the version string in **four places** (keep them in sync):

| File | Field |
|---|---|
| `OhMyGridPlugin.cs` | `public const string PluginVersion` |
| `OhMyGrid.csproj` | `<Version>` |
| `manifest.json` | `version_number` |
| `thunderstore.toml` | `package.versionNumber` |

Update `CHANGELOG.md` with the new section. Keep entries terse and
user-focused.

## Build + package

From the repo root:

```powershell
# (1) Build the DLL
dotnet build -c Release

# (2) Stage and zip the package
$ver  = '<new-version>'   # e.g. 1.1.0
$stg  = "dist/_staging"
Remove-Item $stg -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $stg | Out-Null
Copy-Item manifest.json, icon.png, README.md, CHANGELOG.md $stg
Copy-Item bin\Release\OhMyGrid.dll $stg
$zip = "dist/OhMyGrid-$ver.zip"
Remove-Item $zip -Force -ErrorAction SilentlyContinue
Compress-Archive -Path "$stg/*" -DestinationPath $zip -CompressionLevel Optimal
Remove-Item $stg -Recurse -Force
```

## Publish

```powershell
tcli publish --file dist/OhMyGrid-$ver.zip --token $env:TCLI_AUTH_TOKEN
```

`thunderstore.toml` already pins the namespace, name, version, target
community (`valheim`), and categories (`mods`, `ai-generated`). If you change
the target, update the toml *before* publishing. The `ai-generated` tag must
stay — every OhMyGrid release ships with it since the bulk of the code is
written through Claude Code, and that's the team-wide disclosure rule.

Successful upload prints a `Successfully published suspicious_geet-OhMyGrid`
line plus the download URL. The release becomes visible on
[the package page](https://thunderstore.io/c/valheim/p/suspicious_geet/OhMyGrid/)
within seconds.

## Building on beelink-server (Linux, no Valheim client)

The dedicated server's `valheim_server_Data/Managed/` ships the same
`assembly_valheim.dll` + Unity modules the mod references, and lloesche's
image keeps a BepInEx core at `/opt/valheim/bepinex/BepInEx/core`. Stage them
into a fake install layout and build in a dotnet container (no dotnet on host):

```bash
ST=/tmp/valheim-stage; mkdir -p $ST/valheim_Data/Managed $ST/BepInEx/core
for d in assembly_valheim assembly_guiutils UnityEngine UnityEngine.CoreModule UnityEngine.PhysicsModule          UnityEngine.InputLegacyModule UnityEngine.IMGUIModule UnityEngine.TextRenderingModule; do
  docker cp valheim:/opt/valheim/server/valheim_server_Data/Managed/$d.dll $ST/valheim_Data/Managed/; done
docker cp valheim:/opt/valheim/bepinex/BepInEx/core/. $ST/BepInEx/core/
docker run --rm -v "$PWD":/src -v $ST:/valheim -w /src mcr.microsoft.com/dotnet/sdk:8.0   dotnet build -c Release -p:ValheimInstall=/valheim
```

Package with python (`zipfile`) since `zip` isn't installed, then publish with
`bin/publish` — it reads the token from `secret-provider` (`thunderstore_token`,
stored once with `bin/set-thunderstore-token`) and runs `tcli` in the same
container. Verify signatures after a Valheim update with
`ilspycmd -il` on the built DLL (see 1.0.1 in the changelog for why).

## Git hygiene

```powershell
git add -A
git commit -m "Release v$ver"
git tag -a "v$ver" -m "Thunderstore v$ver"
git push origin master
git push origin "v$ver"
```

## Rollback

Thunderstore does not allow deleting versions — only **deprecating** them.
If a release is broken, ship a `vX.Y.Z+1` with the fix; the bad version
remains in history but mod managers default to the latest.

To deprecate a bad version, go to
`https://thunderstore.io/c/valheim/p/suspicious_geet/OhMyGrid/<version>/`
and use the team-management UI.
