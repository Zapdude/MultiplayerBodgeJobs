using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("BreadMo.Cinders")]
public class CindersOfTheEmbergarden
{
	private sealed class ThreatBatchScope
	{
		internal int Previous;

		internal int Batch;

		internal CindersPendingThreatsComponent State;
	}

	private sealed class StorySettingsState
	{
		internal object Settings;

		internal int Min;

		internal int Max;

		internal int SharedMin;

		internal int SharedMax;
	}

	private sealed class ThreatScope
	{
		internal PatchingUtilities.TimeSnapshot? Clock;

		internal Map Map;

		internal bool Pushed;
	}

	private sealed class ResurrectionSave
	{
		internal List<Hediff> Pending;
	}

	private sealed class IdleTimestampState
	{
		internal bool ValidMinusOne;
	}

	private const int LegacyEncounterTimeoutTicks = 30000;

	[ThreadStatic]
	private static int currentThreatBatch;

	[ThreadStatic]
	private static int handlingEncounterDepth;

	[ThreadStatic]
	private static int worldCycleDepth;

	private static Func<bool> initializingCaches;

	private static FieldInfo tickingMap;

	private static Action<Map, Faction, bool> pushFaction;

	private static Func<Map, Faction> popFaction;

	private static readonly ConditionalWeakTable<GameComponent, ResurrectionSave> ResurrectionSaves = new ConditionalWeakTable<GameComponent, ResurrectionSave>();

	private static readonly ConditionalWeakTable<ThingComp, IdleTimestampState> IdleTimestamps = new ConditionalWeakTable<ThingComp, IdleTimestampState>();

	private static FieldInfo idleTimestamp;

	private static bool Intercept
	{
		get
		{
			if (MP.IsInMultiplayer && MP.InInterface)
			{
				return !MP.IsExecutingSyncCommand;
			}
			return false;
		}
	}

	private static CindersPendingThreatsComponent PendingThreats => Current.Game?.GetComponent<CindersPendingThreatsComponent>();

	private static object MultiplayerGame => AccessTools.Field(AccessTools.TypeByName("Multiplayer.Client.Multiplayer"), "game").GetValue(null);

	private static object FactionStories
	{
		get
		{
			object multiplayerGame = MultiplayerGame;
			if (multiplayerGame == null)
			{
				return null;
			}
			return Get(Get(multiplayerGame, "worldComp"), "factionData");
		}
	}

	private static Type CycleType => T("Storytelling.EmberCycleGame");

	private static object Cycle => Current.Game?.components.FirstOrDefault((GameComponent c) => c.GetType() == CycleType);

	private static bool InitializingCaches => (initializingCaches ?? (initializingCaches = (Func<bool>)AccessTools.PropertyGetter(AccessTools.TypeByName("Multiplayer.Client.LoadPatch"), "IsInitializingCaches").CreateDelegate(typeof(Func<bool>))))();

	private static object StorySettings => AccessTools.Field(T("Storytelling.CinderStorytellerMod"), "Settings").GetValue(null);

	internal static int LocalCalmMin => ((int?)GetLocalStorySetting("CalmMinDays")) ?? 15;

	internal static int LocalCalmMax => ((int?)GetLocalStorySetting("CalmMaxDays")) ?? 30;

	private static object ResurrectionAdaptor => Current.Game?.components.FirstOrDefault((GameComponent c) => c.GetType() == T("GameComponent_ResurrectAdaptor"));

	private static Type T(string name)
	{
		return AccessTools.TypeByName("Embergarden." + name) ?? throw new TypeLoadException("Embergarden." + name);
	}

	private static object Get(object obj, string name)
	{
		object? obj2 = AccessTools.Field(obj.GetType(), name)?.GetValue(obj);
		if (obj2 == null)
		{
			PropertyInfo propertyInfo = AccessTools.Property(obj.GetType(), name);
			if ((object)propertyInfo == null)
			{
				return null;
			}
			obj2 = propertyInfo.GetValue(obj);
		}
		return obj2;
	}

	private static void Set(object obj, string name, object value)
	{
		AccessTools.Field(obj.GetType(), name).SetValue(obj, value);
	}

	private static object Call(object obj, string name, params object[] args)
	{
		return AccessTools.Method(obj.GetType(), name).Invoke(obj, args);
	}

	private static void Patch(MethodBase method, string prefix = null, string postfix = null, string transpiler = null, string finalizer = null)
	{
		if (method == null)
		{
			throw new MissingMethodException("Cinders 1.7 patch target missing");
		}
		MpCompat.harmony.Patch(method, Hook(prefix), Hook(postfix), Hook(transpiler), Hook(finalizer));
		static HarmonyMethod Hook(string name)
		{
			if (name != null)
			{
				return new HarmonyMethod(typeof(CindersOfTheEmbergarden), name);
			}
			return null;
		}
	}

	public CindersOfTheEmbergarden(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(Install);
	}

	private static void Install()
	{
		CommandAuthorityBridge.Install();
		PatchingUtilities.SetupAsyncTime();
		InstallStartupFingerprint();
		string[] array = new string[4] { "TurretCommand", "SelectTurret", "TransformDowned", "AcknowledgePhase" };
		foreach (string methodOrPropertyName in array)
		{
			MP.RegisterSyncMethod(typeof(CindersOfTheEmbergarden), methodOrPropertyName).SetVersion(13);
		}
		MP.RegisterSyncMethod(typeof(CindersOfTheEmbergarden), "SetStorySettings").SetHostOnly().SetVersion(13);
		MP.RegisterSyncMethod(T("CompSecondaryVerb"), "SwitchVerb").SetVersion(13);
		MP.RegisterSyncMethod(T("Comp_TurretTransformableAbstract"), "TryTransform").SetVersion(13);
		Patch(AccessTools.Method(T("CompSecondaryVerb"), "SwitchVerb"), "OwnedCompCommand");
		Patch(AccessTools.Method(T("Comp_TurretTransformableAbstract"), "TryTransform"), "OwnedCompCommand");
		Patch(AccessTools.Method(T("SubTurret"), "SwitchAutoFire"), "TurretSwitch");
		Patch(AccessTools.Method(T("SubTurret"), "ClearTarget"), "TurretClear");
		Patch(AccessTools.Method(T("SubTurret"), "Targetting"), "TurretTarget");
		Patch(AccessTools.Method(T("SubturretGizmo"), "GetTurretOptions"), null, "TurretOptions");
		Patch(AccessTools.Method(T("CompTransformWhenDowned"), "CompGetGizmosExtra"), null, "DownedGizmos");
		InstallRuntime();
		InstallStory();
	}

