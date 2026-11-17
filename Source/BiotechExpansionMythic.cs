using System;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("biotexpans.mythic")]
public class BiotechExpansionMythic
{
	private static Type geneAurumType;

	private static Type geneReverenceType;

	private static ISyncField syncAurumAllowedField;

	private static ISyncField syncReverenceAllowedField;

	public BiotechExpansionMythic(ModContentPack mod)
	{
		Type type = AccessTools.TypeByName("BTE_MY.CompCreateReveredMote");
		MP.RegisterSyncMethod(type, "GetMeditationSpots");
		MP.RegisterSyncMethod(type, "AddProgress").SetDebugOnly();
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod("BTE_MY.AurumUtility:OffsetResource")).SetDebugOnly();
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod("BTE_MY.ReverenceUtility:OffsetResource")).SetDebugOnly();
		syncAurumAllowedField = MP.RegisterSyncField(geneAurumType = AccessTools.TypeByName("BTE_MY.Gene_Aurum"), "aurumFuelAllowed");
		syncReverenceAllowedField = MP.RegisterSyncField(geneReverenceType = AccessTools.TypeByName("BTE_MY.Gene_Reverence"), "ReverenceFuelAllowed");
		LongEventHandler.ExecuteWhenFinished(LatePatch);
	}

	private static void LatePatch()
	{
		string[] array = new string[2] { "BTE_MY.GeneGizmo_ResourceAurum", "BTE_MY.GeneGizmo_ResourceReverence" };
		foreach (string text in array)
		{
			MpCompat.harmony.Patch(AccessTools.DeclaredMethod(text + ":DrawHeader"), new HarmonyMethod(typeof(BiotechExpansionMythic), "PreDrawLabel"));
		}
	}

	private static void PreDrawLabel(GeneGizmo_Resource __instance)
	{
		if (MP.IsInMultiplayer)
		{
			Type type = __instance.gene.GetType();
			if (type == geneAurumType)
			{
				syncAurumAllowedField.Watch(__instance.gene);
			}
			else if (type == geneReverenceType)
			{
				syncReverenceAllowedField.Watch(__instance.gene);
			}
		}
	}
}
