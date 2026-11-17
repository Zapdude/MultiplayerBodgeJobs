using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Multiplayer.Compat;

[MpCompatFor("JellyCreative.IsekaiLeveling")]
public class IsekaiRPGCompat
{
	private static readonly string[] StatAllocationFieldNames = new string[6] { "strength", "vitality", "dexterity", "intelligence", "wisdom", "charisma" };

	private static readonly string[] PendingFieldNames = new string[6] { "pendingSTR", "pendingVIT", "pendingDEX", "pendingINT", "pendingWIS", "pendingCHA" };

	private static Type isekaiComponentType;

	private static Type isekaiStatAllocationType;

	private static Type passiveTreeTrackerType;

	private static Type windowStatsType;

	private static Type iTabType;

	private static Type raidRankSystemType;

	private static Type pawnStatGeneratorType;

	private static Type treeAutoAssignerType;

	private static Type manaCoreCompType;

	private static Type windowCreatureStatsType;

	private static Type iTabCreatureStatsType;

	private static Type mobRankComponentType;

	private static Type auraFeelType;

	private static Type isekaiGizmoProviderType;

	private static Type forgeUtilityType;

	private static Type refineResultType;

	private static Type windowForgeType;

	private static Type windowRunicStationType;

	private static Type compForgeEnhancementType;

	private static FieldInfo[] statAllocationFields;

	private static FieldInfo statAllocationAvailablePoints;

	private static FieldInfo isekaiCompStatsField;

	private static FieldInfo isekaiCompPassiveTreeField;

	private static FieldInfo raidRankSystemRandomField;

	private static FieldInfo pawnStatGeneratorRandomField;

	private static FieldInfo treeAutoAssignerRngField;

	private static FieldInfo mobRankStatsField;

	private static AccessTools.FieldRef<object, Pawn> statsWindowPawn;

	private static AccessTools.FieldRef<object, int>[] statsWindowPending;

	private static AccessTools.FieldRef<object, int> statsWindowPointsSpent;

	private static AccessTools.FieldRef<object, Pawn> creatureWindowPawn;

	private static AccessTools.FieldRef<object, int>[] creatureWindowPending;

	private static AccessTools.FieldRef<object, int> creatureWindowPointsSpent;

	private static ISyncField[] statSyncFields;

	private static ISyncField autoDistributeStatsSyncField;

	private static bool _suppressUnlockPrefix = false;

	private static bool _suppressUnlockChainPrefix = false;

	private static bool _suppressRespecPrefix = false;

	private static bool _suppressDevAddLevelPrefix = false;

	private static bool _suppressRefinePrefix = false;

	private static bool _suppressRepairPrefix = false;

	private static bool _suppressTryAddRune = false;

	private static bool _suppressRemoveRune = false;

	private static FieldInfo pendingBulkAbsorbField;

	private static FieldInfo auraDisplayModeField;

	private static FieldInfo auraConstellationField;

	private static Type characterForgeType;

	private static Type characterSpecType;

	private static FieldInfo characterWindowPawn;

	private static FieldInfo characterWindowLock;

	private static MethodInfo characterBuildSpec;

	private static bool suppressAutoDistributePrefix;

	[ThreadStatic]
	private static bool? synchronizedForgeGodMode;

	private static void SetupAsyncClocks()
	{
		IsekaiAsyncQuests.Install();
		IsekaiAsyncPassives.Install();
		AccessTools.FieldRef<object, int>[] fields = new AccessTools.FieldRef<object, int>[3]
		{
			AccessTools.FieldRefAccess<int>(isekaiComponentType, "auraFeelCooldownTick"),
			AccessTools.FieldRefAccess<int>(isekaiComponentType, "undyingCooldownTick"),
			AccessTools.FieldRefAccess<int>(isekaiComponentType, "echoOfDefeatActiveUntilTick")
		};
		PatchingUtilities.RegisterTimestampFields(isekaiComponentType, (object component, int offset) =>
		{
			AccessTools.FieldRef<object, int>[] array = fields;
			for (int i = 0; i < array.Length; i++)
			{
				ref int reference = ref array[i](component);
				if (reference != 0)
				{
					reference += offset;
				}
			}
		});
	}

