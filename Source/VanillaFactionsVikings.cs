using System;
using HarmonyLib;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("OskarPotocki.VFE.Vikings")]
internal class VanillaFactionsVikings
{
	public VanillaFactionsVikings(ModContentPack mod)
	{
		Type type = AccessTools.TypeByName("VFEV.Apiary");
		MpCompat.RegisterLambdaMethod(type, "GetGizmos", 0, 1).SetDebugOnly();
		PatchingUtilities.PatchSystemRand(AccessTools.Method(type, "ResetTend"), patchPushPop: false);
	}
}
