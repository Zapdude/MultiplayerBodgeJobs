# Multiplayer Compatibility, 1.7 (Bodge Jobs fork)

A RimWorld 1.7 fork of [Multiplayer Compatibility](https://github.com/rwmt/Multiplayer-Compatibility),
maintained as the `1.7` branch of the Multiplayer Bodge Jobs repository. The mod keeps the upstream
package id so existing saves, plans and dependencies resolve to it.

The 1.6 project is MIT licensed. This branch is a 1.7 continuation of it, not a rewrite: the module
code is the upstream compat set as it exists in the temporary 1.7 build, with the library and loader
rebuilt for RimWorld 1.7 on .NET 10. `NOTICE.md` records exactly where the 1.7 code came from and what
was repaired while bringing it in; `PATCHES.md` tracks the changes this fork carries and which of them
are ready to go upstream.

## What is in here

- `Source/` - the compat assembly. One `.cs` file per supported mod, plus the loader, the
  attribute-based patch helpers and `PatchingUtilities`.
- `Source/UnboundWeaponCustomization.cs` - not from upstream. This is the fork's own patch for
  Persona Weapons Unbound and Unique Weapons Unbound, carried over from the original Bodge Jobs mod.
  It syncs the customization commit that the mod only performs on the client that pressed Confirm.
- `Assemblies/` - the built `Multiplayer_Compat.dll` that RimWorld loads.
- `About/` - mod metadata.
- `PATCHES.md` - every fork change, its upstream PR status and its test plan.

## Building

Requirements: .NET 10 SDK. The project takes its references from NuGet, so no game install is
needed:

- `Krafs.Rimworld.Ref 1.7.4985-unstable`
- `Krafs.Publicizer 2.3.0` (the compat code uses non-public RimWorld members)
- `Lib.Harmony 3.0.0-rc.3`
- `RimWorld.MultiplayerAPI 0.6.0`
- `Mono.Cecil 0.11.6` (used by the conditional loader)

```
dotnet build Source/Multiplayer_Compat.csproj -c Release
```

The output goes to `Assemblies/`.

## Known gaps

- The upstream project builds a second assembly, `Referenced/Multiplayer_Compat_Referenced.dll`,
  for the modules that need compile-time references to other mods' assemblies. Those sources are
  not in this repository yet, so the following compat modules are not included:
  `PipeSystem17`, `VanillaExpandedFrameworkReferenced`, `VanillaFactionsClassical`,
  `VanillaFactionsDeserters`, `VanillaFactionsEmpire`, `VanillaGravshipExpanded`,
  `VanillaSeriesRegistration17`. `MpCompatLoader.LoadConditional` already handles the assembly
  being absent.
- Many modules depend on the 1.7 Multiplayer compatibility services (`RoomSettings`,
  `CommandAuthority`, `OwnerClock`, `DecisionOwnership`, `CheckpointState`). If the Multiplayer
  build does not provide them, those modules log `MPCompat :: Exception loading <pkg>` and do
  nothing; the rest still load.
- The 1.7 code is a decompilation. It compiles and matches the shipped assembly, but it has not
  been cleaned into upstream source style yet.

## License

MIT. See `LICENSE`. Original copyright belongs to the Multiplayer Compatibility contributors;
see `NOTICE.md` for the 1.7 provenance.