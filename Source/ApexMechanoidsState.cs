using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace Multiplayer.Compat;

internal static class ApexMechanoidsState
{
	private static Type spreadType;

	private static Type frostType;

	private static FieldInfo shieldMemory;

	private static FieldInfo shieldCleanupTick;

	private static FieldInfo duelMemory;

	private static Type shieldEntryType;

	private static bool initialized;

	private static bool installed;

	internal static bool HasShieldMemory
	{
		get
		{
			Initialize();
			if (shieldMemory != null && shieldCleanupTick != null)
			{
				return shieldEntryType != null;
			}
			return false;
		}
	}

	private static void Initialize()
	{
		if (!initialized)
		{
			spreadType = AccessTools.TypeByName("ApexMechanoids.GasSpreadManager+SpreadJob");
			frostType = AccessTools.TypeByName("ApexMechanoids.CompFrostivusFoodPreservation");
			Type type = AccessTools.TypeByName("ApexMechanoids.JobGiver_AITinkerCombat");
			if (type != null)
			{
				shieldMemory = AccessTools.Field(type, "shieldTargetMemory");
				shieldCleanupTick = AccessTools.Field(type, "lastShieldTargetMemoryCleanupTick");
				FieldInfo fieldInfo = shieldMemory;
				shieldEntryType = (((object)fieldInfo != null) ? fieldInfo.FieldType.GetGenericArguments()[1] : null);
			}
			Type type2 = AccessTools.TypeByName("ApexMechanoids.HarmonyPatches.SearchAndDestroyDuelStateMemory");
			if (type2 != null)
			{
				duelMemory = AccessTools.Field(type2, "pendingRestoreStates");
			}
			initialized = true;
		}
	}

	internal static void Install()
	{
		Initialize();
		if (!installed && !(frostType == null))
		{
			MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(MapComponent), "ExposeData"), null, new HarmonyMethod(typeof(ApexMechanoidsState), "ExposeSpread"));
			MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(ThingComp), "PostExposeData"), null, new HarmonyMethod(typeof(ApexMechanoidsState), "ExposeFrostScan"));
			if (duelMemory != null)
			{
				MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(Pawn), "ExposeData"), null, new HarmonyMethod(typeof(ApexMechanoidsState), "ExposeDuelRestore"));
			}
			installed = true;
		}
	}

	private static void ExposeSpread(MapComponent __instance)
	{
		if (!(__instance.GetType() != spreadType))
		{
			Definition<ThingDef>(__instance, "gasDef");
			Value<IntVec3>(__instance, "center");
			Value(__instance, "maxRadius", 0f);
			Value(__instance, "spreadIntervalTicks", 0);
			Value(__instance, "currentRadius", 0f);
			Value(__instance, "ticksUntilNextSpread", 0);
		}
	}

	private static void ExposeFrostScan(ThingComp __instance)
	{
		if (__instance.GetType() == frostType)
		{
			Value(__instance, "nextRescueScanTick", int.MinValue);
		}
	}

	private static void ExposeDuelRestore(Pawn __instance)
	{
		if (!(duelMemory?.GetValue(null) is IDictionary dictionary))
		{
			return;
		}
		bool value = dictionary.Contains(__instance);
		bool value2 = value && (bool)dictionary[__instance];
		Scribe_Values.Look(ref value, "mpApexDuelRestorePresent", defaultValue: false);
		Scribe_Values.Look(ref value2, "mpApexDuelRestoreEnabled", defaultValue: false);
		if (Scribe.mode == LoadSaveMode.LoadingVars)
		{
			dictionary.Remove(__instance);
			if (value)
			{
				dictionary[__instance] = value2;
			}
		}
	}

	internal static void ResetGameState()
	{
		Initialize();
		(shieldMemory?.GetValue(null) as IDictionary)?.Clear();
		shieldCleanupTick?.SetValue(null, -99999);
		(duelMemory?.GetValue(null) as IDictionary)?.Clear();
	}

	internal static void SnapshotShieldMemory(out List<int> keys, out List<Pawn> targets, out List<int> ticks, out int cleanupTick)
	{
		IDictionary memory = (IDictionary)shieldMemory.GetValue(null);
		keys = (from int key in memory.Keys
			orderby key
			select key).ToList();
		FieldInfo targetField = AccessTools.Field(shieldEntryType, "target");
		FieldInfo tickField = AccessTools.Field(shieldEntryType, "startTick");
		targets = keys.Select((int key) => (Pawn)targetField.GetValue(memory[key])).ToList();
		ticks = keys.Select((int key) => (int)tickField.GetValue(memory[key])).ToList();
		cleanupTick = (int)shieldCleanupTick.GetValue(null);
	}

	internal static void RestoreShieldMemory(List<int> keys, List<Pawn> targets, List<int> ticks, int cleanupTick)
	{
		IDictionary dictionary = (IDictionary)shieldMemory.GetValue(null);
		dictionary.Clear();
		if (keys != null && targets != null && ticks != null)
		{
			for (int i = 0; i < Math.Min(keys.Count, Math.Min(targets.Count, ticks.Count)); i++)
			{
				if (targets[i] != null)
				{
					dictionary[keys[i]] = Activator.CreateInstance(shieldEntryType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[2]
					{
						targets[i],
						ticks[i]
					}, null);
				}
			}
		}
		shieldCleanupTick.SetValue(null, cleanupTick);
	}

	private static void Value<T>(object owner, string name, T defaultValue = default(T))
	{
		FieldInfo fieldInfo = AccessTools.Field(owner.GetType(), name);
		T value = (T)fieldInfo.GetValue(owner);
		Scribe_Values.Look(ref value, "mpApex_" + name, defaultValue);
		fieldInfo.SetValue(owner, value);
	}

	private static void Definition<T>(object owner, string name) where T : Def, new()
	{
		FieldInfo fieldInfo = AccessTools.Field(owner.GetType(), name);
		T value = (T)fieldInfo.GetValue(owner);
		Scribe_Defs.Look(ref value, "mpApex_" + name);
		fieldInfo.SetValue(owner, value);
	}
}
