using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("RimEffect.Drell")]
public class RimEffectDrell
{
	public RimEffectDrell(ModContentPack mod)
	{
		MpCompat.RegisterLambdaMethod("REDrell.Comp_Drell", "CompGetGizmosExtra", 0, 1);
	}
}
