using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Uuugggg.SmartMedicine")]
public class SmartMedicine
{
	private delegate void StockUpPasteSettingsDelegate(Pawn pawn);

	private delegate Dictionary<ThingDef, int> StockUpSettingsDelegate(Pawn pawn);

	private static Type smartMedicineCompType;

	private static StockUpSettingsDelegate stockUpSettingsMethod;

	private static StockUpPasteSettingsDelegate stockUpPasteMethod;

	private static AccessTools.FieldRef<object, Pawn> copiedPawnField;

	private static FastInvokeHandler getSmartMedicinePriorityCareDictionary;

	private static AccessTools.FieldRef<object, object> delegateTypeHediffCareField;

	public SmartMedicine(ModContentPack mod)
	{
		Type type = AccessTools.TypeByName("SmartMedicine.StockUpUtility");
		MethodInfo methodInfo = AccessTools.Method(type, "StockUpPasteSettings");
		stockUpPasteMethod = AccessTools.MethodDelegate<StockUpPasteSettingsDelegate>(methodInfo);
		stockUpSettingsMethod = AccessTools.MethodDelegate<StockUpSettingsDelegate>(AccessTools.Method(type, "StockUpSettings"));
		MpCompat.harmony.Patch(methodInfo, new HarmonyMethod(typeof(SmartMedicine), "PrePasteSettings"));
		MpCompat.harmony.Patch(AccessTools.Method(type, "SetStockCount", new Type[3]
		{
			typeof(Pawn),
			typeof(ThingDef),
			typeof(int)
		}), new HarmonyMethod(typeof(SmartMedicine), "PreSetStockCount"));
		MP.RegisterSyncMethod(AccessTools.Method(type, "StockUpStop", new Type[2]
		{
			typeof(Pawn),
			typeof(ThingDef)
		}));
		MP.RegisterSyncMethod(AccessTools.Method(type, "StockUpClearSettings"));
		MP.RegisterSyncMethod(typeof(SmartMedicine), "SyncedSetStockCount");
		MP.RegisterSyncMethod(typeof(SmartMedicine), "SyncedPasteSettings");
		smartMedicineCompType = AccessTools.TypeByName("SmartMedicine.SmartMedicineGameComp");
		copiedPawnField = AccessTools.FieldRefAccess<Pawn>(smartMedicineCompType, "copiedPawn");
		Type parentType = AccessTools.TypeByName("SmartMedicine.HediffRowPriorityCare");
		MpCompat.RegisterLambdaDelegate(parentType, "LabelButton", 0, 1);
		parentType = AccessTools.TypeByName("PeteTimesSix.CompactHediffs.Rimworld.UI_compat.UI_SmartMedicine");
		if (parentType != null)
		{
			MethodInfo lambda = MpMethodUtil.GetLambda(parentType, "AddSmartMedicineFloatMenuButton", MethodType.Normal, null, 1);
			Type declaringType = lambda.DeclaringType;
			MP.RegisterSyncDelegate(parentType, declaringType.Name, lambda.Name, new string[1] { "hediffs" });
			MpCompat.RegisterLambdaDelegate(parentType, "AddSmartMedicineFloatMenuButton", new string[2] { "CS$<>8__locals1/hediffs", "mc" }, 2);
			delegateTypeHediffCareField = AccessTools.FieldRefAccess<object>(declaringType, "hediffCares");
			MpCompat.harmony.Patch(AccessTools.Constructor(declaringType), new HarmonyMethod(typeof(SmartMedicine), "InitHediffCareDictionary"));
			parentType = AccessTools.TypeByName("SmartMedicine.PriorityCareComp");
			getSmartMedicinePriorityCareDictionary = HarmonyLib.MethodInvoker.GetHandler(AccessTools.Method(parentType, "Get"));
		}
		PatchingUtilities.PatchCancelInInterface("SmartMedicine.InterfaceDrop_Patch:Postfix");
	}

	private static bool PreSetStockCount(Pawn pawn, ThingDef thingDef, int count)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (!stockUpSettingsMethod(pawn).TryGetValue(thingDef, out var value) || count != value)
		{
			SyncedSetStockCount(pawn, thingDef, count);
		}
		return false;
	}

	private static void SyncedSetStockCount(Pawn pawn, ThingDef thingDef, int count)
	{
		stockUpSettingsMethod(pawn)[thingDef] = count;
	}

	private static bool PrePasteSettings(Pawn pawn)
	{
		if (MP.IsInMultiplayer && !MP.IsExecutingSyncCommand)
		{
			GameComponent component = Current.Game.GetComponent(smartMedicineCompType);
			Pawn copiedPawn = copiedPawnField(component);
			SyncedPasteSettings(pawn, copiedPawn);
			return false;
		}
		return true;
	}

	private static void SyncedPasteSettings(Pawn pawn, Pawn copiedPawn)
	{
		GameComponent component = Current.Game.GetComponent(smartMedicineCompType);
		Pawn pawn2 = copiedPawnField(component);
		copiedPawnField(component) = copiedPawn;
		stockUpPasteMethod(pawn);
		copiedPawnField(component) = pawn2;
	}

	private static void InitHediffCareDictionary(object __instance)
	{
		delegateTypeHediffCareField(__instance) = getSmartMedicinePriorityCareDictionary(null);
	}
}
