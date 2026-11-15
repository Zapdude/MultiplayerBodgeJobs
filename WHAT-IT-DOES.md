## Description

So, you wanna learn what exactly this mod does? Alright then sonny, settle down and let old pepaw tell you all about it.

## Mods Patched

* Unique Persona Weapons Unbound
* Unique Weapons Unbound

## Purpose of patches

### UWB

When customizing weapons, both mods start the customize job through Pawn_JobTracker.TryTakeOrderedJob, which Multiplayer syncs fine, but the UI communicates the chosen design by calling JobDriver_CustomizeWeapon.SetSpec on the already-running driver (from starting it), and that call is only given to whoever pressed the confirm button. In such an event every other player (including the host if they didnt press it) DIES; no I'm kidding they just desync immediately. This patch puts the call through an MP sync method so all clients get the same call and no desync occurs. Yay! Nice and easy, right?