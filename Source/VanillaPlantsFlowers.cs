using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("VanillaExpanded.VPEFlowers")]
public class VanillaPlantsFlowers
{
	public VanillaPlantsFlowers(ModContentPack mod)
	{
		MpCompat.RegisterLambdaMethod("VanillaPlantsExpandedFlowers.Zone_BloomingFlowerZone", "GetGizmos", 1, 3);
	}
}
