using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("VanillaExpanded.VPlantsEMushrooms")]
public class VanillaPlantsMushrooms
{
	public VanillaPlantsMushrooms(ModContentPack mod)
	{
		MpCompat.RegisterLambdaMethod("VanillaPlantsExpandedMushrooms.Zone_GrowingMushroom", "GetGizmos", 1, 3);
	}
}
