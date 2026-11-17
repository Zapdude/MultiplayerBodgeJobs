using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using UnityEngine;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("vanillaquestsexpanded.generator")]
public class VanillaQuestsTheGenerator
{
	[MpCompatSyncField("VanillaQuestsExpandedTheGenerator.Building_GenetronWithMaintenance", "maintenanceMultiplier")]
	protected static ISyncField maintenanceMultiplierField;

	[MpCompatSyncField("VanillaQuestsExpandedTheGenerator.Building_GenetronOverdrive", "compRefuelableWithOverdrive", "tuningMultiplier")]
	protected static ISyncField tuningMultiplierField;

	private static SyncType listOfCompRefuelableWithOverdriveSyncType;

	private static AccessTools.FieldRef<Command, IList> commandSetUraniumLevelRefuelablesField;

	private static AccessTools.FieldRef<Window, Building> downgradeWindowBuildingField;

	private static AccessTools.FieldRef<Window, ThingDef> downgradeWindowNewBuildingField;

	public VanillaQuestsTheGenerator(ModContentPack mod)
	{
		MpCompatPatchLoader.LoadPatch(this);
		Type parentType = AccessTools.TypeByName("VanillaQuestsExpandedTheGenerator.Building_Genetron");
		MpCompat.RegisterLambdaMethod(parentType, "GetGizmos", 0, 3).SetDebugOnly();
		MpCompat.RegisterLambdaDelegate(parentType, "GetGizmos", 1, 2).SetDebugOnly();
		MpCompat.RegisterLambdaMethod("VanillaQuestsExpandedTheGenerator.Building_GenetronNuclear", "GetGizmos", 0, 1, 2).Skip(1).SetDebugOnly();
		MpCompat.RegisterLambdaMethod("VanillaQuestsExpandedTheGenerator.Building_GenetronOverdrive", "GetGizmos", 0, 1, 2, 3, 4).Skip(1).SetDebugOnly();
		MpCompat.RegisterLambdaMethod("VanillaQuestsExpandedTheGenerator.Building_GenetronTuning", "GetGizmos", 3).SetDebugOnly();
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod("VanillaQuestsExpandedTheGenerator.Building_GenetronWithCalibration:Signal_CalibrationStarted"));
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod("VanillaQuestsExpandedTheGenerator.Building_GenetronWithComponentCalibration:Signal_CalibrateComponentsStarted"));
		MpCompat.RegisterLambdaMethod("VanillaQuestsExpandedTheGenerator.Building_GenetronWithEmergencyShutDown", "GetGizmos", default(int));
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod("VanillaQuestsExpandedTheGenerator.Building_GenetronWithFuelRodCalibration:Signal_FuelRodCalibrationStarted"));
		parentType = AccessTools.TypeByName("VanillaQuestsExpandedTheGenerator.Building_GenetronWithHazardModes");
		MP.RegisterSyncMethod(parentType, "Signal_ToggleHazardMode");
		MpCompat.RegisterLambdaMethod(parentType, "GetGizmos", 2).SetDebugOnly();
		MpCompat.RegisterLambdaMethod("VanillaQuestsExpandedTheGenerator.Building_GenetronWithMaintenance", "GetGizmos", default(int)).SetDebugOnly();
		MpCompat.RegisterLambdaMethod("VanillaQuestsExpandedTheGenerator.Building_GenetronWithPowerSurge", "GetGizmos", 0, 1, 2).Skip(1).SetDebugOnly();
		MpCompat.RegisterLambdaMethod("VanillaQuestsExpandedTheGenerator.Building_GenetronWithSteamBoost", "GetGizmos", 0, 1, 2).Skip(1).SetDebugOnly();
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod("VanillaQuestsExpandedTheGenerator.Utils:PlaceDistinctBlueprint")).CancelIfAnyArgNull();
		MpCompat.RegisterLambdaMethod("VanillaQuestsExpandedTheGenerator.Building_Genetron_Studiable", "GetGizmos", default(int));
		parentType = AccessTools.TypeByName("VanillaQuestsExpandedTheGenerator.Command_SetTargetUraniumLevel");
		MpCompat.RegisterLambdaDelegate(parentType, "ProcessInput", 1);
		listOfCompRefuelableWithOverdriveSyncType = typeof(List<>).MakeGenericType(AccessTools.TypeByName("VanillaQuestsExpandedTheGenerator.CompRefuelableWithOverdrive"));
		commandSetUraniumLevelRefuelablesField = AccessTools.FieldRefAccess<IList>(parentType, "refuelables");
		MpCompat.RegisterLambdaMethod("VanillaQuestsExpandedTheGenerator.HediffComp_Traitor", "CompGetGizmos", default(int)).SetDebugOnly();
		Type type = AccessTools.TypeByName("VanillaQuestsExpandedTheGenerator.Window_Downgrade");
		downgradeWindowBuildingField = AccessTools.FieldRefAccess<Building>(type, "building");
		downgradeWindowNewBuildingField = AccessTools.FieldRefAccess<ThingDef>(type, "newBuilding");
		PatchingUtilities.PatchPushPopRand(new string[3] { "VanillaQuestsExpandedTheGenerator.Projectile_SpawnsThingAndExplodes:ThrowBlackSmoke", "VanillaQuestsExpandedTheGenerator.Utils:ThrowBlackSmoke", "VanillaQuestsExpandedTheGenerator.Utils:ThrowExtendedAirPuffUp" });
	}

	[MpCompatTranspiler("VanillaQuestsExpandedTheGenerator.Window_Downgrade", "DoWindowContents", MethodType.Normal)]
	private static IEnumerable<CodeInstruction> ReplaceDowngradeAcceptButton(IEnumerable<CodeInstruction> instr, MethodBase baseMethod)
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
		MethodInfo to = MpMethodUtil.MethodOf(new Func<Rect, string, bool, bool, bool, TextAnchor?, Window, bool>(ReplacedAcceptButton));
		return instr.ReplaceMethod(methodInfo, to, baseMethod, ExtraInstructions, null, 1, "OK");
		static IEnumerable<CodeInstruction> ExtraInstructions(CodeInstruction _)
		{
			return new _003C_003Ez__ReadOnlySingleElementList<CodeInstruction>(new CodeInstruction(OpCodes.Ldarg_0));
		}
	}

	private static bool ReplacedAcceptButton(Rect rect, string label, bool drawBackground, bool doMouseoverSound, bool active, TextAnchor? overrideTextAnchor, Window instance)
	{
		bool flag = Widgets.ButtonText(rect, label, drawBackground, doMouseoverSound, active, overrideTextAnchor);
		if (!MP.IsInMultiplayer || !flag)
		{
			return flag;
		}
		instance.Close();
		SyncedAcceptButton(downgradeWindowBuildingField(instance), downgradeWindowNewBuildingField(instance));
		return false;
	}

	[MpCompatSyncMethod(SyncContext.None, cancelIfAnyArgNull = true)]
	private static void SyncedAcceptButton(Building building, ThingDef newBuilding)
	{
		if (building.def != newBuilding)
		{
			GenSpawn.Spawn(ThingMaker.MakeThing(newBuilding), building.PositionHeld, building.Map).SetFaction(building.Faction);
		}
	}

	[MpCompatPrefix("VanillaQuestsExpandedTheGenerator.Window_FineTuning", "DoWindowContents", MethodType.Normal)]
	private static void PreFineTuningDialog(Building ___building)
	{
		if (MP.IsInMultiplayer)
		{
			MP.WatchBegin();
			maintenanceMultiplierField.Watch(___building);
			tuningMultiplierField.Watch(___building);
		}
	}

	[MpCompatPrefix("VanillaQuestsExpandedTheGenerator.Window_SteamTuning", "DoWindowContents", MethodType.Normal)]
	private static void PreSteamTuningDialog(Building ___building)
	{
		if (MP.IsInMultiplayer)
		{
			MP.WatchBegin();
			maintenanceMultiplierField.Watch(___building);
		}
	}

	[MpCompatPrefix("VanillaQuestsExpandedTheGenerator.Window_Tuning", "DoWindowContents", MethodType.Normal)]
	private static void PreTuningDialog(Building ___building)
	{
		if (MP.IsInMultiplayer)
		{
			MP.WatchBegin();
			tuningMultiplierField.Watch(___building);
		}
	}

	[MpCompatPostfix("VanillaQuestsExpandedTheGenerator.Window_FineTuning", "DoWindowContents", MethodType.Normal)]
	[MpCompatPostfix("VanillaQuestsExpandedTheGenerator.Window_SteamTuning", "DoWindowContents", MethodType.Normal)]
	[MpCompatPostfix("VanillaQuestsExpandedTheGenerator.Window_Tuning", "DoWindowContents", MethodType.Normal)]
	private static void WatchEnd()
	{
		if (MP.IsInMultiplayer)
		{
			MP.WatchEnd();
		}
	}

	[MpCompatSyncWorker("VanillaQuestsExpandedTheGenerator.Command_SetTargetUraniumLevel", shouldConstruct = true)]
	private static void SyncCommandSetUraniumLevel(SyncWorker sync, ref Command command)
	{
		if (sync.isWriting)
		{
			sync.Write(commandSetUraniumLevelRefuelablesField(command), listOfCompRefuelableWithOverdriveSyncType);
		}
		else
		{
			commandSetUraniumLevelRefuelablesField(command) = sync.Read<IList>(listOfCompRefuelableWithOverdriveSyncType);
		}
	}
}
