using System;
using System.Linq;
using HarmonyLib;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("DimonSever000.AnimaObelisk.Specific")]
internal class AnimaObelisk
{
	public AnimaObelisk(ModContentPack mod)
	{
		Type type = AccessTools.TypeByName("PsyObelisk.Things.ThingComp_PsyObelisk");
		PatchingUtilities.PatchUnityRand(AccessTools.Method(type, "GlowAround"), patchPushPop: false);
		MpCompat.RegisterLambdaMethod(type, "CompGetGizmosExtra", Enumerable.Range(0, 8).ToArray()).Skip(2).SetDebugOnly();
	}
}