	private static ThingComp TurretOwner(object turret)
	{
		return (Get(turret, "parent") as ThingWithComps)?.AllComps.FirstOrDefault((ThingComp comp) => comp.GetType() == T("CompMultipleTurretGun") && ((IList)Get(comp, "turrets")).Contains(turret));
	}

	private static object Turret(ThingComp owner, string id)
	{
		if (owner?.parent == null || owner.parent.Destroyed || !(owner.GetType() == T("CompMultipleTurretGun")))
		{
			return null;
		}
		return ((IEnumerable)Get(owner, "turrets")).Cast<object>().SingleOrDefault((object t) => (string)Get(t, "ID") == id);
	}

	private static bool TurretSwitch(object __instance)
	{
		if (!Intercept)
		{
			return true;
		}
		TurretCommand(TurretOwner(__instance), (string)Get(__instance, "ID"), 0, LocalTargetInfo.Invalid, !(bool)Get(__instance, "fireAtWill"));
		return false;
	}

	private static bool TurretClear(object __instance)
	{
		if (!Intercept)
		{
			return true;
		}
		TurretCommand(TurretOwner(__instance), (string)Get(__instance, "ID"), 1, LocalTargetInfo.Invalid, value: false);
		return false;
	}

	private static bool TurretTarget(object __instance)
	{
		if (!Intercept)
		{
			return true;
		}
		ThingComp owner = TurretOwner(__instance);
		string id = (string)Get(__instance, "ID");
		Find.Targeter.BeginTargeting(((Verb)Get(__instance, "CurrentEffectiveVerb")).targetParams, (LocalTargetInfo target) =>
		{
			TurretCommand(owner, id, 2, target, value: false);
		});
		return false;
	}

	private static void TurretCommand(ThingComp owner, string id, int operation, LocalTargetInfo target, bool value)
	{
		object obj = Turret(owner, id);
		if (obj == null || owner.parent.MapHeld == null || !OwnedCompCommand(owner))
		{
			return;
		}
		switch (operation)
		{
		case 0:
			Set(obj, "fireAtWill", value);
			break;
		case 1:
			Set(obj, "forcedTarget", LocalTargetInfo.Invalid);
			Set(obj, "currentTarget", LocalTargetInfo.Invalid);
			break;
		case 2:
			if (target.IsValid && (!target.HasThing || target.Thing.MapHeld == owner.parent.MapHeld) && target.Cell.InBounds(owner.parent.MapHeld))
			{
				Set(obj, "forcedTarget", target);
				Set(obj, "currentTarget", target);
			}
			break;
		}
	}

	private static void TurretOptions(object __instance, ref IEnumerable<FloatMenuOption> __result)
	{
		__result = WrapTurretOptions((ThingComp)Get(__instance, "comp"), __result);
	}

	private static IEnumerable<FloatMenuOption> WrapTurretOptions(ThingComp owner, IEnumerable<FloatMenuOption> options)
	{
		int index = 0;
		IList turrets = (IList)Get(owner, "turrets");
		foreach (FloatMenuOption option in options)
		{
			if (MP.IsInMultiplayer && index < turrets.Count)
			{
				string id = (string)Get(turrets[index], "ID");
				option.action = () =>
				{
					SelectTurret(owner, id);
				};
			}
			index++;
			yield return option;
		}
	}

	private static void SelectTurret(ThingComp owner, string id)
	{
		if (Turret(owner, id) != null && OwnedCompCommand(owner))
		{
			Set(owner, "currentTurret", id);
		}
	}

	private static bool OwnedCompCommand(ThingComp __instance)
	{
		if (MP.IsInMultiplayer)
		{
			ThingWithComps thingWithComps = __instance?.parent;
			if (thingWithComps != null && !thingWithComps.Destroyed)
			{
				return CommandAuthorityBridge.CanActForFaction(thingWithComps.Faction);
			}
			return false;
		}
		return true;
	}

	private static void DownedGizmos(ThingComp __instance, ref IEnumerable<Gizmo> __result)
	{
		__result = WrapDownedGizmos(__instance, __result);
	}

	private static IEnumerable<Gizmo> WrapDownedGizmos(ThingComp comp, IEnumerable<Gizmo> gizmos)
	{
		foreach (Gizmo gizmo in gizmos)
		{
			if (MP.IsInMultiplayer && gizmo is Command_Action command_Action)
			{
				command_Action.action = () =>
				{
					TransformDowned(comp.parent as Pawn);
				};
			}
			yield return gizmo;
		}
	}

	private static void TransformDowned(Pawn pawn)
	{
		if (pawn == null || pawn.Destroyed || !pawn.Spawned || !pawn.Downed || !CommandAuthorityBridge.CanActForPawn(pawn))
		{
			return;
		}
		(pawn.abilities?.abilities.FirstOrDefault((Ability a) => a.comps.Any((AbilityComp c) => c.GetType() == T("AbilityCompEffect_Transform"))))?.Activate(pawn, pawn);
	}

	private static void InstallRuntime()
	{
		Patch(AccessTools.Method(T("HediffComp_Crush"), "SpawnFlecks"), "VisualBefore", null, null, "VisualAfter");
		Patch(AccessTools.Method(T("CompTentacleSpreader"), "IncrementTime"), null, null, "TentacleEffecterTranspiler");
		Patch(AccessTools.Method(T("Projectile_Tentacle"), "Tick"), null, null, "TentacleColorTranspiler");
		InstallIdleTimestamp();
		Type type = T("SubTurret");
		FieldInfo attack = AccessTools.Field(type, "lastAttackTargetTick");
		PatchingUtilities.RegisterTimestampFields(type, (object obj, int offset) =>
		{
			if (((LocalTargetInfo)Get(obj, "lastAttackedTarget")).IsValid)
			{
				attack.SetValue(obj, (int)attack.GetValue(obj) + offset);
			}
		});
	}