	public IsekaiRPGCompat(ModContentPack mod)
	{
		IsekaiDeferredExecution.Setup(mod);
		isekaiComponentType = Resolve("IsekaiLeveling.IsekaiComponent");
		isekaiStatAllocationType = Resolve("IsekaiLeveling.IsekaiStatAllocation");
		iTabType = Resolve("IsekaiLeveling.UI.ITab_IsekaiStats");
		windowStatsType = Resolve("IsekaiLeveling.UI.Window_StatsAttribution");
		passiveTreeTrackerType = Resolve("IsekaiLeveling.SkillTree.PassiveTreeTracker");
		raidRankSystemType = Resolve("IsekaiLeveling.MobRanking.RaidRankSystem");
		pawnStatGeneratorType = Resolve("IsekaiLeveling.PawnStatGenerator");
		treeAutoAssignerType = Resolve("IsekaiLeveling.SkillTree.TreeAutoAssigner");
		manaCoreCompType = Resolve("IsekaiLeveling.CompUseEffect_ManaCore");
		auraFeelType = AccessTools.TypeByName("IsekaiLeveling.Abilities.AuraFeel");
		isekaiGizmoProviderType = AccessTools.TypeByName("IsekaiLeveling.UI.IsekaiGizmoProvider");
		forgeUtilityType = AccessTools.TypeByName("IsekaiLeveling.Forge.ForgeUtility");
		refineResultType = AccessTools.TypeByName("IsekaiLeveling.Forge.ForgeUtility+RefineResult");
		windowForgeType = AccessTools.TypeByName("IsekaiLeveling.Forge.Window_Forge");
		windowRunicStationType = AccessTools.TypeByName("IsekaiLeveling.Forge.Window_RunicStation");
		compForgeEnhancementType = AccessTools.TypeByName("IsekaiLeveling.Forge.CompForgeEnhancement");
		if (isekaiComponentType == null || isekaiStatAllocationType == null || passiveTreeTrackerType == null || windowStatsType == null || iTabType == null || raidRankSystemType == null || pawnStatGeneratorType == null || treeAutoAssignerType == null || manaCoreCompType == null)
		{
			Log.Error("[IsekaiMP] One or more required types could not be resolved — patches will NOT be applied.");
			return;
		}
		raidRankSystemRandomField = AccessTools.Field(raidRankSystemType, "random");
		SetupAsyncClocks();
		pawnStatGeneratorRandomField = AccessTools.Field(pawnStatGeneratorType, "random");
		treeAutoAssignerRngField = AccessTools.Field(treeAutoAssignerType, "rng");
		raidRankSystemRandomField?.SetValue(null, PatchingUtilities.RandRedirector.Instance);
		pawnStatGeneratorRandomField?.SetValue(null, PatchingUtilities.RandRedirector.Instance);
		treeAutoAssignerRngField?.SetValue(null, PatchingUtilities.RandRedirector.Instance);
		IsekaiMobRankInitialization.Setup();
		isekaiCompStatsField = AccessTools.Field(isekaiComponentType, "stats");
		isekaiCompPassiveTreeField = AccessTools.Field(isekaiComponentType, "passiveTree");
		statAllocationFields = new FieldInfo[StatAllocationFieldNames.Length];
		for (int i = 0; i < StatAllocationFieldNames.Length; i++)
		{
			statAllocationFields[i] = AccessTools.Field(isekaiStatAllocationType, StatAllocationFieldNames[i]);
		}
		statAllocationAvailablePoints = AccessTools.Field(isekaiStatAllocationType, "availableStatPoints");
		statSyncFields = new ISyncField[StatAllocationFieldNames.Length + 1];
		for (int j = 0; j < StatAllocationFieldNames.Length; j++)
		{
			statSyncFields[j] = MP.RegisterSyncField(isekaiStatAllocationType, StatAllocationFieldNames[j]);
		}
		statSyncFields[StatAllocationFieldNames.Length] = MP.RegisterSyncField(isekaiStatAllocationType, "availableStatPoints");
		statsWindowPawn = AccessTools.FieldRefAccess<Pawn>(windowStatsType, "pawn");
		statsWindowPending = new AccessTools.FieldRef<object, int>[PendingFieldNames.Length];
		for (int k = 0; k < PendingFieldNames.Length; k++)
		{
			statsWindowPending[k] = AccessTools.FieldRefAccess<int>(windowStatsType, PendingFieldNames[k]);
		}
		statsWindowPointsSpent = AccessTools.FieldRefAccess<int>(windowStatsType, "pointsSpent");
		PatchAndLog(isekaiComponentType, "DevAddLevel", "DevAddLevelPrefix");
		PatchAndLog(iTabType, "FillTab", "ITabFillTabPrefix", "ITabFillTabPostfix");
		PatchAndLog(windowStatsType, "ApplyChanges", "ApplyChangesPrefix");
		PatchAndLog(passiveTreeTrackerType, "Unlock", "UnlockNodePrefix");
		PatchAndLog(passiveTreeTrackerType, "TryUnlockChain", "TryUnlockChainPrefix");
		PatchAndLog(passiveTreeTrackerType, "Respec", "RespecPrefix");
		LongEventHandler.ExecuteWhenFinished(() =>
		{
			if (isekaiGizmoProviderType != null && auraFeelType != null)
			{
				PatchAndLog(isekaiGizmoProviderType, "GetGizmos", null, "GetGizmosPostfix");
				MP.RegisterSyncMethod(typeof(IsekaiRPGCompat), "SyncedActivateAuraPressure");
			}
		});
		if (forgeUtilityType != null)
		{
			PatchAndLog(forgeUtilityType, "AttemptRefinement", "AttemptRefinementPrefix");
			PatchAndLog(forgeUtilityType, "RepairItem", "RepairItemPrefix");
			MP.RegisterSyncMethod(typeof(IsekaiRPGCompat), "SyncedAttemptRefinement");
			MP.RegisterSyncMethod(typeof(IsekaiRPGCompat), "SyncedRepairItem");
		}
		if (compForgeEnhancementType != null)
		{
			PatchAndLog(compForgeEnhancementType, "TryAddRune", "TryAddRunePrefix");
			PatchAndLog(compForgeEnhancementType, "RemoveRuneAt", "RemoveRuneAtPrefix");
			MP.RegisterSyncMethod(typeof(IsekaiRPGCompat), "SyncedApplyRune");
			MP.RegisterSyncMethod(typeof(IsekaiRPGCompat), "SyncedRemoveRune");
		}
		MP.RegisterSyncWorker<object>(SyncIsekaiStatAllocation, isekaiStatAllocationType);
		MP.RegisterSyncMethod(typeof(IsekaiRPGCompat), "SyncedDevAddLevel").SetDebugOnly();
		MP.RegisterSyncMethod(typeof(IsekaiRPGCompat), "SyncedApplyStats");
		MP.RegisterSyncMethod(typeof(IsekaiRPGCompat), "SyncedUnlockNode");
		MP.RegisterSyncMethod(typeof(IsekaiRPGCompat), "SyncedUnlockChain");
		MP.RegisterSyncMethod(typeof(IsekaiRPGCompat), "SyncedRespec");
		SetupAdditionalInteractions();
		windowCreatureStatsType = AccessTools.TypeByName("IsekaiLeveling.UI.Window_CreatureStats");
		iTabCreatureStatsType = AccessTools.TypeByName("IsekaiLeveling.UI.ITab_CreatureStats");
		mobRankComponentType = AccessTools.TypeByName("IsekaiLeveling.MobRanking.MobRankComponent");
		if (windowCreatureStatsType == null || iTabCreatureStatsType == null || mobRankComponentType == null)
		{
			Log.Warning("[IsekaiMP] Animal stats types not found — animal stat sync will not be applied.");
			return;
		}
		mobRankStatsField = AccessTools.Field(mobRankComponentType, "stats");
		creatureWindowPawn = AccessTools.FieldRefAccess<Pawn>(windowCreatureStatsType, "pawn");
		creatureWindowPending = new AccessTools.FieldRef<object, int>[PendingFieldNames.Length];
		for (int num = 0; num < PendingFieldNames.Length; num++)
		{
			creatureWindowPending[num] = AccessTools.FieldRefAccess<int>(windowCreatureStatsType, PendingFieldNames[num]);
		}
		creatureWindowPointsSpent = AccessTools.FieldRefAccess<int>(windowCreatureStatsType, "pointsSpent");
		PatchAndLog(windowCreatureStatsType, "ApplyChanges", "CreatureApplyChangesPrefix");
		PatchAndLog(iTabCreatureStatsType, "FillTab", "ITabCreatureFillTabPrefix", "ITabFillTabPostfix");
		MP.RegisterSyncMethod(typeof(IsekaiRPGCompat), "SyncedApplyCreatureStats");
	}

