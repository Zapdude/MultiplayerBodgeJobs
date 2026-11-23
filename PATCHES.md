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

## 3. VGE2 landing crew/contents global IDs (1.6)

| | |
| --- | --- |
| Files | `Source/VanillaGravshipGlobalIds.cs`, `Source/Mods/GravshipLandingIdScope.cs` |
| Upstream | fork-only; the 1.7 branch uses preview suppression instead |
| Status | implemented, awaiting a two-client test |

VGE2 generates the landing raid crew only inside `CaptureGravshipCoroutine` through
`GeneratePawns`, then `DespawnPawns`, and `Impact` respawns the stored `pawnPositions` on the live
map. The coroutine is interface work, so `UniqueIdsPatch` gives the crew local negative IDs, which
they keep into the live map. Negative IDs break `Graphic_Random` (negative modulo), and any
`thingIDNumber`-seeded RNG or `HashSet<Thing>` order later.

The 1.7 branch suppresses the coroutine, but in the 1.6 VGE2 that would remove the crew entirely.
The 1.6 module keeps the preview and forces **global** thing IDs during `GeneratePawns` (crew) and
`Impact` (live station contents) through the new `UniqueIdsPatch.forceGlobalIds` flag in the
Multiplayer fork. The temp-map station props remain on local IDs and are discarded with the temp
map; the Graphic_Random guard covers their preview render.

**Test plan.** Start the VGE2 gravship scenario, trigger the enemy gravship arrival, and confirm
`Player.log` has the `[MPCompat] Forcing global thing IDs during VGE2 ...` lines, no `[MP negative
id]` lines for the crew or live station contents, and no `Exception printing VGE2_*` spam. Both
clients must see the same crew and corpses.

**Merge-ignore for 1.7.** Commit `7f18e3a`. This global-ID scope is 1.6-only: the 1.7 branch
suppresses `CaptureGravshipCoroutine` through `VanillaGravshipPreview`, and its VGE2 build generates
the crew outside that coroutine. Do not fold this commit into 1.7. See `MERGE_IGNORE.md`.

## 4. Graphic_Random negative index guard

| | |
| --- | --- |
| Files | `Source/GraphicRandomNegativeIndexGuard.cs` |
| Upstream | not for upstream; defensive render guard |
| Status | separate commit, can be reverted independently |

`Graphic_Random.SubGraphicFor` computes `subGraphics[num % subGraphics.Length]` from
`OverrideGraphicIndex ?? thingIDNumber`. A negative thing ID produces a negative index and an
`IndexOutOfRangeException` every print (the VGE2 station-prop spam). The guard normalizes the index
and falls back to `BaseContent.BadGraphic` for an empty collection. It is a stopgap: negative IDs
should not reach a live map at all (see section 3). Keep this in its own commit so it can be
reverted without touching the landing-preview fix.

## 5. VGE landing launch-info sync (1.6)

| | |
| --- | --- |
| Files | `Source_Referenced/Multiplayer.Compat/VanillaGravshipExpanded.cs`, `Referenced/Multiplayer_Compat_Referenced.dll` |
| Upstream | fork-only |
| Status | implemented, needs a two-client landing test |

The VGE landing postfix reads `ExtendedLaunchInfo` (launch source tile, forced boon/mishap,
gravtech researcher, VGE2 components). On the client it can be null, which NREs in
`TryTriggerLaunchBoon` and changes gravdata/maintenance results. The module syncs the whole
`ExtendedLaunchInfo` (an `IExposable`) when the landing is confirmed, before the landing command,
and applies it to the engine's `LaunchInfo` on every client. This is option 1: sync the data rather
than skipping the work.

It fails loudly: a missing engine/LaunchInfo/info, a failed registration, or a client that still has
no info logs an `Error`; the landing postfix is skipped on that client instead of NREing. The sync is
installed from `GravshipLandingMarker.BeginLanding` at priority 800, so it is sent before
Multiplayer's own landing command.

