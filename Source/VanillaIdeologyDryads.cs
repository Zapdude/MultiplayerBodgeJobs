using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("VanillaExpanded.Ideo.Dryads")]
internal class VanillaIdeologyDryads
{
	public VanillaIdeologyDryads(ModContentPack mod)
	{
		PatchingUtilities.PatchSystemRandCtor("VanillaIdeologyExpanded_Dryads.HediffComp_PeriodicWounds");
		MP.RegisterSyncMethod(AccessTools.TypeByName("VanillaIdeologyExpanded_Dryads.CompPawnMerge"), "SetDryadAwakenPod");
		MpCompat.RegisterLambdaMethod("VanillaIdeologyExpanded_Dryads.CompSpawnAwakened", "CompGetGizmosExtra", default(int)).SetDebugOnly();
	}
}
