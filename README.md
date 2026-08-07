# S.T.A.G.E.

S.T.A.G.E. is a Windows tool for managing FFXIV animation, VFX, and audio mods.

## Basic usage

1. Open **Settings → Mod Folders**, add your Penumbra mod folder, and select the active mod.
2. Use **Settings → Audio** to configure the baseline SCD, donor SCD, volume, and EQ options.
3. Use **Settings → Animation** to select an optional donor PAP for animation imports and TMB cleanup.
4. Add playlists and songs from the main window. Right-click entries to access animation, expression, VFX, and PAP tools.
5. Use **Settings → Maintenance** to organize the mod or create and restore backups before broad changes.

> **Note:** Expression modification is experimental and may not work in its current state. Back up your mod before using it.

## Build

Open `STAGE.sln` or run:

```powershell
dotnet build STAGE.csproj
```

## Credits

Built on the foundation created by **Solona** and **Pickles**.
