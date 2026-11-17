using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("BiomesTeam.BiomesCore")]
public class BiomesCore
{
	public BiomesCore(ModContentPack mod)
	{
		string[] array = new string[3] { "BiomesCore.CompHarvestAnimalProduct", "BiomesCore.ThingComponents.CompAnimalsAroundThing", "BiomesCore.ThingComponents.CompAnimalThingSpawner" };
		for (int i = 0; i < array.Length; i++)
		{
			MpCompat.RegisterLambdaMethod(array[i], "CompGetGizmosExtra", default(int)).SetDebugOnly();
		}
		array = new string[3] { "BiomesCore.CompCleanFilthAround:ClearFilth", "BiomesCore.CompPlantPolluteOverTime:Pollute", "BiomesCore.CompPlantProximityExplosive:Detonate" };
		for (int i = 0; i < array.Length; i++)
		{
			MP.RegisterSyncMethod(AccessTools.DeclaredMethod(array[i])).SetDebugOnly();
		}
		PatchingUtilities.ReplaceCurrentMapUsage("BiomesCore.GameCondition_Earthquake:GameConditionTick");
		PatchingUtilities.ReplaceCurrentMapUsage("BiomesCore.IncidentWorker_Earthquake:TryExecuteWorker");
		PatchingUtilities.PatchSystemRand(new string[1] { "BiomesCore.GenSteps.ValleyPatch:Postfix" }, patchPushPop: false);
		PatchingUtilities.PatchPushPopRand("BiomesCore.CompPlantReleaseSpore:ThrowPoisonSmoke");
	}
}
