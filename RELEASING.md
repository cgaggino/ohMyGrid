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
community (`valheim`), and category (`mods`). If you change the target,
update the toml *before* publishing.

Successful upload prints a `Successfully published suspicious_geet-OhMyGrid`
line plus the download URL. The release becomes visible on
[the package page](https://thunderstore.io/c/valheim/p/suspicious_geet/OhMyGrid/)
within seconds.

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
