# Multiplayer Compatibility, 1.6 (Bodge Jobs fork)

A RimWorld 1.6 fork of [Multiplayer Compatibility](https://github.com/rwmt/Multiplayer-Compatibility),
maintained as the `main` branch of the Multiplayer Bodge Jobs repository. The mod keeps the upstream
package id so existing saves, plans and dependencies resolve to it.

This branch is the upstream 1.6 source with the fork's changes on top. `PATCHES.md` lists them, their
upstream status and their test plans. `NOTICE.md` records provenance.

## What is in here

- `Source/` - the compat assembly, unchanged from upstream except for the patches in `PATCHES.md`.
  One `.cs` file per supported mod under `Source/Mods/`, plus the loader, the attribute-based patch
  helpers and `PatchingUtilities`.
- `Source/Mods/UnboundWeaponCustomization.cs` - the fork's own patch for Persona Weapons Unbound and
  Unique Weapons Unbound. It syncs the customization commit that the mod only performs on the client
  that pressed Confirm.
- `Source/Mods/VanillaFishingExpanded.cs` - carries the 1.7 Odyssey guard, backported.
- `Assemblies/` - the built `Multiplayer_Compat.dll` that RimWorld loads.
- `About/` - mod metadata.

## Building

```
dotnet build Source/Multiplayer_Compat.csproj -c Release
```

The project targets `net48` and restores its references from NuGet: `Krafs.Rimworld.Ref 1.6.*`,
`Krafs.Publicizer 2.3.*`, `Lib.Harmony 2.3.6` and `RimWorld.MultiplayerAPI 0.6.0`. The output goes to
`Assemblies/`.

## What is not here

- `Source_Referenced/` and `References/` are not carried. That is the second assembly upstream builds
  for the modules that need compile-time references to other mods' assemblies; without it the
  `Referenced` modules (`PipeSystem17`, the VFE faction modules, `VanillaGravshipExpanded` and
  `VanillaSeriesRegistration17`) are not included. `MpCompatLoader.LoadConditional` handles the
  assembly being absent.
- `Languages/` is carried from upstream as-is.

## License

MIT. See `LICENSE`. Original copyright belongs to the Multiplayer Compatibility contributors.
