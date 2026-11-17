using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("i.automaticforaging")]
public class AutomaticForaging
{
	private class ITab_ForgeSpot_Patch
	{
		public static void Prefix(ITab __instance)
		{
			MP.WatchBegin();
			Building target = __instance.SelThing as Building;
			FieldRepeatCount.Watch(target);
			FieldTargetCount.Watch(target);
			FieldPauseWhenSatisfied.Watch(target);
			FieldUnpauseWhenYouHave.Watch(target);
			FieldGatherRadius.Watch(target);
		}

		public static void Postfix()
		{
			MP.WatchEnd();
		}
	}

	private class ITab_ForgePlants_Patch
	{
		public static void Prefix(ITab __instance)
		{
			MP.SetThingFilterContext(new ForgePlantsWrapper(__instance.SelThing as Building));
		}

		public static void Postfix()
		{
			MP.SetThingFilterContext(null);
		}
	}

	private record ForgePlantsWrapper(Building ForageSpot) : ThingFilterContext
	{
		public override ThingFilter Filter => FieldPlantFilter.GetValue(ForageSpot) as ThingFilter;

		public override ThingFilter ParentFilter => FieldFixedPlantFilter.GetValue(ForageSpot) as ThingFilter;
	}

	private static ISyncField FieldRepeatCount;

	private static ISyncField FieldTargetCount;

	private static ISyncField FieldPauseWhenSatisfied;

	private static ISyncField FieldUnpauseWhenYouHave;

	private static ISyncField FieldGatherRadius;

	private static FieldInfo FieldPlantFilter;

	private static FieldInfo FieldFixedPlantFilter;

	public AutomaticForaging(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
	}

	private void LatePatch()
	{
		Type parentType = AccessTools.TypeByName("AutomaticForaging.Building_ForageSpot");
		MpCompat.RegisterLambdaMethod(parentType, "GetGizmos", 1);
		parentType = AccessTools.TypeByName("AutomaticForaging.ITab_ForageSpot");
		MP.RegisterSyncWorker<ITab>(SyncITab_ForgeSpot, parentType, isImplicit: false, shouldConstruct: true);
		MpCompat.harmony.Patch(AccessTools.Method(parentType, "FillTab"), new HarmonyMethod(typeof(ITab_ForgeSpot_Patch), "Prefix"), new HarmonyMethod(typeof(ITab_ForgeSpot_Patch), "Postfix"));
		MpCompat.RegisterLambdaDelegate(parentType, "FillTab", 0, 1, 2, 3, 4, 5, 6).SetContext(SyncContext.MapSelected);
		MpCompat.RegisterLambdaDelegate(parentType, "FillIncludeGroupOptions", 6);
		MpCompat.RegisterLambdaDelegate(parentType, "FillSlotGroupStoreOptions", 1);
		parentType = AccessTools.TypeByName("AutomaticForaging.ITab_ForagePlants");
		MpCompat.harmony.Patch(AccessTools.Method(parentType, "FillTab"), new HarmonyMethod(typeof(ITab_ForgePlants_Patch), "Prefix"), new HarmonyMethod(typeof(ITab_ForgePlants_Patch), "Postfix"));
		parentType = AccessTools.TypeByName("AutomaticForaging.Building_ForageSpot");
		FieldRepeatCount = MP.RegisterSyncField(parentType, "repeatCount");
		FieldTargetCount = MP.RegisterSyncField(parentType, "targetCount");
		FieldPauseWhenSatisfied = MP.RegisterSyncField(parentType, "pauseWhenSatisfied");
		FieldUnpauseWhenYouHave = MP.RegisterSyncField(parentType, "unpauseWhenYouHave");
		FieldGatherRadius = MP.RegisterSyncField(parentType, "gatherRadius");
		FieldPlantFilter = AccessTools.Field(AccessTools.TypeByName("AutomaticForaging.Building_ForageSpot"), "plantFilter");
		FieldFixedPlantFilter = AccessTools.Field(AccessTools.TypeByName("AutomaticForaging.Building_ForageSpot"), "fixedPlantFilter");
	}

	private void SyncITab_ForgeSpot(SyncWorker sync, ref ITab tab)
	{
	}
}
