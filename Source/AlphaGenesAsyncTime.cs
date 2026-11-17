using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using Verse;
using Verse.AI;

namespace Multiplayer.Compat;

internal static class AlphaGenesAsyncTime
{
	private sealed class WaterState
	{
		internal bool ValidNegative;
	}

	private sealed class PocketTransfer
	{
		internal Thing Item;

		internal object Capture;
	}

	private static AccessTools.FieldRef<object, int> metalConsumed;

	private static AccessTools.FieldRef<object, int> waterSeen;

	private static FieldInfo waterSeenField;

	private static readonly ConditionalWeakTable<MentalState, WaterState> waterStates = new ConditionalWeakTable<MentalState, WaterState>();

	private static Func<Thing, object> captureOwnedThing;

	private static Func<object, Map, bool> moveOwnedThing;

	[ThreadStatic]
	private static PocketTransfer pocketTransfer;

	internal static void Install()
	{
		Type type = AccessTools.TypeByName("AlphaGenes.Gene_Resource_Metal");
		Type type2 = AccessTools.TypeByName("AlphaGenes.MentalState_Thalassophobia");
		metalConsumed = AccessTools.FieldRefAccess<int>(type, "lastConsumed");
		waterSeen = AccessTools.FieldRefAccess<int>(type2, "lastWaterSeenTick");
		waterSeenField = AccessTools.DeclaredField(type2, "lastWaterSeenTick");
		PatchingUtilities.RegisterTimestampFields(type, (object obj, int offset) =>
		{
			metalConsumed(obj) = metalConsumed(obj) + offset;
		});
		PatchingUtilities.RegisterTimestampFields(type2, FixWater);
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type2, "MentalStateTick"), null, null, new HarmonyMethod(typeof(AlphaGenesAsyncTime), "WaterMarkers"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type2, "ExposeData"), null, new HarmonyMethod(typeof(AlphaGenesAsyncTime), "ExposeWater"));
		captureOwnedThing = (Func<Thing, object>)AccessTools.DeclaredMethod("Multiplayer.Client.Compatibility.OwnedThingTransfer:Capture").CreateDelegate(typeof(Func<Thing, object>));
		moveOwnedThing = (Func<object, Map, bool>)AccessTools.DeclaredMethod("Multiplayer.Client.Compatibility.OwnedThingTransfer:Move").CreateDelegate(typeof(Func<object, Map, bool>));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod("AlphaGenes.CompTeleportItemsPocketPlane:Apply"), new HarmonyMethod(typeof(AlphaGenesAsyncTime), "BeforePocket"), null, new HarmonyMethod(typeof(AlphaGenesAsyncTime), "PocketSpawn"), new HarmonyMethod(typeof(AlphaGenesAsyncTime), "AfterPocket"));
	}

	private static void FixWater(object obj, int offset)
	{
		MentalState mentalState = (MentalState)obj;
		WaterState value = waterStates.GetValue(mentalState, (MentalState _) => new WaterState());
		ref int reference = ref waterSeen(mentalState);
		if (reference >= 0 || value.ValidNegative)
		{
			reference += offset;
			value.ValidNegative = reference < 0;
		}
	}

	private static int WaterMarker(MentalState owner)
	{
		int num = waterSeen(owner);
		if (num >= 0 || !waterStates.TryGetValue(owner, out var value) || !value.ValidNegative)
		{
			return num;
		}
		return 0;
	}

	private static void RecordWater(MentalState owner, int tick)
	{
		waterSeen(owner) = tick;
		if (waterStates.TryGetValue(owner, out var value))
		{
			value.ValidNegative = false;
		}
	}

	private static void ExposeWater(MentalState __instance)
	{
		WaterState value = waterStates.GetValue(__instance, (MentalState _) => new WaterState());
		if (Scribe.mode == LoadSaveMode.Saving && waterSeen(__instance) >= 0)
		{
			value.ValidNegative = false;
		}
		Scribe_Values.Look(ref value.ValidNegative, "mpAlphaGenesWaterSeenTickIsValidNegative", defaultValue: false);
	}

	private static IEnumerable<CodeInstruction> WaterMarkers(IEnumerable<CodeInstruction> instructions)
	{
		List<CodeInstruction> list = instructions.ToList();
		int num = 0;
		int num2 = 0;
		for (int i = 0; i < list.Count; i++)
		{
			if (i + 1 < list.Count && list[i].LoadsField(waterSeenField) && list[i + 1].LoadsConstant(0L))
			{
				list[i].opcode = OpCodes.Call;
				list[i].operand = AccessTools.Method(typeof(AlphaGenesAsyncTime), "WaterMarker");
				num++;
			}
			if (list[i].StoresField(waterSeenField))
			{
				list[i].opcode = OpCodes.Call;
				list[i].operand = AccessTools.Method(typeof(AlphaGenesAsyncTime), "RecordWater");
				num2++;
			}
		}
		if (num != 2 || num2 != 1)
		{
			throw new InvalidOperationException($"Alpha Genes water timestamp IL changed: {num}/{num2}");
		}
		return list;
	}

	private static void BeforePocket(LocalTargetInfo target, out PocketTransfer __state)
	{
		__state = pocketTransfer;
		if (MP.IsInMultiplayer)
		{
			Thing thing = target.Thing;
			pocketTransfer = new PocketTransfer
			{
				Item = thing,
				Capture = captureOwnedThing(thing)
			};
		}
	}

	private static Exception AfterPocket(Exception __exception, PocketTransfer __state)
	{
		pocketTransfer = __state;
		return __exception;
	}

	private static Thing SpawnPocketItem(Thing thing, IntVec3 cell, Map map, WipeMode mode)
	{
		PocketTransfer pocketTransfer = AlphaGenesAsyncTime.pocketTransfer;
		if (MP.IsInMultiplayer && pocketTransfer != null && thing == pocketTransfer.Item && !thing.Spawned)
		{
			moveOwnedThing(pocketTransfer.Capture, map);
		}
		return GenSpawn.Spawn(thing, cell, map, mode);
	}

	private static IEnumerable<CodeInstruction> PocketSpawn(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo original = AccessTools.Method(typeof(GenSpawn), "Spawn", new Type[4]
		{
			typeof(Thing),
			typeof(IntVec3),
			typeof(Map),
			typeof(WipeMode)
		});
		int matches = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(original))
			{
				instruction.opcode = OpCodes.Call;
				instruction.operand = AccessTools.Method(typeof(AlphaGenesAsyncTime), "SpawnPocketItem");
				matches++;
			}
			yield return instruction;
		}
		if (matches != 1)
		{
			throw new InvalidOperationException($"Alpha Genes pocket item spawn IL changed: {matches}");
		}
	}
}
