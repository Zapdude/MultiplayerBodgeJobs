using System;
using System.Collections;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Orion.Hospitality")]
public class Hospitality
{
	private static AccessTools.FieldRef<IDictionary> guestCompsField;

	private static AccessTools.FieldRef<IDictionary> relationCacheField;

	private static AccessTools.FieldRef<IDictionary> travelDaysCacheField;

	private static AccessTools.FieldRef<IDictionary> relatedCacheField;

	private static AccessTools.FieldRef<int> relatedCacheNextClearTickField;

	private static FastInvokeHandler compGuestMethod;

	private static AccessTools.FieldRef<ISyncField[]> syncFields;

	public Hospitality(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod("Hospitality.Alert_Guest:GetReport"), new HarmonyMethod(typeof(Hospitality), "NoAlertInMp"));
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod("Hospitality.CompVendingMachine:SetEmptyThreshold"));
	}

	private static void LatePatch()
	{
		guestCompsField = AccessTools.StaticFieldRefAccess<IDictionary>(AccessTools.DeclaredField(AccessTools.TypeByName("Hospitality.Utilities.CompUtility"), "guestComps"));
		relationCacheField = AccessTools.StaticFieldRefAccess<IDictionary>(AccessTools.DeclaredField(AccessTools.TypeByName("Hospitality.Utilities.RelationUtility"), "relationCache"));
		travelDaysCacheField = AccessTools.StaticFieldRefAccess<IDictionary>(AccessTools.DeclaredField(AccessTools.TypeByName("Hospitality.Utilities.GenericUtility"), "travelDaysCache"));
		Type type = AccessTools.TypeByName("Hospitality.Utilities.GuestUtility");
		relatedCacheField = AccessTools.StaticFieldRefAccess<IDictionary>(AccessTools.DeclaredField(type, "relatedCache"));
		relatedCacheNextClearTickField = AccessTools.StaticFieldRefAccess<int>(AccessTools.DeclaredField(type, "relatedCacheNextClearTick"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(GameComponentUtility), "FinalizeInit"), null, new HarmonyMethod(typeof(Hospitality), "ClearCache"));
		syncFields = AccessTools.StaticFieldRefAccess<ISyncField[]>(AccessTools.DeclaredField("Hospitality.Multiplayer:guestFields"));
		compGuestMethod = MethodInvoker.GetHandler(AccessTools.DeclaredMethod("Hospitality.Utilities.CompUtility:CompGuest"));
		Type type2 = AccessTools.TypeByName("Hospitality.MainTab.PawnColumnWorker_Relationship");
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type2, "DoHeader"), new HarmonyMethod(typeof(Hospitality), "PreHeader"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type2, "DoCell"), new HarmonyMethod(typeof(Hospitality), "PreCell"), null, null, new HarmonyMethod(typeof(Hospitality), "PostCell"));
	}

	private static bool NoAlertInMp(ref AlertReport __result)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		__result = AlertReport.Inactive;
		return false;
	}

	private static void ClearCache()
	{
		guestCompsField().Clear();
		relationCacheField().Clear();
		travelDaysCacheField().Clear();
		relatedCacheField().Clear();
		relatedCacheNextClearTickField() = 0;
	}

	private static void PreHeader(PawnTable table)
	{
		if (!MP.IsInMultiplayer)
		{
			return;
		}
		ref ISyncField[] reference = ref syncFields();
		if (reference == null)
		{
			return;
		}
		foreach (Pawn item in table.PawnsListForReading)
		{
			object obj = compGuestMethod(null, item);
			if (obj != null)
			{
				ISyncField[] array = reference;
				for (int i = 0; i < array.Length; i++)
				{
					array[i].Watch(obj);
				}
			}
		}
	}

	private static void PreCell(ref ISyncField[] __state)
	{
		if (MP.IsInMultiplayer)
		{
			__state = syncFields();
			syncFields() = null;
		}
	}

	private static void PostCell(ISyncField[] __state)
	{
		if (__state != null)
		{
			syncFields() = __state;
		}
	}
}
