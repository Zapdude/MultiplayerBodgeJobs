# Patch tracking — `1.7` branch

Changes this fork carries on top of [rwmt/Multiplayer-Compatibility](https://github.com/rwmt/Multiplayer-Compatibility)
for RimWorld 1.7, and what it takes to send each one upstream. This file covers the `1.7` branch
only; `main` keeps its own tracker for the 1.6 fork.

Keep this file current when a patch is added, finished or sent upstream. A patch that is ready
for a pull request should have its files, the exact behaviour, and a test plan listed here.

## 1. Unbound weapon customization sync

| | |
| --- | --- |
| Files | `Source/UnboundWeaponCustomization.cs` |
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

## Carried with the 1.7 code, not a fork patch

- The VCEF Odyssey guard in `Source/VanillaFishingExpanded.cs` arrives with the reconstructed 1.7
  module code, so it is not tracked as a patch on this branch. The 1.6 branch backports it.

## Fork plumbing, not for upstream

- All module code in `Source/` is reconstructed from the temporary 1.7 build because upstream has no
  public 1.7 branch. Decompiler, reference assemblies and the repairs applied are listed in
  `NOTICE.md`.
- `Source_Referenced/` compiles to `Referenced/Multiplayer_Compat_Referenced.dll`, five modules.
  `VanillaFactionsClassical` and `VanillaFactionsDeserters` sit in the tree but are excluded until
  those mods have 1.7 builds.
- `About/About.xml` uses the fork's own package id, `zap.multiplayerbodgejobs`, so the original
  Multiplayer Compatibility has to be disabled when this is enabled. It depends on
  `zap.multiplayerbodged` because this fork runs against the Multiplayer Bodged build.
- This branch does not carry the old Bodge Jobs entry points (`MultiplayerBodgeJobsMod`,
  `SkipObsoleteVcefCompat`).