	private static Type Resolve(string typeName)
	{
		Type type = AccessTools.TypeByName(typeName);
		if (type == null)
		{
			Log.Error("[IsekaiMP] Could not resolve type '" + typeName + "' — mod version may have changed.");
		}
		return type;
	}

	private static void PatchAndLog(Type targetType, string methodName, string prefix = null, string postfix = null, string transpiler = null)
	{
		MethodInfo methodInfo = AccessTools.DeclaredMethod(targetType, methodName);
		if (methodInfo == null)
		{
			Log.Error($"[IsekaiMP] Could not find method '{targetType.Name}.{methodName}' — patch skipped.");
		}
		else
		{
			MpCompat.harmony.Patch(methodInfo, (prefix != null) ? new HarmonyMethod(typeof(IsekaiRPGCompat), prefix) : null, (postfix != null) ? new HarmonyMethod(typeof(IsekaiRPGCompat), postfix) : null, (transpiler != null) ? new HarmonyMethod(typeof(IsekaiRPGCompat), transpiler) : null);
		}
	}

	private static ThingComp GetCompByType(Pawn pawn, Type compType)
	{
		if (pawn?.AllComps == null || compType == null)
		{
			return null;
		}
		foreach (ThingComp allComp in pawn.AllComps)
		{
			if (compType.IsInstanceOfType(allComp))
			{
				return allComp;
			}
		}
		return null;
	}

	private static ThingComp GetThingCompByType(Thing thing, Type compType)
	{
		if (!(thing is ThingWithComps { AllComps: not null } thingWithComps) || compType == null)
		{
			return null;
		}
		foreach (ThingComp allComp in thingWithComps.AllComps)
		{
			if (compType.IsInstanceOfType(allComp))
			{
				return allComp;
			}
		}
		return null;
	}

	private static Pawn FindPawnByComp(Func<ThingComp, bool> match)
	{
		foreach (Map map in Find.Maps)
		{
			foreach (Pawn item in map.mapPawns.AllPawnsSpawned)
			{
				ThingComp compByType = GetCompByType(item, isekaiComponentType);
				if (compByType != null && match(compByType))
				{
					return item;
				}
			}
		}
		if (Find.WorldPawns != null)
		{
			foreach (Pawn item2 in Find.WorldPawns.AllPawnsAlive)
			{
				ThingComp compByType2 = GetCompByType(item2, isekaiComponentType);
				if (compByType2 != null && match(compByType2))
				{
					return item2;
				}
			}
		}
		return null;
	}

	private static bool IsPlayerForgeUiInteraction()
	{
		if (!UnityData.IsInMainThread || Event.current == null)
		{
			return false;
		}
		WindowStack windowStack = Find.WindowStack;
		if (windowStack == null)
		{
			return false;
		}
		if (windowForgeType != null && windowStack.IsOpen(windowForgeType))
		{
			return true;
		}
		if (windowRunicStationType != null && windowStack.IsOpen(windowRunicStationType))
		{
			return true;
		}
		return false;
	}

