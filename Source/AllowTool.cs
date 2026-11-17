using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Multiplayer.Compat;

[MpCompatFor("UnlimitedHugs.AllowTool")]
public class AllowTool
{
	private static FastInvokeHandler partyHuntWorldSettingsGetter;

	private static FastInvokeHandler activationResultMessageGetter;

	private static FastInvokeHandler activationResultMessageTypeGetter;

	private static FastInvokeHandler createWorkGiverInstance;

	private static AccessTools.FieldRef<object, Job> innerClassJobField;

	private static AccessTools.FieldRef<object, WorkGiver_Scanner> innerClassWorkGiverField;

	private static AccessTools.FieldRef<object, object> innerClassParentField;

	private static Type innerParentType;

	private static AccessTools.FieldRef<object, Pawn> parentInnerClassPawnField;

	private static AccessTools.FieldRef<object, Thing> parentInnerClassTargetField;

	private static Type selectSimilarType;

	private static bool? shiftHeldState;

	private static bool? controlHeldState;

	private static CellRect? visibleMapRectState;

	public AllowTool(ModContentPack mod)
	{
		partyHuntWorldSettingsGetter = HarmonyLib.MethodInvoker.GetHandler(AccessTools.PropertyGetter("AllowTool.Command_PartyHunt:WorldSettings"));
		Type type = AccessTools.TypeByName("AllowTool.Settings.PartyHuntSettings");
		MP.RegisterSyncMethod(type, "AutoFinishOff");
		MP.RegisterSyncMethod(type, "HuntDesignatedOnly");
		MP.RegisterSyncMethod(type, "UnforbidDrops");
		MP.RegisterSyncMethod(type, "TogglePawnPartyHunting");
		MP.RegisterSyncWorker<object>(SyncDraftedHuntSettings, type);
		Type type2 = AccessTools.TypeByName("AllowTool.Context.BaseContextMenuEntry");
		Type[] second = new Type[3]
		{
			AccessTools.TypeByName("AllowTool.Context.MenuEntry_SelectSimilarAll"),
			AccessTools.TypeByName("AllowTool.Context.MenuEntry_SelectSimilarHome"),
			AccessTools.TypeByName("AllowTool.Context.MenuEntry_SelectSimilarVisible")
		};
		foreach (Type item in type2.AllSubclasses().Concat(type2).Except(second))
		{
			MethodInfo methodInfo = AccessTools.DeclaredMethod(item, "Activate");
			if (methodInfo != null)
			{
				MP.RegisterSyncMethod(methodInfo).SetContext(SyncContext.MapSelected).SetPostInvoke(ClearTemporaryState);
				MpCompat.harmony.Patch(methodInfo, null, new HarmonyMethod(typeof(AllowTool), "ShowActivationResultSelf"));
			}
		}
		MP.RegisterSyncWorker<object>(ShiftControlStateOnlySyncWorker, type2, isImplicit: true, shouldConstruct: true);
		Type[] array = new Type[3]
		{
			AccessTools.TypeByName("AllowTool.Context.MenuEntry_AllowVisible"),
			AccessTools.TypeByName("AllowTool.Context.MenuEntry_ForbidVisible"),
			AccessTools.TypeByName("AllowTool.Context.MenuEntry_HaulUrgentVisible")
		};
		foreach (Type targetType in array)
		{
			MP.RegisterSyncWorker<object>(ShiftControlCameraStateOnlySyncWorker, targetType, isImplicit: false, shouldConstruct: true);
		}
		type2 = AccessTools.TypeByName("AllowTool.Context.ActivationResult");
		activationResultMessageGetter = HarmonyLib.MethodInvoker.GetHandler(AccessTools.DeclaredPropertyGetter(type2, "Message"));
		activationResultMessageTypeGetter = HarmonyLib.MethodInvoker.GetHandler(AccessTools.DeclaredPropertyGetter(type2, "MessageType"));
		Type type3 = AccessTools.TypeByName("AllowTool.Designator_AllowAll");
		MP.RegisterSyncMethod(AccessTools.Method(type3, "AllowAllTheThings")).SetPostInvoke(ClearTemporaryState);
		MP.RegisterSyncWorker<object>(ShiftControlStateOnlySyncWorker, type3, isImplicit: false, shouldConstruct: true);
		selectSimilarType = AccessTools.TypeByName("AllowTool.Designator_SelectSimilar");
		type3 = AccessTools.TypeByName("Multiplayer.Client.DesignatorPatches");
		string[] array2 = new string[3] { "DesignateSingleCell", "DesignateMultiCell", "DesignateThing" };
		foreach (string name in array2)
		{
			MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type3, name), new HarmonyMethod(typeof(AllowTool), "StopDesignatorSyncing"));
		}
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(Designator), "Finalize"), new HarmonyMethod(typeof(AllowTool), "PostDesignatorFinalize"));
		Type type4 = AccessTools.TypeByName("AllowTool.HaulUrgentlyCacheHandler");
		MpCompat.harmony.Patch(AccessTools.Method(type4, "RecacheIfNeeded"), new HarmonyMethod(typeof(AllowTool), "DeterministicallyHandleReCaching"));
		type4 = AccessTools.Inner(type4, "ThingsCacheEntry");
		MpCompat.harmony.Patch(AccessTools.Method(type4, "IsValid"), new HarmonyMethod(typeof(AllowTool), "ScaleReCachingTimerToTickSpeed"));
		Type type5 = AccessTools.TypeByName("AllowTool.WorkGiver_FinishOff");
		createWorkGiverInstance = HarmonyLib.MethodInvoker.GetHandler(AccessTools.Method(type5, "CreateInstance"));
		MethodInfo lambda = MpMethodUtil.GetLambda(type5, "InjectThingFloatOptionIfNeeded");
		type5 = lambda.DeclaringType;
		innerClassJobField = AccessTools.FieldRefAccess<Job>(type5, "job");
		innerClassWorkGiverField = AccessTools.FieldRefAccess<WorkGiver_Scanner>(type5, "giver");
		FieldInfo fieldInfo = AccessTools.GetDeclaredFields(type5).FirstOrDefault((FieldInfo t) => t.FieldType.Name.StartsWith("<>c__DisplayClass"));
		innerClassParentField = AccessTools.FieldRefAccess<object, object>(fieldInfo);
		MP.RegisterSyncMethod(lambda).SetContext(SyncContext.QueueOrder_Down);
		MP.RegisterSyncWorker<object>(SyncFinishOffInnerClass, type5, isImplicit: false, shouldConstruct: true);
		innerParentType = fieldInfo.FieldType;
		parentInnerClassPawnField = AccessTools.FieldRefAccess<Pawn>(innerParentType, "selPawn");
		parentInnerClassTargetField = AccessTools.FieldRefAccess<Thing>(innerParentType, "target");
		Type type6 = AccessTools.TypeByName("HugsLib.Utils.HugsLibUtility");
		MpCompat.harmony.Patch(AccessTools.PropertyGetter(type6, "ShiftIsHeld"), new HarmonyMethod(typeof(AllowTool), "ReplaceShiftHeld"));
		MpCompat.harmony.Patch(AccessTools.PropertyGetter(type6, "ControlIsHeld"), new HarmonyMethod(typeof(AllowTool), "ReplaceControlHeld"));
		MpCompat.harmony.Patch(AccessTools.Method("AllowTool.AllowToolUtility:GetVisibleMapRect"), new HarmonyMethod(typeof(AllowTool), "ReplaceCurrentMapRect"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod("AllowTool.Designator_HaulUrgently:ThingIsRelevant"), new HarmonyMethod(typeof(AllowTool), "PreThingIsRelevant"));
		LongEventHandler.ExecuteWhenFinished(LatePatch);
	}

	private static void LatePatch()
	{
		MP.RegisterSyncMethod(AccessTools.Method("AllowTool.Designator_StripMine:DesignateCells"));
	}

	private static void ShowActivationResultSelf(object __result)
	{
		if (MP.IsInMultiplayer && MP.IsExecutingSyncCommandIssuedBySelf && __result != null && activationResultMessageGetter(__result) is string text && activationResultMessageTypeGetter(__result) is MessageTypeDef def)
		{
			Messages.Message(text, def, historical: false);
		}
	}

	private static bool StopDesignatorSyncing([HarmonyArgument("__instance")] Designator instance, ref bool __result)
	{
		if (MP.IsInMultiplayer && !selectSimilarType.IsInstanceOfType(instance))
		{
			return true;
		}
		__result = true;
		return false;
	}

	private static void PostDesignatorFinalize(Designator __instance, bool somethingSucceeded)
	{
		if (MP.IsInMultiplayer && !MP.IsExecutingSyncCommand && somethingSucceeded && selectSimilarType.IsInstanceOfType(__instance))
		{
			__instance.FinalizeDesignationSucceeded();
		}
	}

	private static void ClearTemporaryState(object instance, object[] args)
	{
		shiftHeldState = null;
		controlHeldState = null;
		visibleMapRectState = null;
	}

	private static bool ReplaceShiftHeld(ref bool __result)
	{
		if (!shiftHeldState.HasValue)
		{
			return true;
		}
		__result = shiftHeldState.Value;
		return false;
	}

	private static bool ReplaceControlHeld(ref bool __result)
	{
		if (!controlHeldState.HasValue)
		{
			return true;
		}
		__result = controlHeldState.Value;
		return false;
	}

	private static bool ReplaceCurrentMapRect(ref CellRect __result)
	{
		if (!visibleMapRectState.HasValue)
		{
			return true;
		}
		__result = visibleMapRectState.Value;
		return false;
	}

	private static bool DeterministicallyHandleReCaching(ref float currentTime)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (MP.InInterface)
		{
			return false;
		}
		currentTime = Find.TickManager.TicksGame;
		return true;
	}

	private static bool ScaleReCachingTimerToTickSpeed(float currentTime, float ___createdTime, ref bool __result)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		float num = Find.TickManager.TickRateMultiplier;
		if (num <= 0.15f)
		{
			num = 0.15f;
		}
		__result = ___createdTime > 0f && ___createdTime < currentTime + 60f * num;
		return false;
	}

	private static void SyncDraftedHuntSettings(SyncWorker sync, ref object settings)
	{
		if (!sync.isWriting)
		{
			settings = partyHuntWorldSettingsGetter(null);
		}
	}

	private static void ShiftControlStateOnlySyncWorker(SyncWorker sync, ref object _)
	{
		SyncShiftControlState(sync);
	}

	private static void ShiftControlCameraStateOnlySyncWorker(SyncWorker sync, ref object _)
	{
		SyncShiftControlState(sync);
		SyncCameraState(sync);
	}

	private static void SyncShiftControlState(SyncWorker sync)
	{
		if (sync.isWriting)
		{
			sync.Write(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
			sync.Write(Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.LeftApple) || Input.GetKey(KeyCode.RightApple));
		}
		else
		{
			shiftHeldState = sync.Read<bool>();
			controlHeldState = sync.Read<bool>();
		}
	}

	private static void SyncCameraState(SyncWorker sync)
	{
		if (sync.isWriting)
		{
			CellRect currentViewRect = Find.CameraDriver.CurrentViewRect;
			sync.Write(currentViewRect.minX);
			sync.Write(currentViewRect.maxX);
			sync.Write(currentViewRect.minZ);
			sync.Write(currentViewRect.maxZ);
		}
		else
		{
			visibleMapRectState = new CellRect
			{
				minX = sync.Read<int>(),
				maxX = sync.Read<int>(),
				minZ = sync.Read<int>(),
				maxZ = sync.Read<int>()
			};
		}
	}

	private static void SyncFinishOffInnerClass(SyncWorker sync, ref object obj)
	{
		if (sync.isWriting)
		{
			object instance = innerClassParentField(obj);
			sync.Write(parentInnerClassPawnField(instance));
			sync.Write(parentInnerClassTargetField(instance));
			return;
		}
		try
		{
			shiftHeldState = true;
			object obj2 = Activator.CreateInstance(innerParentType);
			Pawn pawn = sync.Read<Pawn>();
			Thing thing = sync.Read<Thing>();
			parentInnerClassPawnField(obj2) = pawn;
			parentInnerClassTargetField(obj2) = thing;
			innerClassParentField(obj) = obj2;
			WorkGiver_Scanner workGiver_Scanner = (WorkGiver_Scanner)createWorkGiverInstance(null);
			innerClassWorkGiverField(obj) = workGiver_Scanner;
			innerClassJobField(obj) = workGiver_Scanner.JobOnThing(pawn, thing, forced: true);
		}
		finally
		{
			shiftHeldState = null;
		}
	}

	private static bool PreThingIsRelevant(Thing thing, ref bool __result)
	{
		if (thing.Map != null)
		{
			return true;
		}
		__result = false;
		return false;
	}
}
