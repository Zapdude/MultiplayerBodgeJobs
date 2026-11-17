using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("XMB.AncientUrbanrUins.MO")]
public class AncientUrbanRuins
{
	private static Type ancientMarketGameCompType;

	private static FastInvokeHandler ancientMarketGameCompGetScheduleMethod;

	private static AccessTools.FieldRef<GameComponent, IDictionary> ancientMarketGameCompSchedulesField;

	private static AccessTools.FieldRef<object, IList> levelScheduleAllowedLevelsField;

	private static AccessTools.FieldRef<object, List<bool>> levelScheduleTimeScheduleField;

	private static AccessTools.FieldRef<PocketMapParent, MapPortal> customMapEntranceField;

	private static FastInvokeHandler levelPowerTabCompGetter;

	private static Type powerPlantLevelCompType;

	private static FastInvokeHandler powerPlantLevelCompLinkedCompGetter;

	private static AccessTools.FieldRef<CompPowerPlant, float> powerPlantLevelCompTargetOutputLevelField;

	[MpCompatSyncField("AncientMarket_Libraray.CompPowerPlantLevel", "name")]
	protected static ISyncField powerPlantLevelCompNameSyncField;

	[MpCompatSyncField("AncientMarket_Libraray.CompPowerPlantLevel", "outputMode")]
	protected static ISyncField powerPlantLevelCompOutputModeSyncField;

	[MpCompatSyncField("AncientMarket_Libraray.CompPowerPlantLevel", "targetPowerOutput")]
	protected static ISyncField powerPlantLevelTargetOutputLevelSyncField;

	[MpCompatSyncField("AncientMarket_Libraray.CompPowerPlantLevel", "comp")]
	protected static ISyncField powerPlantLevelCompLinkedThingSyncField;

	[MpCompatSyncField("AncientMarket_Libraray.CompPowerPlantLevel", "linked")]
	protected static ISyncField powerPlantLevelCompLinkedCompSyncField;

	private static FastInvokeHandler levelTransmitTabReceiverGetter;

	[MpCompatSyncField("AncientMarket_Libraray.Building_Receive", "name")]
	protected static ISyncField buildingTransmitNameSyncField;

