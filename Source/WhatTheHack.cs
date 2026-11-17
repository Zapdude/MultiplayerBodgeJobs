using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("roolo.whatthehack")]
[MpCompatFor("zal.whatthehack")]
internal class WhatTheHack
{
	private static Dictionary<int, Thing> thingsById;

	private static object extendedDataStorageInstance;

	private static AccessTools.FieldRef<object, IDictionary> pawnStorageDictionary;

	private static FastInvokeHandler getExtendedDataForPawnMethod;

	private static AccessTools.FieldRef<object, Need> maintenanceNeedField;

	private static AccessTools.FieldRef<object, Need> powerNeedField;

	public WhatTheHack(ModContentPack mod)
	{
		thingsById = (Dictionary<int, Thing>)AccessTools.Field(AccessTools.TypeByName("Multiplayer.Client.ThingsById"), "thingsById").GetValue(null);
		Type type = AccessTools.TypeByName("WhatTheHack.Base");
		MpCompat.harmony.Patch(AccessTools.Method(type, "WorldLoaded"), null, new HarmonyMethod(typeof(WhatTheHack), "PostWorldLoaded"));
		type = AccessTools.TypeByName("WhatTheHack.Storage.ExtendedDataStorage");
		pawnStorageDictionary = AccessTools.FieldRefAccess<IDictionary>(type, "_store");
		getExtendedDataForPawnMethod = MethodInvoker.GetHandler(AccessTools.Method(type, "GetExtendedDataFor", new Type[1] { typeof(Pawn) }));
		type = AccessTools.TypeByName("WhatTheHack.Storage.ExtendedPawnData");
		MP.RegisterSyncWorker<object>(SyncExtendedPawnData, type);
		PatchingUtilities.PatchSystemRand(new string[4] { "WhatTheHack.Harmony.IncidentWorker_Raid_TryExecuteWorker:Prefix", "WhatTheHack.Harmony.Pawn_JobTracker_DetermineNextJob:HackedPoorlyEvent", "WhatTheHack.Needs.Need_Maintenance:MaybeUnhackMechanoid", "WhatTheHack.Recipes.Recipe_Hacking:CheckHackingFail" }, patchPushPop: false);
		PatchingUtilities.PatchSystemRand(MpMethodUtil.GetMethod(AccessTools.TypeByName("WhatTheHack.Harmony.Thing_ButcherProducts"), "GenerateExtraButcherProducts", MethodType.Enumerator, new Type[3]
		{
			typeof(IEnumerable<Thing>),
			typeof(Pawn),
			typeof(float)
		}));
		MP.RegisterSyncMethod(AccessTools.Method("WhatTheHack.Buildings.Building_MechanoidBeacon:StartupHibernatingParts"));
		MpCompat.RegisterLambdaMethod("WhatTheHack.Buildings.Building_MechanoidPlatform", "GetGizmos", 1, 3);
		Type parentType = AccessTools.TypeByName("WhatTheHack.Buildings.Building_RogueAI");
		MpCompat.RegisterLambdaMethod(parentType, "GetTalkGibberishGizmo", default(int));
		MpCompat.RegisterLambdaMethod(parentType, "GetManagePowerNetworkGizmo", 1);
		MpCompat.RegisterLambdaMethod(parentType, "GetControlTurretAvtivateGizmo", default(int));
		MpCompat.RegisterLambdaMethod(parentType, "GetControlMechanoidActivateGizmo", default(int));
		MpCompat.RegisterLambdaMethod(parentType, "GetHackingAvtivateGizmo", default(int));
		MpCompat.RegisterLambdaDelegate(parentType, "GetControlMechanoidCancelGizmo", default(int));
		MpCompat.RegisterLambdaDelegate(parentType, "GetControlTurretCancelGizmo", default(int));
		MpCompat.RegisterLambdaDelegate(parentType, "GetHackingCancelGizmo", default(int));
		parentType = AccessTools.TypeByName("WhatTheHack.Command_SetMaintenanceThreshold");
		maintenanceNeedField = AccessTools.FieldRefAccess<Need>(parentType, "maintenanceNeed");
		MpCompat.RegisterLambdaMethod(parentType, "ProcessInput", 1);
		MP.RegisterSyncWorker<Command>(SyncCommand_SetMaintenanceThreshold, parentType, isImplicit: false, shouldConstruct: true);
		parentType = AccessTools.TypeByName("WhatTheHack.Command_SetWorkThreshold");
		powerNeedField = AccessTools.FieldRefAccess<Need>(parentType, "powerNeed");
		MpCompat.RegisterLambdaMethod(parentType, "ProcessInput", 1);
		MP.RegisterSyncWorker<Command>(SyncCommand_SetWorkThreshold, parentType, isImplicit: false, shouldConstruct: true);
		parentType = AccessTools.TypeByName("WhatTheHack.Harmony.Pawn_GetGizmos");
		MpCompat.RegisterLambdaDelegate(parentType, "CreateGizmo_SearchAndDestroy", 1);
		MpCompat.RegisterLambdaDelegate(parentType, "CreateGizmo_AutoRecharge", 1);
		MpCompat.RegisterLambdaDelegate(parentType, "CreateGizmo_Work", 1);
		MpCompat.RegisterLambdaDelegate(parentType, "CreateGizmo_SelfDestruct", default(int));
		MpCompat.RegisterLambdaDelegate(parentType, "CreateGizmo_SelfRepair", default(int));
		MpCompat.RegisterLambdaDelegate(parentType, "CreateGizmo_Repair", default(int));
		MpCompat.RegisterLambdaDelegate(parentType, "CreateGizmo_EquipBelt", default(int));
		MpCompat.RegisterLambdaDelegate(parentType, "CreateGizmo_Overdrive", default(int));
		parentType = AccessTools.TypeByName("WhatTheHack.RemoteController");
		MpCompat.RegisterLambdaDelegate(parentType, "GetRemoteControlDeActivateGizmo", default(int));
		MpCompat.RegisterLambdaDelegate(parentType, "GetRemoteControlActivateGizmo", default(int));
		parentType = AccessTools.TypeByName("WhatTheHack.Harmony.CompLongRangeMineralScanner_CompGetGizmosExtra");
		MpCompat.RegisterLambdaDelegate(parentType, "AddMechPartsOption", 1).SetContext(SyncContext.MapSelected);
	}

