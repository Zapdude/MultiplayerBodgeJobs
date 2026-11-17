# Provenance

## Upstream project

Multiplayer Compatibility, https://github.com/rwmt/Multiplayer-Compatibility
MIT License, Copyright (c) 2019 Meru and the Multiplayer Compatibility contributors.
The license text is kept in `LICENSE`.

## Where the 1.7 module code came from

The upstream repository has no public 1.7 branch. The 1.7 module code in `Source/` and
`Source_Referenced/` was reconstructed by decompiling `Multiplayer_Compat.dll` and
`Referenced/Multiplayer_Compat_Referenced.dll` from the temporary 1.7 build published at workshop
item 3820003665 (`(1.7)Multiplayer Compatibility-temp`, updated 2026-11-16). That build carries the
upstream MIT notice in its `FORK_NOTICE.txt`; the original authors retain copyright.

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
- `VanillaFactionsEmpire` referred to the nested `ColonistTitleCache.RoyaltyTracker` type by its
  short name because ILSpy dropped the `using static` that brought it into scope.

## The Referenced project

`Source_Referenced/` is the same reconstructed code, compiled against the installed 1.7 mods:

- `PipeSystem17`, `VanillaExpandedFrameworkReferenced` - VEF 1.7 (`PipeSystem.dll`, `Outposts.dll`)
- `VanillaFactionsEmpire` - VFEEmpire 1.7
- `VanillaGravshipExpanded` - VGE 1.7
- `VanillaSeriesRegistration17` - no references

`VanillaFactionsClassical` and `VanillaFactionsDeserters` are in the tree but excluded from the
build: their installed assemblies stop at 1.6, so the 1.7 code cannot be compiled against them. The
project file marks the two `Compile Remove` lines and the references to add when 1.7 builds exist.

Two build details are specific to the 1.7 reference stack. The game's 1.7 reference package models
UnityEngine with `UnityStubs`, while VEF and the other pre-move mods expose `UnityEngine.CoreModule`
types in their signatures. `UnityEngine.CoreModule` is therefore referenced behind an alias, which
lets the compiler resolve those signatures without a duplicate-type error. And the module named
`VanillaGravshipExpanded` exists in both assemblies by design: the main assembly handles the landing
lifecycle, the Referenced assembly handles terminals, markers and clocks. The compiler warning about
the shared name is suppressed.

## What is not from upstream

`Source/UnboundWeaponCustomization.cs` is our own code. It is not present in the upstream
project or in the temporary 1.7 build.

## What is not included

The temp build's `Referenced/Multiplayer_Compat_Referenced.dll` is not redistributed. The assembly is
built from `Source_Referenced/` instead.
