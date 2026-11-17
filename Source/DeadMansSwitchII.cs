using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Xml;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Aoba.DeadManSwitch.Core")]
public class DeadMansSwitchII
{
	private sealed class GestationClock
	{
		internal bool ValidNegative;
	}

	private sealed class DmsSettingsEdit
	{
		internal object Settings;

		internal Game Game;

		internal bool LocalValue;

		internal bool SharedValue;
	}

	private static readonly ConditionalWeakTable<object, GestationClock> GestationClocks = new ConditionalWeakTable<object, GestationClock>();

	private static FieldInfo GestationFinishTick;

	private static readonly Dictionary<Type, FieldInfo> PermitFactions = new Dictionary<Type, FieldInfo>();

	private static MethodInfo lifterLaunch;

	private static MethodInfo lifterMap;

	private static MethodInfo lifterFallback;

	private static MethodInfo lifterValidCell;

	private static Type lifterLandingType;

	private static Func<bool> lifterReplay;

	private static Func<bool> lifterSimulating;

	private static FieldInfo lifterArbiter;

	private static readonly FieldInfo LifterCarrierPods = AccessTools.DeclaredField(typeof(TravellingTransporters), "transporters");

	private static readonly FieldInfo LifterCarrierArrived = AccessTools.DeclaredField(typeof(TravellingTransporters), "arrived");

	private static readonly FieldInfo LifterCarrierTraveled = AccessTools.DeclaredField(typeof(TravellingTransporters), "traveledPct");

	private static readonly FieldInfo LifterCarrierInitialTile = AccessTools.DeclaredField(typeof(TravellingTransporters), "initialTile");

	[ThreadStatic]
	private static TravellingTransporters arrivingLifterCarrier;

	private static FieldInfo DmsSettingsField;

	private static FieldInfo ArtifuscleRotsField;

	internal static bool LifterAvailable => lifterLandingType != null;

	internal static DmsLifterLandingState LifterState => Current.Game?.GetComponent<DmsLifterLandingState>();

	private static bool LifterHasColonist => LifterState?.pending.Any((DmsPendingLifterLanding record) => record.pods.Any((ActiveTransporterInfo pod) => pod.innerContainer.OfType<Pawn>().Any((Pawn pawn) => !pawn.Dead && pawn.IsFreeColonist))) ?? false;

	internal static bool LifterCanPresent
	{
		get
		{
			if (MP.IsInMultiplayer)
			{
				if (MP.InInterface)
				{
					Func<bool> func = lifterReplay;
					if (func != null && !func())
					{
						Func<bool> func2 = lifterSimulating;
						if (func2 != null && !func2())
						{
							return !(bool)(lifterArbiter?.GetValue(null) ?? ((object)true));
						}
					}
				}
				return false;
			}
			return true;
		}
	}

	internal static object DmsSettings => DmsSettingsField?.GetValue(null);

	internal static bool LocalArtifuscleRots
	{
		get
		{
			if (DmsSettings != null)
			{
				return (bool)ArtifuscleRotsField.GetValue(DmsSettings);
			}
			return true;
		}
	}

	private static void InitializeDmsTimestamps()
	{
		MpCompat.harmony.Patch(DmsMethod("FleetTrackingUtility", "SuspendAll"), null, null, new HarmonyMethod(typeof(DeadMansSwitchII), "DmsFleetClockTranspiler"));
		(string, string)[] array = new (string, string)[3]
		{
			("CompBlastScanner", "cooldownUntilTick"),
			("CompAlertEffector_GasVent", "cooldownUntil"),
			("CompAlertEffector_SubsonicEmitter", "activeUntil")
		};
		for (int i = 0; i < array.Length; i++)
		{
			(string, string) tuple = array[i];
			Type type = DmsType(tuple.Item1);
			FieldInfo field = AccessTools.DeclaredField(type, tuple.Item2) ?? throw new MissingFieldException(type.FullName, tuple.Item2);
			PatchingUtilities.RegisterTimestampFields(type, (object value, int offset) =>
			{
				ShiftDmsDeadline(value, field, offset);
			});
		}
		Type type2 = DmsType("Building_SecondaryGestation");
		GestationFinishTick = AccessTools.DeclaredField(type2, "finishTick") ?? throw new MissingFieldException(type2.FullName, "finishTick");
		PatchingUtilities.RegisterTimestampFields(type2, ShiftDmsGestation);
		string[] array2 = new string[2] { "SpawnSetup", "Tick" };
		foreach (string name in array2)
		{
			MpCompat.harmony.Patch(DmsMethod("Building_SecondaryGestation", name), null, null, new HarmonyMethod(typeof(DeadMansSwitchII), "GestationMarkerTranspiler"));
		}
		MpCompat.harmony.Patch(DmsMethod("Building_SecondaryGestation", "ExposeData"), null, new HarmonyMethod(typeof(DeadMansSwitchII), "GestationExpose"));
		MpCompat.harmony.Patch(DmsMethod("Building_SecondaryGestation", "FinishGestation"), null, new HarmonyMethod(typeof(DeadMansSwitchII), "GestationCompleted"));
	}

