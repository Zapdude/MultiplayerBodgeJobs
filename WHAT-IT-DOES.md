## Description

So, you wanna learn what exactly this mod does? Alright then sonny, settle down and let old pepaw tell you all about it.

## Mods Patched

* Unique Persona Weapons Unbound
* Unique Weapons Unbound
* Vanilla Fishing Expanded (via Multiplayer Compatibility)

## Purpose of patches

### UWB

When customizing weapons, both mods start the customize job through Pawn_JobTracker.TryTakeOrderedJob, which Multiplayer syncs fine, but the UI communicates the chosen design by calling JobDriver_CustomizeWeapon.SetSpec on the already-running driver (from starting it), and that call is only given to whoever pressed the confirm button. In such an event every other player (including the host if they didnt press it) DIES; no I'm kidding they just desync immediately. This patch puts the call through an MP sync method so all clients get the same call and no desync occurs. Yay! Nice and easy, right?

### VCEF

Multiplayer Compatibility ships a handler for Vanilla Fishing Expanded that was written against the build without Odyssey. When Odyssey is installed, VCEF loads a different assembly that removes Command_SetFishList, Zone_Fishing and JobDriver_Fish, so the handler's type lookup returns null and its constructor throws. MpCompatLoader catches the exception and logs it, but the handler is left half-initialized and the error repeats on every load.

This patch skips that one constructor when Command_SetFishList is missing. The check is on the type rather than on Odyssey, so it also covers any other VCEF build that drops the class, and it stops applying by itself if VCEF puts the class back. Nothing else in Multiplayer Compatibility is affected.