	public AncientUrbanRuins(ModContentPack mod)
	{
		MpCompatPatchLoader.LoadPatch(this);
		MpSyncWorkers.Requires<PocketMapParent>();
		PatchingUtilities.PatchUnityRand(new string[9] { "AncientMarket_Libraray.ACM_RandomUtility:ReplaceWallAndBreakThem", "AncientMarket_Libraray.ACM_SketchResolver_ACM_Resturant:ResolveInt", "AncientMarket_Libraray.ACM_SketchResolver_BuildingRoom:ResolveInt", "AncientMarket_Libraray.ACM_SketchResolver_BuildingRoomWithNoForklift:ResolveInt", "AncientMarket_Libraray.ACM_SketchResolver_ContainerRoom:ResolveInt", "AncientMarket_Libraray.ACM_SketchResolver_HoboRoom:ResolveInt", "AncientMarket_Libraray.ACM_SketchResolver_StoreRoom:ResolveInt", "AncientMarket_Libraray.ACM_SketchResolver_UnderRoom:ResolveInt", "AncientMarket_Libraray.ComplexThreatWorker_HangingPirates:SpawnThreatPawns" });
		MpCompat.RegisterLambdaDelegate("AncientMarket_Libraray.BuildingTrader", "GetFloatMenuOptions", default(int));
		MpCompat.RegisterLambdaDelegate("AncientMarket_Libraray.CompDialogable", "CompFloatMenuOptions", default(int));
		LongEventHandler.ExecuteWhenFinished(() =>
		{
			MpCompat.RegisterLambdaMethod("AncientMarket_Libraray.CustomSite", "GetGizmos", 1);
		});
		MpCompat.RegisterLambdaMethod("AncientMarket_Libraray.CompFillPortal", "CompGetGizmosExtra", default(int));
		Type type = (ancientMarketGameCompType = AccessTools.TypeByName("AncientMarket_Libraray.GameComponent_AncientMarket"));
		ancientMarketGameCompGetScheduleMethod = HarmonyLib.MethodInvoker.GetHandler(AccessTools.DeclaredMethod(type, "GetSchedule"));
		ancientMarketGameCompSchedulesField = AccessTools.FieldRefAccess<IDictionary>(type, "schedules");
		Type type2 = AccessTools.TypeByName("AncientMarket_Libraray.LevelSchedule");
		levelScheduleAllowedLevelsField = AccessTools.FieldRefAccess<IList>(type2, "allowedLevels");
		levelScheduleTimeScheduleField = AccessTools.FieldRefAccess<List<bool>>(type2, "timeSchedule");
		customMapEntranceField = AccessTools.FieldRefAccess<MapPortal>("AncientMarket_Libraray.MapParent_Custom:entrance");
		MpCompat.RegisterLambdaDelegate("AncientMarket_Libraray.Window_AllowLevel", "DoWindowContents", new string[1] { "schedule" }, 2, 4);
		Serializer<Thing, int> serializer = Serializer.New((Thing t) => t.thingIDNumber, (int id) => (!MP.TryGetThingById(id, out var value)) ? null : value);
		Type type3 = AccessTools.TypeByName("AncientMarket_Libraray.Tab_LevelPower");
		levelPowerTabCompGetter = HarmonyLib.MethodInvoker.GetHandler(AccessTools.DeclaredPropertyGetter(type3, "Comp"));
		MpCompat.RegisterLambdaMethod(type3, "FillTab", 1)[0].TransformArgument(0, serializer).SetContext(SyncContext.MapSelected).CancelIfAnyArgNull();
		Type type4 = (powerPlantLevelCompType = AccessTools.TypeByName("AncientMarket_Libraray.CompPowerPlantLevel"));
		powerPlantLevelCompLinkedCompGetter = HarmonyLib.MethodInvoker.GetHandler(AccessTools.DeclaredPropertyGetter(type4, "LinkedComp"));
		powerPlantLevelCompTargetOutputLevelField = AccessTools.FieldRefAccess<float>(type4, "targetPowerOutput");
		Type type5 = AccessTools.TypeByName("AncientMarket_Libraray.Tab_LevelTransmit");
		levelTransmitTabReceiverGetter = HarmonyLib.MethodInvoker.GetHandler(AccessTools.DeclaredPropertyGetter(type5, "receive"));
		MpCompat.RegisterLambdaMethod(type5, "FillTab", 1)[0].TransformArgument(0, serializer).SetContext(SyncContext.MapSelected).CancelIfAnyArgNull();
	}

	[MpCompatPrefix("AncientMarket_Libraray.Dialog_Confirm", "DoWindowContents", MethodType.Normal)]
	private static void CloseDialogIfSiteDestroyed(Window __instance, Site ___Site)
	{
		if (___Site == null || ___Site.Destroyed)
		{
			__instance.Close();
		}
	}

	[MpCompatTranspiler("AncientMarket_Libraray.Dialog_Confirm", "DoWindowContents", MethodType.Normal)]
	private static IEnumerable<CodeInstruction> ReplaceDestroyWithSyncedCall(IEnumerable<CodeInstruction> instr)
	{
		MethodInfo target = AccessTools.DeclaredMethod(typeof(WorldObject), "Destroy");
		MethodInfo replacement = MpMethodUtil.MethodOf(new Action<WorldObject>(SyncedDestroySite));
		foreach (CodeInstruction item in instr)
		{
			if (item.Calls(target))
			{
				item.opcode = OpCodes.Call;
				item.operand = replacement;
			}
			yield return item;
		}
	}

	[MpCompatSyncMethod(SyncContext.None)]
	private static void SyncedDestroySite(WorldObject site)
	{
		if (!site.Destroyed)
		{
			site.Destroy();
		}
	}

