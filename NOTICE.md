# Provenance

## Upstream project

Multiplayer Compatibility, https://github.com/rwmt/Multiplayer-Compatibility
MIT License, Copyright (c) 2019 Meru and the Multiplayer Compatibility contributors.
The license text is kept in `LICENSE`.

The `main` (1.6) branch is the upstream `master` source with this fork's patches applied. Those
patches are listed in `PATCHES.md`.

The `1.7` branch is built from the upstream MIT project as it exists in the temporary 1.7 build
published at workshop item 3820003665 (`(1.7)Multiplayer Compatibility-temp`, updated 2026-11-16).
That build carries the upstream MIT notice in its `FORK_NOTICE.txt`; the original authors retain
copyright. Its module code was reconstructed by decompiling `Multiplayer_Compat.dll` with ILSpy
11.2.0.10050 against RimWorld 1.7 reference assemblies, Harmony 1.7 and Multiplayer 1.7, then
retargeted to net10.0. See the `1.7` branch's `NOTICE.md` for the repairs applied to the
decompiler output.

## What is not from upstream

`Source/Mods/UnboundWeaponCustomization.cs` is our own code on both branches.

## What is not included

The `References/` third-party assemblies upstream copies into the build; the `Source_Referenced`
project references the installed workshop assemblies instead. `Source_Referenced/` itself is carried
on this branch: it was decompiled from the 1.6 `Referenced/Multiplayer_Compat_Referenced.dll` shipped
in workshop item 1629973374 with ILSpy 11.2.0.10050 against the RimWorld 1.6 reference assemblies.
The modules whose mod is not installed at 1.6 stay in the tree but are excluded from the build (see
`README.md`).
