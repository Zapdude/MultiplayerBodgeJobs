using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("longwater.smartstockpilecreation")]
internal class SmartStockpileCreation
{
	private static Type designatorType;

	private static FastInvokeHandler allowedThingsGetter;

	private static AccessTools.FieldRef<object, SpecialThingFilterDef> specialThingFilterDefLabel;

	private static AccessTools.FieldRef<object, StoragePriority> priorityLabel;

	private static AccessTools.FieldRef<object, string> contentsLabel;

	private static Type reallowGizmoType;

	private static AccessTools.FieldRef<object, ThingDef> thingDefLabel;

	private static AccessTools.FieldRef<object, Zone_Stockpile> zoneStockpileLabel;

	private static AccessTools.FieldRef<object, ThingCategoryDef> categoryLabel;

	private static Type disallowGizmoType;

	private static AccessTools.FieldRef<object, Thing> thingLabel;

	public SmartStockpileCreation(ModContentPack mod)
	{
		designatorType = AccessTools.TypeByName("SmartStockpileCreation.RimObjs.SmartStockpile.SmartStockpileDesignator");
		allowedThingsGetter = MethodInvoker.GetHandler(AccessTools.PropertyGetter(designatorType, "AllowedThings"));
		specialThingFilterDefLabel = AccessTools.FieldRefAccess<SpecialThingFilterDef>(designatorType, "_specialThingFilterDef");
		priorityLabel = AccessTools.FieldRefAccess<StoragePriority>(designatorType, "_priority");
		contentsLabel = AccessTools.FieldRefAccess<string>(designatorType, "_stockpileContentsLabel");
		MP.RegisterSyncWorker<Designator_ZoneAddStockpile>(SmartStockpileDesignatorWorker, designatorType);
		reallowGizmoType = AccessTools.TypeByName("SmartStockpileCreation.RimObjs.DisallowInStockpile.ReallowInStockpileGizmo");
		thingDefLabel = AccessTools.FieldRefAccess<ThingDef>(reallowGizmoType, "_thingDef");
		zoneStockpileLabel = AccessTools.FieldRefAccess<Zone_Stockpile>(reallowGizmoType, "_zoneStockpile");
		categoryLabel = AccessTools.FieldRefAccess<ThingCategoryDef>(reallowGizmoType, "_category");
		MP.RegisterSyncMethod(AccessTools.Method(reallowGizmoType, "ProcessInput")).SetContext(SyncContext.MapSelected);
		MP.RegisterSyncWorker<Command>(ReallowInStockpileGizmoWorker, reallowGizmoType);
		disallowGizmoType = AccessTools.TypeByName("SmartStockpileCreation.RimObjs.DisallowInStockpile.RemoveFromStockpileGizmo");
		thingLabel = AccessTools.FieldRefAccess<Thing>(disallowGizmoType, "_thing");
		MP.RegisterSyncMethod(AccessTools.Method(disallowGizmoType, "Process")).SetContext(SyncContext.MapSelected);
		MP.RegisterSyncWorker<Command>(RemoveFromStockpileGizmoWorker, disallowGizmoType);
	}

	private void SmartStockpileDesignatorWorker(SyncWorker sync, ref Designator_ZoneAddStockpile obj)
	{
		if (sync.isWriting)
		{
			sync.Write(new List<ThingDef>((IEnumerable<ThingDef>)allowedThingsGetter(obj)));
			sync.Write(specialThingFilterDefLabel(obj));
			sync.Write(priorityLabel(obj));
			sync.Write(contentsLabel(obj));
			return;
		}
		List<ThingDef> list = sync.Read<List<ThingDef>>();
		SpecialThingFilterDef specialThingFilterDef = sync.Read<SpecialThingFilterDef>();
		StoragePriority storagePriority = sync.Read<StoragePriority>();
		string text = sync.Read<string>();
		obj = (Designator_ZoneAddStockpile)Activator.CreateInstance(designatorType, list, specialThingFilterDef, storagePriority, text);
	}

	private void ReallowInStockpileGizmoWorker(SyncWorker sync, ref Command obj)
	{
		if (sync.isWriting)
		{
			sync.Write(thingDefLabel(obj));
			sync.Write(zoneStockpileLabel(obj));
			sync.Write(categoryLabel(obj));
		}
		else
		{
			obj = (Command)RuntimeHelpers.GetUninitializedObject(reallowGizmoType);
			thingDefLabel(obj) = sync.Read<ThingDef>();
			zoneStockpileLabel(obj) = sync.Read<Zone_Stockpile>();
			categoryLabel(obj) = sync.Read<ThingCategoryDef>();
		}
	}

	private void RemoveFromStockpileGizmoWorker(SyncWorker sync, ref Command obj)
	{
		if (sync.isWriting)
		{
			sync.Write(thingLabel(obj));
			return;
		}
		obj = (Command)RuntimeHelpers.GetUninitializedObject(disallowGizmoType);
		thingLabel(obj) = sync.Read<Thing>();
	}
}
