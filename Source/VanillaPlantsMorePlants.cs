using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("VanillaExpanded.VPlantsEMore")]
internal class VanillaPlantsMorePlants
{
	public VanillaPlantsMorePlants(ModContentPack mod)
	{
		PatchingUtilities.PatchSystemRand("VanillaPlantsExpandedMorePlants.Plant_SowsAdjacent:SpawnSetup", patchPushPop: false);
		PatchingUtilities.PatchSystemRand("VanillaPlantsExpandedMorePlants.Plant_TransformOnMaturity:TickLong", patchPushPop: false);
		MpCompat.RegisterLambdaMethod("VanillaPlantsExpandedMorePlants.Zone_GrowingAquatic", "GetGizmos", 1, 3);
		MpCompat.RegisterLambdaMethod("VanillaPlantsExpandedMorePlants.Zone_GrowingSandy", "GetGizmos", 1, 3);
		ISyncMethod[] array = MpCompat.RegisterLambdaMethod("VanillaPlantsExpandedMorePlants.Zone_GrowingFlowerGarden", "GetGizmos", 1, 3);
		for (int i = 0; i < array.Length; i++)
		{
			array[i].SetVersion(17);
		}
	}
}