	private static void PostWorldLoaded(WorldComponent ____extendedDataStorage)
	{
		extendedDataStorageInstance = ____extendedDataStorage;
	}

	private static void SyncCommand_SetMaintenanceThreshold(SyncWorker sync, ref Command command)
	{
		if (sync.isWriting)
		{
			sync.Write(maintenanceNeedField(command));
		}
		else
		{
			maintenanceNeedField(command) = sync.Read<Need>();
		}
	}

	private static void SyncCommand_SetWorkThreshold(SyncWorker sync, ref Command command)
	{
		if (sync.isWriting)
		{
			sync.Write(powerNeedField(command));
		}
		else
		{
			powerNeedField(command) = sync.Read<Need>();
		}
	}

	private static void SyncExtendedPawnData(SyncWorker sync, ref object data)
	{
		if (sync.isWriting)
		{
			int obj = int.MaxValue;
			foreach (DictionaryEntry item in pawnStorageDictionary(extendedDataStorageInstance))
			{
				if (item.Value == data)
				{
					obj = (int)item.Key;
					break;
				}
			}
			sync.Write(obj);
		}
		else
		{
			int num = sync.Read<int>();
			if (num != int.MaxValue && thingsById.TryGetValue(num, out var value) && value is Pawn)
			{
				data = getExtendedDataForPawnMethod(extendedDataStorageInstance, value);
			}
		}
	}
}
