# Multiplayer Compatibility, 1.7 (Bodge Jobs fork)

A RimWorld 1.7 fork of [Multiplayer Compatibility](https://github.com/rwmt/Multiplayer-Compatibility),
maintained as the `1.7` branch of the Multiplayer Bodge Jobs repository. This fork uses its own
package id, `zap.multiplayerbodgejobs`, so disable the original Multiplayer Compatibility if you use
it; the game will not warn about the duplicate.

The 1.6 project is MIT licensed. This branch is a 1.7 continuation of it, not a rewrite: the module
code is the upstream compat set as it exists in the temporary 1.7 build, with the library and loader
rebuilt for RimWorld 1.7 on .NET 10. `NOTICE.md` records exactly where the 1.7 code came from and what
was repaired while bringing it in; `PATCHES.md` tracks the changes this fork carries and which of them
are ready to go upstream.

## What is in here

- `Source/` - the compat assembly. One `.cs` file per supported mod, plus the loader, the
  attribute-based patch helpers and `PatchingUtilities`.
- `Source_Referenced/` - the modules that need compile-time references to other mods' assemblies.
  See the table below.
- `Source/UnboundWeaponCustomization.cs` - not from upstream. This is the fork's own patch for
  Persona Weapons Unbound and Unique Weapons Unbound, carried over from the original Bodge Jobs mod.
  It syncs the customization commit that the mod only performs on the client that pressed Confirm.
- `Assemblies/` - the built `Multiplayer_Compat.dll` that RimWorld loads.
- `Referenced/` - the built `Multiplayer_Compat_Referenced.dll` that the loader merges at startup.
- `About/` - mod metadata.
- `PATCHES.md` - every fork change, its upstream PR status and its test plan.

## Building

Requirements: .NET 10 SDK. The main project takes its references from NuGet, so no game install is
needed:

- `Krafs.Rimworld.Ref 1.7.4985-unstable`
- `Krafs.Publicizer 2.3.0` (the compat code uses non-public RimWorld members)
- `Lib.Harmony 3.0.0-rc.3`
- `RimWorld.MultiplayerAPI 0.6.0`
- `Mono.Cecil 0.11.6` (used by the conditional loader)

```
dotnet build Source/Multiplayer_Compat.csproj -c Release
dotnet build Source_Referenced/Multiplayer_Compat_Referenced.csproj -c Release
```

The first output goes to `Assemblies/`, the second to `Referenced/`. The second project references
`Assemblies/Multiplayer_Compat.dll`, so build the main project first (the project reference enforces
the order).

`Source_Referenced` compiles against the installed mods, so it also needs the Steam Workshop and the
game's Managed folder. Both paths default to the standard Steam locations and can be overridden:

```
dotnet build Source_Referenced/Multiplayer_Compat_Referenced.csproj -c Release `
  -p:WorkshopDir="D:\SteamLibrary\steamapps\workshop\content\294100" `
  -p:RimWorldManagedDir="D:\SteamLibrary\steamapps\common\RimWorld\RimWorldWin64_Data\Managed"
```

It uses `2023507013` (VEF, which ships `PipeSystem.dll` and `Outposts.dll`), `2938820380`
(VFE Empire) and `3609835606` (VGE), all from their `1.7` folders.

## What is in the Referenced assembly

`Source_Referenced/` holds the modules that need compile-time references to other mods' assemblies:

| Module | Compiled | Needs |
| --- | --- | --- |
| `PipeSystem17` | yes | `PipeSystem.dll` from VEF 1.7 |
| `VanillaExpandedFrameworkReferenced` | yes | `Outposts.dll` from VEF 1.7 |
| `VanillaFactionsEmpire` | yes | `VFEEmpire.dll` 1.7 |
| `VanillaGravshipExpanded` | yes | `VanillaGravshipExpanded.dll` 1.7 |
| `VanillaSeriesRegistration17` | yes | nothing |
| `VanillaFactionsClassical` | no | no 1.7 build of VFE Classical is installed |
| `VanillaFactionsDeserters` | no | no 1.7 build of VFE Deserters is installed |

The two excluded modules are in the tree for reading; their `Compile Remove` lines and the missing
references are marked in the project file. Add the 1.7 DLLs and remove the lines when those mods are
ported.

## Known gaps

- Many modules depend on the 1.7 Multiplayer compatibility services (`RoomSettings`,
  `CommandAuthority`, `OwnerClock`, `DecisionOwnership`, `CheckpointState`). If the Multiplayer build
  does not provide them, those modules log `MPCompat :: Exception loading <pkg>` and do nothing; the
  rest still load.
- The 1.7 code is a decompilation. It compiles and matches the shipped assembly, but it has not been
  cleaned into upstream source style yet.

## License

MIT. See `LICENSE`. Original copyright belongs to the Multiplayer Compatibility contributors;
see `NOTICE.md` for the 1.7 provenance.