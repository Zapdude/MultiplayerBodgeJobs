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

[MpCompatFor("vanillaexpanded.gravship2")]
public class VanillaGravshipExpanded2
{
	private static FieldInfo incomingThreat;

	private static FieldInfo threatDef;

	private static FieldInfo tributeLeader;

	private static FieldInfo tributeAmount;

	private static readonly Dictionary<MethodBase, int> worldClockReads = new Dictionary<MethodBase, int>();

	public VanillaGravshipExpanded2(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
	}

	private static void LatePatch()
	{
		VanillaGravshipExpanded2Workshop.Install();
		Type type = AccessTools.TypeByName("VanillaGravshipExpanded2.WorldComponent_GravshipCombat");
		Type type2 = AccessTools.TypeByName("VanillaGravshipExpanded2.CompLaunchable_Warpod");
		Type type3 = AccessTools.TypeByName("VanillaGravshipExpanded2.CompVacuumWarningLight");
		Type type4 = AccessTools.TypeByName("VanillaGravshipExpanded2.CompEscapePod");
		MP.RegisterSyncWorker<object>(SyncTurretLinker, AccessTools.TypeByName("VanillaGravshipExpanded.ITurretLinker"));
		Type type5 = AccessTools.TypeByName("VanillaGravshipExpanded2.Apparel_GravshipBombardmentTargeter");
		PatchAsyncClocks(type5, type4);
		Serializer<Thing, int> serializer = Serializer.New((Thing thing) => thing?.thingIDNumber ?? (-1), (int id) => (!MP.TryGetThingById(id, out var value)) ? null : value);
		MP.RegisterSyncMethod(type5, "LinkTo").CancelIfAnyArgNull().TransformArgument(0, serializer, skipTypeCheck: true);
		MP.RegisterSyncMethod(type5, "Unlink").CancelIfAnyArgNull().TransformArgument(0, serializer, skipTypeCheck: true);
		VanillaGravshipPreview.Install("VanillaGravshipExpanded2.LandingStructure_StructureSet", "VanillaGravshipExpanded2.LandingStructure_EnemyStructure", "VanillaGravshipExpanded2.LandingStructure_EnemyGravshipRaid");
		MP.RegisterSyncMethod(AccessTools.TypeByName("VanillaGravshipExpanded2.VGE2_MapComponent"), "EvacuationActive");
		MP.RegisterSyncMethod(type4, "ClaimIfNeeded");
		MpCompat.RegisterLambdaMethod(type4, "CompGetGizmosExtra", 1);
		MpCompat.RegisterLambdaMethod(type4, "CompGetGizmosExtra", 4).SetContext(SyncContext.MapSelected);
		MpCompat.RegisterLambdaDelegate(VanillaGravshipExpanded2Workshop.EscapeMenuBody, "CompFloatMenuOptions", 0, 2, 3);
		MP.RegisterSyncMethod(type3, "ConcerningVacuumLevel");
		MP.RegisterSyncMethod(type3, "EvacuatePawns");
		MP.RegisterSyncMethod(type, "PayTribute");
		MP.RegisterSyncMethod(type, "SpawnActiveWarplatform");
		MP.RegisterSyncMethod(VanillaGravshipExpanded2Workshop.ResolveTributeDecision);
		incomingThreat = AccessTools.Field(type, "incomingWarplatform");
		threatDef = AccessTools.Field(type, "activeThreatDef");
		tributeLeader = AccessTools.Field(type, "salvagerLeader");
		tributeAmount = AccessTools.Field(type, "salvagerTributeAmount");
		MpCompat.harmony.Patch(AccessTools.Method(type, "PayTribute"), new HarmonyMethod(typeof(VanillaGravshipExpanded2), "BeforePayTribute"));
		MpCompat.harmony.Patch(AccessTools.Method(type, "SpawnActiveWarplatform"), new HarmonyMethod(typeof(VanillaGravshipExpanded2), "BeforeSpawnThreat"));
		MpCompat.RegisterLambdaDelegate("VanillaGravshipExpanded2.Building_CommsConsole_GetFloatMenuOptions_Patch", "Postfix", default(int));
		MP.RegisterSyncMethod(type2, "LaunchWarpodTo");
		MP.RegisterSyncMethod(type2, "LaunchHellpodTo");
		string[] array = new string[2] { "LaunchWarpodTo", "LaunchHellpodTo" };
		foreach (string name in array)
		{
			MpCompat.harmony.Patch(AccessTools.Method(type2, name), new HarmonyMethod(typeof(VanillaGravshipExpanded2), "BeforeLaunchPods"));
		}
		MP.RegisterSyncMethod(AccessTools.TypeByName("VanillaGravshipExpanded2.Designator_GenerateEmptyOrbit"), "ProcessInput").SetContext(SyncContext.CurrentMap);
		MpCompat.RegisterLambdaDelegate("VanillaGravshipExpanded2.CompPowerEmergencyGravshipGenerator", "CompGetGizmosExtra", 0, 1, 2).SetDebugOnly();
		MpCompat.RegisterLambdaMethod("VanillaGravshipExpanded2.CompGravshipShieldGeneratorWithHeat", "CompGetGizmosExtra", 0, 1, 2).SetDebugOnly();
		MpCompat.RegisterLambdaMethod("VanillaGravshipExpanded2.CompPower_InputOnlyBattery", "CompGetGizmosExtra", 0, 1, 2).SetDebugOnly();
		MpCompat.RegisterLambdaMethod("VanillaGravshipExpanded2.CompApparelVerbOwner_Oxygen", "CompGetWornGizmosExtra", default(int)).SetDebugOnly();
	}

