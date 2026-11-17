using System.Linq;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("MrKociak.BiotechMoreMechStuffMechUpgradePod")]
public class BiotechMechStuffElongatedMechanoidUpgrades
{
	public BiotechMechStuffElongatedMechanoidUpgrades(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
		MpCompat.RegisterLambdaMethod("RimWorld.CompRepeatCycle_BMU", "CompGetGizmosExtra", 1, 3, 5, 7, 9, 11, 13, 15, 17);
		MpCompat.RegisterLambdaMethod("RimWorld.CompSwapBuilding_BMU", "CompGetGizmosExtra", default(int));
	}

	private static void LatePatch()
	{
		MpCompat.RegisterLambdaMethod("Building_MechUpgrader", "GetGizmos", 2, 6, 0, 8).TakeLast(2).SetDebugOnly();
	}
}