	private static void VisualBefore(out bool __state)
	{
		__state = MP.IsInMultiplayer;
		if (__state)
		{
			Rand.PushState();
		}
	}

	private static Exception VisualAfter(Exception __exception, bool __state)
	{
		if (__state)
		{
			Rand.PopState();
		}
		return __exception;
	}

	private static Effecter SpawnTentacleEffect(EffecterDef def, Thing thing, Map map, float scale)
	{
		bool isInMultiplayer = MP.IsInMultiplayer;
		if (isInMultiplayer)
		{
			Rand.PushState();
		}
		try
		{
			return def.SpawnAttached(thing, map, scale);
		}
		finally
		{
			if (isInMultiplayer)
			{
				Rand.PopState();
			}
		}
	}

	private static void TickTentacleEffect(Effecter effect, TargetInfo source, TargetInfo target)
	{
		bool isInMultiplayer = MP.IsInMultiplayer;
		if (isInMultiplayer)
		{
			Rand.PushState();
		}
		try
		{
			effect.EffectTick(source, target);
		}
		finally
		{
			if (isInMultiplayer)
			{
				Rand.PopState();
			}
		}
	}

	private static IEnumerable<CodeInstruction> TentacleEffecterTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		int spawn = 0;
		int tick = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.operand is MethodInfo methodInfo && methodInfo.DeclaringType == typeof(EffecterDef) && methodInfo.Name == "SpawnAttached")
			{
				spawn++;
				yield return CodeInstruction.Call(typeof(CindersOfTheEmbergarden), "SpawnTentacleEffect").WithLabels(instruction.labels).WithBlocks(instruction.blocks);
			}
			else if (instruction.operand is MethodInfo methodInfo2 && methodInfo2.DeclaringType == typeof(Effecter) && methodInfo2.Name == "EffectTick")
			{
				tick++;
				yield return CodeInstruction.Call(typeof(CindersOfTheEmbergarden), "TickTentacleEffect").WithLabels(instruction.labels).WithBlocks(instruction.blocks);
			}
			else
			{
				yield return instruction;
			}
		}
		if (spawn != 1 || tick != 1)
		{
			throw new InvalidOperationException($"Cinders tentacle visual IL changed: {spawn}/{tick}");
		}
	}

	private static float TentacleColor(Thing projectile)
	{
		if (!MP.IsInMultiplayer)
		{
			return Rand.Value;
		}
		return (float)(Gen.HashCombineInt(projectile.thingIDNumber, 105303) & 0xFFFFFF) / 16777216f;
	}

	private static IEnumerable<CodeInstruction> TentacleColorTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		int count = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(AccessTools.PropertyGetter(typeof(Rand), "Value")))
			{
				count++;
				yield return new CodeInstruction(OpCodes.Ldarg_0).WithLabels(instruction.labels).WithBlocks(instruction.blocks);
				yield return CodeInstruction.Call(typeof(CindersOfTheEmbergarden), "TentacleColor");
			}
			else
			{
				yield return instruction;
			}
		}
		if (count != 1)
		{
			throw new InvalidOperationException($"Cinders tentacle colour IL changed: {count}");
		}
	}

	private static void InstallPendingThreats()
	{
		Patch(AccessTools.Method(T("Storytelling.StorytellerComp_EmberCycle"), "TryFireThreat"), null, null, "QueueThreatIl");
		Patch(AccessTools.Method(T("Storytelling.StorytellerComp_EmberCycle"), "TryFireForcedMechOrCradle"), null, null, "QueueThreatIl");
		Patch(AccessTools.Method(T("Storytelling.BattleEncounter"), "IsClear"), "EncounterClockBefore", null, null, "ThreatClockAfter");
		Patch(AccessTools.Method(T("Storytelling.BattleEncounter"), "IsClear"), "PendingEncounterBefore");
		Patch(AccessTools.Method(T("Storytelling.BattleEncounter"), "IsTimedOut"), "EncounterTimeoutBefore", "EncounterTimeoutAfter");
		Patch(AccessTools.Method(CycleType, "FireBatch"), "ThreatBatchBefore", null, null, "ThreatBatchAfter");
		Patch(AccessTools.Method(CycleType, "HandleEncounter"), "HandleEncounterBefore", null, null, "HandleEncounterAfter");
		Patch(AccessTools.Method(CycleType, "BeginBattle"), "BeginBattleEpoch");
	}

	private static void BeginBattleEpoch()
	{
		if (MP.IsInMultiplayer)
		{
			CindersPendingThreatsComponent pendingThreats = PendingThreats;
			if (pendingThreats != null)
			{
				pendingThreats.BattleEpoch++;
			}
		}
	}

	private static void HandleEncounterBefore(out int __state)
	{
		__state = handlingEncounterDepth;
		if (MP.IsInMultiplayer)
		{
			handlingEncounterDepth++;
		}
	}

	private static Exception HandleEncounterAfter(Exception __exception, int __state)
	{
		handlingEncounterDepth = __state;
		return __exception;
	}

	private static void ThreatBatchBefore(out ThreatBatchScope __state)
	{
		__state = null;
		if (MP.IsInMultiplayer)
		{
			CindersPendingThreatsComponent pendingThreats = PendingThreats;
			if (pendingThreats != null)
			{
				__state = new ThreatBatchScope
				{
					Previous = currentThreatBatch,
					Batch = pendingThreats.NextBatch++,
					State = pendingThreats
				};
				currentThreatBatch = __state.Batch;
			}
		}
	}

	private static Exception ThreatBatchAfter(object __instance, int __2, Exception __exception, ThreatBatchScope __state)
	{
		if (__state != null)
		{
			try
			{
				IEnumerable<CindersPendingThreat> enumerable = __state.State.Pending.Where((CindersPendingThreat p) => p.Batch == __state.Batch);
				if (__exception == null && enumerable.Any())
				{
					__state.State.WorldBatch = __state.Batch;
					object obj = Get(__instance, "encounter");
					if (obj != null)
					{
						int num = (int)Get(obj, "timeoutTick") - __2;
						foreach (CindersPendingThreat item in enumerable)
						{
							if (item.Battle && num > 0)
							{
								item.EncounterTimeoutTicks = num;
							}
						}
					}
				}
			}
			finally
			{
				currentThreatBatch = __state.Previous;
			}
		}
		return __exception;
	}

	private static IEnumerable<CindersPendingThreat> WorldEncounterJobs(object encounter)
	{
		if (MP.IsInMultiplayer)
		{
			object cycle = Cycle;
			if (cycle != null && Get(cycle, "encounter") == encounter)
			{
				CindersPendingThreatsComponent state = PendingThreats;
				if (state != null)
				{
					return state.Pending.Where((CindersPendingThreat p) => p.Batch == state.WorldBatch);
				}
			}
		}
		return Enumerable.Empty<CindersPendingThreat>();
	}

	private static bool PendingEncounterBefore(object __instance, ref bool __result)
	{
		if (!WorldEncounterJobs(__instance).Any((CindersPendingThreat p) => !p.Dispatched))
		{
			return true;
		}
		__result = false;
		return false;
	}

	private static bool EncounterTimeoutBefore(object __instance, ref bool __result)
	{
		CindersPendingThreat[] array = WorldEncounterJobs(__instance).ToArray();
		if (array.Length == 0 || array.Any((CindersPendingThreat p) => p.WorldEncounterStarted))
		{
			return true;
		}
		__result = false;
		return false;
	}

	private static void EncounterTimeoutAfter(object __instance, bool __result)
	{
		if (!__result || handlingEncounterDepth == 0)
		{
			return;
		}
		CindersPendingThreatsComponent pendingThreats = PendingThreats;
		if (pendingThreats == null)
		{
			return;
		}
		object cycle = Cycle;
		if (cycle == null || !(bool)Get(Get(cycle, "Schedule"), "Battle"))
		{
			return;
		}
		CindersPendingThreat[] array = (from p in WorldEncounterJobs(__instance)
			orderby p.Sequence
			select p).ToArray();
		if (array.Length != 0)
		{
			if (!pendingThreats.TimedOutPhases.Contains(array[0].Phase))
			{
				pendingThreats.TimedOutPhases.Add(array[0].Phase);
			}
			CindersPendingThreat cindersPendingThreat = array.FirstOrDefault((CindersPendingThreat p) => p.Dispatched);
			if (cindersPendingThreat != null)
			{
				cindersPendingThreat.TimeoutReleased = true;
			}
		}
	}

	private static void EncounterClockBefore(object __instance, out ThreatScope __state)
	{
		ThreatClockBefore(Get(__instance, "map") as Map, out __state);
	}

	private static IEnumerable<CodeInstruction> QueueThreatIl(IEnumerable<CodeInstruction> instructions)
	{
		int count = 0;
		MethodInfo fire = AccessTools.Method(typeof(Storyteller), "TryFire", new Type[2]
		{
			typeof(FiringIncident),
			typeof(bool)
		});
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(fire))
			{
				count++;
				yield return CodeInstruction.Call(typeof(CindersOfTheEmbergarden), "QueueThreat").WithLabels(instruction.labels).WithBlocks(instruction.blocks);
			}
			else
			{
				yield return instruction;
			}
		}
		if (count != 1)
		{
			throw new InvalidOperationException($"Cinders threat dispatch IL changed: {count}");
		}
	}

	private static bool QueueThreat(Storyteller storyteller, FiringIncident incident, bool queued)
	{
		if (!MP.IsInMultiplayer)
		{
			return storyteller.TryFire(incident, queued);
		}
		if (InitializingCaches || !(incident?.parms?.target is Map { Disposed: false } map) || PendingThreats == null)
		{
			return false;
		}
		object cycle = Cycle;
		object obj = ((cycle == null) ? null : Get(cycle, "Schedule"));
		PendingThreats.Pending.Add(new CindersPendingThreat
		{
			Sequence = PendingThreats.NextSequence++,
			Map = map,
			Recipient = map.ParentFaction,
			Incident = incident,
			QueuedWorldTick = CinderWorldTicks(),
			WasQueued = queued,
			Batch = currentThreatBatch,
			Battle = (obj != null && (bool)Get(obj, "Battle")),
			Phase = PendingThreats.BattleEpoch,
			IgnoreExistingDanger = (cycle != null && (bool)Get(cycle, "pastEncounterTimeout"))
		});
		return true;
	}

	private static void ProcessPendingThreats(Map map)
	{
		CindersPendingThreatsComponent pendingThreats = PendingThreats;
		if (pendingThreats == null)
		{
			return;
		}
		CindersPendingThreat job = (from p in pendingThreats.Pending
			where p.Map == map
			orderby p.Sequence
			select p).FirstOrDefault();
		if (job == null)
		{
			return;
		}
		if (map.Disposed || job.Recipient != map.ParentFaction || job.Incident?.def == null)
		{
			pendingThreats.Pending.Remove(job);
			return;
		}
		if (job.Dispatched)
		{
			if (job.TimeoutReleased || job.Encounter == null || (bool)Call(job.Encounter, "IsClear", CinderWorldTicks()))
			{
				pendingThreats.Pending.Remove(job);
			}
			return;
		}
		Storyteller storyteller = StoryForMap(map);
		if (storyteller == null)
		{
			return;
		}
		bool flag = job.IgnoreExistingDanger || pendingThreats.TimedOutPhases.Contains(job.Phase);
		IExposable exposable = (IExposable)Activator.CreateInstance(T("Storytelling.BattleEncounter"), map, flag);
		int num = ((job.EncounterTimeoutTicks > 0) ? job.EncounterTimeoutTicks : 30000);
		if (job.Battle)
		{
			Call(exposable, "SetTimeout", CinderWorldTicks(), num);
		}
		object cycle = Cycle;
		if (cycle != null)
		{
			object obj = Get(cycle, "encounter");
			if (obj != null && job.Batch == pendingThreats.WorldBatch && !pendingThreats.Pending.Any((CindersPendingThreat p) => p.Batch == job.Batch && p.WorldEncounterStarted))
			{
				if (job.Battle)
				{
					Call(obj, "SetTimeout", CinderWorldTicks(), num);
				}
				foreach (CindersPendingThreat item in pendingThreats.Pending.Where((CindersPendingThreat p) => p.Batch == job.Batch))
				{
					item.WorldEncounterStarted = true;
				}
			}
		}
		job.Incident.source = storyteller.storytellerComps?.FirstOrDefault((StorytellerComp c) => c.GetType() == T("Storytelling.StorytellerComp_EmberCycle"));
		job.Dispatched = true;
		try
		{
			if (storyteller.TryFire(job.Incident, job.WasQueued))
			{
				job.Encounter = exposable;
			}
			else
			{
				pendingThreats.Pending.Remove(job);
			}
		}
		catch
		{
			pendingThreats.Pending.Remove(job);
			throw;
		}
	}

	private static void InstallStartupFingerprint()
	{
		StartupDefFingerprint.Register("MPCompat.Cinders.StartupBalance", LoadedBalanceChecksum);
	}

	private static int LoadedBalanceChecksum()
	{
		List<object> values = new List<object>();
		Add<HediffDef>("Cinder_UncontrollableTentacles Cinder_Regen Cinder_Resurrect Cinder_LongDream", new string[3] { "stages", "comps", "modExtensions" });
		Add<HediffDef>("Cinder_ResurrectComa Cinder_NeuralCrystallizationDissociation", new string[3] { "stages", "initialSeverity", "maxSeverity" });
		Add<RecipeDef>("Make_Cinder_RegeneratedEnhancedSerum", new string[1] { "ingredients" });
		Add<ThoughtDef>("Cinder_NCD", new string[1] { "stages" });
		Add<AbilityDef>("Cinder_EternalFearAbility Cinder_FearPulse Cinder_BurnHemostasis Cinder_AmorphousIED Cinder_AmorphousWinding Cinder_ErodedBomb Cinder_SuffocatingWhispers Cinder_LaunchHellfireMissile Cinder_LaunchHellfireMissileB", new string[2] { "cooldownTicksRange", "charges" });
		Add<ThingDef>("Cinder_Bullet_RPG CinderBullet_Hellfire Cinder_TentacleGourdProjB", new string[2] { "projectile", "comps" });
		Add<ThingDef>("Cinder_Trailer Cinder_Mech_Krepost Cinder_Mech_Tombstone Cinder_Mech_ErodedEXO Cinder_Mech_WarAttacker Cinder_Mech_WarSweeper Cinder_Mech_WalkerAutoCannon Cinder_Mech_WalkerSniper Cinder_Mech_MiniTurretWalker Cinder_erodedKrepostBuliding", new string[4] { "statBases", "race.baseHungerRate", "race.baseHealthScale", "comps" });
		Add<DamageDef>("Cinder_BombB", new string[1] { "externalViolence" });
		Add<PawnKindDef>("Cinder_Mech_Krepost Cinder_Mech_MPTombstone Cinder_Mech_ErodedEXO Cinder_Mech_WalkerAutoCannon Cinder_Mech_WalkerSniper Cinder_Mech_MiniTurretWalker Cinder_Mech_Tombstone", new string[3] { "startingHediffs", "combatPower", "maxPerGroup" });
		Add<BodyPartDef>("Cinder_MechanicalThorax Cinder_MechanicalThoraxB", new string[1] { "hitPoints" });
		return StartupDefFingerprint.Hash(values);
		void Add<T>(string names, params string[] paths) where T : Def
		{
			string[] array = names.Split(' ');
			foreach (string text in array)
			{
				T namedSilentFail = DefDatabase<T>.GetNamedSilentFail(text);
				values.Add(typeof(T).FullName + ":" + text);
				foreach (string text2 in paths)
				{
					values.Add(text2);
					object obj = namedSilentFail;
					string[] array2 = text2.Split('.');
					foreach (string name in array2)
					{
						obj = ((obj == null) ? null : AccessTools.Field(obj.GetType(), name)?.GetValue(obj));
					}
					values.Add(obj);
				}
			}
		}
	}

	private static bool IsOwnerTick(Map map)
	{
		return (tickingMap ?? (tickingMap = AccessTools.Field(AccessTools.TypeByName("Multiplayer.Client.AsyncTimeComp"), "tickingMap"))).GetValue(null) == map;
	}

	private static object GetLocalStorySetting(string name)
	{
		Type type = AccessTools.TypeByName("Embergarden.Storytelling.CinderStorytellerMod");
		object obj = ((type == null) ? null : AccessTools.Field(type, "Settings")?.GetValue(null));
		if (obj != null)
		{
			return Get(obj, name);
		}
		return null;
	}

	private static void InstallStory()
	{
		Patch(AccessTools.Method(CycleType, "GameComponentUpdate"), "CycleUpdateBefore", null, "CycleUpdateTranspiler");
		Patch(AccessTools.Method(CycleType, "Advance"), "CycleAdvanceBefore");
		Patch(AccessTools.Method(CycleType, "EnsureInitialized"), "CycleInitializeBefore");
		Patch(AccessTools.Method(typeof(GameComponent), "GameComponentTick"), null, "CycleTick");
		Patch(AccessTools.Method(CycleType, "AcknowledgeDialog"), "AcknowledgeBefore");
		Patch(AccessTools.Method(CycleType, "StartCalm"), "CalmSettingsBefore", null, null, "CalmSettingsAfter");
		Patch(AccessTools.Method(T("Storytelling.StorytellerSettings"), "Draw"), "SettingsBefore", null, null, "SettingsAfter");
		Patch(AccessTools.Method(T("Storytelling.StorytellerComp_EmberCycle"), "TryFireThreat"), "ThreatClockBefore", null, null, "ThreatClockAfter");
		Patch(AccessTools.Method(T("Storytelling.StorytellerComp_EmberCycle"), "TryFireForcedMechOrCradle"), "ThreatClockBefore", null, null, "ThreatClockAfter");
		InstallPendingThreats();
		Patch(AccessTools.PropertyGetter(CycleType, "Selected"), "SelectedStory");
		Patch(AccessTools.Method(CycleType, "CycleSource"), "SharedCycleSource");
		Patch(AccessTools.Method(CycleType, "ToggleCycle"), "ToggleWorldTime");
		foreach (Type item in new Type[1] { CycleType }.Concat(CycleType.GetNestedTypes(AccessTools.all)))
		{
			foreach (MethodInfo declaredMethod in AccessTools.GetDeclaredMethods(item))
			{
				if (declaredMethod.GetMethodBody() != null && PatchProcessor.GetOriginalInstructions(declaredMethod).Any((CodeInstruction i) => i.Calls(AccessTools.PropertyGetter(typeof(Map), "IsPlayerHome"))))
				{
					Patch(declaredMethod, null, null, "EligibleHomesTranspiler");
				}
			}
		}
		Type[] array = new Type[3]
		{
			CycleType,
			T("Storytelling.DialogueContext"),
			T("Storytelling.EmberCycleDebug")
		};
		for (int num = 0; num < array.Length; num++)
		{
			foreach (MethodInfo declaredMethod2 in AccessTools.GetDeclaredMethods(array[num]))
			{
				if (declaredMethod2.GetMethodBody() != null && PatchProcessor.GetOriginalInstructions(declaredMethod2).Any((CodeInstruction i) => i.Calls(AccessTools.PropertyGetter(typeof(TickManager), "TicksGame"))))
				{
					Patch(declaredMethod2, null, null, "CinderWorldClock");
				}
				if (declaredMethod2.GetMethodBody() != null && PatchProcessor.GetOriginalInstructions(declaredMethod2).Any((CodeInstruction i) => i.Calls(AccessTools.PropertyGetter(typeof(Find), "Storyteller"))))
				{
					Patch(declaredMethod2, null, null, "CycleStoryTranspiler");
				}
			}
		}
		Patch(AccessTools.Method(T("GameComponent_ResurrectAdaptor"), "GameComponentTick"), "ResurrectionWorldTick");
		Patch(AccessTools.Method(typeof(Map), "MapPostTick"), null, "ResurrectionMapTick");
		Patch(AccessTools.Method(typeof(GameComponent), "ExposeData"), null, "ResurrectionExpose");
	}

	private static Storyteller StoryForMap(Map map)
	{
		if (!(FactionStories is IDictionary dictionary) || map?.ParentFaction == null)
		{
			return Find.Storyteller;
		}
		object obj = dictionary[map.ParentFaction.loadID];
		if (obj != null)
		{
			return Get(obj, "storyteller") as Storyteller;
		}
		return null;
	}

	private static bool EligibleHome(Map map)
	{
		if (!MP.IsInMultiplayer || MultiplayerGame == null)
		{
			return map.IsPlayerHome;
		}
		if (map != null && map.ParentFaction?.IsPlayer == true && map.Parent != null && (map.wasSpawnedViaGravShipLanding || map.Parent.def.canBePlayerHome))
		{
			return StoryForMap(map)?.def?.defName == "Cinder_EmberWarden";
		}
		return false;
	}

	private static bool SelectedStory(ref bool __result)
	{
		if (!MP.IsInMultiplayer || MultiplayerGame == null)
		{
			return true;
		}
		__result = Find.Maps.Any(EligibleHome);
		return false;
	}

	private static bool SharedCycleSource(ref object __result)
	{
		if (!MP.IsInMultiplayer || MultiplayerGame == null)
		{
			return true;
		}
		__result = (from map in Find.Maps.Where(EligibleHome)
			orderby map.uniqueID
			select map).Select(StoryForMap).SelectMany((Storyteller story) => story.storytellerComps).FirstOrDefault((StorytellerComp comp) => comp.GetType() == T("Storytelling.StorytellerComp_EmberCycle"));
		return false;
	}

	private static Storyteller CycleStoryteller()
	{
		if (!MP.IsInMultiplayer || MultiplayerGame == null)
		{
			return Find.Storyteller;
		}
		return (from map in Find.Maps.Where(EligibleHome)
			orderby map.uniqueID
			select map).Select(StoryForMap).FirstOrDefault() ?? Find.Storyteller;
	}

	private static IEnumerable<CodeInstruction> CycleStoryTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		foreach (CodeInstruction instruction in instructions)
		{
			yield return instruction.Calls(AccessTools.PropertyGetter(typeof(Find), "Storyteller")) ? CodeInstruction.Call(typeof(CindersOfTheEmbergarden), "CycleStoryteller").WithLabels(instruction.labels).WithBlocks(instruction.blocks) : instruction;
		}
	}

	private static void ToggleWorldTime(ref int now)
	{
		if (MP.IsInMultiplayer)
		{
			now = CinderWorldTicks();
		}
	}

	private static IEnumerable<CodeInstruction> EligibleHomesTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		foreach (CodeInstruction instruction in instructions)
		{
			yield return instruction.Calls(AccessTools.PropertyGetter(typeof(Map), "IsPlayerHome")) ? CodeInstruction.Call(typeof(CindersOfTheEmbergarden), "EligibleHome").WithLabels(instruction.labels).WithBlocks(instruction.blocks) : instruction;
		}
	}

	private static bool CycleAdvanceBefore()
	{
		if (MP.IsInMultiplayer)
		{
			return worldCycleDepth > 0;
		}
		return true;
	}

	private static bool CycleInitializeBefore()
	{
		if (MP.IsInMultiplayer && worldCycleDepth <= 0)
		{
			return MP.IsExecutingSyncCommand;
		}
		return true;
	}

	private static bool CycleUpdateBefore(object __instance)
	{
		if (MP.IsInMultiplayer && (int)Get(__instance, "pendingDialog") != 0)
		{
			return Get(__instance, "dialogue") != null;
		}
		return true;
	}

	private static void ExpiredFromUpdate(object owner)
	{
		if (!MP.IsInMultiplayer)
		{
			Call(owner, "CheckExpiredCalm");
		}
	}

	private static IEnumerable<CodeInstruction> CycleUpdateTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		int count = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(AccessTools.Method(CycleType, "CheckExpiredCalm")))
			{
				count++;
				yield return CodeInstruction.Call(typeof(CindersOfTheEmbergarden), "ExpiredFromUpdate").WithLabels(instruction.labels).WithBlocks(instruction.blocks);
			}
			else
			{
				yield return instruction;
			}
		}
		if (count != 1)
		{
			throw new InvalidOperationException($"Cinders Update schedule IL changed: {count}");
		}
	}

	private static void CycleTick(GameComponent __instance)
	{
		if (!MP.IsInMultiplayer || __instance.GetType() != CycleType || InitializingCaches || (MultiplayerGame != null && !Find.Maps.Any(EligibleHome)))
		{
			return;
		}
		worldCycleDepth++;
		try
		{
			Call(__instance, "CheckExpiredCalm");
			if (CinderWorldTicks() % 1000 == 0 && Find.Storyteller != null)
			{
				object obj = AccessTools.Method(CycleType, "CycleSource").Invoke(null, null);
				if (obj != null)
				{
					Call(__instance, "Advance", obj);
				}
			}
			int num = (int)Get(__instance, "pendingDialog");
			if (num != 0 && Get(__instance, "dialogue") == null)
			{
				object obj2 = AccessTools.Method(T("Storytelling.DialogueContext"), "Capture").Invoke(null, new object[2]
				{
					__instance,
					Get(Get(__instance, "Schedule"), "WaveGoal")
				});
				Set(__instance, "dialogue", AccessTools.Method(T("Storytelling.DialoguePlayback"), "Create").Invoke(null, new object[2]
				{
					num == 1,
					obj2
				}));
			}
		}
		finally
		{
			worldCycleDepth--;
		}
	}

	private static int CinderWorldTicks()
	{
		if (!MP.IsInMultiplayer)
		{
			return Find.TickManager.TicksGame;
		}
		return FortifiedFeaturesFramework.WorldTicks();
	}

	private static IEnumerable<CodeInstruction> CinderWorldClock(IEnumerable<CodeInstruction> instructions)
	{
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(AccessTools.PropertyGetter(typeof(TickManager), "TicksGame")))
			{
				yield return new CodeInstruction(OpCodes.Pop).WithLabels(instruction.labels).WithBlocks(instruction.blocks);
				yield return CodeInstruction.Call(typeof(CindersOfTheEmbergarden), "CinderWorldTicks");
			}
			else
			{
				yield return instruction;
			}
		}
	}

	private static bool AcknowledgeBefore(object __instance, object expected)
	{
		if (!Intercept)
		{
			return true;
		}
		if (Get(__instance, "dialogue") == expected)
		{
			object obj = Get(__instance, "Schedule");
			AcknowledgePhase((int)Get(__instance, "pendingDialog"), (int)Get(obj, "CalmEndTick"), (int)Get(obj, "WaveGoal"), (int)Get(obj, "WavesSent"));
		}
		return false;
	}

	private static void AcknowledgePhase(int pending, int calmEnd, int goal, int waves)
	{
		object cycle = Cycle;
		if (cycle != null && pending != 0)
		{
			object obj = Get(cycle, "Schedule");
			if ((int)Get(cycle, "pendingDialog") == pending && (int)Get(obj, "CalmEndTick") == calmEnd && (int)Get(obj, "WaveGoal") == goal && (int)Get(obj, "WavesSent") == waves)
			{
				Call(cycle, "AcknowledgeDialog", Get(cycle, "dialogue"));
			}
		}
	}

	private static void CalmSettingsBefore(out StorySettingsState __state)
	{
		__state = null;
		if (MP.IsInMultiplayer && Current.Game != null && StorySettings != null)
		{
			CindersStorySettingsComponent component = Current.Game.GetComponent<CindersStorySettingsComponent>();
			__state = new StorySettingsState
			{
				Settings = StorySettings,
				Min = LocalCalmMin,
				Max = LocalCalmMax
			};
			Set(__state.Settings, "CalmMinDays", component.MinDays);
			Set(__state.Settings, "CalmMaxDays", component.MaxDays);
		}
	}

	private static Exception CalmSettingsAfter(Exception __exception, StorySettingsState __state)
	{
		if (__state != null)
		{
			Set(__state.Settings, "CalmMinDays", __state.Min);
			Set(__state.Settings, "CalmMaxDays", __state.Max);
		}
		return __exception;
	}

	private static void SettingsBefore(out StorySettingsState __state)
	{
		CalmSettingsBefore(out __state);
		if (__state != null)
		{
			__state.SharedMin = (int)Get(__state.Settings, "CalmMinDays");
			__state.SharedMax = (int)Get(__state.Settings, "CalmMaxDays");
		}
	}

	private static Exception SettingsAfter(Exception __exception, StorySettingsState __state)
	{
		if (__state == null)
		{
			return __exception;
		}
		int num = (int)Get(__state.Settings, "CalmMinDays");
		int num2 = (int)Get(__state.Settings, "CalmMaxDays");
		CalmSettingsAfter(__exception, __state);
		if (__exception == null && MP.IsHosting && Intercept && (num != __state.SharedMin || num2 != __state.SharedMax))
		{
			SetStorySettings(num, num2);
		}
		return __exception;
	}

	private static void SetStorySettings(int min, int max)
	{
		if (Current.Game != null && min >= 1 && min <= max && max <= 120)
		{
			CindersStorySettingsComponent component = Current.Game.GetComponent<CindersStorySettingsComponent>();
			component.MinDays = min;
			component.MaxDays = max;
		}
	}

	private static void ThreatClockBefore(Map map, out ThreatScope __state)
	{
		__state = null;
		if (!MP.IsInMultiplayer)
		{
			return;
		}
		__state = new ThreatScope
		{
			Map = map
		};
		if (MultiplayerGame != null && map != null && map.ParentFaction?.IsPlayer == true)
		{
			Type type = AccessTools.TypeByName("Multiplayer.Client.Factions.FactionExtensions");
			if (pushFaction == null)
			{
				pushFaction = AccessTools.Method(type, "PushFaction", new Type[3]
				{
					typeof(Map),
					typeof(Faction),
					typeof(bool)
				}).CreateDelegate<Action<Map, Faction, bool>>();
			}
			if (popFaction == null)
			{
				popFaction = AccessTools.Method(type, "PopFaction", new Type[1] { typeof(Map) }).CreateDelegate<Func<Map, Faction>>();
			}
			pushFaction(map, map.ParentFaction, arg3: true);
			__state.Pushed = true;
		}
		__state.Clock = PatchingUtilities.TimeSnapshot.GetAndSetFromMap(map);
	}

	private static Exception ThreatClockAfter(Exception __exception, ThreatScope __state)
	{
		if (__state != null)
		{
			try
			{
				__state.Clock?.Set();
			}
			finally
			{
				if (__state.Pushed)
				{
					popFaction(__state.Map);
				}
			}
		}
		return __exception;
	}

	private static void ProcessResurrections(Map map)
	{
		object resurrectionAdaptor = ResurrectionAdaptor;
		if (resurrectionAdaptor == null)
		{
			return;
		}
		IList list = (IList)Get(resurrectionAdaptor, "hediffs");
		HediffComp[] array = list.Cast<HediffComp>().ToArray();
		foreach (HediffComp hediffComp in array)
		{
			if ((hediffComp.Pawn?.Corpse?.MapHeld ?? hediffComp.Pawn?.MapHeld) == map)
			{
				Call(hediffComp, "Resurrect");
				list.Remove(hediffComp);
			}
		}
	}

	private static bool ResurrectionWorldTick()
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (!InitializingCaches)
		{
			ProcessResurrections(null);
		}
		return false;
	}

	private static void ResurrectionMapTick(Map __instance)
	{
		if (MP.IsInMultiplayer && !InitializingCaches && IsOwnerTick(__instance))
		{
			ProcessResurrections(__instance);
			ProcessPendingThreats(__instance);
		}
	}

	private static void ResurrectionExpose(GameComponent __instance)
	{
		if (__instance.GetType() != T("GameComponent_ResurrectAdaptor"))
		{
			return;
		}
		ResurrectionSave value = ResurrectionSaves.GetValue(__instance, (GameComponent _) => new ResurrectionSave());
		IList list = (IList)Get(__instance, "hediffs");
		if (Scribe.mode == LoadSaveMode.Saving)
		{
			value.Pending = list.Cast<HediffComp>().Select((Func<HediffComp, Hediff>)((HediffComp comp) => comp.parent)).ToList();
		}
		Scribe_Collections.Look(ref value.Pending, "mpCindersPendingResurrections", LookMode.Reference);
		if (Scribe.mode != LoadSaveMode.PostLoadInit)
		{
			return;
		}
		list.Clear();
		IEnumerable<Hediff> pending = value.Pending;
		foreach (Hediff item in pending ?? Enumerable.Empty<Hediff>())
		{
			HediffComp hediffComp = ((item is HediffWithComps) ? ((IEnumerable)Get(item, "comps")).Cast<HediffComp>().FirstOrDefault((HediffComp c) => c.GetType() == T("HediffComp_LimitedResurrect")) : null);
			if (hediffComp != null && !list.Contains(hediffComp))
			{
				list.Add(hediffComp);
			}
		}
	}

	private static void InstallIdleTimestamp()
	{
		Type type = T("Comp_TurretTransformableAbstract");
		idleTimestamp = AccessTools.Field(type, "lastHaveTargetTick");
		PatchingUtilities.RegisterTimestampFields(type, FixIdleTimestamp);
		Patch(AccessTools.Method(type, "TransformTick"), null, null, "IdleTimestampIl");
		Patch(AccessTools.Method(type, "PostExposeData"), null, "IdleTimestampExpose");
	}

	private static void FixIdleTimestamp(object owner, int offset)
	{
		ThingComp thingComp = (ThingComp)owner;
		IdleTimestampState value = IdleTimestamps.GetValue(thingComp, (ThingComp _) => new IdleTimestampState());
		int num = (int)idleTimestamp.GetValue(thingComp);
		if (num != -1 || value.ValidMinusOne)
		{
			int num2 = num + offset;
			idleTimestamp.SetValue(thingComp, num2);
			value.ValidMinusOne = num2 == -1;
		}
	}

	private static int IdleSentinel(ThingComp comp)
	{
		int num = (int)idleTimestamp.GetValue(comp);
		if (!MP.IsInMultiplayer || num != -1 || !IdleTimestamps.TryGetValue(comp, out var value) || !value.ValidMinusOne)
		{
			return num;
		}
		return -2;
	}

	private static int IdleRecorded(ThingComp comp)
	{
		int num = IdleSentinel(comp);
		if (!MP.IsInMultiplayer || num >= -1)
		{
			return num;
		}
		return 1;
	}

	private static int ClearIdleTimestamp(ThingComp comp)
	{
		if (IdleTimestamps.TryGetValue(comp, out var value))
		{
			value.ValidMinusOne = false;
		}
		return -1;
	}

	private static IEnumerable<CodeInstruction> IdleTimestampIl(IEnumerable<CodeInstruction> instructions)
	{
		List<CodeInstruction> list = instructions.ToList();
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		for (int i = 0; i + 1 < list.Count; i++)
		{
			if (list[i].LoadsField(idleTimestamp) && (list[i + 1].LoadsConstant(0L) || list[i + 1].LoadsConstant(-1L)))
			{
				bool flag = list[i + 1].LoadsConstant(0L);
				list[i].opcode = OpCodes.Call;
				list[i].operand = AccessTools.Method(typeof(CindersOfTheEmbergarden), flag ? "IdleRecorded" : "IdleSentinel");
				if (flag)
				{
					num++;
				}
				else
				{
					num2++;
				}
			}
			if (list[i].LoadsConstant(-1L) && list[i + 1].StoresField(idleTimestamp))
			{
				list[i].opcode = OpCodes.Dup;
				list[i].operand = null;
				list.Insert(++i, CodeInstruction.Call(typeof(CindersOfTheEmbergarden), "ClearIdleTimestamp"));
				num3++;
			}
		}
		if (num != 1 || num2 != 1 || num3 != 1)
		{
			throw new InvalidOperationException($"Cinders idle timestamp IL changed: {num}/{num2}/{num3}");
		}
		return list;
	}

	private static void IdleTimestampExpose(ThingComp __instance)
	{
		IdleTimestampState value = IdleTimestamps.GetValue(__instance, (ThingComp _) => new IdleTimestampState());
		if (Scribe.mode == LoadSaveMode.Saving && (int)idleTimestamp.GetValue(__instance) != -1)
		{
			value.ValidMinusOne = false;
		}
		Scribe_Values.Look(ref value.ValidMinusOne, "mpCindersIdleTimestampIsValidMinusOne", defaultValue: false);
	}
}
