using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("vanillaracesexpanded.saurid")]
public class VanillaRacesSaurid
{
	public VanillaRacesSaurid(ModContentPack mod)
	{
		Type type = AccessTools.TypeByName("VRESaurids.Comp_HumanHatcher");
		MP.RegisterSyncMethod(type, "Hatch").SetDebugOnly();
		MP.RegisterSyncMethod(type, "RegenerateChild").SetDebugOnly();
	}
}
