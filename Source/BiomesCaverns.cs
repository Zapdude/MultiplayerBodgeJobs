using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("BiomesTeam.BiomesCaverns")]
public class BiomesCaverns
{
	public BiomesCaverns(ModContentPack mod)
	{
		MpCompat.RegisterLambdaDelegate("BiomesCaverns.Patches.Building_PodLauncher_GetGizmos_Patch", "DrillPodGizmo", default(int));
		MpCompat.RegisterLambdaMethod("Building_MushroomFermentingBarrel", "GetGizmos", default(int)).SetDebugOnly();
		PatchingUtilities.PatchPushPopRand("Caveworld_Flora_Unleashed.FruitingBody_Gleamcap:ThrowPoisonSmoke");
	}
}
