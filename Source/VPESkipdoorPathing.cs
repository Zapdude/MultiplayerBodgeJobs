using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("v337.VPESkipdoorPathing")]
public class VPESkipdoorPathing
{
	public VPESkipdoorPathing(ModContentPack mod)
	{
		MpCompat.RegisterLambdaMethod("VPESkipdoorPathing.CompSkipdoorPathing", "CompGetGizmosExtra", 1);
	}
}
