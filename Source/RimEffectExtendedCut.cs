using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("RimEffect.ExtendedCut")]
public class RimEffectExtendedCut
{
	public RimEffectExtendedCut(ModContentPack mod)
	{
		MpCompat.RegisterLambdaDelegate("RimEffectExtendedCut.Building_WarzoneTable", "GetBattleSetOptions", default(int));
		MpCompat.RegisterLambdaMethod("RimEffectExtendedCut.CompPowerOutDoorLamp", "CompGetGizmosExtra", 0, 1).SetDebugOnly();
	}
}