	private static void ShiftDmsDeadline(object value, FieldInfo field, int offset)
	{
		field.SetValue(value, (int)field.GetValue(value) + offset);
	}

	private static IEnumerable<CodeInstruction> DmsFleetClockTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo getter = AccessTools.PropertyGetter(typeof(TickManager), "TicksGame");
		int replaced = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(getter))
			{
				replaced++;
				yield return new CodeInstruction(OpCodes.Pop).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
				yield return new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(FortifiedFeaturesFramework), "WorldTicks"));
			}
			else
			{
				yield return instruction;
			}
		}
		if (replaced != 1)
		{
			throw new InvalidOperationException($"DMS fleet clock IL changed: {replaced}");
		}
	}

	private static void ShiftDmsGestation(object value, int offset)
	{
		int num = (int)GestationFinishTick.GetValue(value);
		GestationClock orCreateValue = GestationClocks.GetOrCreateValue(value);
		if (num >= 0 || orCreateValue.ValidNegative)
		{
			int num2 = num + offset;
			GestationFinishTick.SetValue(value, num2);
			orCreateValue.ValidNegative = num2 < 0;
		}
	}

	private static int GestationMarker(object value)
	{
		int num = (int)GestationFinishTick.GetValue(value);
		if (!MP.IsInMultiplayer || num >= 0 || !GestationClocks.TryGetValue(value, out var value2) || !value2.ValidNegative)
		{
			return num;
		}
		return 0;
	}

	private static IEnumerable<CodeInstruction> GestationMarkerTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		List<CodeInstruction> list = instructions.ToList();
		int replaced = 0;
		for (int i = 0; i < list.Count; i++)
		{
			CodeInstruction codeInstruction = list[i];
			if (codeInstruction.LoadsField(GestationFinishTick) && i + 1 < list.Count && list[i + 1].LoadsConstant(0L))
			{
				replaced++;
				yield return new CodeInstruction(codeInstruction)
				{
					opcode = OpCodes.Call,
					operand = AccessTools.DeclaredMethod(typeof(DeadMansSwitchII), "GestationMarker")
				};
			}
			else
			{
				yield return codeInstruction;
			}
		}
		if (replaced != 1)
		{
			throw new InvalidOperationException($"DMS gestation marker IL changed: {replaced}");
		}
	}

	private static void GestationExpose(object __instance)
	{
		GestationClock orCreateValue = GestationClocks.GetOrCreateValue(__instance);
		if (Scribe.mode == LoadSaveMode.Saving && (int)GestationFinishTick.GetValue(__instance) >= 0)
		{
			orCreateValue.ValidNegative = false;
		}
		Scribe_Values.Look(ref orCreateValue.ValidNegative, "mpDmsGestationValidNegative", defaultValue: false);
	}

	private static void GestationCompleted(object __instance)
	{
		if (GestationClocks.TryGetValue(__instance, out var value))
		{
			value.ValidNegative = false;
		}
	}

	public DeadMansSwitchII(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
	}

	private static void LatePatch()
	{
		InitializeNonLifter();
		InitializeLifter();
		Type type = AccessTools.TypeByName("Multiplayer.Client.Compatibility.CheckpointState");
		MethodInfo methodInfo = ((type == null) ? null : AccessTools.DeclaredMethod(type, "RegisterLocalMote", new Type[2]
		{
			typeof(ThingDef),
			typeof(string)
		}));
		if (methodInfo != null)
		{
			Action<ThingDef, string> action = AccessTools.MethodDelegate<Action<ThingDef, string>>(methodInfo);
			action(DefDatabase<ThingDef>.GetNamed("DMS_Mote_ReactionChamberActive"), "dms-reaction-chamber-overlay-v1");
			action(DefDatabase<ThingDef>.GetNamed("DMS_Mote_SinteringFurnaceActive"), "dms-sintering-furnace-overlay-v1");
		}
	}

	private static Type DmsType(string name)
	{
		return AccessTools.TypeByName("DMS." + name) ?? throw new TypeLoadException("DMS." + name);
	}

	private static MethodInfo DmsMethod(string type, string name, Type[] args = null)
	{
		return AccessTools.DeclaredMethod(DmsType(type), name, args) ?? throw new MissingMethodException("DMS." + type, name);
	}

	private static void InitializeNonLifter()
	{
		(string, string)[] array = new (string, string)[4]
		{
			("CompBlastScanner", "Ignite"),
			("CompSupplyRequest", "AutoDesignate"),
			("CompSupplyRequest", "Deliver"),
			("CompRationMagazine", "TryStartLoadJob")
		};
		for (int i = 0; i < array.Length; i++)
		{
			(string, string) tuple = array[i];
			MP.RegisterSyncMethod(DmsMethod(tuple.Item1, tuple.Item2)).CancelIfAnyArgNull();
			MpCompat.harmony.Patch(DmsMethod(tuple.Item1, tuple.Item2), new HarmonyMethod(typeof(DeadMansSwitchII), (tuple.Item1 == "CompRationMagazine") ? "RationCommandAllowed" : "DmsCompCommandAllowed"));
		}
		string[] array2 = new string[2] { "RoyalTitlePermitWorker_RewardLifter", "RoyalTitlePermitWorker_RewardShuttle" };
		foreach (string text in array2)
		{
			Type type = DmsType(text);
			PermitFactions[type] = AccessTools.DeclaredField(type, "calledFaction") ?? throw new MissingFieldException(type.FullName, "calledFaction");
			MP.RegisterSyncWorker<RoyalTitlePermitWorker_Targeted>(SyncPermitFaction, type, isImplicit: true);
			MP.RegisterSyncMethod(DmsMethod(text, "OrderForceTarget"));
			MpCompat.harmony.Patch(DmsMethod(text, "OrderForceTarget"), new HarmonyMethod(typeof(DeadMansSwitchII), "PermitTargetValid"));
		}
		InitializeDmsSettings();
		InitializeDmsTimestamps();
		InitializeRaidGroupSelection();
		MP.RegisterSyncMethod(DmsMethod("CompBlastScanner", "Detonate")).SetDebugOnly();
		(string, string, int[])[] array3 = new (string, string, int[])[11]
		{
			("CompBlastScanner", "CompGetGizmosExtra", new int[1]),
			("CompRationMagazine", "CompGetGizmosExtra", new int[2] { 1, 2 }),
			("CompHydraulicFracturing", "CompGetGizmosExtra", new int[1]),
			("CompZeroPointGenerator", "CompGetGizmosExtra", new int[2] { 0, 1 }),
			("CompZeroPointReactor", "CompGetGizmosExtra", new int[1]),
			("CompAlertEffector_GasVent", "CompGetGizmosExtra", new int[1]),
			("CompAlertEffector_SubsonicEmitter", "CompGetGizmosExtra", new int[1]),
			("CompServerHackable", "CompGetGizmosExtra", new int[1]),
			("CompBossKillRelay", "CompGetGizmosExtra", new int[1]),
			("Building_SecondaryGestation", "GetGizmos", new int[2] { 0, 1 }),
			("Building_AccessKeyTransformer", "GetGizmos", new int[1])
		};
		for (int i = 0; i < array3.Length; i++)
		{
			(string, string, int[]) tuple2 = array3[i];
			ISyncMethod[] array4 = MpCompat.RegisterLambdaMethod(DmsType(tuple2.Item1), tuple2.Item2, tuple2.Item3);
			for (int j = 0; j < array4.Length; j++)
			{
				array4[j].SetDebugOnly();
			}
		}
		array = new (string, string)[3]
		{
			("CompHydraulicFracturing", "TickSpray"),
			("CompHydraulicFracturing", "StartSpray"),
			("CompBlastScanner", "PlayBlastEffects")
		};
		for (int i = 0; i < array.Length; i++)
		{
			(string, string) tuple3 = array[i];
			MpCompat.harmony.Patch(DmsMethod(tuple3.Item1, tuple3.Item2), new HarmonyMethod(typeof(DeadMansSwitchII), "DmsVisualBefore")
			{
				priority = 0
			}, null, null, new HarmonyMethod(typeof(DeadMansSwitchII), "DmsVisualAfter")
			{
				priority = 800
			});
		}
	}

	private static void SyncPermitFaction(SyncWorker sync, ref RoyalTitlePermitWorker_Targeted worker)
	{
		FieldInfo fieldInfo = PermitFactions[worker.GetType()];
		if (sync.isWriting)
		{
			sync.Write((Faction)fieldInfo.GetValue(worker));
		}
		else
		{
			fieldInfo.SetValue(worker, sync.Read<Faction>());
		}
	}

	private static bool PermitTargetValid(RoyalTitlePermitWorker_Targeted __instance, LocalTargetInfo target)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		Pawn pawn = (Pawn)AccessTools.Field(typeof(RoyalTitlePermitWorker_Targeted), "caller").GetValue(__instance);
		Map map = (Map)AccessTools.Field(typeof(RoyalTitlePermitWorker_Targeted), "map").GetValue(__instance);
		bool flag = (bool)AccessTools.Field(typeof(RoyalTitlePermitWorker_Targeted), "free").GetValue(__instance);
		Faction faction = (Faction)PermitFactions[__instance.GetType()].GetValue(__instance);
		if (pawn == null || !pawn.Spawned || pawn.Dead || pawn.Downed || pawn.Map != map || faction == null || pawn.royalty == null || __instance.def == null || faction.HostileTo(Faction.OfPlayer) || !CommandAuthorityBridge.CanActForPawn(pawn))
		{
			return false;
		}
		FactionPermit permit = pawn.royalty.GetPermit(__instance.def, faction);
		if (permit == null || (flag && permit.OnCooldown) || (!flag && pawn.royalty.GetFavor(faction) < __instance.def.royalAid.favorCost))
		{
			return false;
		}
		return __instance.ValidateTarget(target, showMessages: false);
	}

	private static void DmsVisualBefore(out bool __state)
	{
		__state = false;
		if (MP.IsInMultiplayer)
		{
			Rand.PushState();
			__state = true;
		}
	}

	private static bool DmsCompCommandAllowed(ThingComp __instance)
	{
		return FortifiedFeaturesFramework.CommandAllowed(__instance?.parent);
	}

	private static bool RationCommandAllowed(ThingComp __instance, Pawn __0)
	{
		if (DmsCompCommandAllowed(__instance))
		{
			return CommandAuthorityBridge.CanActForPawn(__0);
		}
		return false;
	}

	private static Exception DmsVisualAfter(Exception __exception, bool __state)
	{
		if (__state)
		{
			Rand.PopState();
		}
		return __exception;
	}

	private static void InitializeLifter()
	{
		lifterLaunch = DmsMethod("CompLaunchable_Lifter", "TryLaunchLifter");
		lifterLandingType = DmsType("TransportersArrivalAction_LifterLanding");
		lifterMap = DmsMethod("TransportersArrivalAction_LifterLanding", "GetOrGenerateLandingMap");
		lifterFallback = DmsMethod("TransportersArrivalAction_LifterLanding", "FindFallbackCell");
		lifterValidCell = DmsMethod("TransportersArrivalAction_LifterLanding", "CanLandInCell");
		Type type = AccessTools.TypeByName("Multiplayer.Client.Multiplayer");
		lifterReplay = (Func<bool>)AccessTools.PropertyGetter(type, "IsReplay").CreateDelegate(typeof(Func<bool>));
		lifterSimulating = (Func<bool>)AccessTools.PropertyGetter(AccessTools.TypeByName("Multiplayer.Client.TickPatch"), "Simulating").CreateDelegate(typeof(Func<bool>));
		lifterArbiter = AccessTools.Field(type, "arbiterInstance");
		InitializeLifterCarrier();
		MP.RegisterSyncMethod(typeof(DeadMansSwitchII), "SyncLifterLaunch").ExposeParameter(2);
		MP.RegisterSyncMethod(typeof(DeadMansSwitchII), "SyncLifterLand");
		MP.RegisterSyncMethod(typeof(DeadMansSwitchII), "SyncLifterClaim");
		MpCompat.harmony.Patch(lifterLaunch, new HarmonyMethod(typeof(DeadMansSwitchII), "BeforeLifterLaunch"), null, new HarmonyMethod(typeof(DeadMansSwitchII), "LifterLaunchPresentation"));
		MpCompat.harmony.Patch(DmsMethod("TransportersArrivalAction_LifterLanding", "Arrived"), new HarmonyMethod(typeof(DeadMansSwitchII), "BeforeLegacyLifterArrival"));
		MpCompat.harmony.Patch(AccessTools.Method(typeof(GameEnder), "CheckOrUpdateGameOver"), new HarmonyMethod(typeof(DeadMansSwitchII), "LifterBeforeGameOver"));
		MpCompat.harmony.Patch(AccessTools.Method(typeof(GameEnder), "CanSpawnNewWanderers"), new HarmonyMethod(typeof(DeadMansSwitchII), "LifterBeforeWanderers"));
		MpCompat.RegisterLambdaMethod(DmsType("CompLaunchable_Lifter"), "CompGetGizmosExtra", 4, 5).Do((ISyncMethod method) =>
		{
			method.SetDebugOnly();
		});
	}

	private static bool BeforeLifterLaunch(ThingComp __instance, PlanetTile destinationTile, TransportersArrivalAction arrivalAction)
	{
		if (!MP.IsInMultiplayer || !MP.InInterface || MP.IsExecutingSyncCommand)
		{
			return true;
		}
		Game game = Current.Game;
		Map map = __instance.parent.Map;
		SyncLifterLaunch(__instance, destinationTile, arrivalAction, MP.PlayerName);
		if (Current.Game == game && map != null && Find.Maps.Contains(map) && LifterCanPresent)
		{
			game.CurrentMap = map;
			CameraJumper.TryHideWorld();
		}
		return false;
	}

	private static bool ValidLifterOwner(string owner)
	{
		if (string.IsNullOrWhiteSpace(owner) || owner.Length > 128)
		{
			return false;
		}
		try
		{
			XmlConvert.VerifyXmlChars(owner);
			return true;
		}
		catch (XmlException)
		{
			return false;
		}
	}

	private static void SyncLifterLaunch(ThingComp launchable, PlanetTile destinationTile, TransportersArrivalAction arrivalAction, string owner)
	{
		if (MP.IsInMultiplayer && MP.IsExecutingSyncCommand && ValidLifterOwner(owner) && launchable != null && launchable.parent?.Spawned == true && !launchable.parent.Destroyed && lifterLaunch.DeclaringType.IsInstanceOfType(launchable))
		{
			if (arrivalAction != null && lifterLandingType.IsInstanceOfType(arrivalAction))
			{
				arrivalAction = new DmsOwnedLifterArrival
				{
					native = arrivalAction,
					owner = owner
				};
			}
			lifterLaunch.Invoke(launchable, new object[2] { destinationTile, arrivalAction });
		}
	}

	private static IEnumerable<CodeInstruction> LifterLaunchPresentation(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo setMap = AccessTools.PropertySetter(typeof(Game), "CurrentMap");
		MethodInfo hideWorld = AccessTools.Method(typeof(CameraJumper), "TryHideWorld");
		int maps = 0;
		int hides = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(setMap))
			{
				instruction.opcode = OpCodes.Call;
				instruction.operand = AccessTools.Method(typeof(DeadMansSwitchII), "LifterLaunchMap");
				maps++;
			}
			else if (instruction.Calls(hideWorld))
			{
				instruction.opcode = OpCodes.Call;
				instruction.operand = AccessTools.Method(typeof(DeadMansSwitchII), "LifterLaunchHideWorld");
				hides++;
			}
			yield return instruction;
		}
		if (maps != 1 || hides != 1)
		{
			throw new InvalidOperationException($"DMS lifter launch presentation changed: {maps}/{hides}");
		}
	}

	private static void LifterLaunchMap(Game game, Map map)
	{
		if (!MP.IsInMultiplayer)
		{
			game.CurrentMap = map;
		}
	}

	private static bool LifterLaunchHideWorld()
	{
		if (!MP.IsInMultiplayer)
		{
			return CameraJumper.TryHideWorld();
		}
		return false;
	}

	private static bool BeforeLegacyLifterArrival(List<ActiveTransporterInfo> transporters, PlanetTile tile)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		LifterState.Remember(transporters, tile, "");
		return false;
	}

	private static bool LifterBeforeGameOver(GameEnder __instance)
	{
		if (!LifterHasColonist)
		{
			return true;
		}
		__instance.gameEnding = false;
		return false;
	}

	private static bool LifterBeforeWanderers(ref bool __result)
	{
		if (!LifterHasColonist)
		{
			return true;
		}
		__result = false;
		return false;
	}

	internal static Map LifterMap(PlanetTile tile)
	{
		return (Map)lifterMap.Invoke(null, new object[1] { tile });
	}

	internal static IntVec3 LifterFallback(Map map, IntVec2 size)
	{
		return (IntVec3)lifterFallback.Invoke(null, new object[2] { map, size });
	}

	internal static bool LifterCellValid(IntVec3 cell, Map map, IntVec2 size)
	{
		if (map != null && Find.Maps.Contains(map))
		{
			return (bool)lifterValidCell.Invoke(null, new object[3] { cell, map, size });
		}
		return false;
	}

	internal static void SyncLifterClaim(int id, int revision, string expectedOwner, string owner)
	{
		if (MP.IsInMultiplayer && MP.IsExecutingSyncCommand && ValidLifterOwner(owner))
		{
			DmsPendingLifterLanding dmsPendingLifterLanding = LifterState?.pending.FirstOrDefault((DmsPendingLifterLanding p) => p.id == id);
			if (dmsPendingLifterLanding != null && dmsPendingLifterLanding.revision == revision && string.Equals(dmsPendingLifterLanding.owner, expectedOwner, StringComparison.Ordinal))
			{
				dmsPendingLifterLanding.owner = owner;
				dmsPendingLifterLanding.revision++;
			}
		}
	}

	internal static void SyncLifterLand(int id, int revision, string owner, IntVec3 cell, bool useFallback)
	{
		if (MP.IsInMultiplayer && !MP.IsExecutingSyncCommand)
		{
			return;
		}
		DmsLifterLandingState lifterState = LifterState;
		DmsPendingLifterLanding dmsPendingLifterLanding = lifterState?.pending.FirstOrDefault((DmsPendingLifterLanding p) => p.id == id);
		if (dmsPendingLifterLanding == null || dmsPendingLifterLanding.revision != revision || !string.Equals(dmsPendingLifterLanding.owner, owner, StringComparison.Ordinal))
		{
			return;
		}
		if (dmsPendingLifterLanding.map == null || !Find.Maps.Contains(dmsPendingLifterLanding.map))
		{
			dmsPendingLifterLanding.map = LifterMap(dmsPendingLifterLanding.tile);
			dmsPendingLifterLanding.fallback = LifterFallback(dmsPendingLifterLanding.map, dmsPendingLifterLanding.Size);
			useFallback = true;
		}
		if (useFallback)
		{
			cell = dmsPendingLifterLanding.fallback;
		}
		if (!LifterCellValid(cell, dmsPendingLifterLanding.map, dmsPendingLifterLanding.Size))
		{
			if (!useFallback)
			{
				return;
			}
			cell = LifterFallback(dmsPendingLifterLanding.map, dmsPendingLifterLanding.Size);
			if (!LifterCellValid(cell, dmsPendingLifterLanding.map, dmsPendingLifterLanding.Size))
			{
				return;
			}
		}
		Thing lookTarget = TransportersArrivalActionUtility.GetLookTarget(dmsPendingLifterLanding.pods);
		while (dmsPendingLifterLanding.pods.Count > 0)
		{
			ActiveTransporterInfo activeTransporterInfo = dmsPendingLifterLanding.pods[0];
			activeTransporterInfo.savePawnsWithReferenceMode = false;
			TransportersArrivalActionUtility.RemovePawnsFromWorldPawns(new ActiveTransporterInfo[1] { activeTransporterInfo });
			bool flag = false;
			try
			{
				DropPodUtility.MakeDropPodAt(cell, dmsPendingLifterLanding.map, activeTransporterInfo);
				flag = true;
			}
			finally
			{
				if (flag || ThingOwnerUtility.SpawnedParentOrMe(activeTransporterInfo) != null || activeTransporterInfo.parent is Thing { Destroyed: not false })
				{
					dmsPendingLifterLanding.pods.Remove(activeTransporterInfo);
				}
				else
				{
					RebindLifterCarrier(dmsPendingLifterLanding);
				}
			}
		}
		lifterState.pending.Remove(dmsPendingLifterLanding);
		CompleteLifterCarrier(dmsPendingLifterLanding);
		DmsLifterLandingLetter[] array = (from l in Find.LetterStack.LettersListForReading.OfType<DmsLifterLandingLetter>()
			where l.recordId == id
			select l).ToArray();
		foreach (DmsLifterLandingLetter dmsLifterLandingLetter in array)
		{
			Find.LetterStack.RemoveLetter(dmsLifterLandingLetter);
		}
		Messages.Message("MessageTransportPodsArrived".Translate(), lookTarget, MessageTypeDefOf.TaskCompletion);
	}

	internal static string LifterText(string key)
	{
		bool flag = LanguageDatabase.activeLanguage?.LegacyFolderName == "ChineseSimplified";
		switch (key)
		{
		case "Title":
			if (flag)
			{
				return "升降机等待着陆";
			}
			return "Lifter awaiting landing";
		case "Pending":
			if (flag)
			{
				return "升降机已抵达，人员和货物正在等待选择落点。发起人可以选择着陆地点；需要交接时，打开本通知即可接手。";
			}
			return "The lifter has arrived. Its crew and cargo are waiting for a landing site. The launch initiator chooses the site; open this letter to take over the landing if needed.";
		case "Regenerate":
			if (flag)
			{
				return "着陆地图已被移除。重建目标地图并自动选择落点，让升降机着陆？";
			}
			return "The landing map was removed. Regenerate the destination map and land the lifter at an automatically selected site?";
		case "Claim":
			if (!flag)
			{
				break;
			}
			return "由你继续选择这次升降机的着陆地点？";
		}
		return "Continue choosing this lifter's landing site yourself?";
	}

	private static void InitializeLifterCarrier()
	{
		MethodInfo original = AccessTools.DeclaredMethod(typeof(TravellingTransporters), "DoArrivalAction") ?? throw new MissingMethodException(typeof(TravellingTransporters).FullName, "DoArrivalAction");
		MpCompat.harmony.Patch(original, new HarmonyMethod(typeof(DeadMansSwitchII), "BeforeLifterCarrierArrival"), null, new HarmonyMethod(typeof(DeadMansSwitchII), "LifterCarrierCleanup"), new HarmonyMethod(typeof(DeadMansSwitchII), "AfterLifterCarrierArrival"));
	}

	private static List<ActiveTransporterInfo> CarrierPods(TravellingTransporters carrier)
	{
		return (List<ActiveTransporterInfo>)LifterCarrierPods.GetValue(carrier);
	}

	private static void BeforeLifterCarrierArrival(TravellingTransporters __instance, out TravellingTransporters __state)
	{
		__state = arrivingLifterCarrier;
		arrivingLifterCarrier = __instance;
	}

	private static Exception AfterLifterCarrierArrival(Exception __exception, TravellingTransporters __state)
	{
		arrivingLifterCarrier = __state;
		return __exception;
	}

	private static bool RetainedLifterCarrier(TravellingTransporters carrier)
	{
		if (carrier != null)
		{
			return LifterState?.pending.Any((DmsPendingLifterLanding record) => record.carrier == carrier && record.pods == CarrierPods(carrier) && record.pods.Count > 0) ?? false;
		}
		return false;
	}

	private static void LifterClearCarrierPods(List<ActiveTransporterInfo> pods)
	{
		if (((arrivingLifterCarrier == null) ? null : CarrierPods(arrivingLifterCarrier)) != pods || !RetainedLifterCarrier(arrivingLifterCarrier))
		{
			pods.Clear();
		}
	}

	private static void LifterDestroyCarrier(WorldObject worldObject)
	{
		if (!(worldObject is TravellingTransporters carrier) || !RetainedLifterCarrier(carrier))
		{
			worldObject.Destroy();
		}
	}

	private static IEnumerable<CodeInstruction> LifterCarrierCleanup(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo clear = AccessTools.DeclaredMethod(typeof(List<ActiveTransporterInfo>), "Clear");
		MethodInfo destroy = AccessTools.DeclaredMethod(typeof(WorldObject), "Destroy");
		int clears = 0;
		int destroys = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(clear))
			{
				clears++;
				yield return new CodeInstruction(instruction)
				{
					opcode = OpCodes.Call,
					operand = AccessTools.DeclaredMethod(typeof(DeadMansSwitchII), "LifterClearCarrierPods")
				};
			}
			else if (instruction.Calls(destroy))
			{
				destroys++;
				yield return new CodeInstruction(instruction)
				{
					opcode = OpCodes.Call,
					operand = AccessTools.DeclaredMethod(typeof(DeadMansSwitchII), "LifterDestroyCarrier")
				};
			}
			else
			{
				yield return instruction;
			}
		}
		if (clears != 1 || destroys != 1)
		{
			throw new InvalidOperationException($"Native transporter arrival cleanup changed: {clears}/{destroys}");
		}
	}

	internal static void AttachLifterCarrier(DmsPendingLifterLanding record, List<ActiveTransporterInfo> pods)
	{
		TravellingTransporters travellingTransporters = record.carrier;
		if (travellingTransporters == null && arrivingLifterCarrier != null && CarrierPods(arrivingLifterCarrier) == pods)
		{
			travellingTransporters = arrivingLifterCarrier;
		}
		if (travellingTransporters == null)
		{
			travellingTransporters = pods.FirstOrDefault()?.parent as TravellingTransporters;
		}
		if (travellingTransporters == null)
		{
			travellingTransporters = (TravellingTransporters)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.TravellingTransporters);
			travellingTransporters.Tile = record.tile;
			travellingTransporters.destinationTile = record.tile;
			LifterCarrierInitialTile.SetValue(travellingTransporters, record.tile);
			travellingTransporters.SetFaction(Faction.OfPlayer);
			Find.WorldObjects.Add(travellingTransporters);
			record.carrier = travellingTransporters;
			ActiveTransporterInfo[] array = pods.ToArray();
			foreach (ActiveTransporterInfo contents in array)
			{
				travellingTransporters.AddTransporter(contents, justLeftTheMap: false);
			}
		}
		record.carrier = travellingTransporters;
		record.pods = CarrierPods(travellingTransporters);
		LifterCarrierArrived.SetValue(travellingTransporters, true);
		LifterCarrierTraveled.SetValue(travellingTransporters, 1f);
		foreach (ActiveTransporterInfo pod in record.pods)
		{
			pod.parent = travellingTransporters;
			pod.savePawnsWithReferenceMode = true;
		}
		Pawn[] array2 = record.pods.SelectMany((ActiveTransporterInfo pod) => pod.innerContainer.OfType<Pawn>()).ToArray();
		foreach (Pawn pawn in array2)
		{
			if (!Find.WorldPawns.Contains(pawn))
			{
				Find.WorldPawns.PassToWorld(pawn);
			}
		}
	}

	internal static void RebindLifterCarrier(DmsPendingLifterLanding record)
	{
		if (record.carrier != null)
		{
			AttachLifterCarrier(record, CarrierPods(record.carrier));
		}
		else if (record.pods.Count > 0)
		{
			AttachLifterCarrier(record, record.pods);
		}
	}

	internal static void RestoreLifterCarrierPod(DmsPendingLifterLanding record, ActiveTransporterInfo pod)
	{
		pod.parent = record.carrier;
		pod.savePawnsWithReferenceMode = true;
		Pawn[] array = pod.innerContainer.OfType<Pawn>().ToArray();
		foreach (Pawn pawn in array)
		{
			if (!Find.WorldPawns.Contains(pawn))
			{
				Find.WorldPawns.PassToWorld(pawn);
			}
		}
	}

	internal static void CompleteLifterCarrier(DmsPendingLifterLanding record)
	{
		if (record.carrier != null && CarrierPods(record.carrier).Count == 0 && !record.carrier.Destroyed)
		{
			record.carrier.Destroy();
		}
	}

	private static void InitializeRaidGroupSelection()
	{
		ISyncDelegate[] array = MpCompat.RegisterLambdaDelegate(DmsType("CompSelectableRaidGroup"), "CompGetGizmosExtra", 1);
		for (int i = 0; i < array.Length; i++)
		{
			((ISyncMethod)array[i]).SetVersion(13);
		}
	}

	private static void InitializeDmsSettings()
	{
		DmsSettingsField = AccessTools.DeclaredField(DmsType("DMS_Mod"), "Settings") ?? throw new MissingFieldException("DMS_Mod", "Settings");
		ArtifuscleRotsField = AccessTools.DeclaredField(DmsType("DMS_Settings"), "artifuscleRots") ?? throw new MissingFieldException("DMS_Settings", "artifuscleRots");
		MP.RegisterSyncMethod(typeof(DeadMansSwitchII), "SyncArtifuscleRots").SetHostOnly();
		MpCompat.harmony.Patch(DmsMethod("DMS_Mod", "DoSettingsWindowContents"), new HarmonyMethod(typeof(DeadMansSwitchII), "DmsSettingsBefore"), null, null, new HarmonyMethod(typeof(DeadMansSwitchII), "DmsSettingsAfter"));
		MpCompat.harmony.Patch(DmsMethod("Patch_CompRottable_Active", "Postfix"), null, null, new HarmonyMethod(typeof(DeadMansSwitchII), "DmsRotSettingTranspiler"));
	}

	private static void DmsSettingsBefore(out DmsSettingsEdit __state)
	{
		__state = null;
		if (MP.IsInMultiplayer && Current.Game != null && DmsSettings != null)
		{
			DeadMansSwitchIISettingsComponent component = Current.Game.GetComponent<DeadMansSwitchIISettingsComponent>();
			__state = new DmsSettingsEdit
			{
				Settings = DmsSettings,
				Game = Current.Game,
				LocalValue = LocalArtifuscleRots,
				SharedValue = component.ArtifuscleRots
			};
			ArtifuscleRotsField.SetValue(__state.Settings, __state.SharedValue);
		}
	}

	private static Exception DmsSettingsAfter(Exception __exception, DmsSettingsEdit __state)
	{
		if (__state == null)
		{
			return __exception;
		}
		bool flag = (bool)ArtifuscleRotsField.GetValue(__state.Settings);
		ArtifuscleRotsField.SetValue(__state.Settings, __state.LocalValue);
		if (__exception == null && MP.IsHosting && MP.InInterface && Current.Game == __state.Game && flag != __state.SharedValue)
		{
			SyncArtifuscleRots(flag);
		}
		return __exception;
	}

	private static void SyncArtifuscleRots(bool value)
	{
		if (Current.Game != null)
		{
			Current.Game.GetComponent<DeadMansSwitchIISettingsComponent>().ArtifuscleRots = value;
		}
	}

	private static bool SharedArtifuscleRots(object settings)
	{
		if (!MP.IsInMultiplayer || Current.Game == null)
		{
			return (bool)ArtifuscleRotsField.GetValue(settings);
		}
		return Current.Game.GetComponent<DeadMansSwitchIISettingsComponent>().ArtifuscleRots;
	}

	private static IEnumerable<CodeInstruction> DmsRotSettingTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		int replaced = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.LoadsField(ArtifuscleRotsField))
			{
				replaced++;
				yield return new CodeInstruction(instruction)
				{
					opcode = OpCodes.Call,
					operand = AccessTools.DeclaredMethod(typeof(DeadMansSwitchII), "SharedArtifuscleRots")
				};
			}
			else
			{
				yield return instruction;
			}
		}
		if (replaced != 1)
		{
			throw new InvalidOperationException($"DMS artifuscle setting IL changed: {replaced} reads");
		}
	}
}
