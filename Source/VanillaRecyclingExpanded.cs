using System;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("VanillaExpanded.Recycling")]
public class VanillaRecyclingExpanded
{
	public VanillaRecyclingExpanded(ModContentPack mod)
	{
		MpCompatPatchLoader.LoadPatch(this);
		Type parentType = AccessTools.TypeByName("VanillaRecyclingExpanded.CompBiopackDissolution");
		MpCompat.RegisterLambdaMethod(parentType, "CompGetGizmosExtra", 0, 1, 2).SetDebugOnly();
		MP.RegisterSyncDelegateLambda(parentType, "CompGetGizmosExtra", 4).SetDebugOnly();
		parentType = AccessTools.TypeByName("VanillaRecyclingExpanded.CompSuperSimpleProcessor");
		MP.RegisterSyncMethod(parentType, "EjectContents");
		MP.RegisterSyncMethod(parentType, "DoAtomize").SetDebugOnly();
		MP.RegisterSyncMethodLambda(parentType, "CompGetGizmosExtra", 1);
		MP.RegisterSyncDelegateLambda(parentType, "CompGetGizmosExtra", 3).SetDebugOnly();
	}

	[MpCompatPrefix("VanillaRecyclingExpanded.CompSuperSimpleProcessor", "DoAtomize", MethodType.Normal)]
	private static bool PreDoAtomize(CompThingContainer __instance)
	{
		Thing containedThing = __instance.ContainedThing;
		if (containedThing != null)
		{
			return containedThing.stackCount > 0;
		}
		return false;
	}
}
