# Patch tracking — `main` branch (RimWorld 1.6)

Changes this fork carries on top of [rwmt/Multiplayer-Compatibility](https://github.com/rwmt/Multiplayer-Compatibility)
for RimWorld 1.6, and what it takes to send each one upstream. This file covers the `main` branch
only; `1.7` keeps its own tracker for the 1.7 build.

Keep this file current when a patch is added, finished or sent upstream. A patch that is ready
for a pull request should have its files, the exact behaviour, and a test plan listed here.

## 1. Unbound weapon customization sync

| | |
| --- | --- |
| Files | `Source/Mods/UnboundWeaponCustomization.cs` |
| Upstream | new compat module, PR candidate |
| Status | implemented, needs a two-client test |

Targets Persona Weapons Unbound (`shunter.personaweaponsunbound`) and Unique Weapons Unbound
(`shunter.uniqueweaponsunbound`).

**Problem.** Both mods start the customize job through `Pawn_JobTracker.TryTakeOrderedJob`, which
Multiplayer syncs, but the chosen spec is committed afterwards by
`JobDriver_CustomizeWeapon.SetSpec`. That call only happens on the client that pressed Confirm, so
every other client keeps the old spec and desyncs.

**What the module does.** Resolves the driver type, the spec type and the `spec` field per mod,
patches `SetSpec`, and routes the commit through a synced method registered with
`SyncContext.QueueOrder_Down`. The spec is a plain `IExposable`, so the parameter is registered with
`ExposeParameter(1)` and travels by Scribe. The synced method writes the field directly to avoid
re-entering the prefix. Everything is reflection-based, so the compat assembly takes no compile-time
dependency on the two Unbound assemblies.

**Test plan.** Host and one client, both mods installed. Host customizes a weapon; client opens the
same weapon and sees the new name, colour, texture and traits. Repeat with the client pressing
Confirm. Check `Player.log` for a missing `SetSpec` warning after a mod update.

**PR notes.** The module is self-contained. A PR only needs the new file; it follows the
`[MpCompatFor]` convention and needs no project changes.

## 2. VCEF Odyssey guard (1.6 backport)

| | |
| --- | --- |
| Files | `Source/Mods/VanillaFishingExpanded.cs` |
| Upstream | PR candidate to `master` (1.6) |
| Status | implemented, needs a 1.6 + Odyssey load test |

Backport of the guard the 1.7 build ships, needed here because this is the branch that targets
upstream `master`.

**Problem.** Vanilla Fishing Expanded ships a different assembly when Odyssey is installed. That
build drops `Command_SetFishList`, `Zone_Fishing` and `JobDriver_Fish`, so the compat class's type
lookups return null and the constructor throws during `MpCompatLoader.Load`. The loader catches it
and logs, but the compat class stays half-initialised and the error repeats on every load.

**What the change does.** Wraps the constructor body in the same condition the 1.7 build uses:

```csharp
if (!ModsConfig.OdysseyActive
    || AccessTools.TypeByName("VCE_Fishing.VCE_Fishing_FishingUtility_GetCatchesFor_Patch") == null)
```

The marker type means "new VCEF build"; when Odyssey is active and the marker is present, the module
does not run. When Odyssey is off, or the old build is installed, it runs as before.

**Test plan.** 1.6 with Odyssey and VCEF: load a game, confirm `Player.log` has no
`MPCompat :: Exception loading VanillaExpanded.VCEF` and no repeated constructor error. Then a
non-Odyssey 1.6 load: confirm VCEF fishing zones still sync (change a zone's fish list on one client
and see it on the other).

**PR notes.** One file, one constructor. The existing constructor-skip workaround in the old Bodge
Jobs mod is removed, so this replaces it rather than stacking with it.

## Fork plumbing, not for upstream

- `About/About.xml` uses the fork's own package id, `zap.multiplayerbodgejobs`, so the original
  Multiplayer Compatibility has to be disabled when this is enabled. It depends on
  `zap.multiplayerbodged` because this fork runs against the Multiplayer Bodged build.
- `Source_Referenced/` compiles to `Referenced/Multiplayer_Compat_Referenced.dll` from the 1.6
  assembly upstream ships in workshop item 1629973374. It builds `AlteredCarbon`, `HemogenExtractor`,
  `VanillaExpandedFrameworkReferenced`, `VanillaFactionsClassical`, `VanillaFactionsDeserters`,
  `VanillaFactionsEmpire` and `VanillaGravshipExpanded`; the remaining modules stay in the tree but
  are excluded because their mods are not installed at 1.6.
- This branch does not carry the old Bodge Jobs entry points (`MultiplayerBodgeJobsMod`,
  `SkipObsoleteVcefCompat`).