	private static void PatchAsyncClocks(Type targeter, Type escapePod)
	{
		PatchingUtilities.SetupAsyncTime();
		AccessTools.FieldRef<object, int> targeterLastFire = AccessTools.FieldRefAccess<object, int>(AccessTools.Field(targeter, "lastFireTick"));
		PatchingUtilities.RegisterTimestampFields(targeter, (object obj, int offset) =>
		{
			targeterLastFire(obj) += offset;
		});
		AccessTools.FieldRef<object, int> escapeTrigger = AccessTools.FieldRefAccess<object, int>(AccessTools.Field(escapePod, "triggerOnTick"));
		PatchingUtilities.RegisterTimestampFields(escapePod, (object obj, int offset) =>
		{
			escapeTrigger(obj) += offset;
		});
		(string, string, int)[] array = new (string, string, int)[7]
		{
			("GravshipThreatWorker", "Fire", 1),
			("GravshipThreatWorker_OrbitalCluster", "OnEarlyEscape", 1),
			("GravshipThreatWorker_OrbitalCluster", "OnEscape", 1),
			("GravshipThreatWorker_SalvagerStation", "OnEscape", 1),
			("MapParent_WarPlatform", "Defeat", 1),
			("MapParent_WarPlatform", "GetInspectString", 1),
			("MapParent_WarPlatform", "ShouldRemoveMapNow", 2)
		};
		for (int num = 0; num < array.Length; num++)
		{
			(string, string, int) tuple = array[num];
			string item = tuple.Item1;
			string item2 = tuple.Item2;
			int item3 = tuple.Item3;
			MethodInfo methodInfo = AccessTools.DeclaredMethod("VanillaGravshipExpanded2." + item + ":" + item2);
			worldClockReads.Add(methodInfo, item3);
			MpCompat.harmony.Patch(methodInfo, null, null, new HarmonyMethod(typeof(VanillaGravshipExpanded2), "ReadThreatWorldClock"));
		}
	}

	private static int WorldTicks(TickManager manager)
	{
		if (!MP.IsInMultiplayer || !PatchingUtilities.IsAsyncTime)
		{
			return manager.TicksGame;
		}
		PatchingUtilities.TimeSnapshot? andSetFromWorld = PatchingUtilities.TimeSnapshot.GetAndSetFromWorld();
		try
		{
			return manager.TicksGame;
		}
		finally
		{
			andSetFromWorld?.Set();
		}
	}

