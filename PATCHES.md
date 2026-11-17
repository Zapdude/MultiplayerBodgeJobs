# Patch tracking

Changes this fork carries on top of [rwmt/Multiplayer-Compatibility](https://github.com/rwmt/Multiplayer-Compatibility),
and what it takes to send each one upstream. `main` is the 1.6 fork; `1.7` is the 1.7 fork.

Keep this file current when a patch is added, finished or sent upstream. A patch that is ready
for a pull request should have its files, the exact behaviour, and a test plan listed here.

## 1. Unbound weapon customization sync

| | |
| --- | --- |
| Files | `Source/UnboundWeaponCustomization.cs` |
| Branches | `main`, `1.7` |
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
| Branches | `main` |
| Upstream | PR candidate to `master` (1.6) |
| Status | implemented, needs a 1.6 + Odyssey load test |

`1.7` already carries this guard; this is the 1.6 backport.

**Problem.** Vanilla Fishing Expanded ships a different assembly when Odyssey is installed. That
build drops `Command_SetFishList`, `Zone_Fishing` and `JobDriver_Fish`, so the compat class's type
lookups return null and the constructor throws during `MpCompatLoader.Load`. The loader catches it
and logs, but the compat class stays half-initialised and the error repeats on every load.

**What the change does.** Wraps the constructor body in the same condition the 1.7 branch uses:

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

- The 1.7 module code was reconstructed from the temporary 1.7 build because upstream has no public
  1.7 branch. See `NOTICE.md`.
- `About/About.xml` depends on `zap.multiplayerbodged` instead of `rwmt.Multiplayer`, because this
  fork runs against the Multiplayer Bodged build.
- `main` removes the old Bodge Jobs entry points (`MultiplayerBodgeJobsMod`,
  `SkipObsoleteVcefCompat`) now that the Unbound patch and the VCEF fix live in the compat modules.
- The `Referenced` assembly and its modules are not part of this fork yet on either branch.