**Test plan.** Host and client, VGE/VGE2 landing. Confirm `Player.log` has
`[MPCompat] VGE landing info sync installed.` at startup, no `[VGE] Exception in LandingEnded
Postfix`, and identical gravdata/maintenance/boon results. If the sync fails, the error names the
missing piece and the postfix is skipped.

## 6. Character Development wants, quirks and rewards (1.6)

| | | |
| --- | --- | --- |
| Files | `Source/Mods/CharacterDevelopment.cs` |
| Upstream | new compat module, PR candidate |
| Status | implemented, needs a two-client test |

Targets Character Development (`ferny.characterdevelopment`). Its wants tab, characters menu and
reward dialog are local UI: a button edits the selected pawn's data, the reward dialog decrements
points and adds a quirk, and the bubble physics runs from each client's frame time. Nothing there
tells the other clients, so the pawns, points and saved bubble positions diverge.

**What the module does.** Reflects the mod's types (`PawnWantsData`, `ActiveWant`, `RewardNode`,
`Quirk`, `State`, `WantsAndQuirksUtility`, the tab and dialog) and redirects each player action to a
synced method. Every synced method takes a stable key instead of a list index or object reference -
wants are keyed by def, assigned tick, reroll count and target, reward nodes by def, item and pawn -
plus a seed computed by the acting client, so the random parts run identically on every client. The
redirects only fire in a multiplayer interface; ticks and running commands keep the mod's own code
path. Covered actions: reroll, remove and reorder wants; remove and add quirks; complete a want; add
character points; generate and validate reward bubbles; clean wants with a dead target; claim a
reward. Claiming replaces the reward dialog with a small one that validates through the mod's own
`CanBestowOn`/`CanHaveWants` and then queues the claim, and the bubble physics tick is disabled in
multiplayer while the dragged position is restored after each frame, because `RewardNode.pos` is
scribed.

**Test plan.** Host and one client, both with the mod. On each client in turn: open a pawn's wants
tab and reroll, remove and reorder a want; remove a quirk through the confirmation and add one from
the characters menu; complete a want; open the characters menu and claim a bubble for a pawn. The
other client must show the same wants, quirks, point totals and bubble in the same tick, with no
`Exception` in `Player.log` and no "could not resolve" warning. Save and reload on both; the bubble
positions must match.

**PR notes.** One file, reflection-only, so it takes no compile-time dependency on the mod. The
lambda callbacks it patches (want reorder and quirk removal) are located by name and signature under
`ITab_Pawn_WantsAndQuirks`; a mod update that renames them logs a warning instead of breaking the
load.

## 7. Nice Research Tab: research queue edits from the tree UI

| | |
| --- | --- |
| Files | `Source/Mods/NiceResearchTab.cs` |
| Upstream | new compat module, PR candidate |
| Status | implemented; build verified 2026-11-23; runtime **not_run** (the mod is installed but not enabled in the active plan; the two-client queue test is pending) |
| Test scope | build only. The reflected surface matches the installed 1.6 assembly; no in-game queue edit has been observed with this module. |

Targets `andromeda.niceresearchetab`. The mod syncs its queue commands, but every `Apply*` method
solves through `ResearchTreeWidget.Instance`, which exists only on a client that has opened the
research tab in this session. On the receiving client `ApplyRestartQueue` dereferences that null at
its first instruction (`ldsfld ResearchTreeWidget::Instance`), the queues and the current project
diverge, and the game desyncs.

The module intercepts `AddToQueue`, `InsertToFront` and `RestartQueue` at `Priority.First`, solves
the prerequisite chain from research defs on the client that clicked (the mod's tree solver when the
tab exists, the def-only solver otherwise), and sends that list as one synced command. Every client
applies it from game data only: `GetQueueFor`, `GravtechQueue`, `CachedFinalizedIndex`,
`EnsureAllQueuesIsStarted`. `EnsureCurrentInQueue` and `EnsureGravtechInQueue` are skipped in the
interface, and the `Apply*` refresh calls are made null-safe so a client without the tab cannot
throw halfway through.

`InstallSafely` disables the module with one logged error if the mod surface moved. The `#fdpo`
module was read as a behaviour reference (MIT); this implementation is our own.