	[MpCompatSyncWorker("AncientMarket_Libraray.LevelSchedule")]
	private static void SyncLevelSchedule(SyncWorker sync, ref object schedule)
	{
		GameComponent component = Current.Game.GetComponent(ancientMarketGameCompType);
		if (sync.isWriting)
		{
			if (schedule == null)
			{
				sync.Write<Pawn>(null);
				return;
			}
			IDictionary obj = ancientMarketGameCompSchedulesField(component);
			Pawn obj2 = null;
			foreach (DictionaryEntry item in obj)
			{
				if (item.Value == schedule)
				{
					obj2 = item.Key as Pawn;
					break;
				}
			}
			sync.Write(obj2);
		}
		else
		{
			Pawn pawn = sync.Read<Pawn>();
			if (pawn != null)
			{
				schedule = ancientMarketGameCompGetScheduleMethod(component, pawn);
			}
		}
	}

	[MpCompatPrefix("AncientMarket_Libraray.Window_AllowLevel", "DoWindowContents", 2, MethodType.Normal)]
	private static bool PreMapAddedToSchedule(PocketMapParent m, object ___schedule)
	{
		if (!MP.IsInMultiplayer || !MP.IsExecutingSyncCommand)
		{
			return true;
		}
		if (m == null || ___schedule == null)
		{
			return false;
		}
		IList obj = levelScheduleAllowedLevelsField(___schedule);
		MapPortal value = customMapEntranceField(m);
		return !obj.Contains(value);
	}

	[MpCompatSyncMethod(SyncContext.None, cancelIfAnyArgNull = true)]
	private static void SyncedSetTimeAssignment(Pawn pawn, int hour, bool allow)
	{
		GameComponent component = Current.Game.GetComponent(ancientMarketGameCompType);
		object instance = ancientMarketGameCompGetScheduleMethod(component, pawn);
		levelScheduleTimeScheduleField(instance)[hour] = allow;
	}

	private static void ReplacedSetTimeSchedule(List<bool> schedule, int hour, bool allow, Pawn pawn)
	{
		if (schedule[hour] != allow)
		{
			SyncedSetTimeAssignment(pawn, hour, allow);
		}
	}

	[MpCompatTranspiler("AncientMarket_Libraray.PawnColumnWorker_LevelTimetable", "DoTimeAssignment", MethodType.Normal)]
	private static IEnumerable<CodeInstruction> ReplaceIndexerSetterWithSyncedTimetableChange(IEnumerable<CodeInstruction> instr, MethodBase baseMethod)
	{
		MethodInfo target = AccessTools.DeclaredIndexerSetter(typeof(List<>).MakeGenericType(typeof(bool)), new Type[1] { typeof(int) });
		MethodInfo replacement = MpMethodUtil.MethodOf(new Action<List<bool>, int, bool, Pawn>(ReplacedSetTimeSchedule));
		int replacedCount = 0;
		foreach (CodeInstruction ci in instr)
		{
			if (ci.Calls(target))
			{
				yield return new CodeInstruction(OpCodes.Ldarg_2);
				ci.opcode = OpCodes.Call;
				ci.operand = replacement;
				replacedCount++;
			}
			yield return ci;
		}
		if (replacedCount != 1)
		{
			string value = ((baseMethod.DeclaringType?.Namespace).NullOrEmpty() ? baseMethod.Name : (baseMethod.DeclaringType.Name + ":" + baseMethod.Name));
			Log.Warning($"Patched incorrect number of Find.CameraDriver.MapPosition calls (patched {replacedCount}, expected {1}) for method {value}");
		}
	}

