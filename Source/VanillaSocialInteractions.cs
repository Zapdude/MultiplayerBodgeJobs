using System;
using System.Collections;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("VanillaExpanded.VanillaSocialInteractionsExpanded")]
public class VanillaSocialInteractions
{
	private static AccessTools.FieldRef<IDictionary> averageOpinionCacheField;

	private static AccessTools.FieldRef<object> socialInteractionsManagerField;

	private static AccessTools.FieldRef<IDictionary> checkedPawnsField;

	private static FastInvokeHandler calculateAverageOpinion;

	public VanillaSocialInteractions(ModContentPack mod)
	{
		averageOpinionCacheField = AccessTools.StaticFieldRefAccess<IDictionary>(AccessTools.DeclaredField("VanillaSocialInteractionsExpanded.VSIE_Utils:averageOpinionOfCache"));
		socialInteractionsManagerField = AccessTools.StaticFieldRefAccess<object>(AccessTools.DeclaredField("VanillaSocialInteractionsExpanded.VSIE_Utils:sManager"));
		checkedPawnsField = AccessTools.StaticFieldRefAccess<IDictionary>(AccessTools.DeclaredField("VanillaSocialInteractionsExpanded.ImmunityRecord_ImmunityTickInterval_Patch:checkedPawns"));
		Type type = AccessTools.TypeByName("VanillaSocialInteractionsExpanded.VSIE_Utils");
		calculateAverageOpinion = MethodInvoker.GetHandler(AccessTools.DeclaredMethod(type, "AverageOpinionOf"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, "GetAverageOpinionOf"), new HarmonyMethod(typeof(VanillaSocialInteractions), "ReadOpinionInInterface"));
		VanillaSocialInteractionsTime.Install();
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(GameComponentUtility), "FinalizeInit"), null, new HarmonyMethod(typeof(VanillaSocialInteractions), "ClearCache"));
	}

	private static bool ReadOpinionInInterface(Pawn pawn, ref float __result)
	{
		if (!MP.IsInMultiplayer || !MP.InInterface)
		{
			return true;
		}
		__result = (float)calculateAverageOpinion(null, pawn);
		return false;
	}

	private static void ClearCache()
	{
		averageOpinionCacheField().Clear();
		socialInteractionsManagerField() = null;
		if (!VanillaSocialInteractionsTime.BindCurrent())
		{
			checkedPawnsField().Clear();
		}
	}
}
