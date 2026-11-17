using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("MrHydralisk.AnomaliesExpected")]
internal class AnomaliesExpected
{
	public AnomaliesExpected(ModContentPack mod)
	{
		Type type = AccessTools.TypeByName("AnomaliesExpected.Comp_AnomalyHospitalBed");
		MpCompat.RegisterLambdaMethod(type, "CompGetGizmosExtra", 0, 2, 3, 4, 5, 6);
		PatchingUtilities.PatchPushPopRand(AccessTools.Method(type, "Sign"));
		MpCompat.RegisterLambdaMethod("AnomaliesExpected.Comp_BakingPies", "CompGetGizmosExtra", 1, 2, 3, 4, 5, 6, 7);
		Type parentType = AccessTools.TypeByName("AnomaliesExpected.Comp_BeamTarget");
		MpCompat.RegisterLambdaMethod(parentType, "CompGetGizmosExtra", 1, 2, 3, 4, 5, 6, 7, 8);
		MP.RegisterSyncDelegateLambda(parentType, "TargetLocation", 1);
		MpCompat.RegisterLambdaMethod("AnomaliesExpected.Comp_BloodPump", "CompGetGizmosExtra", default(int));
		MpCompat.RegisterLambdaMethod("AnomaliesExpected.Comp_BrokenStatue", "CompGetGizmosExtra", default(int));
		MethodInfo original = AccessTools.Method("AnomaliesExpected.HediffComp_ObservingStage:CompPostTick");
		MpCompat.harmony.Patch(original, new HarmonyMethod(new Func<HediffComp, bool>(ObservePawnsInstead)));
		MpCompat.RegisterLambdaMethod("AnomaliesExpected.Comp_CanDestroyedAfterStudy", "CompGetGizmosExtra", default(int));
		MpCompat.RegisterLambdaMethod("AnomaliesExpected.Comp_ChristmasStockings", "CompGetGizmosExtra", 1, 2, 3, 4, 5, 6, 7);
		MpCompat.RegisterLambdaMethod("AnomaliesExpected.Comp_MeatGrinder", "CompGetGizmosExtra", 1, 3, 4, 5, 6, 7, 8, 10, 11, 12, 13, 14, 15);
		MpCompat.RegisterLambdaMethod("AnomaliesExpected.Comp_Speedometer", "CompGetGizmosExtra", default(int));
		MpCompat.RegisterLambdaDelegate("AnomaliesExpected.Hediff_SpeedometerLevel", "GetGizmos", 1, 3, 4, 5);
		MpCompat.RegisterLambdaDelegate("AnomaliesExpected.Comp_StudyNotepad", "CompGetGizmosExtra", 2);
		MpCompat.RegisterLambdaMethod("AnomaliesExpected.CompAbilityEffect_FleshbeastCommand", "CompGetGizmosExtra", 1);
		MpCompat.RegisterLambdaMethod("AnomaliesExpected.CompObelisk_Clockwork", "CompGetGizmosExtra", 0, 1, 2, 4, 6, 8, 9);
	}

	private static bool ObservePawnsInstead(HediffComp __instance)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		float severity = 1f;
		Pawn pawn = __instance.parent.pawn;
		foreach (Pawn item in pawn.Map.mapPawns.AllHumanlikeSpawned.ToList())
		{
			if (item.Position.InHorDistOf(pawn.Position, 8f) && GenSight.LineOfSightToThing(pawn.Position, item, pawn.Map))
			{
				severity = 0.5f;
				break;
			}
		}
		__instance.parent.Severity = severity;
		return false;
	}
}
