using System.Collections.Generic;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("vanillaracesexpanded.fungoid")]
public class VanillaRacesFungoid
{
	private static AccessTools.FieldRef<IDictionary<Pawn, XenotypeDef>> pawnsAndXenotypesDictionaryField;

	public VanillaRacesFungoid(ModContentPack mod)
	{
		PatchingUtilities.PatchSystemRand(new string[2] { "VanillaRacesExpandedFungoid.DamageWorker_ExtraFungoidInfection:ApplySpecialEffectsToPart", "VanillaRacesExpandedFungoid.DamageWorker_ExtraFungoidInfection_Bite:ApplySpecialEffectsToPart" });
		PatchingUtilities.PatchUnityRand("VanillaRacesExpandedFungoid.Building_FungoidShip:PopUpFungoids");
		MpCompat.RegisterLambdaMethod("VanillaRacesExpandedFungoid.Hediff_GeneInfected", "GetGizmos", default(int)).SetDebugOnly();
		ISyncMethod[] array = MpCompat.RegisterLambdaMethod("VanillaRacesExpandedFungoid.HediffComp_GeneInfection", "CompGetGizmos", default(int));
		for (int i = 0; i < array.Length; i++)
		{
			array[i].SetDebugOnly().SetVersion(17);
		}
		pawnsAndXenotypesDictionaryField = AccessTools.StaticFieldRefAccess<IDictionary<Pawn, XenotypeDef>>(AccessTools.DeclaredField(AccessTools.TypeByName("VanillaRacesExpandedFungoid.StaticCollectionsClass"), "pawns_and_xenotypes"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(GameComponentUtility), "FinalizeInit"), null, new HarmonyMethod(typeof(VanillaRacesFungoid), "ClearCache"));
	}

	private static void ClearCache()
	{
		pawnsAndXenotypesDictionaryField().Clear();
	}
}