**Test plan.** Two clients with Nice Research Tab and this module, the research tree opened on one
client only. Host adds, inserts to front and restarts a project; the client without the tab must
apply the same queue and current project. Repeat with the client driving. Check both `Player.log`s
for the old `World cmd exception (Sync)` and the `ApplyRestartQueue` `NullReferenceException`;
neither should appear.

## 8. Storyteller: Waves & Rewards: settings and slave market

| | |
| --- | --- |
| Files | `Source/Mods/WavesRewardsShared.cs`, `WavesRewardsSettings.cs`, `WavesRewardsSlaveMarket.cs` |
| Upstream | new compat modules, PR candidates |
| Status | implemented; build verified 2026-11-23; runtime **not_run** (the mod was subscribed today, not enabled in a plan; no two-client test) |
| Test scope | build only. Reflection checked against the installed 1.6 assembly; no in-game settings edit or slave order observed. |

Targets `ruslan.wavesrewards` (`Ruslan.WavesRewards`, workshop `3841796795`). Do not confuse it with
`patrickschwarzer.waves` (`#4khv`), a water/coast mod with the same short name.

**Settings.** The simulation reads `WavesRewardsMod.Settings` continuously. In a multiplayer game the
settings window and the "who comes" dialog edit a copy; on close the fields that changed are sent as
one command and every PC applies and saves them. `modLanguage` and `verboseLog` stay per PC. 22
fields are synced, including the float, int, bool, enum and `HashSet<string>` members.

**Slave market.** The window generated its five offers per kind while drawing and paid/ordered from
the interface on one PC. Now the simulation generates both offer lists when the window is created;
"order" sends (window key, kind, count) as one command that prices, pays, places the order and
discards the rest; closing without ordering is a command too. Window keys are tick plus order within
the tick, so the same window has the same key on every client. Known limit from the reference: a
player who joins while the market is open does not receive the offers.

`InstallSafely` disables each module with one logged error if the mod surface moved. The `#fdpo`
modules were read as a behaviour reference (MIT); these are reimplementations.

**Test plan.** Two clients with the mod and this module. Change a setting (first wave day, growth,
allowed threat kinds) on the host, close the settings window, confirm the client sees the same value
and the simulation behaves the same. Open the slave market from the comms console on both; compare
the five offers per kind; order from one, confirm both see the same silver, vouchers and delivered
pawns; close without ordering on one and confirm the window closes on both.

## 9. VEF mercenary contracts (hiring / cancel)

| | |
| --- | --- |
| Files | `Source/Mods/VefHiring.cs` |
| Upstream | new compat module, PR candidate |
| Status | implemented; install verified in an isolated 1.6 menu run 2026-11-23; the two-client contract test is **not_run** |
| Test scope | module load and patch installation only. The filter, cancel lambda, ideology reset and save-check patches were all found and applied; no contract was hired or cancelled. |

Targets Vanilla Expanded Framework (`oskarpotocki.vanillafactionsexpanded.core`). MPCompat's VEF
module synced a piece of `Dialog_ContractInfo` by its position among the window's compiler-generated
methods; after the VEF update that position is the pawn-list filter, so the window tried to sync a
display-class delegate every frame, failed with "No writer for type
VEF.Planet.Dialog_ContractInfo+<>c", and was not drawn. The filter now just filters. "Cancel
contract" is one synced command that writes `HiringContractTracker.endTicks` and closes the window
on every PC. `Dialog_Hire.hiredIdeo` is cleared at every load, and the developer-mode save check
treats the `Hireable_` objects as saved because VEF rebuilds them before each load.

The filter and the cancel lambda are located by name, signature and IL (the filter is the
four-instruction `Pawn != null`; cancel is the lambda that stores to `endTicks`). A VEF change drops
that part with a logged message instead of aborting compat.

Behaviour reference: Multiplayer Fix (MIT); implementation is ours.

**Test plan.** Two clients with VEF and a contract running. Open the contract window on a client
that has not opened it; it must draw the pawn list. Cancel from either; the contract ends and the
window closes on both. Hire from one and confirm the pawn, silver and duration on the other. Repeat
with a refusal and an expired contract.

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