	[MpCompatPrefix("AncientMarket_Libraray.Tab_LevelPower", "FillTab", MethodType.Normal)]
	private static void PreLevelPowerITabFillTab(ITab __instance, ref bool __state)
	{
		if (!MP.IsInMultiplayer)
		{
			return;
		}
		object obj = levelPowerTabCompGetter(__instance);
		if (obj != null)
		{
			__state = true;
			MP.WatchBegin();
			powerPlantLevelCompNameSyncField.Watch(obj);
			powerPlantLevelCompOutputModeSyncField.Watch(obj);
			powerPlantLevelTargetOutputLevelSyncField.Watch(obj);
			powerPlantLevelCompLinkedThingSyncField.Watch(obj);
			powerPlantLevelCompLinkedCompSyncField.Watch(obj);
			object obj2 = powerPlantLevelCompLinkedCompGetter(obj);
			if (obj2 != null)
			{
				powerPlantLevelCompLinkedThingSyncField.Watch(obj2);
				powerPlantLevelCompLinkedCompSyncField.Watch(obj2);
			}
		}
	}

	[MpCompatFinalizer("AncientMarket_Libraray.Tab_LevelPower", "FillTab", MethodType.Normal)]
	private static void PostLevelPowerITabFillTab(bool __state)
	{
		if (__state)
		{
			MP.WatchEnd();
		}
	}

	[MpCompatSyncMethod(SyncContext.None, cancelIfAnyArgNull = true)]
	private static void SyncedAcceptPowerChange(CompPowerPlant comp)
	{
		comp.PowerOutput = 0f - powerPlantLevelCompTargetOutputLevelField(comp);
	}

	private static bool ReplacedAcceptPowerChangeButton(Rect rect, string label, bool drawBackground, bool doMouseoverSound, bool active, TextAnchor? overrideTextAnchor)
	{
		bool flag = Widgets.ButtonText(rect, label, drawBackground, doMouseoverSound, active, overrideTextAnchor);
		if (!MP.IsInMultiplayer || !flag)
		{
			return flag;
		}
		if (!(Find.Selector.SingleSelectedThing is ThingWithComps thingWithComps))
		{
			return false;
		}
		foreach (ThingComp allComp in thingWithComps.AllComps)
		{
			if (allComp is CompPowerPlant compPowerPlant && powerPlantLevelCompType.IsInstanceOfType(compPowerPlant))
			{
				SyncedAcceptPowerChange(compPowerPlant);
				break;
			}
		}
		return false;
	}

	[MpCompatTranspiler("AncientMarket_Libraray.Tab_LevelPower", "FillTab", MethodType.Normal)]
	private static IEnumerable<CodeInstruction> ReplaceApplyButton(IEnumerable<CodeInstruction> instr, MethodBase baseMethod)
	{
		MethodInfo methodInfo = AccessTools.DeclaredMethod(typeof(Widgets), "ButtonText", new Type[6]
		{
			typeof(Rect),
			typeof(string),
			typeof(bool),
			typeof(bool),
			typeof(bool),
			typeof(TextAnchor?)
		});
		MethodInfo to = MpMethodUtil.MethodOf(new Func<Rect, string, bool, bool, bool, TextAnchor?, bool>(ReplacedAcceptPowerChangeButton));
		return instr.ReplaceMethod(methodInfo, to, baseMethod, null, null, 1, "Apply");
	}

	[MpCompatPrefix("AncientMarket_Libraray.Tab_LevelTransmit", "FillTab", MethodType.Normal)]
	private static void PreLevelTransmitITabFillTab(ITab __instance, ref bool __state)
	{
		if (MP.IsInMultiplayer)
		{
			object obj = levelTransmitTabReceiverGetter(__instance);
			if (obj != null)
			{
				__state = true;
				MP.WatchBegin();
				buildingTransmitNameSyncField.Watch(obj);
			}
		}
	}

	[MpCompatFinalizer("AncientMarket_Libraray.Tab_LevelTransmit", "FillTab", MethodType.Normal)]
	private static void PostLevelTransmitITabFillTab(bool __state)
	{
		if (__state)
		{
			MP.WatchEnd();
		}
	}

	[MpCompatSyncWorker("AncientMarket_Libraray.Tab_LevelPower", shouldConstruct = true)]
	[MpCompatSyncWorker("AncientMarket_Libraray.Tab_LevelTransmit", shouldConstruct = true)]
	private static void NoSync(SyncWorker sync, ref ITab tab)
	{
	}
}
