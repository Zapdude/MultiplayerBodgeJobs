# Multiplayer Compatibility, 1.6 (Bodge Jobs fork)

A RimWorld 1.6 fork of [Multiplayer Compatibility](https://github.com/rwmt/Multiplayer-Compatibility),
maintained as the `main` branch of the Multiplayer Bodge Jobs repository. This fork uses its own
package id, `zap.multiplayerbodgejobs`, so disable the original Multiplayer Compatibility if you use
it; the game will not warn about the duplicate.

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
dotnet build Source_Referenced/Multiplayer_Compat_Referenced.csproj -c Release
```

The projects target `net48` and restore their references from NuGet: `Krafs.Rimworld.Ref 1.6.*`,
`Krafs.Publicizer 2.3.*`, `Lib.Harmony 2.3.6` and `RimWorld.MultiplayerAPI 0.6.0`. The outputs go to
`Assemblies/` and `Referenced/`. `Source_Referenced` additionally references the installed 1.6
assemblies of the mods whose types its modules use.

## What is not here

- `References/`, the third-party assemblies upstream copies into the build, is not carried. The
  `Source_Referenced` project references the same assemblies from the installed workshop library
  instead.
- `Source_Referenced/` carries the 1.6 modules that have a compile-time dependency on another mod:
  `AlteredCarbon`, `HemogenExtractor`, `VanillaExpandedFrameworkReferenced`, `VanillaFactionsClassical`,
  `VanillaFactionsDeserters`, `VanillaFactionsEmpire` and `VanillaGravshipExpanded`. The sources for
  `ARimWorldOfMagic`, `CashRegister`, `EccentricTechDefenseGrid`, `Gastronomy`, `Storefront`,
  `VehicleFramework` and `ZombieLand` are kept, but excluded from the build because their mods are not
  installed at 1.6. `MpCompatLoader.LoadConditional` handles the assembly being absent.
- `Languages/` is carried from upstream as-is.

## License

MIT. See `LICENSE`. Original copyright belongs to the Multiplayer Compatibility contributors.