	private static IEnumerable<CodeInstruction> ReadThreatWorldClock(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
	{
		int matches = 0;
		MethodInfo getter = AccessTools.PropertyGetter(typeof(TickManager), "TicksGame");
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(getter))
			{
				instruction.opcode = OpCodes.Call;
				instruction.operand = AccessTools.Method(typeof(VanillaGravshipExpanded2), "WorldTicks");
				matches++;
			}
			yield return instruction;
		}
		if (matches != worldClockReads[__originalMethod])
		{
			throw new InvalidOperationException($"Unexpected VGE2 world-clock reads in {__originalMethod}: {matches}");
		}
	}

	private static void SyncTurretLinker(SyncWorker sync, ref object linker)
	{
		Thing obj = linker as Thing;
		if (sync.isWriting && linker != null && obj == null)
		{
			throw new InvalidOperationException("Unsupported gravship turret linker: " + linker.GetType());
		}
		sync.Bind(ref obj);
		if (!sync.isWriting)
		{
			linker = obj;
		}
	}

	private static bool HasPendingThreat(object component)
	{
		if (component != null && (bool)incomingThreat.GetValue(component))
		{
			return threatDef.GetValue(component) != null;
		}
		return false;
	}

	private static bool BeforeSpawnThreat(object __instance)
	{
		if (MP.IsInMultiplayer)
		{
			return HasPendingThreat(__instance);
		}
		return true;
	}

	private static bool BeforePayTribute(object __instance, Map map)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (HasPendingThreat(__instance) && tributeLeader.GetValue(__instance) is Pawn { Destroyed: false } && map != null && Find.Maps.Contains(map))
		{
			return TradeUtility.ColonyHasEnoughSilver(map, (int)tributeAmount.GetValue(__instance));
		}
		return false;
	}

	private static bool BeforeLaunchPods(object[] __args)
	{
		if (!MP.IsInMultiplayer || !MP.IsExecutingSyncCommand)
		{
			return true;
		}
		if (!(__args[0] is MapParent { Destroyed: false, Tile: { Valid: not false } } mapParent) || !(__args[^1] is IList { Count: not 0 } list))
		{
			return false;
		}
		if (__args.Length == 4)
		{
			if (__args[1] is PlanetTile planetTile && !(planetTile != mapParent.Tile))
			{
				Map map = mapParent.Map;
				if (map != null && __args[2] is IntVec3 c && c.InBounds(map))
				{
					goto IL_00b7;
				}
			}
			return false;
		}
		if (!(__args[1] is PawnsArrivalModeDef))
		{
			return false;
		}
		goto IL_00b7;
		IL_00b7:
		HashSet<Thing> hashSet = new HashSet<Thing>();
		Dictionary<CompRefuelable, float> dictionary = new Dictionary<CompRefuelable, float>();
		foreach (object item in list)
		{
			if (!(item is CompLaunchable { parent: { Spawned: not false } parent } compLaunchable) || !hashSet.Add(parent) || !compLaunchable.CanLaunch().Accepted)
			{
				return false;
			}
			int num = Find.WorldGrid.TraversalDistanceBetween(parent.Map.Tile, mapParent.Tile, passImpassable: true, int.MaxValue, canTraverseLayers: true);
			if (num > compLaunchable.MaxLaunchDistanceAtFuelLevel(compLaunchable.FuelLevel, mapParent.Tile.Layer))
			{
				return false;
			}
			CompRefuelable refuelable = compLaunchable.Refuelable;
			if (refuelable == null)
			{
				return false;
			}
			float num2 = Mathf.Max(compLaunchable.FuelNeededToLaunchAtDist(num, mapParent.Tile.Layer), 5f);
			dictionary.TryGetValue(refuelable, out var value);
			dictionary[refuelable] = value + num2;
		}
		foreach (KeyValuePair<CompRefuelable, float> item2 in dictionary)
		{
			if (item2.Key.Fuel < item2.Value)
			{
				return false;
			}
		}
		return true;
	}
}
