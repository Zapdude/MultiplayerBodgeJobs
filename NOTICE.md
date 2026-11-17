# Provenance

## Upstream project

Multiplayer Compatibility, https://github.com/rwmt/Multiplayer-Compatibility
MIT License, Copyright (c) 2019 Meru and the Multiplayer Compatibility contributors.
The license text is kept in `LICENSE`.

## Where the 1.7 module code came from

The upstream repository has no public 1.7 branch. The 1.7 module code in `Source/` was
reconstructed by decompiling `Multiplayer_Compat.dll` from the temporary 1.7 build published at
workshop item 3820003665 (`(1.7)Multiplayer Compatibility-temp`, updated 2026-11-16). That build
carries the upstream MIT notice in its `FORK_NOTICE.txt`; the original authors retain copyright.

Decompiler: ILSpy 11.2.0.10050, run with reference assemblies so that nested types, attribute
arguments and nullability resolve:

- RimWorld 1.7 reference assemblies (`Krafs.Rimworld.Ref 1.7.4985-unstable`)
- `0Harmony.dll` from Harmony 1.7 (3.0.0.0)
- `0MultiplayerAPI.dll` from Multiplayer 1.7

## Repairs applied to the decompiler output

The decompilation is faithful, but a few C# 14 constructs do not round-trip through ILSpy. The
repairs are behaviour-preserving and are listed here so a future re-decompilation can repeat them:

- Method-group conversions cached in compiler-generated types (`<>A{...}<>`, `<>F{...}<>`) were
  emitted as delegate constructors. They are written as the original method groups again.
- An attribute call with a `null` argument was ambiguous between the `Type[]` and `string`
  overloads; it is cast to `(Type[])null`.
- `Window.Margin` is public in 1.7; one override had the pre-publicizer accessibility.
- A nested lambda in `StartupDefFingerprint` reused the outer parameter name for a new local.
- One named argument (`clamp:`) referred to a parameter name that the decompiler had renamed.

## What is not from upstream

`Source/UnboundWeaponCustomization.cs` is our own code. It is not present in the upstream
project or in the temporary 1.7 build.

## What is not included

`Referenced/Multiplayer_Compat_Referenced.dll` from the temporary build is not redistributed
here. Its seven modules need compile-time references to other mods' assemblies and will be
added as source when those references are set up.