	private static bool ApplyChangesPrefix(object __instance)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		Pawn pawn = statsWindowPawn(__instance);
		if (pawn == null)
		{
			return true;
		}
		int[] array = new int[StatAllocationFieldNames.Length];
		for (int i = 0; i < array.Length; i++)
		{
			array[i] = statsWindowPending[i](__instance);
		}
		int num = 0;
		bool flag = Prefs.DevMode && DebugSettings.godMode;
		if (!flag)
		{
			ThingComp compByType = GetCompByType(pawn, isekaiComponentType);
			if (compByType != null)
			{
				object value = isekaiCompStatsField.GetValue(compByType);
				for (int j = 0; j < array.Length; j++)
				{
					num += array[j] - (int)statAllocationFields[j].GetValue(value);
				}
			}
		}
		SyncedApplyStats(pawn, array, num, flag);
		return false;
	}

	private static void SyncedApplyStats(Pawn pawn, int[] statValues, int pointsSpent, bool godMode)
	{
		ThingComp compByType = GetCompByType(pawn, isekaiComponentType);
		if (compByType != null)
		{
			object value = isekaiCompStatsField.GetValue(compByType);
			for (int i = 0; i < statAllocationFields.Length; i++)
			{
				statAllocationFields[i].SetValue(value, statValues[i]);
			}
			if (!godMode && pointsSpent > 0)
			{
				int num = (int)statAllocationAvailablePoints.GetValue(value);
				statAllocationAvailablePoints.SetValue(value, num - pointsSpent);
			}
			AccessTools.DeclaredMethod(pawnStatGeneratorType, "UpdateRankTraitFromStats")?.Invoke(null, new object[2] { pawn, compByType });
			AccessTools.DeclaredMethod(isekaiComponentType, "InvalidateStatCache")?.Invoke(compByType, null);
			RefreshStatsWindows(pawn, value);
		}
	}

	private static void RefreshStatsWindows(Pawn pawn, object statsObj)
	{
		foreach (Window window in Find.WindowStack.Windows)
		{
			if (windowStatsType.IsInstanceOfType(window) && statsWindowPawn(window) == pawn)
			{
				for (int i = 0; i < statAllocationFields.Length; i++)
				{
					statsWindowPending[i](window) = (int)statAllocationFields[i].GetValue(statsObj);
				}
				statsWindowPointsSpent(window) = 0;
			}
		}
	}

	private static void ITabFillTabPrefix(object __instance, ref bool __state)
	{
		if (!MP.IsInMultiplayer || !(Find.Selector.SingleSelectedThing is Pawn pawn))
		{
			return;
		}
		ThingComp compByType = GetCompByType(pawn, isekaiComponentType);
		object obj = ((compByType != null) ? isekaiCompStatsField.GetValue(compByType) : null);
		if (obj != null)
		{
			__state = true;
			MP.WatchBegin();
			ISyncField[] array = statSyncFields;
			for (int i = 0; i < array.Length; i++)
			{
				array[i].Watch(obj);
			}
			autoDistributeStatsSyncField?.Watch(compByType);
		}
	}

	private static void ITabFillTabPostfix(bool __state)
	{
		if (__state)
		{
			MP.WatchEnd();
		}
	}

	private static bool UnlockNodePrefix(string nodeId, Pawn pawn, ref bool __result)
	{
		if (!MP.IsInMultiplayer || _suppressUnlockPrefix || pawn == null)
		{
			return true;
		}
		SyncedUnlockNode(pawn, nodeId);
		__result = false;
		return false;
	}

	private static void SyncedUnlockNode(Pawn pawn, string nodeId)
	{
		ThingComp compByType = GetCompByType(pawn, isekaiComponentType);
		if (compByType == null)
		{
			Log.Warning("[IsekaiMP] SyncedUnlockNode: no IsekaiComponent on " + pawn.LabelShort);
			return;
		}
		object value = isekaiCompPassiveTreeField.GetValue(compByType);
		if (value == null)
		{
			Log.Warning("[IsekaiMP] SyncedUnlockNode: null passiveTree on " + pawn.LabelShort);
			return;
		}
		_suppressUnlockPrefix = true;
		try
		{
			AccessTools.DeclaredMethod(passiveTreeTrackerType, "Unlock").Invoke(value, new object[3] { nodeId, pawn, false });
		}
		finally
		{
			_suppressUnlockPrefix = false;
		}
	}

	private static bool TryUnlockChainPrefix(string targetNodeId, Pawn pawn, ref int __result, ref int pointsSpent, ref string failReason)
	{
		if (!MP.IsInMultiplayer || _suppressUnlockChainPrefix || pawn == null)
		{
			return true;
		}
		SyncedUnlockChain(pawn, targetNodeId);
		__result = 0;
		pointsSpent = 0;
		failReason = null;
		return false;
	}

	private static void SyncedUnlockChain(Pawn pawn, string targetNodeId)
	{
		ThingComp compByType = GetCompByType(pawn, isekaiComponentType);
		if (compByType == null)
		{
			return;
		}
		object value = isekaiCompPassiveTreeField.GetValue(compByType);
		if (value == null)
		{
			return;
		}
		_suppressUnlockPrefix = true;
		_suppressUnlockChainPrefix = true;
		try
		{
			object[] parameters = new object[4] { targetNodeId, pawn, 0, null };
			AccessTools.DeclaredMethod(passiveTreeTrackerType, "TryUnlockChain")?.Invoke(value, parameters);
		}
		finally
		{
			_suppressUnlockPrefix = false;
			_suppressUnlockChainPrefix = false;
		}
	}

	private static bool RespecPrefix(object __instance)
	{
		if (!MP.IsInMultiplayer || _suppressRespecPrefix)
		{
			return true;
		}
		Pawn pawn = FindPawnByComp((ThingComp c) => isekaiCompPassiveTreeField.GetValue(c) == __instance);
		if (pawn == null)
		{
			Log.Warning("[IsekaiMP] RespecPrefix: could not identify owning pawn — running locally (may desync!)");
			return true;
		}
		SyncedRespec(pawn);
		return false;
	}

	private static void SyncedRespec(Pawn pawn)
	{
		ThingComp compByType = GetCompByType(pawn, isekaiComponentType);
		if (compByType == null)
		{
			Log.Warning("[IsekaiMP] SyncedRespec: no IsekaiComponent on " + pawn.LabelShort);
			return;
		}
		object value = isekaiCompPassiveTreeField.GetValue(compByType);
		if (value == null)
		{
			Log.Warning("[IsekaiMP] SyncedRespec: null passiveTree on " + pawn.LabelShort);
			return;
		}
		_suppressRespecPrefix = true;
		try
		{
			AccessTools.DeclaredMethod(passiveTreeTrackerType, "Respec").Invoke(value, null);
		}
		finally
		{
			_suppressRespecPrefix = false;
		}
	}

	private static bool DevAddLevelPrefix(object __instance, int levels)
	{
		if (!MP.IsInMultiplayer || _suppressDevAddLevelPrefix)
		{
			return true;
		}
		Pawn pawn = FindPawnByComp((ThingComp c) => c == __instance);
		if (pawn == null)
		{
			Log.Warning("[IsekaiMP] DevAddLevelPrefix: could not identify owning pawn — running locally (may desync!)");
			return true;
		}
		SyncedDevAddLevel(pawn, levels);
		return false;
	}

	private static void SyncedDevAddLevel(Pawn pawn, int levels)
	{
		ThingComp compByType = GetCompByType(pawn, isekaiComponentType);
		if (compByType == null)
		{
			Log.Warning("[IsekaiMP] SyncedDevAddLevel: no IsekaiComponent on " + pawn.LabelShort);
			return;
		}
		_suppressDevAddLevelPrefix = true;
		try
		{
			AccessTools.DeclaredMethod(isekaiComponentType, "DevAddLevel").Invoke(compByType, new object[1] { levels });
		}
		finally
		{
			_suppressDevAddLevelPrefix = false;
		}
	}

	private static Pawn FindPawnByStatAllocation(object statsAllocation)
	{
		foreach (Map map in Find.Maps)
		{
			foreach (Pawn item in map.mapPawns.AllPawnsSpawned)
			{
				if (OwnsStatAllocation(item, statsAllocation))
				{
					return item;
				}
			}
		}
		if (Find.WorldPawns != null)
		{
			foreach (Pawn item2 in Find.WorldPawns.AllPawnsAlive)
			{
				if (OwnsStatAllocation(item2, statsAllocation))
				{
					return item2;
				}
			}
		}
		return null;
	}

	private static bool OwnsStatAllocation(Pawn pawn, object statsAllocation)
	{
		ThingComp compByType = GetCompByType(pawn, isekaiComponentType);
		if (compByType != null && isekaiCompStatsField.GetValue(compByType) == statsAllocation)
		{
			return true;
		}
		if (mobRankComponentType != null && mobRankStatsField != null)
		{
			ThingComp compByType2 = GetCompByType(pawn, mobRankComponentType);
			if (compByType2 != null && mobRankStatsField.GetValue(compByType2) == statsAllocation)
			{
				return true;
			}
		}
		return false;
	}

	private static void SyncIsekaiStatAllocation(SyncWorker sync, ref object statsAllocation)
	{
		if (sync.isWriting)
		{
			object statsAllocation2 = statsAllocation;
			sync.Write(FindPawnByStatAllocation(statsAllocation2));
			return;
		}
		Pawn pawn = sync.Read<Pawn>();
		if (pawn == null)
		{
			return;
		}
		ThingComp compByType = GetCompByType(pawn, isekaiComponentType);
		if (compByType != null)
		{
			statsAllocation = isekaiCompStatsField.GetValue(compByType);
		}
		else if (mobRankComponentType != null && mobRankStatsField != null)
		{
			ThingComp compByType2 = GetCompByType(pawn, mobRankComponentType);
			if (compByType2 != null)
			{
				statsAllocation = mobRankStatsField.GetValue(compByType2);
			}
		}
	}

	private static bool CreatureApplyChangesPrefix(object __instance)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		Pawn pawn = creatureWindowPawn(__instance);
		if (pawn == null)
		{
			return true;
		}
		int[] array = new int[StatAllocationFieldNames.Length];
		for (int i = 0; i < array.Length; i++)
		{
			array[i] = creatureWindowPending[i](__instance);
		}
		int num = 0;
		bool flag = Prefs.DevMode && DebugSettings.godMode;
		if (!flag)
		{
			ThingComp compByType = GetCompByType(pawn, mobRankComponentType);
			if (compByType != null)
			{
				object value = mobRankStatsField.GetValue(compByType);
				for (int j = 0; j < array.Length; j++)
				{
					num += array[j] - (int)statAllocationFields[j].GetValue(value);
				}
			}
		}
		SyncedApplyCreatureStats(pawn, array, num, flag);
		return false;
	}

	private static void SyncedApplyCreatureStats(Pawn pawn, int[] statValues, int pointsSpent, bool godMode)
	{
		ThingComp compByType = GetCompByType(pawn, mobRankComponentType);
		if (compByType == null)
		{
			return;
		}
		object value = mobRankStatsField.GetValue(compByType);
		if (value != null)
		{
			for (int i = 0; i < statAllocationFields.Length; i++)
			{
				statAllocationFields[i].SetValue(value, statValues[i]);
			}
			if (!godMode && pointsSpent > 0)
			{
				int num = (int)statAllocationAvailablePoints.GetValue(value);
				statAllocationAvailablePoints.SetValue(value, num - pointsSpent);
			}
			RefreshCreatureStatsWindows(pawn, value);
		}
	}

	private static void RefreshCreatureStatsWindows(Pawn pawn, object statsObj)
	{
		foreach (Window window in Find.WindowStack.Windows)
		{
			if (windowCreatureStatsType.IsInstanceOfType(window) && creatureWindowPawn(window) == pawn)
			{
				for (int i = 0; i < statAllocationFields.Length; i++)
				{
					creatureWindowPending[i](window) = (int)statAllocationFields[i].GetValue(statsObj);
				}
				creatureWindowPointsSpent(window) = 0;
			}
		}
	}

	private static void ITabCreatureFillTabPrefix(object __instance, ref bool __state)
	{
		if (!MP.IsInMultiplayer || !(Find.Selector.SingleSelectedThing is Pawn pawn))
		{
			return;
		}
		ThingComp compByType = GetCompByType(pawn, mobRankComponentType);
		object obj = ((compByType != null) ? mobRankStatsField.GetValue(compByType) : null);
		if (obj != null)
		{
			__state = true;
			MP.WatchBegin();
			ISyncField[] array = statSyncFields;
			for (int i = 0; i < array.Length; i++)
			{
				array[i].Watch(obj);
			}
		}
	}

	private static IEnumerable<Gizmo> GetGizmosPostfix(IEnumerable<Gizmo> __result, Pawn pawn)
	{
		foreach (Gizmo item in __result)
		{
			if (item is Command_Action command_Action && command_Action.defaultLabel == "Isekai_AuraFeel_Label".Translate())
			{
				command_Action.action = () =>
				{
					SyncedActivateAuraPressure(pawn);
				};
			}
			yield return item;
		}
	}

	private static void SyncedActivateAuraPressure(Pawn pawn)
	{
		if (pawn != null)
		{
			ThingComp compByType = GetCompByType(pawn, isekaiComponentType);
			if (compByType != null)
			{
				AccessTools.DeclaredMethod(auraFeelType, "Activate")?.Invoke(null, new object[2] { pawn, compByType });
			}
		}
	}

	private static bool AttemptRefinementPrefix(Thing item, Map map, Pawn crafter, ref object __result)
	{
		if (!MP.IsInMultiplayer || _suppressRefinePrefix || !IsPlayerForgeUiInteraction())
		{
			return true;
		}
		if (Prefs.DevMode && DebugSettings.godMode)
		{
			SyncedAttemptRefinementGodMode(item, map);
		}
		else
		{
			SyncedAttemptRefinement(item, map);
		}
		if (refineResultType != null)
		{
			__result = Enum.ToObject(refineResultType, 1);
		}
		return false;
	}

	private static void SyncedAttemptRefinement(Thing item, Map map)
	{
		ExecuteForgeRefinement(item, map, godMode: false);
	}

	private static void ExecuteForgeRefinement(Thing item, Map map, bool godMode)
	{
		if (item == null || item.Destroyed || map == null || item.MapHeld != map)
		{
			return;
		}
		bool? flag = synchronizedForgeGodMode;
		synchronizedForgeGodMode = godMode;
		_suppressRefinePrefix = true;
		try
		{
			AccessTools.DeclaredMethod(forgeUtilityType, "AttemptRefinement")?.Invoke(null, new object[3] { item, map, null });
		}
		finally
		{
			_suppressRefinePrefix = false;
			synchronizedForgeGodMode = flag;
		}
	}

	private static bool RepairItemPrefix(Thing item, Map map, ref bool __result)
	{
		if (!MP.IsInMultiplayer || _suppressRepairPrefix || !IsPlayerForgeUiInteraction())
		{
			return true;
		}
		if (Prefs.DevMode && DebugSettings.godMode)
		{
			SyncedRepairItemGodMode(item, map);
		}
		else
		{
			SyncedRepairItem(item, map);
		}
		__result = false;
		return false;
	}

	private static void SyncedRepairItem(Thing item, Map map)
	{
		ExecuteForgeRepair(item, map, godMode: false);
	}

	private static void ExecuteForgeRepair(Thing item, Map map, bool godMode)
	{
		if (item == null || item.Destroyed || map == null || item.MapHeld != map)
		{
			return;
		}
		bool? flag = synchronizedForgeGodMode;
		synchronizedForgeGodMode = godMode;
		_suppressRepairPrefix = true;
		try
		{
			AccessTools.DeclaredMethod(forgeUtilityType, "RepairItem")?.Invoke(null, new object[2] { item, map });
		}
		finally
		{
			_suppressRepairPrefix = false;
			synchronizedForgeGodMode = flag;
		}
	}

	private static bool TryAddRunePrefix(ThingComp __instance, Def rune, int rank, ref bool __result)
	{
		if (!MP.IsInMultiplayer || _suppressTryAddRune || !IsPlayerForgeUiInteraction())
		{
			return true;
		}
		if (rune != null && __instance?.parent != null)
		{
			if (Prefs.DevMode && DebugSettings.godMode)
			{
				SyncedApplyRuneGodMode(__instance.parent, rune.defName, rank);
			}
			else
			{
				SyncedApplyRune(__instance.parent, rune.defName, rank);
			}
			__result = false;
			return false;
		}
		return true;
	}

	private static void SyncedApplyRune(Thing equipment, string runeDefName, int rank)
	{
		ExecuteApplyRune(equipment, runeDefName, rank, godMode: false);
	}

	private static void ExecuteApplyRune(Thing equipment, string runeDefName, int rank, bool godMode)
	{
		if (equipment == null || equipment.Destroyed || string.IsNullOrEmpty(runeDefName))
		{
			return;
		}
		ThingComp thingCompByType = GetThingCompByType(equipment, compForgeEnhancementType);
		if (thingCompByType == null)
		{
			return;
		}
		Type type = AccessTools.TypeByName("IsekaiLeveling.Forge.RuneDef");
		if (type == null)
		{
			return;
		}
		Def def = GenDefDatabase.GetDef(type, runeDefName, errorOnFail: false);
		if (def == null)
		{
			return;
		}
		FieldInfo fieldInfo = AccessTools.Field(type, "maxRank");
		if (fieldInfo != null)
		{
			rank = Mathf.Clamp(rank, 1, (int)fieldInfo.GetValue(def));
		}
		Thing thing = null;
		if (!godMode)
		{
			string text = (runeDefName.StartsWith("Isekai_RuneDef_") ? runeDefName.Substring("Isekai_RuneDef_".Length) : runeDefName);
			string text2 = "Isekai_Rune_" + text + rank switch
			{
				5 => "_V", 
				4 => "_IV", 
				3 => "_III", 
				2 => "_II", 
				_ => "", 
			};
			Map mapHeld = equipment.MapHeld;
			if (mapHeld?.listerThings?.AllThings == null)
			{
				return;
			}
			foreach (Thing allThing in mapHeld.listerThings.AllThings)
			{
				if (allThing != null && !allThing.Destroyed && allThing.Spawned && allThing.stackCount > 0 && allThing.def?.defName == text2 && (thing == null || allThing.thingIDNumber < thing.thingIDNumber))
				{
					thing = allThing;
				}
			}
			if (thing == null)
			{
				return;
			}
		}
		_suppressTryAddRune = true;
		try
		{
			MethodInfo methodInfo = AccessTools.DeclaredMethod(compForgeEnhancementType, "TryAddRune");
			if (!(methodInfo == null) && (bool)methodInfo.Invoke(thingCompByType, new object[2] { def, rank }) && thing != null)
			{
				if (thing.stackCount > 1)
				{
					thing.stackCount--;
				}
				else
				{
					thing.Destroy();
				}
			}
		}
		finally
		{
			_suppressTryAddRune = false;
		}
	}

	private static bool RemoveRuneAtPrefix(ThingComp __instance, int index, ref bool __result)
	{
		if (!MP.IsInMultiplayer || _suppressRemoveRune || !IsPlayerForgeUiInteraction())
		{
			return true;
		}
		SyncedRemoveRune(__instance.parent, index);
		__result = false;
		return false;
	}

	private static void SyncedRemoveRune(Thing equipment, int index)
	{
		if (equipment == null)
		{
			return;
		}
		ThingComp thingCompByType = GetThingCompByType(equipment, compForgeEnhancementType);
		if (thingCompByType == null)
		{
			return;
		}
		_suppressRemoveRune = true;
		try
		{
			AccessTools.DeclaredMethod(compForgeEnhancementType, "RemoveRuneAt").Invoke(thingCompByType, new object[1] { index });
		}
		finally
		{
			_suppressRemoveRune = false;
		}
	}

	private static void SetupAdditionalInteractions()
	{
		pendingBulkAbsorbField = AccessTools.Field(manaCoreCompType, "pendingBulkAbsorb");
		if (AccessTools.Field(isekaiComponentType, "autoDistributeStats") != null)
		{
			autoDistributeStatsSyncField = MP.RegisterSyncField(isekaiComponentType, "autoDistributeStats");
			PatchAndLog(isekaiComponentType, "AutoDistributeByClass", "AutoDistributePrefix");
			MP.RegisterSyncMethod(typeof(IsekaiRPGCompat), "SyncedAutoDistribute");
		}
		PatchAndLog(manaCoreCompType, "CompFloatMenuOptions", "BulkMenuPrefix");
		PatchAndLog(manaCoreCompType, "DoEffect", "ManaCoreEffectPrefix");
		MP.RegisterSyncMethod(typeof(IsekaiRPGCompat), "SyncedOrderBulkAbsorb").SetContext(SyncContext.QueueOrder_Down);
		Type type = AccessTools.TypeByName("IsekaiLeveling.UI.Window_Mastery");
		if (type != null)
		{
			PatchAndLog(type, "DrawDevButtons", "MasteryDevButtonsPrefix");
		}
		auraDisplayModeField = AccessTools.Field(isekaiComponentType, "auraDisplayMode");
		auraConstellationField = AccessTools.Field(isekaiComponentType, "auraConstellation");
		if (auraDisplayModeField != null && auraConstellationField != null && AccessTools.DeclaredMethod(iTabType, "OpenAuraMenu") != null)
		{
			PatchAndLog(iTabType, "OpenAuraMenu", "OpenAuraMenuPrefix");
			MP.RegisterSyncMethod(typeof(IsekaiRPGCompat), "SyncedSetAuraDisplay");
		}
		Type type2 = AccessTools.TypeByName("IsekaiLeveling.CharacterForge.Window_CharacterForge");
		characterForgeType = AccessTools.TypeByName("IsekaiLeveling.CharacterForge.IsekaiCharacterForge");
		characterSpecType = AccessTools.TypeByName("IsekaiLeveling.CharacterForge.IsekaiCharacterSpec");
		if (type2 != null && characterForgeType != null && characterSpecType != null)
		{
			characterWindowPawn = AccessTools.Field(type2, "pawn");
			characterWindowLock = AccessTools.Field(type2, "lockOnApply");
			characterBuildSpec = AccessTools.DeclaredMethod(type2, "BuildStagedSpec");
			PatchAndLog(type2, "DoApply", "CharacterForgeApplyPrefix");
			MP.RegisterSyncMethod(typeof(IsekaiRPGCompat), "SyncedApplyCharacterForge").SetDebugOnly();
		}
		if (forgeUtilityType != null)
		{
			PatchAndLog(forgeUtilityType, "AttemptRefinement", null, null, "ForgeGodModeTranspiler");
			PatchAndLog(forgeUtilityType, "RepairItem", null, null, "ForgeGodModeTranspiler");
			MP.RegisterSyncMethod(typeof(IsekaiRPGCompat), "SyncedAttemptRefinementGodMode").SetDebugOnly();
			MP.RegisterSyncMethod(typeof(IsekaiRPGCompat), "SyncedRepairItemGodMode").SetDebugOnly();
		}
		if (compForgeEnhancementType != null)
		{
			MP.RegisterSyncMethod(typeof(IsekaiRPGCompat), "SyncedApplyRuneGodMode").SetDebugOnly();
		}
	}

	private static bool BulkMenuPrefix(object __instance, Pawn selPawn, ref IEnumerable<FloatMenuOption> __result)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		IEnumerable<FloatMenuOption> options = (IEnumerable<FloatMenuOption>)AccessTools.DeclaredMethod(manaCoreCompType, "GetBulkAbsorbOptions").Invoke(__instance, new object[1] { selPawn });
		__result = BulkOptions(options, selPawn, ((ThingComp)__instance).parent);
		return false;
	}

	private static IEnumerable<FloatMenuOption> BulkOptions(IEnumerable<FloatMenuOption> options, Pawn pawn, Thing core)
	{
		foreach (FloatMenuOption option in options)
		{
			if (option.action != null)
			{
				option.action = () =>
				{
					SyncedOrderBulkAbsorb(pawn, core, core.stackCount);
				};
			}
			yield return option;
		}
	}

	private static void SyncedOrderBulkAbsorb(Pawn pawn, Thing core, int count)
	{
		if (pawn != null && !pawn.Destroyed && !pawn.Dead && pawn.Spawned && pawn.jobs != null && core != null && !core.Destroyed && core.Spawned && pawn.Map == core.Map && count >= 1)
		{
			ThingComp thingCompByType = GetThingCompByType(core, manaCoreCompType);
			if (thingCompByType != null && AccessTools.DeclaredMethod(manaCoreCompType, "CanBeUsedBy").Invoke(thingCompByType, new object[1] { pawn }) is AcceptanceReport { Accepted: not false } && pawn.CanReach(core, PathEndMode.Touch, Danger.Deadly))
			{
				Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("UseItem"), core);
				job.count = Math.Min(count, core.stackCount);
				pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
			}
		}
	}

	private static void ManaCoreEffectPrefix(ThingComp __instance, Pawn usedBy)
	{
		if (MP.IsInMultiplayer)
		{
			Job job = usedBy?.CurJob;
			int num = ((job?.def?.defName == "UseItem" && job.targetA.Thing == __instance.parent) ? Math.Max(0, job.count) : 0);
			pendingBulkAbsorbField.SetValue(__instance, num);
		}
	}

	private static bool MasteryDevButtonsPrefix(float y, ref float __result)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		__result = y;
		return false;
	}

	private static bool AutoDistributePrefix(ThingComp __instance)
	{
		if (!MP.IsInMultiplayer || !MP.InInterface || MP.IsExecutingSyncCommand || suppressAutoDistributePrefix || !UnityData.IsInMainThread || Event.current == null)
		{
			return true;
		}
		if (__instance.parent is Pawn pawn)
		{
			SyncedAutoDistribute(pawn);
		}
		return false;
	}

	private static void SyncedAutoDistribute(Pawn pawn)
	{
		ThingComp compByType = GetCompByType(pawn, isekaiComponentType);
		if (compByType == null || pawn.Destroyed)
		{
			return;
		}
		suppressAutoDistributePrefix = true;
		try
		{
			AccessTools.DeclaredMethod(isekaiComponentType, "AutoDistributeByClass").Invoke(compByType, null);
			AccessTools.DeclaredMethod(isekaiComponentType, "InvalidateStatCache")?.Invoke(compByType, null);
		}
		finally
		{
			suppressAutoDistributePrefix = false;
		}
	}

	private static bool OpenAuraMenuPrefix(object comp)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		Pawn pawn = (comp as ThingComp)?.parent as Pawn;
		if (pawn == null)
		{
			return false;
		}
		List<FloatMenuOption> list = new List<FloatMenuOption>();
		object value = isekaiCompPassiveTreeField.GetValue(comp);
		if (value != null && AccessTools.DeclaredMethod(passiveTreeTrackerType, "GetEnteredTrees")?.Invoke(value, null) is IEnumerable enumerable)
		{
			foreach (object item in enumerable)
			{
				string name = AccessTools.Field(item.GetType(), "treeClass")?.GetValue(item) as string;
				if (!string.IsNullOrEmpty(name))
				{
					list.Add(new FloatMenuOption(name, () =>
					{
						SyncedSetAuraDisplay(pawn, 0, name);
					}));
				}
			}
		}
		list.Add(new FloatMenuOption("Isekai_AuraDisplay_DefaultFlame".Translate(), () =>
		{
			SyncedSetAuraDisplay(pawn, 1, null);
		}));
		list.Add(new FloatMenuOption("Isekai_AuraDisplay_None".Translate(), () =>
		{
			SyncedSetAuraDisplay(pawn, 2, null);
		}));
		Find.WindowStack.Add(new FloatMenu(list));
		return false;
	}

	private static void SyncedSetAuraDisplay(Pawn pawn, int mode, string constellation)
	{
		ThingComp compByType = GetCompByType(pawn, isekaiComponentType);
		if (compByType == null || pawn.Destroyed)
		{
			return;
		}
		switch (mode)
		{
		default:
			return;
		case 0:
		{
			object value = isekaiCompPassiveTreeField.GetValue(compByType);
			if (!string.IsNullOrEmpty(constellation) && value != null)
			{
				object obj = AccessTools.DeclaredMethod(passiveTreeTrackerType, "HasEnteredTreeByName")?.Invoke(value, new object[1] { constellation });
				if (!(obj is bool) || !(bool)obj)
				{
					return;
				}
				auraConstellationField.SetValue(compByType, constellation);
				break;
			}
			return;
		}
		case 1:
		case 2:
			break;
		}
		auraDisplayModeField.SetValue(compByType, Enum.ToObject(auraDisplayModeField.FieldType, mode));
	}

	private static bool CharacterForgeApplyPrefix(object __instance)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		Pawn pawn = characterWindowPawn.GetValue(__instance) as Pawn;
		object obj = characterBuildSpec.Invoke(__instance, null);
		if (pawn == null || obj == null)
		{
			return false;
		}
		int[] array = new int[StatAllocationFieldNames.Length];
		for (int i = 0; i < array.Length; i++)
		{
			array[i] = ((int?)AccessTools.Field(characterSpecType, StatAllocationFieldNames[i]).GetValue(obj)).GetValueOrDefault();
		}
		int level = ((int?)AccessTools.Field(characterSpecType, "level").GetValue(obj)) ?? (-1);
		int silver = ((int?)AccessTools.Field(characterSpecType, "inventorySilver").GetValue(obj)) ?? (-1);
		string tree = (string)AccessTools.Field(characterSpecType, "constellation").GetValue(obj);
		SyncedApplyCharacterForge(pawn, array, level, tree, silver, (bool)characterWindowLock.GetValue(__instance));
		return false;
	}

	private static void SyncedApplyCharacterForge(Pawn pawn, int[] stats, int level, string tree, int silver, bool locked)
	{
		if (pawn != null && !pawn.Destroyed && stats != null && stats.Length == StatAllocationFieldNames.Length)
		{
			object obj = Activator.CreateInstance(characterSpecType);
			for (int i = 0; i < stats.Length; i++)
			{
				AccessTools.Field(characterSpecType, StatAllocationFieldNames[i]).SetValue(obj, Math.Max(0, stats[i]));
			}
			AccessTools.Field(characterSpecType, "level").SetValue(obj, (level >= 0) ? new int?(level) : ((int?)null));
			AccessTools.Field(characterSpecType, "inventorySilver").SetValue(obj, (silver >= 0) ? new int?(silver) : ((int?)null));
			AccessTools.Field(characterSpecType, "constellation").SetValue(obj, tree);
			AccessTools.DeclaredMethod(characterForgeType, "Apply").Invoke(null, new object[3] { pawn, obj, locked });
			if (!locked)
			{
				AccessTools.DeclaredMethod(characterForgeType, "SetLocked").Invoke(null, new object[2] { pawn, false });
			}
		}
	}

	private static IEnumerable<CodeInstruction> ForgeGodModeTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo devMode = AccessTools.PropertyGetter(typeof(Prefs), "DevMode");
		FieldInfo godMode = AccessTools.Field(typeof(DebugSettings), "godMode");
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(devMode))
			{
				instruction.opcode = OpCodes.Call;
				instruction.operand = AccessTools.DeclaredMethod(typeof(IsekaiRPGCompat), "ForgeDevMode");
			}
			else if (instruction.LoadsField(godMode))
			{
				instruction.opcode = OpCodes.Call;
				instruction.operand = AccessTools.DeclaredMethod(typeof(IsekaiRPGCompat), "ForgeGodMode");
			}
			yield return instruction;
		}
	}

	private static bool ForgeDevMode()
	{
		return synchronizedForgeGodMode ?? Prefs.DevMode;
	}

	private static bool ForgeGodMode()
	{
		return synchronizedForgeGodMode ?? DebugSettings.godMode;
	}

	private static void SyncedAttemptRefinementGodMode(Thing item, Map map)
	{
		ExecuteForgeRefinement(item, map, godMode: true);
	}

	private static void SyncedRepairItemGodMode(Thing item, Map map)
	{
		ExecuteForgeRepair(item, map, godMode: true);
	}

	private static void SyncedApplyRuneGodMode(Thing equipment, string runeDefName, int rank)
	{
		ExecuteApplyRune(equipment, runeDefName, rank, godMode: true);
	}
}
