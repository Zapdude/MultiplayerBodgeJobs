# Patch tracking — `1.7` branch

Changes this fork carries on top of [rwmt/Multiplayer-Compatibility](https://github.com/rwmt/Multiplayer-Compatibility)
for RimWorld 1.7, and what it takes to send each one upstream. This file covers the `1.7` branch
only; `main` keeps its own tracker for the 1.6 fork.

Keep this file current when a patch is added, finished or sent upstream. A patch that is ready
for a pull request should have its files, the exact behaviour, and a test plan listed here.

## Folding from 1.6

- Ignore 1.6 commit `7f18e3a` (the VGE2 global-ID scope). This branch suppresses the landing preview
  through `VanillaGravshipPreview` and its VGE2 build generates the crew outside the preview; do not
  carry the 1.6 scope. The Unbound dialog-close part of that commit is already on this branch
  separately. See the 1.6 `MERGE_IGNORE.md`.

## 1. Unbound weapon customization sync

| | |
| --- | --- |
| Files | `Source/UnboundWeaponCustomization.cs` |
| Upstream | new compat module, PR candidate |
| Status | implemented, two-client test exposed and fixed a dialog-close race; re-test pending |

Targets Persona Weapons Unbound (`shunter.personaweaponsunbound`) and Unique Weapons Unbound
(`shunter.uniqueweaponsunbound`).

**Problem.** Both mods start the customize job through `Pawn_JobTracker.TryTakeOrderedJob`, which
Multiplayer syncs, but the chosen spec is committed afterwards by
`JobDriver_CustomizeWeapon.SetSpec`. That call only happens on the client that pressed Confirm, so
every other client keeps the old spec and desyncs.

**What the module does.** Resolves the driver type, the spec type and the `spec` field per mod,
patches `SetSpec`, and routes the commit through a synced method registered with
`SyncContext.QueueOrder_Down`. The spec is a plain `IExposable`, so the parameter is registered with
`ExposeParameter(1)` and travels by Scribe. The synced method repeats the dialog's reservation and
writes the field directly; the prefix only intercepts while the interface is open, so a command
never re-enters it. Everything is reflection-based, so the compat assembly takes no compile-time
dependency on the two Unbound assemblies.

It also registers `Dialog_WeaponCustomization` with `DialogUtilities.RegisterDialogCloseSync`. The
driver's waiting toil ends the job with `Incompletable` as soon as `spec` is null and the dialog is
gone, and Confirm calls `SetSpec` then `Close()` in the same frame. Deferring only the spec left the
confirming client with a closed dialog and a null spec for a tick: it ended the job, rolled the
trash/pack-food job, consumed map RNG and handed out a new job ID while the other client kept
waiting. Deferring the close into the same command stream keeps every client on the same toil, and
makes Cancel/Escape/close-X deterministic too.

The dialog also reserves the ingredients and commits the job's haul plan before it calls `SetSpec`,
and that work only happens on the confirming client. The module patches
`HaulPlanning.IngredientReservation.TryReserveIngredientsForJob` so the dialog's own call is skipped
in multiplayer, and the synced commit repeats it on every client before writing the spec. If the
reservation fails there, the spec is not written and an error is logged. The skip is installed only
when a default `ReservationResult` reports success, because skipping the original leaves the dialog
checking the return value's default.

**Test plan.** Host and one client, both mods installed. Host customizes a weapon; client opens the
same weapon and sees the new name, colour, texture and traits. Repeat with the client pressing
Confirm. The non-confirming client's dialog must close when the confirming client commits, and its
job must continue past the workbench instead of ending. Both clients must end up with the same
ingredient reservations and haul plan. Repeat with Cancel and with Escape. Check
`Player.log` for a missing `SetSpec` warning after a mod update.

**PR notes.** The module is self-contained. A PR only needs the new file; it follows the
`[MpCompatFor]` convention and needs no project changes.

## 2. Supply Shelf: freeze setting sync and the current reservation patch

| | |
| --- | --- |
| Files | `Source/SupplyShelf.cs`, `Source/SupplyShelfSettingsComponent.cs` |
| Upstream | fork fix on the reconstructed 1.7 module; PR candidate when upstream carries 1.7 |
| Status | implemented, needs a two-client test |

The module as it shipped in the temp 1.7 build predates the mod's settings window and the current
`DoBillShelfIngredients_Patch`, so two things change here.

**Freeze setting.** `freeze` changes def power use and each shelf's cached frozen state, so a
client with a different value behaves differently. The settings window is patched so only the host
can toggle it, the host's change travels through a host-only synced method, and the per-game
snapshot registered with `SessionServices` carries the value to a client that joins later (a small
game component, as in the other settings modules, keeps the snapshot in the save). The reference
also tracks a session epoch and blocks the mod's own `Apply` while the window is open; here the
window's finalizer restores the previous snapshot, and re-applies it once on the frame of the
toggle, so the field only moves when the command runs. A session change between the window's first
and last frame is not possible (both run inside one call), and the `Current.Game` check discards a
stale edit, so the epoch would add nothing.

**Reservation check.** The build installed today calls `CompDirectSupply.Covers` from
`DoBillShelfIngredients_Patch.Postfix`, not from the older `CollectIngredientsToils_Patch` lambda
the module used to patch, so the module failed to load against the current mod. The transpiler now
replaces that one call with a check that also requires the bill pawn to be on the bench's map,
allowed to take the ingredient, and holding the reservation, so the ingredients a bill queues are
the same on every client.

**Test plan.** Two clients, Supply Shelf installed. Link a bench and move the stack limit on the
client; both clients must show the link and the same limit, also after a save and load. Queue a bill
that pulls from the shelf and confirm both clients plan the same haul. In the mod settings a client
must see the freeze checkbox disabled; the host toggles it and both clients' shelf power draw and
frozen contents change. A client that joins after the toggle must start with the host's value.

**PR notes.** The module plus one game component, reflection-only, so no compile-time reference to
the mod.

## 3. Squishy Melee: mirror the presentation flags during a synced block

| | |
| --- | --- |
| Files | `Source/SquishyMelee.cs` |
| Upstream | fork fix on the reconstructed 1.7 module; PR candidate when upstream carries 1.7 |
| Status | implemented, needs a visual two-client test |

The author sets `MeleeTrack.Caster` (the pawn whose block is being animated) and `MeleeTrack.Blocked`
around its own damage path. Multiplayer applies the shield block from this module's `ApplyGuard`, so
those thread-static fields were never set and the block animation and the `SM_Blocked` text were
missing for the blocked pawn. Install now mirrors both fields, and the block path sets `Blocked` when
the damage instigator is the tracked caster.

**Test plan.** Two clients, SQUISHY Melee installed, shield block enabled. Let a pawn block a melee
attack: both clients must show the block motion and the blocked text for that pawn, and the pawn
must take the reduced damage on both.

**PR notes.** Four lines in the install and four in the block path; no behaviour changes elsewhere.

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

## Requires a newer Multiplayer than the 1.7 fork had

`Source_Referenced/VanillaGravshipExpanded.cs` reflects on three Multiplayer types:

- `Multiplayer.Client.Patches.GravshipCutsceneSync.HasPendingTakeoff`
- `Multiplayer.Client.Persistent.GravshipMapDecisions.HandleDialog` / `.WasManaged`
- `Multiplayer.Client.Persistent.GravshipUiOwnership.LandingPresentationAllowed` /
  `.SetLandingMarkerFromUi`

The 1.7 Multiplayer fork (`Multiplayer-fork`, branch `experimental-1.7`) lacked them, and the module
throws when a contract is missing, which aborted every VGE sync registration after it. Those types
were recovered from the 1.7 Multiplayer build (workshop `3820003322`) and ported into that branch as
commit `ab47b9e`. Without a Multiplayer build that carries them, the whole `vanillaexpanded.gravship`
module logs `MPCompat :: Exception loading vanillaexpanded.gravship` and does nothing.

Not ported, because this module does not use them: `GravshipFlightPreparation` and the ritual-dialog
guards from the same build.

## Not built: the Chapter 2 body module

The temporary 1.7 build also ships `Referenced/Multiplayer_Gravship2Overrides.dll`. The loader
requires it when `vanillaexpanded.gravship2` is loaded and throws `FileNotFoundException` otherwise,
which aborts compat initialisation entirely. The file is 25 classes in
`Multiplayer.Compat.Workshop17.Bodies`, each replacing one VGE2 method body with a deterministic
version, applied by `Source/VanillaGravshipExpanded2Workshop.cs` from an embedded
`Workshop17.replacements.txt` manifest. Building it needs a `Source_Referenced/Gravship2` project
compiled against `VanillaGravshipExpanded2.dll` (installed, workshop `3799737423`) plus that manifest
extracted from the shipped assembly. Until then, Chapter 2 players hit the missing-file throw.
