using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("VanillaExpanded.VFEFarming")]
internal class VFEF
{
	public VFEF(ModContentPack mod)
	{
		PatchingUtilities.PatchPushPopRand(new string[2] { "VFEF.MoteSprinkler:NewMote", "VFEF.MoteSprinkler:ThrowWaterSpray" });
		MpCompat.RegisterLambdaMethod("VFEF.Building_Scarecrow", "GetGizmos", 1);
	}
}
