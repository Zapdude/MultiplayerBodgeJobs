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
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Aoba.DeadManSwitch.Expedition")]
public sealed class DeadMansSwitchExpedition
{
	private sealed class ExpeditionMapScope : IDisposable
	{
		private readonly object owner;

		internal ExpeditionMapScope(Map map)
		{
			owner = mapTime.Invoke(null, new object[1] { map });
			mapPre.Invoke(owner, null);
		}

		public void Dispose()
		{
			mapPost.Invoke(owner, null);
		}
	}

	private sealed class DefenseRead
	{
		internal object State;

		internal IDictionary Ledger;

		internal int Id;

		internal int Stock;

		internal int Restock;

		internal bool Existing;
	}

	private sealed class AirDefenseRead
	{
		internal object Defense;

		internal float Stock;

		internal int Restock;
	}

	private sealed class ConfigDraft
	{
		internal object Config;

		internal bool Confirmed;

		internal string Base;

		internal string Expected;
	}

	private sealed class ConfigEdit
	{
		internal ThingComp Comp;

		internal object Pending;

		internal bool Confirmed;

		internal ConfigDraft Draft;

		internal string Before;
	}

	private sealed class AutoEdit
	{
		internal object Wave;

		internal bool Before;

		internal ThingComp Console;

		internal int Mission;

		internal int Index;
	}

	private sealed class Commander
	{
		internal int Player = -1;

		internal string Name;
	}

	private static readonly System.Random benchNameRandom = new System.Random();

	[ThreadStatic]
	private static int benchNameDepth;

	private static MethodInfo mapTime;

	private static MethodInfo mapPre;

	private static MethodInfo mapPost;

	private static FieldInfo mapTicking;

	private static Func<bool> warmingCaches;

	private static readonly ConditionalWeakTable<object, DmsBvrMotion> movement = new ConditionalWeakTable<object, DmsBvrMotion>();

	[ThreadStatic]
	private static bool doingMapArrival;

	private static Type buildingTurretType;

	private static MethodInfo buildingTurretRange;

	private static MethodInfo buildingTurretTarget;

	private static readonly ConditionalWeakTable<ThingComp, ConfigDraft> drafts = new ConditionalWeakTable<ThingComp, ConfigDraft>();

	[ThreadStatic]
	private static int editingConfig;

	[ThreadStatic]
	private static bool instantCommand;

	internal static bool Installed;

	[ThreadStatic]
	private static int queuedCommander = -1;

	[ThreadStatic]
	private static DmsExpeditionMapJob terminalChoice;

	private static Game targetingGame;

	private static readonly HashSet<int> openedTerminals = new HashSet<int>();

	[ThreadStatic]
	private static bool huntdownMapTick;

	private static Func<object> activeSimulationOwner;

	private static Type objectiveLauncher;

	private static Type objectiveSearchRadar;

	private static Type objectiveFireRadar;

	private static readonly MethodInfo refreshClone = AccessTools.Method(typeof(object), "MemberwiseClone");

	[ThreadStatic]
	private static int physicalDebris;

	private static System.WeakReference<World> airspaceWorld = new System.WeakReference<World>(null);

	[ThreadStatic]
	private static int craterWrite;

	private static readonly ConditionalWeakTable<ThingComp, Commander> commanders = new ConditionalWeakTable<ThingComp, Commander>();

	private static readonly ConditionalWeakTable<WorldObject, Commander> flightCommanders = new ConditionalWeakTable<WorldObject, Commander>();

	private static FieldInfo executingPlayer;

	private static MethodInfo factionPush;

	private static MethodInfo moveThings;

	[ThreadStatic]
	private static bool landingNow;

	private static bool LocalObjectiveRead
	{
		get
		{
			if (MP.IsInMultiplayer && MP.InInterface && !MP.IsExecutingSyncCommand)
			{
				return activeSimulationOwner() == null;
			}
			return false;
		}
	}

	internal static object Settings
	{
		get
		{
			if (!Installed)
			{
				return null;
			}
			return F("DMSEMod", "Settings").GetValue(null);
		}
	}

	private static object Session => AccessTools.Field(AccessTools.TypeByName("Multiplayer.Client.Multiplayer"), "session").GetValue(null);

	private static IEnumerable<object> Players
	{
		get
		{
			if (Session != null)
			{
				return ((IEnumerable)Get(Session, "players")).Cast<object>();
			}
			return Enumerable.Empty<object>();
		}
	}

	private static void InstallAssemblyBench()
	{
		string[] array = new string[5] { "SetDraft", "PlaceOrder", "SetRemaining", "SetOutputMode", "CancelOrder" };
		foreach (string text in array)
		{
			MethodInfo methodInfo = M("CompMissileAssemblyBench", text);
			ISyncMethod syncMethod = MP.RegisterSyncMethod(methodInfo).SetVersion(31);
			if ((text == "SetDraft" || text == "PlaceOrder") ? true : false)
			{
				syncMethod.ExposeParameter(0);
			}
			MpCompat.harmony.Patch(methodInfo, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "AssemblyBenchCommand"));
		}
		MP.RegisterSyncMethod(typeof(DeadMansSwitchExpedition), "SyncInstantBenchOrder").SetVersion(31).SetDebugOnly()
			.ExposeParameter(1);
		MpCompat.harmony.Patch(M("CompMissileAssemblyBench", "PlaceOrder"), new HarmonyMethod(typeof(DeadMansSwitchExpedition), "BenchInstantOrder")
		{
			priority = 800
		});
		array = new string[3] { "Generate", "Candidates", "BuildRuleString" };
		foreach (string name in array)
		{
			MpCompat.harmony.Patch(M("MissileDesignNamer", name), new HarmonyMethod(typeof(DeadMansSwitchExpedition), "BenchNameBefore"), null, null, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "BenchNameAfter"));
		}
	}

	private static bool BenchInstantOrder(ThingComp __instance, IExposable cfg, string name, bool locked, string rule, int count, object mode)
	{
		if (!MP.IsInMultiplayer || !MP.InInterface || MP.IsExecutingSyncCommand || !DebugSettings.godMode || !(bool)Get(Settings, "godModeInstantAssembly"))
		{
			return true;
		}
		if (Owned(__instance))
		{
			SyncInstantBenchOrder(__instance, cfg, name, locked, rule, count, Convert.ToInt32(mode));
		}
		return false;
	}

	private static void SyncInstantBenchOrder(ThingComp bench, IExposable config, string name, bool locked, string rule, int count, int mode)
	{
		if (bench?.GetType() != T("CompMissileAssemblyBench") || config?.GetType() != T("MissileConfig") || !Owned(bench) || !Enum.IsDefined(T("BenchOutputMode"), mode))
		{
			return;
		}
		bool flag = instantCommand;
		instantCommand = true;
		try
		{
			Call(bench, "PlaceOrder", config, name, locked, rule, count, Enum.ToObject(T("BenchOutputMode"), mode));
		}
		finally
		{
			instantCommand = flag;
		}
	}

	private static bool AssemblyBenchCommand(ThingComp __instance, MethodBase __originalMethod, object[] __args)
	{
		if (!MP.IsInMultiplayer || !MP.IsExecutingSyncCommand)
		{
			return true;
		}
		if (!Owned(__instance))
		{
			return false;
		}
		string name = __originalMethod.Name;
		if (name == "SetOutputMode" || name == "PlaceOrder")
		{
			object obj = __args[(!(name == "SetOutputMode")) ? 5 : 0];
			if (obj == null || !Enum.IsDefined(T("BenchOutputMode"), obj))
			{
				return false;
			}
		}
		if ((!(name == "SetDraft") && !(name == "PlaceOrder")) || 1 == 0)
		{
			return true;
		}
		if (__args[1] is string { Length: >0 } text && (text.Length > 40 || !(bool)M("MissileDesignNamer", "IsValidName").Invoke(null, new object[1] { text })))
		{
			return false;
		}
		if (__args[3] is string { Length: >4096 })
		{
			return false;
		}
		object obj2 = __args[0];
		if (obj2 == null)
		{
			return name == "SetDraft";
		}
		if (obj2.GetType() != T("MissileConfig") || !(Get(obj2, "parts") is IList { Count: <=32 } list))
		{
			return false;
		}
		object body = Get(obj2, "body");
		if (body == null)
		{
			if (name == "SetDraft")
			{
				return list.Count == 0;
			}
			return false;
		}
		if (list.Cast<object>().Any((object p) => p == null || p.GetType() != T("MissilePartDef") || !(bool)Call(p, "CompatibleWith", body)))
		{
			return false;
		}
		if ((from object p in list
			select Get(p, "category")).Distinct().Count() != list.Count)
		{
			return false;
		}
		if (name == "SetDraft")
		{
			return true;
		}
		object[] parameters = new object[2] { body, null };
		if (!(bool)M("MissileItemUtility", "CanProduce").Invoke(null, parameters))
		{
			return false;
		}
		foreach (object item in list)
		{
			object[] parameters2 = new object[2] { item, null };
			if (!(bool)M("MissileItemUtility", "PartUsable").Invoke(null, parameters2))
			{
				return false;
			}
		}
		return true;
	}

	private static void BenchNameBefore(out bool __state)
	{
		__state = false;
		if (MP.IsInMultiplayer && MP.InInterface && !MP.IsExecutingSyncCommand)
		{
			if (benchNameDepth == 0)
			{
				Rand.PushState(benchNameRandom.Next());
			}
			benchNameDepth++;
			__state = true;
		}
	}

	private static Exception BenchNameAfter(Exception __exception, bool __state)
	{
		if (__state && --benchNameDepth == 0)
		{
			Rand.PopState();
		}
		return __exception;
	}

	private static void InstallAsync()
	{
		Type type = AccessTools.TypeByName("Multiplayer.Client.AsyncTimeComp");
		mapTime = AccessTools.DeclaredMethod("Multiplayer.Client.Extensions:AsyncTime");
		mapPre = AccessTools.DeclaredMethod(type, "PreContext");
		mapPost = AccessTools.DeclaredMethod(type, "PostContext");
		mapTicking = AccessTools.Field(type, "tickingMap");
		warmingCaches = (Func<bool>)AccessTools.PropertyGetter(AccessTools.TypeByName("Multiplayer.Client.LoadPatch"), "IsInitializingCaches").CreateDelegate(typeof(Func<bool>));
		MethodInfo clock = AccessTools.PropertyGetter(typeof(TickManager), "TicksGame");
		foreach (Type item in from t in T("MissileWorldObject").Assembly.GetTypes()
			where typeof(WorldComponent).IsAssignableFrom(t) || typeof(WorldObject).IsAssignableFrom(t) || typeof(WorldObjectComp).IsAssignableFrom(t) || t.DeclaringType == T("WorldComponent_SettlementDefense")
			select t)
		{
			foreach (MethodInfo item2 in from m in item.GetMethods(AccessTools.allDeclared)
				where !m.ContainsGenericParameters && m.GetMethodBody() != null
				select m)
			{
				if (PatchProcessor.GetCurrentInstructions(item2).Any((CodeInstruction i) => i.Calls(clock)))
				{
					MpCompat.harmony.Patch(item2, null, null, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "WorldClockIl"));
				}
			}
		}
		MpCompat.harmony.Patch(M("WorldComponent_BVRNetwork", "ExposeData"), null, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "MotionExpose"));
		MpCompat.harmony.Patch(M("WorldComponent_BVRNetwork", "RebuildNodes"), null, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "SortNodes"));
		MpCompat.harmony.Patch(M("WorldComponent_BVRNetwork", "TrySpawnDropPodTrack"), new HarmonyMethod(typeof(DeadMansSwitchExpedition), "PodTrackBefore"), null, null, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "PodTrackAfter"));
		string[] array = new string[2] { "WorldObject_IncomingMissile", "ScorerProjectile_WorldObject" };
		foreach (string type2 in array)
		{
			MpCompat.harmony.Patch(M(type2, "OnArrived"), new HarmonyMethod(typeof(DeadMansSwitchExpedition), "MapArrival"));
		}
		MethodInfo methodInfo = AccessTools.DeclaredMethod(T("WorldObject_DropPodTrack"), "Notify_Intercepted", new Type[1] { typeof(Faction) });
		if (methodInfo != null)
		{
			MpCompat.harmony.Patch(methodInfo, null, null, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "PodInterceptionIl"));
		}
		else
		{
			MpCompat.harmony.Patch(M("WorldObject_DropPodTrack", "OnIntercepted"), new HarmonyMethod(typeof(DeadMansSwitchExpedition), "PodIntercepted"));
		}
		MpCompat.harmony.Patch(M("WorldObject_ArtilleryStrike", "Arrived"), new HarmonyMethod(typeof(DeadMansSwitchExpedition), "ArtilleryArrived"));
		MpCompat.harmony.Patch(M("CompMissileLauncher_Interceptor", "ReadyToFire"), new HarmonyMethod(typeof(DeadMansSwitchExpedition), "LauncherReady"));
		MpCompat.harmony.Patch(M("CompMissileLauncher_Interceptor", "FireWorldInterceptor"), new HarmonyMethod(typeof(DeadMansSwitchExpedition), "WorldInterceptorBefore"), null, null, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "WorldInterceptorAfter"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(Map), "MapPostTick"), null, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "ExpeditionMapTick"));
		(string, string, bool)[] array2 = new (string, string, bool)[10]
		{
			("CompMissileLauncher", "cooldownUntil", false),
			("CompArtilleryStrike", "cooldownUntil", false),
			("CompArtilleryStrike", "warmupEndTick", true),
			("CompArtilleryStrike", "channelReleaseTick", true),
			("CompTransferConsole", "countdownEndTick", true),
			("CompManPortableSAM", "cooldownUntil", false),
			("StrikeMission", "computeDoneTick", true),
			("StrikeMission", "createdTick", false),
			("StrikeWave", "launchedTick", true),
			("StrikeSlot", "launchTick", true)
		};
		for (int num = 0; num < array2.Length; num++)
		{
			(string, string, bool) tuple = array2[num];
			FieldInfo field = F(tuple.Item1, tuple.Item2);
			bool sentinel = tuple.Item3;
			PatchingUtilities.RegisterTimestampFields(T(tuple.Item1), (object value, int offset) =>
			{
				int num2 = (int)field.GetValue(value);
				if (!sentinel || num2 != -1)
				{
					field.SetValue(value, num2 + offset);
				}
			});
		}
		Type nestedType = T("WorldComponent_SettlementDefense").GetNestedType("SettlementDefense", AccessTools.all);
		MpCompat.harmony.Patch(AccessTools.PropertyGetter(nestedType, "Stock"), new HarmonyMethod(typeof(DeadMansSwitchExpedition), "DefenseReadBefore"), null, null, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "DefenseReadAfter"));
		MpCompat.harmony.Patch(AccessTools.PropertyGetter(T("WorldObjectComp_AirDefense"), "Stock"), new HarmonyMethod(typeof(DeadMansSwitchExpedition), "AirDefenseReadBefore"), null, null, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "AirDefenseReadAfter"));
		MpCompat.harmony.Patch(AccessTools.PropertyGetter(T("GameComponent_MissileEngage"), "Comp"), new HarmonyMethod(typeof(DeadMansSwitchExpedition), "EngageComp"));
		MpCompat.harmony.Patch(M("GameComponent_MissileEngage", "GameComponentTick"), new HarmonyMethod(typeof(DeadMansSwitchExpedition), "EngageWorldTick"));
	}

	private static int WorldClock(TickManager manager)
	{
		if (!MP.IsInMultiplayer)
		{
			return manager.TicksGame;
		}
		return FortifiedFeaturesFramework.WorldTicks();
	}

	private static IEnumerable<CodeInstruction> WorldClockIl(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo clock = AccessTools.PropertyGetter(typeof(TickManager), "TicksGame");
		int count = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(clock))
			{
				count++;
				yield return new CodeInstruction(instruction)
				{
					opcode = OpCodes.Call,
					operand = AccessTools.Method(typeof(DeadMansSwitchExpedition), "WorldClock")
				};
			}
			else
			{
				yield return instruction;
			}
		}
		if (count == 0)
		{
			throw new InvalidOperationException("DMSE world clock IL changed");
		}
	}

	private static bool MapPaused(Map map)
	{
		if (map != null && !map.Disposed)
		{
			return (bool)AccessTools.Property(mapTime.ReturnType, "Paused").GetValue(mapTime.Invoke(null, new object[1] { map }));
		}
		return true;
	}

	private static int MapClock(Map map)
	{
		PatchingUtilities.TimeSnapshot? andSetFromMap = PatchingUtilities.TimeSnapshot.GetAndSetFromMap(map);
		try
		{
			return GenTicks.TicksGame;
		}
		finally
		{
			andSetFromMap?.Set();
		}
	}

	private static bool LauncherReady(ThingComp __instance, ref int now, ref bool __result)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		Map map = __instance.parent.Map;
		if (MapPaused(map))
		{
			__result = false;
			return false;
		}
		now = MapClock(map);
		return true;
	}

	private static bool WorldInterceptorBefore(ThingComp __instance, ref int now, ref bool __result, out ExpeditionMapScope __state)
	{
		__state = null;
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (MapPaused(__instance.parent.Map))
		{
			__result = false;
			return false;
		}
		__state = new ExpeditionMapScope(__instance.parent.Map);
		now = GenTicks.TicksGame;
		return true;
	}

	private static Exception WorldInterceptorAfter(Exception __exception, ExpeditionMapScope __state)
	{
		__state?.Dispose();
		return __exception;
	}

	private static bool EngageComp(ref object __result)
	{
		if (!MP.IsInMultiplayer || Current.Game == null)
		{
			return true;
		}
		__result = Current.Game.components.FirstOrDefault((GameComponent c) => c?.GetType() == T("GameComponent_MissileEngage"));
		return false;
	}

	private static bool EngageWorldTick(object __instance)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		IDictionary dictionary = (IDictionary)Get(__instance, "times");
		Map[] array = (from Map m in dictionary.Keys
			where m == null || m.Disposed || (m.Parent?.Destroyed ?? true)
			select m).ToArray();
		foreach (Map key in array)
		{
			dictionary.Remove(key);
		}
		return false;
	}

	private static void MotionExpose(object __instance)
	{
		DmsBvrMotion value = movement.GetValue(__instance, (object _) => new DmsBvrMotion());
		IDictionary dictionary = (IDictionary)Get(__instance, "motion");
		if (Scribe.mode == LoadSaveMode.Saving)
		{
			value.Last = (int)Get(__instance, "lastMotionSampleTick");
			value.Samples.Clear();
			foreach (int item in from int i in dictionary.Keys
				orderby i
				select i)
			{
				object value2 = dictionary[item];
				value.Samples.Add(new DmsBvrSample
				{
					Id = item,
					Tile = (PlanetTile)Get(value2, "tile"),
					Tick = (int)Get(value2, "tick"),
					Seen = (int)Get(value2, "lastSeen"),
					Speed = (float)Get(value2, "tilesPerTick")
				});
			}
		}
		value.ExposeData();
		if (Scribe.mode != LoadSaveMode.PostLoadInit)
		{
			return;
		}
		dictionary.Clear();
		Type type = dictionary.GetType().GetGenericArguments()[1];
		foreach (DmsBvrSample item2 in value.Samples ?? new List<DmsBvrSample>())
		{
			object value3 = Activator.CreateInstance(type);
			Set(value3, "tile", item2.Tile);
			Set(value3, "tick", item2.Tick);
			Set(value3, "lastSeen", item2.Seen);
			Set(value3, "tilesPerTick", item2.Speed);
			dictionary[item2.Id] = value3;
		}
		Set(__instance, "lastMotionSampleTick", value.Last);
	}

	private static void SortNodes(object __instance)
	{
		if (MP.IsInMultiplayer)
		{
			IList list = (IList)Get(__instance, "nodes");
			object[] array = (from object n in list
				orderby ((WorldObject)Get(n, "parent"))?.ID ?? (-1), ((Faction)Get(n, "faction"))?.loadID ?? (-1)
				select n).ToArray();
			list.Clear();
			object[] array2 = array;
			foreach (object value in array2)
			{
				list.Add(value);
			}
		}
	}

	private static void PodTrackBefore(Map map, object wave, out int? __state)
	{
		__state = null;
		if (MP.IsInMultiplayer && map != null && wave != null)
		{
			int num = (int)Get(wave, "tickToImpact");
			__state = num;
			Set(wave, "tickToImpact", FortifiedFeaturesFramework.WorldTicks() + Math.Max(0, num - MapClock(map)));
		}
	}

	private static Exception PodTrackAfter(Exception __exception, object wave, int? __state)
	{
		if (__state.HasValue)
		{
			Set(wave, "tickToImpact", __state.Value);
		}
		return __exception;
	}

	private static bool MapArrival(WorldObject __instance, Map map)
	{
		if (!MP.IsInMultiplayer || doingMapArrival || map == null)
		{
			return true;
		}
		EnqueueMapArrival(__instance, map, 0);
		return false;
	}

	private static bool PodIntercepted(WorldObject __instance)
	{
		if (!MP.IsInMultiplayer || doingMapArrival)
		{
			return true;
		}
		if (Get(__instance, "targetMap") is Map { Disposed: false } map)
		{
			EnqueueMapArrival(null, map, 1, (int)Get(__instance, "waveTargetId"));
		}
		return false;
	}

	private static IEnumerable<CodeInstruction> PodInterceptionIl(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo remove = M("MapComponent_BVRCombat", "RemoveTarget");
		MethodInfo queue = AccessTools.DeclaredMethod(typeof(DeadMansSwitchExpedition), "RemoveInterceptedPodTarget");
		int count = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (!instruction.Calls(remove))
			{
				yield return instruction;
				continue;
			}
			count++;
			yield return new CodeInstruction(instruction)
			{
				opcode = OpCodes.Call,
				operand = queue
			};
		}
		if (count != 1)
		{
			throw new InvalidOperationException($"DMSE pod interception removal call sites changed: {count}");
		}
	}

	private static void RemoveInterceptedPodTarget(MapComponent combat, int target)
	{
		if (!MP.IsInMultiplayer || doingMapArrival)
		{
			Call(combat, "RemoveTarget", target);
			return;
		}
		Map map = combat.map;
		if (map != null && !map.Disposed)
		{
			EnqueueMapArrival(null, map, 1, target);
		}
	}

	private static bool ArtilleryArrived(WorldObject __instance)
	{
		if (!MP.IsInMultiplayer || doingMapArrival)
		{
			return true;
		}
		PlanetTile tile = (PlanetTile)Get(__instance, "destinationTile");
		Map map = Find.Maps.FirstOrDefault((Map m) => m.Tile == tile);
		if (map == null)
		{
			return true;
		}
		Set(__instance, "arrived", true);
		EnqueueMapArrival(__instance, map, 2);
		return false;
	}

	private static void EnqueueMapArrival(WorldObject track, Map map, int action, int target = -1)
	{
		List<DmsExpeditionMapJob> arrivals = Current.Game.GetComponent<DmsExpeditionMapJobs>().Arrivals;
		if (!arrivals.Any((DmsExpeditionMapJob j) => j.Track == track && j.Map == map && j.Action == action && j.Target == target))
		{
			arrivals.Add(new DmsExpeditionMapJob
			{
				Track = track,
				Map = map,
				Action = action,
				Target = target
			});
		}
	}

	private static void ExpeditionMapTick(Map __instance)
	{
		if (!MP.IsInMultiplayer || warmingCaches() || mapTicking.GetValue(null) != __instance)
		{
			return;
		}
		HuntdownTick(__instance);
		GameComponent gameComponent = Current.Game.components.FirstOrDefault((GameComponent c) => c?.GetType() == T("GameComponent_MissileEngage"));
		if (gameComponent != null && Get(gameComponent, "times") is IDictionary dictionary && dictionary.Contains(__instance) && GenTicks.TicksGame >= (int)dictionary[__instance])
		{
			dictionary.Remove(__instance);
		}
		List<DmsExpeditionMapJob> arrivals = Current.Game.GetComponent<DmsExpeditionMapJobs>().Arrivals;
		DmsExpeditionMapJob[] array = (from j in arrivals
			where j.Map == __instance
			orderby j.Track?.ID ?? (-1), j.Target
			select j).ToArray();
		foreach (DmsExpeditionMapJob dmsExpeditionMapJob in array)
		{
			doingMapArrival = true;
			try
			{
				if (dmsExpeditionMapJob.Action == 1)
				{
					MapComponent mapComponent = __instance.components.FirstOrDefault((MapComponent c) => c.GetType() == T("MapComponent_BVRCombat"));
					if (mapComponent != null)
					{
						Call(mapComponent, "RemoveTarget", dmsExpeditionMapJob.Target);
					}
					goto IL_02b5;
				}
				WorldObject track = dmsExpeditionMapJob.Track;
				if (track == null || track.Destroyed)
				{
					goto IL_02b5;
				}
				if (dmsExpeditionMapJob.Action == 2)
				{
					Set(dmsExpeditionMapJob.Track, "arrived", false);
					Call(dmsExpeditionMapJob.Track, "Arrived");
					goto IL_02b5;
				}
				object obj = Get(dmsExpeditionMapJob.Track, "config");
				if (obj == null || !(bool)Property(obj, "PlayerChoosesTarget"))
				{
					goto IL_026a;
				}
				Faction faction = dmsExpeditionMapJob.Track.Faction;
				if (faction == null || !faction.IsPlayer || dmsExpeditionMapJob.HasCell)
				{
					goto IL_026a;
				}
				dmsExpeditionMapJob.Action = 3;
				continue;
				IL_026a:
				terminalChoice = dmsExpeditionMapJob;
				try
				{
					using (ShipFaction(dmsExpeditionMapJob.Track))
					{
						Call(dmsExpeditionMapJob.Track, "OnArrived", __instance);
					}
				}
				finally
				{
					terminalChoice = null;
				}
				goto IL_02b5;
				IL_02b5:
				arrivals.Remove(dmsExpeditionMapJob);
			}
			finally
			{
				doingMapArrival = false;
			}
		}
		TransferMapTick(__instance);
	}

	private static void DefenseReadBefore(object __instance, out DefenseRead __state)
	{
		__state = null;
		if (MP.IsInMultiplayer && MP.InInterface)
		{
			object value = Get(__instance, "owner");
			WorldObject worldObject = (WorldObject)Get(__instance, "parent");
			IDictionary dictionary = (IDictionary)Get(value, "states");
			bool existing = dictionary.Contains(worldObject.ID);
			object obj = Property(__instance, "State");
			__state = new DefenseRead
			{
				State = obj,
				Ledger = dictionary,
				Id = worldObject.ID,
				Existing = existing,
				Stock = (int)Get(obj, "stock"),
				Restock = (int)Get(obj, "lastRestockTick")
			};
		}
	}

	private static Exception DefenseReadAfter(Exception __exception, DefenseRead __state)
	{
		if (__state != null)
		{
			Set(__state.State, "stock", __state.Stock);
			Set(__state.State, "lastRestockTick", __state.Restock);
			if (!__state.Existing)
			{
				__state.Ledger.Remove(__state.Id);
			}
		}
		return __exception;
	}

	private static void AirDefenseReadBefore(object __instance, out AirDefenseRead __state)
	{
		__state = ((MP.IsInMultiplayer && MP.InInterface) ? new AirDefenseRead
		{
			Defense = __instance,
			Stock = (float)Get(__instance, "stock"),
			Restock = (int)Get(__instance, "lastRestockTick")
		} : null);
	}

	private static Exception AirDefenseReadAfter(Exception __exception, AirDefenseRead __state)
	{
		if (__state != null)
		{
			Set(__state.Defense, "stock", __state.Stock);
			Set(__state.Defense, "lastRestockTick", __state.Restock);
		}
		return __exception;
	}

	private static void InstallBuildingTurretOrders()
	{
		buildingTurretType = T("CompBuildingTurretGun");
		buildingTurretRange = AccessTools.DeclaredMethod(buildingTurretType, "InRange", new Type[2]
		{
			typeof(LocalTargetInfo),
			typeof(string).MakeByRefType()
		});
		buildingTurretTarget = AccessTools.DeclaredMethod(buildingTurretType, "TrySetForcedTarget", new Type[2]
		{
			typeof(LocalTargetInfo),
			typeof(string).MakeByRefType()
		});
		if (buildingTurretRange?.ReturnType != typeof(bool) || buildingTurretTarget?.ReturnType != typeof(bool))
		{
			throw new MissingMethodException("DMSE building turret target signature changed");
		}
		MP.RegisterSyncMethod(typeof(DeadMansSwitchExpedition), "SyncBuildingTurretTarget").SetVersion(13).CancelIfAnyArgNull();
		MpCompat.harmony.Patch(buildingTurretTarget, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "BuildingTurretTarget"));
		(string, Type[])[] array = new (string, Type[])[2]
		{
			("ResetForcedTarget", Type.EmptyTypes),
			("SetHoldFire", new Type[1] { typeof(bool) })
		};
		for (int i = 0; i < array.Length; i++)
		{
			(string, Type[]) tuple = array[i];
			MethodInfo methodInfo = AccessTools.DeclaredMethod(buildingTurretType, tuple.Item1, tuple.Item2);
			if (methodInfo?.ReturnType != typeof(void))
			{
				throw new MissingMethodException(buildingTurretType.FullName, tuple.Item1);
			}
			MP.RegisterSyncMethod(methodInfo).SetVersion(13);
			MpCompat.harmony.Patch(methodInfo, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "OwnBuildingTurret"));
		}
	}

	private static bool ValidBuildingTurret(ThingComp turret)
	{
		if (turret != null && buildingTurretType.IsInstanceOfType(turret) && Owned(turret) && turret.parent.Spawned)
		{
			Map map = turret.parent.Map;
			if (map != null && !map.Disposed && Current.Game != null)
			{
				return Find.Maps.Contains(map);
			}
		}
		return false;
	}

	private static bool ValidBuildingTarget(ThingComp turret, LocalTargetInfo target)
	{
		if (ValidBuildingTurret(turret) && target.IsValid && target.Cell.InBounds(turret.parent.Map))
		{
			if (target.HasThing)
			{
				if (!target.Thing.Destroyed)
				{
					return target.Thing.MapHeld == turret.parent.Map;
				}
				return false;
			}
			return true;
		}
		return false;
	}

	private static bool OwnBuildingTurret(ThingComp __instance)
	{
		if (MP.IsExecutingSyncCommand)
		{
			return ValidBuildingTurret(__instance);
		}
		return true;
	}

	private static bool BuildingTurretTarget(ThingComp __instance, LocalTargetInfo target, ref string reason, ref bool __result)
	{
		if (!LocalObjectiveRead)
		{
			return true;
		}
		reason = null;
		__result = false;
		if (!ValidBuildingTarget(__instance, target) || (target.HasThing && target.Thing == __instance.parent))
		{
			return false;
		}
		object[] array = new object[2] { target, null };
		if (!(bool)buildingTurretRange.Invoke(__instance, array))
		{
			reason = (string)array[1];
			return false;
		}
		SyncBuildingTurretTarget(__instance, target);
		__result = true;
		return false;
	}

	private static void SyncBuildingTurretTarget(ThingComp turret, LocalTargetInfo target)
	{
		if (ValidBuildingTarget(turret, target))
		{
			buildingTurretTarget.Invoke(turret, new object[2] { target, null });
		}
	}

	private static void InstallConfig()
	{
		MP.RegisterSyncMethod(typeof(DeadMansSwitchExpedition), "SyncConfigEdit").SetVersion(13).ExposeParameter(1);
		MpCompat.harmony.Patch(M("ITab_MissileAssembly", "FillTab"), new HarmonyMethod(typeof(DeadMansSwitchExpedition), "ConfigTabBefore"), null, null, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "ConfigAfter"));
		int num = 0;
		Type[] nestedTypes = T("ITab_MissileAssembly").GetNestedTypes(AccessTools.all);
		for (int i = 0; i < nestedTypes.Length; i++)
		{
			foreach (MethodInfo item in from m in nestedTypes[i].GetMethods(AccessTools.allDeclared)
				where m.Name.Contains("b__") && m.ReturnType == typeof(void) && m.GetParameters().Length == 0
				select m)
			{
				if (PatchProcessor.GetCurrentInstructions(item).Any((CodeInstruction code) => code.StoresField(F("CompMissileConfig", "assemblyConfirmed"))))
				{
					MpCompat.harmony.Patch(item, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "ConfigClosureBefore"), null, null, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "ConfigAfter"));
					num++;
				}
			}
		}
		if (num != 3)
		{
			throw new InvalidOperationException($"DMSE config UI changed: {num} actions");
		}
		MpCompat.harmony.Patch(M("CompMissileConfig", "RefundDeliveredIfIdle"), new HarmonyMethod(typeof(DeadMansSwitchExpedition), "ConfigRefund"));
		MpCompat.harmony.Patch(AccessTools.PropertyGetter(T("MissileAssemblyUtility"), "GodModeInstant"), new HarmonyMethod(typeof(DeadMansSwitchExpedition), "InstantAssemblyMode"));
		MpCompat.harmony.Patch(M("MissileAssemblyUtility", "TryInstantAssemble"), new HarmonyMethod(typeof(DeadMansSwitchExpedition), "InstantAssemblyCall"));
		MP.RegisterSyncMethod(typeof(DeadMansSwitchExpedition), "SyncInstantAssembly").SetVersion(13).SetDebugOnly();
	}

	private static object CloneConfig(object config)
	{
		return Call(config, "Clone");
	}

	private static string ConfigKey(object config, bool confirmed)
	{
		if (config != null)
		{
			return ((Def)Get(config, "body"))?.defName + ":" + string.Join(",", from Def d in (IEnumerable)Get(config, "parts")
				select d?.defName ?? "null") + ":" + confirmed;
		}
		return "null";
	}

	private static void ConfigTabBefore(object __instance, out ConfigEdit __state)
	{
		__state = BeginConfig(Property(__instance, "Comp") as ThingComp);
	}

	private static ThingComp ClosureComp(object closure, int depth = 0)
	{
		if (closure == null || depth > 3)
		{
			return null;
		}
		FieldInfo[] fields = closure.GetType().GetFields(AccessTools.allDeclared);
		foreach (FieldInfo fieldInfo in fields)
		{
			object value = fieldInfo.GetValue(closure);
			if (value is ThingComp thingComp && thingComp.GetType() == T("CompMissileConfig"))
			{
				return thingComp;
			}
			if (fieldInfo.FieldType.Name.StartsWith("<>c__DisplayClass"))
			{
				ThingComp thingComp2 = ClosureComp(value, depth + 1);
				if (thingComp2 != null)
				{
					return thingComp2;
				}
			}
		}
		return null;
	}

	private static void RebindConfig(object closure, object config, int depth = 0)
	{
		if (closure == null || depth > 3)
		{
			return;
		}
		FieldInfo[] fields = closure.GetType().GetFields(AccessTools.allDeclared);
		foreach (FieldInfo fieldInfo in fields)
		{
			if (fieldInfo.FieldType == T("MissileConfig"))
			{
				fieldInfo.SetValue(closure, config);
			}
			else if (fieldInfo.FieldType.Name.StartsWith("<>c__DisplayClass"))
			{
				RebindConfig(fieldInfo.GetValue(closure), config, depth + 1);
			}
		}
	}

	private static void ConfigClosureBefore(object __instance, out ConfigEdit __state)
	{
		__state = BeginConfig(ClosureComp(__instance));
		if (__state != null)
		{
			RebindConfig(__instance, __state.Draft.Config);
		}
	}

	private static ConfigEdit BeginConfig(ThingComp comp)
	{
		if (!MP.IsInMultiplayer || !MP.InInterface || comp == null || editingConfig != 0)
		{
			return null;
		}
		object obj = Get(comp, "pending");
		bool confirmed = (bool)Get(comp, "assemblyConfirmed");
		string text = ConfigKey(obj, confirmed);
		ConfigDraft value = drafts.GetValue(comp, (ThingComp _) => new ConfigDraft());
		if (value.Config == null || (text != value.Base && text != value.Expected))
		{
			value.Config = CloneConfig(obj);
			value.Confirmed = confirmed;
			value.Base = (value.Expected = text);
		}
		else if (text == value.Expected)
		{
			value.Base = text;
		}
		ConfigEdit result = new ConfigEdit
		{
			Comp = comp,
			Pending = obj,
			Confirmed = confirmed,
			Draft = value,
			Before = ConfigKey(value.Config, value.Confirmed)
		};
		Set(comp, "pending", value.Config);
		Set(comp, "assemblyConfirmed", value.Confirmed);
		editingConfig++;
		return result;
	}

	private static Exception ConfigAfter(Exception __exception, ConfigEdit __state)
	{
		if (__state == null)
		{
			return __exception;
		}
		ThingComp comp = __state.Comp;
		ConfigDraft draft = __state.Draft;
		draft.Config = Get(comp, "pending");
		draft.Confirmed = (bool)Get(comp, "assemblyConfirmed");
		Set(comp, "pending", __state.Pending);
		Set(comp, "assemblyConfirmed", __state.Confirmed);
		editingConfig--;
		string text = ConfigKey(draft.Config, draft.Confirmed);
		if (__exception == null && text != __state.Before)
		{
			SyncConfigEdit(comp, (IExposable)CloneConfig(draft.Config), draft.Confirmed, draft.Expected);
			draft.Expected = text;
			if (DebugSettings.godMode && (bool)Get(Settings, "godModeInstantAssembly"))
			{
				SyncInstantAssembly(comp.parent);
			}
		}
		return __exception;
	}

	private static bool ConfigRefund()
	{
		if (MP.IsInMultiplayer)
		{
			return editingConfig == 0;
		}
		return true;
	}

	private static bool InstantAssemblyMode(ref bool __result)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		__result = instantCommand || (MP.InInterface && DebugSettings.godMode && (bool)Get(Settings, "godModeInstantAssembly"));
		return false;
	}

	private static bool InstantAssemblyCall()
	{
		if (MP.IsInMultiplayer)
		{
			return !MP.InInterface;
		}
		return true;
	}

	private static void SyncInstantAssembly(Thing thing)
	{
		if (thing?.MapHeld == null || thing.MapHeld.ParentFaction != Faction.OfPlayer)
		{
			return;
		}
		instantCommand = true;
		try
		{
			M("MissileAssemblyUtility", "TryInstantAssemble").Invoke(null, new object[1] { thing });
		}
		finally
		{
			instantCommand = false;
		}
	}

	private static void SyncConfigEdit(ThingComp comp, IExposable config, bool confirmed, string expected)
	{
		if (Owned(comp) && !(config?.GetType() != T("MissileConfig")) && !(ConfigKey(Get(comp, "pending"), (bool)Get(comp, "assemblyConfirmed")) != expected) && !(bool)M("StrikeReservation", "IsReserved").Invoke(null, new object[1] { comp.parent }) && Get(config, "body") is Def value && (bool)Property(value, "ResearchSatisfied"))
		{
			object[] array = ((IEnumerable)Get(config, "parts")).Cast<object>().ToArray();
			if (array.Length <= 32 && !array.Any((object p) => p == null || !(bool)Property(p, "ResearchSatisfied")))
			{
				Call(config, "PruneIncompatible");
				Set(comp, "pending", CloneConfig(config));
				Set(comp, "assemblyConfirmed", confirmed);
				Call(comp, "RefundDeliveredIfIdle");
			}
		}
	}

	private static Type T(string name)
	{
		return AccessTools.TypeByName("DMSE." + name) ?? throw new TypeLoadException("DMSE." + name);
	}

	private static MethodInfo M(string type, string name)
	{
		return AccessTools.DeclaredMethod(T(type), name) ?? throw new MissingMethodException(type, name);
	}

	private static FieldInfo F(string type, string name)
	{
		return AccessTools.Field(T(type), name) ?? throw new MissingFieldException(type, name);
	}

	private static object Get(object value, string name)
	{
		return AccessTools.Field(value.GetType(), name).GetValue(value);
	}

	private static void Set(object value, string name, object field)
	{
		AccessTools.Field(value.GetType(), name).SetValue(value, field);
	}

	private static object Call(object value, string name, params object[] args)
	{
		return AccessTools.Method(value.GetType(), name).Invoke(value, args);
	}

	private static object Property(object value, string name)
	{
		return AccessTools.Property(value.GetType(), name).GetValue(value);
	}

	public DeadMansSwitchExpedition(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(Install);
	}

	private static void Install()
	{
		Installed = true;
		(string, string, bool)[] array = new (string, string, bool)[7]
		{
			("CompTransferConsole", "SetPlan", true),
			("CompTransferConsole", "AbortPlan", false),
			("CompTransferConsole", "BeginLaunchSequence", false),
			("CompTransferConsole", "SetShipName", false),
			("CompStrikeConsole", "TryFireFromUI", false),
			("CompStrikeConsole", "CancelMission", false),
			("CompArtilleryStrike", "StopSustainedFire", false)
		};
		for (int i = 0; i < array.Length; i++)
		{
			(string, string, bool) tuple = array[i];
			MethodInfo methodInfo = M(tuple.Item1, tuple.Item2);
			ISyncMethod syncMethod = MP.RegisterSyncMethod(methodInfo).SetVersion(13);
			if (tuple.Item3)
			{
				syncMethod.ExposeParameter(0);
			}
			MpCompat.harmony.Patch(methodInfo, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "OwnCompCommand"));
		}
		(string, string)[] array2 = new (string, string)[2]
		{
			("CompTransferConsole", "BeginCountdown"),
			("CompStrikeConsole", "FinishComputation")
		};
		for (int i = 0; i < array2.Length; i++)
		{
			(string, string) tuple2 = array2[i];
			MP.RegisterSyncMethod(M(tuple2.Item1, tuple2.Item2)).SetVersion(13).SetDebugOnly();
		}
		ISyncMethod[] array3 = MpCompat.RegisterLambdaMethod(T("CompLandFillable"), "CompGetGizmosExtra", default(int));
		for (int i = 0; i < array3.Length; i++)
		{
			array3[i].SetVersion(13);
		}
		array3 = MpCompat.RegisterLambdaMethod(T("Building_MissileRack"), "GetGizmos", default(int));
		for (int i = 0; i < array3.Length; i++)
		{
			array3[i].SetVersion(13).SetDebugOnly();
		}
		string[] array4 = new string[2] { "OrderLink", "OrderDismantle" };
		foreach (string name in array4)
		{
			MP.RegisterSyncMethod(M("PileLinkUtility", name)).SetVersion(13).CancelIfAnyArgNull();
			MpCompat.harmony.Patch(M("PileLinkUtility", name), new HarmonyMethod(typeof(DeadMansSwitchExpedition), "OwnPiles"));
		}
		MP.RegisterSyncMethod(typeof(DeadMansSwitchExpedition), "SyncCancelBeam").SetVersion(13).CancelIfAnyArgNull();
		MpCompat.harmony.Patch(M("CompStructuralPile", "CancelOrder"), new HarmonyMethod(typeof(DeadMansSwitchExpedition), "CancelBeam"));
		MP.RegisterSyncMethod(M("CompStructuralPile", "ClearAllLinksAndOrders")).SetVersion(13).SetDebugOnly();
		MpCompat.harmony.Patch(M("CompArtilleryStrike", "ChooseWorldTarget"), new HarmonyMethod(typeof(DeadMansSwitchExpedition), "ArtilleryTarget"));
		MP.RegisterSyncMethod(typeof(DeadMansSwitchExpedition), "SyncArtilleryTarget").SetVersion(13).CancelIfAnyArgNull();
		InstallConfig();
		InstallStrike();
		InstallAsync();
		InstallTransfer();
		InstallSettings();
		InstallVisual();
		InstallHuntdown();
		InstallRefresh();
		InstallAssemblyBench();
		InstallInterceptionRisk();
		InstallObjectiveViews();
		InstallBuildingTurretOrders();
	}

	private static bool Owned(ThingComp comp)
	{
		ThingWithComps thingWithComps = comp?.parent;
		if (thingWithComps == null || thingWithComps.Destroyed || thingWithComps.MapHeld == null)
		{
			return false;
		}
		Faction faction = thingWithComps.Faction ?? thingWithComps.MapHeld.ParentFaction;
		if (faction != null && faction.IsPlayer)
		{
			return CommandAuthorityBridge.CanActForFaction(faction);
		}
		return false;
	}

	private static bool OwnCompCommand(ThingComp __instance)
	{
		if (MP.IsExecutingSyncCommand)
		{
			return Owned(__instance);
		}
		return true;
	}

	private static bool OwnPiles(ThingComp a, ThingComp b)
	{
		if (MP.IsExecutingSyncCommand)
		{
			if (Owned(a) && Owned(b))
			{
				return a.parent.Map == b.parent.Map;
			}
			return false;
		}
		return true;
	}

	private static bool CancelBeam(ThingComp __instance, object order)
	{
		if (!MP.IsInMultiplayer || !MP.InInterface)
		{
			return true;
		}
		if (order != null)
		{
			SyncCancelBeam(__instance, (Thing)Get(order, "target"), (bool)Get(order, "dismantle"));
		}
		return false;
	}

	private static void SyncCancelBeam(ThingComp pile, Thing target, bool dismantle)
	{
		if (Owned(pile))
		{
			object obj = ((IEnumerable)Property(pile, "Orders")).Cast<object>().FirstOrDefault((object o) => Get(o, "target") == target && (bool)Get(o, "dismantle") == dismantle);
			if (obj != null)
			{
				Call(pile, "CancelOrder", obj);
			}
		}
	}

	private static bool ArtilleryTarget(ThingComp __instance, GlobalTargetInfo target, ref bool __result)
	{
		if (!MP.IsInMultiplayer || !MP.InInterface)
		{
			return true;
		}
		SyncArtilleryTarget(__instance, target);
		__result = true;
		return false;
	}

	private static void SyncArtilleryTarget(ThingComp artillery, GlobalTargetInfo target)
	{
		if (Owned(artillery))
		{
			M("CompArtilleryStrike", "ChooseWorldTarget").Invoke(artillery, new object[1] { target });
		}
	}

	private static void InstallTerminalGuidance()
	{
		MP.RegisterSyncMethod(typeof(DeadMansSwitchExpedition), "SyncSingleMissile").SetVersion(13).CancelIfAnyArgNull();
		MP.RegisterSyncMethod(typeof(DeadMansSwitchExpedition), "SyncTerminalCell").SetVersion(13).CancelIfAnyArgNull();
		MpCompat.harmony.Patch(M("CompMissileLauncher_WorldTargeting", "TryLaunchRound"), new HarmonyMethod(typeof(DeadMansSwitchExpedition), "SingleMissileBefore"));
		MpCompat.harmony.Patch(M("CompMissileLauncher_WorldTargeting", "TryLaunchRoundCore"), null, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "SingleMissileAfter"));
		MpCompat.harmony.Patch(M("CompStrikeConsole", "ExecuteQueued"), new HarmonyMethod(typeof(DeadMansSwitchExpedition), "QueuedCommanderBefore"), null, null, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "QueuedCommanderAfter"));
		MpCompat.harmony.Patch(M("MissileWorldObject", "ExposeData"), null, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "FlightCommanderExpose"));
		MpCompat.harmony.Patch(AccessTools.PropertyGetter(T("MissileConfig"), "PlayerChoosesTarget"), new HarmonyMethod(typeof(DeadMansSwitchExpedition), "TerminalNativeBranch"));
		MpCompat.harmony.Patch(M("GuidanceType", "PickCell"), new HarmonyMethod(typeof(DeadMansSwitchExpedition), "TerminalNativeCell"));
	}

	private static bool SingleMissileBefore(ThingComp __instance, GlobalTargetInfo target, Thing round, ref string failReason, ref object __result)
	{
		if (!MP.IsInMultiplayer || !MP.InInterface)
		{
			return true;
		}
		SyncSingleMissile(__instance, target, round);
		__result = RuntimeHelpers.GetUninitializedObject(T("ScorerProjectile_WorldObject"));
		failReason = null;
		return false;
	}

	private static void SyncSingleMissile(ThingComp launcher, GlobalTargetInfo target, Thing round)
	{
		if (Owned(launcher))
		{
			Call(launcher, "TryLaunchRound", target, round, false, null);
		}
	}

	private static void SingleMissileAfter(ThingComp __instance, WorldObject __result)
	{
		if (MP.IsInMultiplayer && !MP.InInterface && __result != null)
		{
			Commander value = flightCommanders.GetValue(__result, (WorldObject _) => new Commander());
			value.Player = (MP.IsExecutingSyncCommand ? ((int)executingPlayer.GetValue(null)) : queuedCommander);
			value.Name = PlayerName(value.Player);
		}
	}

	private static void QueuedCommanderBefore(ThingComp __instance, out int __state)
	{
		__state = queuedCommander;
		if (MP.IsInMultiplayer)
		{
			queuedCommander = commanders.GetValue(__instance, (ThingComp _) => new Commander()).Player;
		}
	}

	private static Exception QueuedCommanderAfter(Exception __exception, int __state)
	{
		queuedCommander = __state;
		return __exception;
	}

	private static bool TerminalNativeBranch(ref bool __result)
	{
		if (terminalChoice == null)
		{
			return true;
		}
		__result = false;
		return false;
	}

	private static bool TerminalNativeCell(Map map, ref IntVec3 __result)
	{
		DmsExpeditionMapJob dmsExpeditionMapJob = terminalChoice;
		if (dmsExpeditionMapJob == null || !dmsExpeditionMapJob.HasCell || terminalChoice.Map != map)
		{
			return true;
		}
		__result = terminalChoice.Cell;
		return false;
	}

	internal static void TerminalUiTick()
	{
		if (!MP.IsInMultiplayer || Current.Game == null || LongEventHandler.AnyEventNowOrWaiting)
		{
			return;
		}
		if (targetingGame != Current.Game)
		{
			targetingGame = Current.Game;
			openedTerminals.Clear();
		}
		foreach (DmsExpeditionMapJob arrival in Current.Game.GetComponent<DmsExpeditionMapJobs>().Arrivals)
		{
			if (arrival.Action != 3 || arrival.HasCell)
			{
				continue;
			}
			WorldObject track = arrival.Track;
			if (track == null || track.Destroyed || arrival.Map == null || arrival.Map.Disposed || !OwnLandingUi(arrival.Track) || !openedTerminals.Add(arrival.Track.ID))
			{
				continue;
			}
			DmsExpeditionMapJob captured = arrival;
			bool chosen = false;
			CameraJumper.TryJump(arrival.Map.Center, arrival.Map);
			Find.Targeter.BeginTargeting(new TargetingParameters
			{
				canTargetLocations = true,
				canTargetItems = false
			}, (LocalTargetInfo target) =>
			{
				chosen = true;
				SyncTerminalCell(captured.Track, captured.Map, target.Cell);
			}, null, null, null, () =>
			{
				if (!chosen)
				{
					SyncTerminalCell(captured.Track, captured.Map, captured.Map.Center);
				}
			});
			break;
		}
	}

	private static void SyncTerminalCell(WorldObject track, Map map, IntVec3 cell)
	{
		if (track == null || track.Destroyed || track.Faction != Faction.OfPlayer || map == null || !cell.InBounds(map) || !CurrentCommander(flightCommanders.GetValue(track, (WorldObject _) => new Commander()), (int)executingPlayer.GetValue(null)))
		{
			return;
		}
		foreach (DmsExpeditionMapJob arrival in Current.Game.GetComponent<DmsExpeditionMapJobs>().Arrivals)
		{
			if (arrival.Track == track && arrival.Map == map && arrival.Action == 3 && !arrival.HasCell)
			{
				arrival.Cell = cell;
				arrival.HasCell = true;
				break;
			}
		}
	}

	private static void InstallHuntdown()
	{
		MpCompat.harmony.Patch(M("ScenPart_Huntdown", "Tick"), new HarmonyMethod(typeof(DeadMansSwitchExpedition), "HuntdownWorldTick"));
		MpCompat.harmony.Patch(M("ScenPart_Huntdown", "StartTimers"), new HarmonyMethod(typeof(DeadMansSwitchExpedition), "HuntdownStartBefore"), null, null, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "HuntdownStartAfter"));
		MpCompat.harmony.Patch(AccessTools.PropertyGetter(T("ScenPart_Huntdown"), "AlertCached"), new HarmonyMethod(typeof(DeadMansSwitchExpedition), "HuntdownAlertBefore"), null, null, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "HuntdownAlertAfter"));
	}

	private static bool HuntdownWorldTick()
	{
		if (MP.IsInMultiplayer)
		{
			return huntdownMapTick;
		}
		return true;
	}

	private static void HuntdownStartBefore(Map map, out ExpeditionMapScope __state)
	{
		__state = ((MP.IsInMultiplayer && map != null) ? new ExpeditionMapScope(map) : null);
	}

	private static Exception HuntdownStartAfter(Exception __exception, ExpeditionMapScope __state)
	{
		__state?.Dispose();
		return __exception;
	}

	private static void HuntdownAlertBefore(out PatchingUtilities.TimeSnapshot? __state)
	{
		__state = (MP.IsInMultiplayer ? PatchingUtilities.TimeSnapshot.GetAndSetFromMap(Find.CurrentMap) : ((PatchingUtilities.TimeSnapshot?)null));
	}

	private static Exception HuntdownAlertAfter(Exception __exception, PatchingUtilities.TimeSnapshot? __state)
	{
		__state?.Set();
		return __exception;
	}

	private static void HuntdownTick(Map map)
	{
		if (GenTicks.TicksGame % 2500 != 0 || Find.Scenario == null)
		{
			return;
		}
		foreach (ScenPart item in Find.Scenario.AllParts.Where((ScenPart p) => p.GetType() == T("ScenPart_Huntdown")))
		{
			Dictionary<Map, int> dictionary = (Dictionary<Map, int>)Get(item, "mapWarningTimers");
			Dictionary<Map, int> dictionary2 = (Dictionary<Map, int>)Get(item, "mapRaidTimers");
			if (!dictionary.TryGetValue(map, out var value) || !dictionary2.TryGetValue(map, out var value2))
			{
				continue;
			}
			Dictionary<Map, int> dictionary3 = new Dictionary<Map, int> { [map] = value };
			Dictionary<Map, int> dictionary4 = new Dictionary<Map, int> { [map] = value2 };
			Set(item, "mapWarningTimers", dictionary3);
			Set(item, "mapRaidTimers", dictionary4);
			bool flag = huntdownMapTick;
			huntdownMapTick = true;
			try
			{
				M("ScenPart_Huntdown", "Tick").Invoke(item, null);
			}
			finally
			{
				huntdownMapTick = flag;
				Set(item, "mapWarningTimers", dictionary);
				Set(item, "mapRaidTimers", dictionary2);
				if (dictionary3.TryGetValue(map, out value))
				{
					dictionary[map] = value;
				}
				else
				{
					dictionary.Remove(map);
				}
				if (dictionary4.TryGetValue(map, out value2))
				{
					dictionary2[map] = value2;
				}
				else
				{
					dictionary2.Remove(map);
				}
			}
		}
	}

	private static void InstallInterceptionRisk()
	{
		Type type = AccessTools.TypeByName("DMSE.InterceptRiskUtility");
		if (!(type == null))
		{
			Type[] parameters = new Type[6]
			{
				typeof(PlanetTile),
				typeof(PlanetTile),
				typeof(Faction),
				typeof(bool),
				typeof(Action),
				typeof(Action)
			};
			MethodInfo methodInfo = AccessTools.DeclaredMethod(type, "ConfirmOrDefer", parameters);
			if (methodInfo == null || !methodInfo.IsStatic || methodInfo.ReturnType != typeof(bool) || type.GetMethods(AccessTools.allDeclared).Count((MethodInfo m) => m.Name == "ConfirmOrDefer") != 1)
			{
				throw new MissingMethodException("DMSE.InterceptRiskUtility.ConfirmOrDefer signature changed");
			}
			MpCompat.harmony.Patch(methodInfo, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "InterceptionRiskPresentation"));
		}
	}

	private static bool InterceptionRiskPresentation(ref bool __result)
	{
		if (!MP.IsInMultiplayer || MP.InInterface)
		{
			return true;
		}
		__result = true;
		return false;
	}

	private static void InstallObjectiveViews()
	{
		MethodInfo methodInfo = AccessTools.DeclaredPropertyGetter(AccessTools.TypeByName("Multiplayer.Client.SimulationTimeContext"), "CurrentOwner") ?? throw new MissingMethodException("Multiplayer SimulationTimeContext.CurrentOwner");
		if (!methodInfo.IsStatic || methodInfo.ReturnType.IsValueType || methodInfo.GetParameters().Length != 0)
		{
			throw new MissingMethodException("Multiplayer simulation owner getter changed");
		}
		activeSimulationOwner = methodInfo.CreateDelegate<Func<object>>();
		objectiveLauncher = T("CompMissileLauncher_WorldTargeting");
		objectiveSearchRadar = T("CompSearchRadar");
		objectiveFireRadar = T("CompFireControlRadar");
		string[] array = new string[2] { "RemainingLaunchers", "RemainingRadars" };
		foreach (string text in array)
		{
			MethodInfo methodInfo2 = AccessTools.DeclaredPropertyGetter(T("MapComponent_MissileBase"), text);
			if (methodInfo2 == null || methodInfo2.IsStatic || methodInfo2.ReturnType != typeof(IEnumerable<Building>))
			{
				throw new MissingMethodException("DMSE missile objective getter changed: " + text);
			}
			MpCompat.harmony.Patch(methodInfo2, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "ObjectiveView"));
		}
	}

	private static bool HasObjectiveComp(Building building, Type type)
	{
		return building.AllComps?.Any((ThingComp comp) => comp != null && type.IsInstanceOfType(comp)) ?? false;
	}

	private static Building[] ObjectiveSnapshot(Map map, bool radars)
	{
		if (map != null && !map.Disposed && Current.Game != null && Find.Maps.Contains(map) && map.listerBuildings != null)
		{
			Faction parentFaction = map.ParentFaction;
			if (parentFaction != null)
			{
				List<Building> list = new List<Building>();
				foreach (Building item in map.listerBuildings.allBuildingsNonColonist)
				{
					if (item == null || !item.Spawned || item.Destroyed || item.Faction != parentFaction)
					{
						continue;
					}
					bool flag = HasObjectiveComp(item, objectiveLauncher);
					bool num;
					if (!radars)
					{
						num = flag;
					}
					else
					{
						if (flag || item is Building_Turret)
						{
							continue;
						}
						if (HasObjectiveComp(item, objectiveSearchRadar))
						{
							goto IL_00b4;
						}
						num = HasObjectiveComp(item, objectiveFireRadar);
					}
					if (!num)
					{
						continue;
					}
					goto IL_00b4;
					IL_00b4:
					list.Add(item);
				}
				return list.ToArray();
			}
		}
		return Array.Empty<Building>();
	}

	private static bool ObjectiveView(MapComponent __instance, MethodBase __originalMethod, ref IEnumerable<Building> __result)
	{
		if (!LocalObjectiveRead)
		{
			return true;
		}
		__result = ObjectiveSnapshot(__instance.map, __originalMethod.Name == "get_RemainingRadars");
		return false;
	}

	private static void InstallRefresh()
	{
		CommandAuthorityBridge.Install();
		Type type = T("WorldComponent_FactionAirDefenseAlert");
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, "StateFor"), new HarmonyMethod(typeof(DeadMansSwitchExpedition), "AlertReadProjection"));
		MethodInfo[] array = new MethodInfo[2]
		{
			M("AirDefenseAlertUI", "InspectFor"),
			AccessTools.PropertyGetter(T("AirspaceRestrictionUtility"), "ActiveZones")
		};
		foreach (MethodInfo original in array)
		{
			MpCompat.harmony.Patch(original, null, null, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "WorldClockIl"));
		}
		MpCompat.harmony.Patch(AccessTools.PropertyGetter(T("AirspaceRestrictionUtility"), "ActiveZones"), new HarmonyMethod(typeof(DeadMansSwitchExpedition), "AirspaceWorldBefore"));
		string[] array2 = new string[3] { "DevInject", "DevAddPressure", "DevSetStage" };
		foreach (string name in array2)
		{
			MP.RegisterSyncMethod(AccessTools.DeclaredMethod(type, name)).SetVersion(30).SetDebugOnly();
		}
		Type type2 = T("CompMissileRackStorage");
		MethodInfo makeThing = AccessTools.DeclaredMethod(typeof(ThingMaker), "MakeThing");
		MethodInfo[] array3 = (from m in type2.GetMethods(AccessTools.allDeclared)
			where m.Name.Contains("b__") && m.ReturnType == typeof(void) && m.GetParameters().Length == 0 && PatchProcessor.GetOriginalInstructions(m).Any((CodeInstruction code) => code.Calls(makeThing))
			select m).ToArray();
		if (array3.Length != 1)
		{
			throw new InvalidOperationException($"DMSE rack fill action changed: {array3.Length}");
		}
		MP.RegisterSyncMethod(array3[0]).SetVersion(30).SetDebugOnly();
		MpCompat.harmony.Patch(array3[0], new HarmonyMethod(typeof(DeadMansSwitchExpedition), "OwnCompCommand"));
		array2 = new string[2] { "cooldownUntil", "nextShotTick" };
		foreach (string name2 in array2)
		{
			FieldInfo field = F("CompTerminalCIWS", name2);
			PatchingUtilities.RegisterTimestampFields(T("CompTerminalCIWS"), (object value, int offset) =>
			{
				field.SetValue(value, (int)field.GetValue(value) + offset);
			});
		}
		StartupDefFingerprint.Register("MPCompat.DMSExpedition.AirDefenseTuning", () =>
		{
			object tuning = AccessTools.PropertyGetter(T("AirDefenseAlertTuningDef"), "Current").Invoke(null, null);
			return StartupDefFingerprint.Hash((from f in T("AirDefenseAlertTuningDef").GetFields(BindingFlags.Instance | BindingFlags.Public)
				where f.DeclaringType == T("AirDefenseAlertTuningDef")
				select f).OrderBy((FieldInfo f) => f.Name, StringComparer.Ordinal).SelectMany((FieldInfo f) => new object[2]
			{
				f.Name,
				f.GetValue(tuning)
			}).ToArray());
		});
		InstallRefreshVisuals();
		DmsExpeditionCombatSettings.Install();
	}

	private static bool AlertReadProjection(object __instance, MapParent parent, bool create, ref object __result)
	{
		if (!MP.IsInMultiplayer || !MP.InInterface)
		{
			return true;
		}
		__result = null;
		if (parent == null || parent.Destroyed || parent.Faction == null || parent.Faction.IsPlayer)
		{
			return false;
		}
		IDictionary dictionary = (IDictionary)Get(__instance, "sites");
		object obj;
		if (dictionary.Contains(parent.ID))
		{
			obj = refreshClone.Invoke(dictionary[parent.ID], null);
			string[] array = new string[2] { "records", "waves" };
			foreach (string name in array)
			{
				IList list = (IList)Get(obj, name);
				IList list2 = (IList)Activator.CreateInstance(list.GetType());
				foreach (object item in list)
				{
					list2.Add((item == null) ? null : refreshClone.Invoke(item, null));
				}
				Set(obj, name, list2);
			}
			Set(obj, "parent", parent);
			if (Get(obj, "faction") != parent.Faction)
			{
				Call(__instance, "Reset", obj, parent);
			}
		}
		else
		{
			if (!create)
			{
				return false;
			}
			obj = Activator.CreateInstance(T("AirDefenseSiteAlert"));
			Set(obj, "siteId", parent.ID);
			Set(obj, "parent", parent);
			Call(__instance, "Reset", obj, parent);
		}
		__result = obj;
		return false;
	}

	private static void InstallRefreshVisuals()
	{
		(string, string)[] array = new (string, string)[2]
		{
			("MapComponent_MissileFx", "MapComponentUpdate"),
			("MapComponent_InterceptEffects", "MapComponentUpdate")
		};
		for (int i = 0; i < array.Length; i++)
		{
			(string, string) tuple = array[i];
			MpCompat.harmony.Patch(M(tuple.Item1, tuple.Item2), new HarmonyMethod(typeof(DeadMansSwitchExpedition), "VisualBefore")
			{
				priority = 0
			}, null, null, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "VisualAfter")
			{
				priority = 800
			});
		}
		MpCompat.harmony.Patch(M("MapComponent_InterceptEffects", "SpawnMajorDebris"), new HarmonyMethod(typeof(DeadMansSwitchExpedition), "PhysicalDebrisBefore"), null, null, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "PhysicalDebrisAfter"));
		MpCompat.harmony.Patch(AccessTools.PropertyGetter(T("InterceptFxBudget"), "Density"), new HarmonyMethod(typeof(DeadMansSwitchExpedition), "PhysicalDebrisDensity"));
	}

	private static void PhysicalDebrisBefore(out int __state)
	{
		__state = physicalDebris;
		if (MP.IsInMultiplayer)
		{
			physicalDebris++;
		}
	}

	private static Exception PhysicalDebrisAfter(Exception __exception, int __state)
	{
		physicalDebris = __state;
		return __exception;
	}

	private static bool PhysicalDebrisDensity(ref int __result)
	{
		if (!MP.IsInMultiplayer || physicalDebris == 0)
		{
			return true;
		}
		__result = 1;
		return false;
	}

	private static void AirspaceWorldBefore()
	{
		if (MP.IsInMultiplayer && Find.World != null && (!airspaceWorld.TryGetTarget(out var target) || target != Find.World))
		{
			F("AirspaceRestrictionUtility", "cacheTick").SetValue(null, -1);
			airspaceWorld.SetTarget(Find.World);
		}
	}

	private static void InstallSettings()
	{
		StartupDefFingerprint.Register("MPCompat.DMSExpedition.SpaceTraversal", () =>
		{
			TerrainDef namedSilentFail = DefDatabase<TerrainDef>.GetNamedSilentFail("Space");
			return StartupDefFingerprint.Hash(new object[3]
			{
				namedSilentFail?.passability,
				namedSilentFail?.pathCost,
				namedSilentFail?.affordances?.Select((TerrainAffordanceDef d) => d.defName).OrderBy((string n) => n, StringComparer.Ordinal).ToArray()
			});
		});
		FieldInfo records = F("PlayerConfigSettings", "records");
		foreach (MethodInfo item in from m in T("ImpactCraterService").GetMethods(AccessTools.allDeclared)
			where m.GetMethodBody() != null && PatchProcessor.GetCurrentInstructions(m).Any((CodeInstruction i) => i.LoadsField(records))
			select m)
		{
			MpCompat.harmony.Patch(item, null, null, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "CraterRecordsIl"));
		}
		MpCompat.harmony.Patch(M("ImpactCraterService", "AddOrUpdate"), new HarmonyMethod(typeof(DeadMansSwitchExpedition), "CraterWriteBefore"), null, null, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "CraterWriteAfter"));
		MpCompat.harmony.Patch(M("ImpactCraterService", "Remove"), new HarmonyMethod(typeof(DeadMansSwitchExpedition), "CraterWriteBefore"), null, null, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "CraterWriteAfter"));
		MpCompat.harmony.Patch(AccessTools.Method(typeof(ModSettings), "Write"), new HarmonyMethod(typeof(DeadMansSwitchExpedition), "CraterWriteFile"));
	}

	internal static List<IExposable> LocalRecords()
	{
		if (Settings == null || !(Get(Settings, "records") is IEnumerable source))
		{
			return new List<IExposable>();
		}
		return source.Cast<IExposable>().ToList();
	}

	private static object CraterRecords(object settings)
	{
		if (!MP.IsInMultiplayer || Current.Game == null)
		{
			return Get(settings, "records");
		}
		DmsExpeditionSettings component = Current.Game.GetComponent<DmsExpeditionSettings>();
		if (component.EditingRecords != null)
		{
			return component.EditingRecords;
		}
		IList list = (IList)Activator.CreateInstance(F("PlayerConfigSettings", "records").FieldType);
		foreach (IExposable record in component.Records)
		{
			list.Add(record);
		}
		if (craterWrite > 0)
		{
			component.EditingRecords = list;
		}
		return list;
	}

	private static IEnumerable<CodeInstruction> CraterRecordsIl(IEnumerable<CodeInstruction> instructions)
	{
		FieldInfo field = F("PlayerConfigSettings", "records");
		int count = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.LoadsField(field))
			{
				count++;
				yield return new CodeInstruction(instruction)
				{
					opcode = OpCodes.Call,
					operand = AccessTools.Method(typeof(DeadMansSwitchExpedition), "CraterRecords")
				};
				yield return new CodeInstruction(OpCodes.Castclass, field.FieldType);
			}
			else
			{
				yield return instruction;
			}
		}
		if (count == 0)
		{
			throw new InvalidOperationException("DMSE crater records IL changed");
		}
	}

	private static void CraterWriteBefore(object[] __args, out bool __state)
	{
		__state = MP.IsInMultiplayer && Current.Game != null;
		if (__state)
		{
			if (__args.Length == 1 && __args[0]?.GetType() == T("ImpactCraterRecord"))
			{
				object value = __args[0];
				Set(value, "recordId", "mp:" + Get(value, "campaignId")?.ToString() + ":" + Get(value, "impactTick")?.ToString() + ":" + Get(value, "craterName"));
			}
			craterWrite++;
		}
	}

	private static Exception CraterWriteAfter(Exception __exception, bool __state)
	{
		if (!__state)
		{
			return __exception;
		}
		craterWrite--;
		DmsExpeditionSettings component = Current.Game.GetComponent<DmsExpeditionSettings>();
		if (craterWrite == 0 && component.EditingRecords != null)
		{
			component.Records = component.EditingRecords.Cast<IExposable>().ToList();
			component.EditingRecords = null;
		}
		return __exception;
	}

	private static bool CraterWriteFile()
	{
		if (MP.IsInMultiplayer && craterWrite != 0)
		{
			return MP.IsHosting;
		}
		return true;
	}

	private static void InstallStrike()
	{
		MP.RegisterSyncMethod(typeof(DeadMansSwitchExpedition), "SyncCommitStrike").SetVersion(13).ExposeParameter(1);
		MP.RegisterSyncMethod(typeof(DeadMansSwitchExpedition), "SyncAdjustStrike").SetVersion(13).ExposeParameter(2);
		MP.RegisterSyncMethod(typeof(DeadMansSwitchExpedition), "SyncAutoStrike").SetVersion(13);
		MP.RegisterSyncMethod(typeof(DeadMansSwitchExpedition), "SyncRenameStrike").SetVersion(13);
		MpCompat.harmony.Patch(M("CompStrikeConsole", "Commit"), new HarmonyMethod(typeof(DeadMansSwitchExpedition), "CommitStrike"));
		MpCompat.harmony.Patch(M("CompStrikeConsole", "ApplyAdjustment"), new HarmonyMethod(typeof(DeadMansSwitchExpedition), "AdjustStrike"));
		MpCompat.harmony.Patch(M("StrikeMission", "set_RenamableLabel"), new HarmonyMethod(typeof(DeadMansSwitchExpedition), "RenameStrike")
		{
			priority = 800
		});
		MpCompat.harmony.Patch(M("Dialog_StrikePlan", "DrawAutoFireToggle"), new HarmonyMethod(typeof(DeadMansSwitchExpedition), "AutoStrikeBefore"), null, null, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "AutoStrikeAfter"));
		MethodInfo[] array = new MethodInfo[2]
		{
			M("CompStrikeConsole", "OpenPlanner"),
			M("Dialog_RenameStrikeMission", "DoWindowContents")
		};
		foreach (MethodInfo original in array)
		{
			MpCompat.harmony.Patch(original, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "LocalRandBefore"), null, null, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "LocalRandAfter"));
		}
	}

	private static void LocalRandBefore(out bool __state)
	{
		__state = MP.IsInMultiplayer && MP.InInterface;
		if (__state)
		{
			Rand.PushState();
		}
	}

	private static Exception LocalRandAfter(Exception __exception, bool __state)
	{
		if (__state)
		{
			Rand.PopState();
		}
		return __exception;
	}

	private static IEnumerable<ThingComp> StrikeConsoles()
	{
		return from c in (from t in Find.Maps.OrderBy((Map m) => m.uniqueID).SelectMany((Map m) => m.listerThings.AllThings.OfType<ThingWithComps>())
				orderby t.thingIDNumber
				select t).SelectMany((ThingWithComps t) => t.AllComps)
			where c.GetType() == T("CompStrikeConsole")
			select c;
	}

	private static ThingComp ConsoleFor(object mission)
	{
		if (mission != null)
		{
			return StrikeConsoles().FirstOrDefault((ThingComp c) => Get(c, "mission") == mission);
		}
		return null;
	}

	private static bool CommitStrike(ThingComp __instance, IExposable draft, ref string reason, ref bool __result)
	{
		if (!MP.IsInMultiplayer || !MP.InInterface)
		{
			return true;
		}
		SyncCommitStrike(__instance, draft);
		reason = null;
		__result = true;
		return false;
	}

	private static void SyncCommitStrike(ThingComp console, IExposable draft)
	{
		if (Owned(console) && !(draft?.GetType() != T("StrikeMission")))
		{
			Set(draft, "createdTick", GenTicks.TicksGame);
			Call(console, "Commit", draft, null);
		}
	}

	private static bool AdjustStrike(ThingComp __instance, IList waves, ref string reason, ref bool __result)
	{
		if (!MP.IsInMultiplayer || !MP.InInterface)
		{
			return true;
		}
		object obj = Get(__instance, "mission");
		if (obj != null)
		{
			SyncAdjustStrike(__instance, (int)Get(obj, "id"), new DmsStrikeWaves
			{
				Waves = waves.Cast<IExposable>().ToList()
			});
		}
		reason = null;
		__result = true;
		return false;
	}

	private static void SyncAdjustStrike(ThingComp console, int missionId, DmsStrikeWaves payload)
	{
		if (!Owned(console))
		{
			return;
		}
		object obj = Get(console, "mission");
		if (obj == null || (int)Get(obj, "id") != missionId || payload?.Waves == null || payload.Waves.Count > 100)
		{
			return;
		}
		IList list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(T("StrikeWave")));
		foreach (IExposable wave in payload.Waves)
		{
			if (wave?.GetType() != T("StrikeWave"))
			{
				return;
			}
			list.Add(wave);
		}
		Call(console, "ApplyAdjustment", list, null);
	}

	private static bool RenameStrike(object __instance, string value)
	{
		if (!MP.IsInMultiplayer || !MP.InInterface)
		{
			return true;
		}
		ThingComp thingComp = ConsoleFor(__instance);
		if (thingComp == null)
		{
			Set(__instance, "name", value?.Trim());
			return false;
		}
		SyncRenameStrike(thingComp, (int)Get(__instance, "id"), value);
		return false;
	}

	private static void SyncRenameStrike(ThingComp console, int missionId, string name)
	{
		if (Owned(console))
		{
			object obj = Get(console, "mission");
			if (obj != null && (int)Get(obj, "id") == missionId)
			{
				Call(obj, "set_RenamableLabel", name);
			}
		}
	}

	private static void AutoStrikeBefore(object wave, out AutoEdit __state)
	{
		__state = null;
		if (!MP.IsInMultiplayer || !MP.InInterface)
		{
			return;
		}
		foreach (ThingComp item in StrikeConsoles())
		{
			object obj = Get(item, "mission");
			if (obj != null)
			{
				int num = ((IList)Get(obj, "waves")).IndexOf(wave);
				if (num >= 0)
				{
					__state = new AutoEdit
					{
						Wave = wave,
						Before = (bool)Get(wave, "autoFire"),
						Console = item,
						Mission = (int)Get(obj, "id"),
						Index = num
					};
					break;
				}
			}
		}
	}

	private static Exception AutoStrikeAfter(Exception __exception, AutoEdit __state)
	{
		if (__state == null)
		{
			return __exception;
		}
		bool flag = (bool)Get(__state.Wave, "autoFire");
		Set(__state.Wave, "autoFire", __state.Before);
		if (__exception == null && flag != __state.Before)
		{
			SyncAutoStrike(__state.Console, __state.Mission, __state.Index, flag);
		}
		return __exception;
	}

	private static void SyncAutoStrike(ThingComp console, int missionId, int index, bool value)
	{
		if (!Owned(console))
		{
			return;
		}
		object obj = Get(console, "mission");
		if (obj != null && (int)Get(obj, "id") == missionId)
		{
			IList list = (IList)Get(obj, "waves");
			if (index >= 0 && index < list.Count)
			{
				Set(list[index], "autoFire", value);
			}
		}
	}

	private static void InstallTransfer()
	{
		executingPlayer = AccessTools.Field(AccessTools.TypeByName("Multiplayer.Client.TickPatch"), "currentExecutingCmdPlayerId");
		factionPush = AccessTools.Method(AccessTools.TypeByName("Multiplayer.Client.Util.MpScope"), "PushFaction", new Type[1] { typeof(Faction) });
		moveThings = AccessTools.Method(AccessTools.TypeByName("Multiplayer.Client.Patches.TimestampFixer"), "MoveThings");
		string[] array = new string[2] { "SetPlan", "BeginLaunchSequence" };
		foreach (string name in array)
		{
			MpCompat.harmony.Patch(M("CompTransferConsole", name), null, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "RecordCommander"));
		}
		MpCompat.harmony.Patch(M("CompTransferConsole", "PostExposeData"), null, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "CommanderExpose"));
		array = new string[2] { "Commit", "TryFireFromUI" };
		foreach (string name2 in array)
		{
			MpCompat.harmony.Patch(M("CompStrikeConsole", name2), null, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "RecordCommander"));
		}
		MpCompat.harmony.Patch(M("CompStrikeConsole", "PostExposeData"), null, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "CommanderExpose"));
		MpCompat.harmony.Patch(M("WorldObject_TransferShip", "BeginFlight"), null, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "FlightCommander"));
		MpCompat.harmony.Patch(M("WorldObject_TransferShip", "ExposeData"), null, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "FlightCommanderExpose"));
		MpCompat.harmony.Patch(M("GameComponent_TransferLanding", "Request"), new HarmonyMethod(typeof(DeadMansSwitchExpedition), "OwnLandingUi"));
		MpCompat.harmony.Patch(M("TransferLandingUtility", "Land"), new HarmonyMethod(typeof(DeadMansSwitchExpedition), "LandTransfer"));
		MpCompat.harmony.Patch(M("TransferLandingUtility", "BeginLanding"), new HarmonyMethod(typeof(DeadMansSwitchExpedition), "BeginLandingUi"));
		MP.RegisterSyncMethod(typeof(DeadMansSwitchExpedition), "SyncBeginLanding").SetVersion(13).CancelIfAnyArgNull();
		MP.RegisterSyncMethod(M("TransferLandingUtility", "LandAutomatically")).SetVersion(13).CancelIfAnyArgNull();
		MP.RegisterSyncMethod(typeof(DeadMansSwitchExpedition), "SyncCancelLanding").SetVersion(13).CancelIfAnyArgNull();
		array = new string[3] { "DebugNextPhase", "Impact", "Arrive" };
		foreach (string name3 in array)
		{
			MP.RegisterSyncMethod(M("WorldObject_TransferShip", name3)).SetVersion(13).SetDebugOnly();
		}
		int num = 0;
		foreach (MethodInfo item in from m in T("WorldObject_TransferShip").GetMethods(AccessTools.allDeclared)
			where m.Name.Contains("b__") && m.ReturnType == typeof(void) && m.GetParameters().Length == 0
			select m)
		{
			if (PatchProcessor.GetCurrentInstructions(item).Any((CodeInstruction code) => code.StoresField(F("WorldObject_TransferShip", "landingTarget"))))
			{
				MpCompat.harmony.Patch(item, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "CancelLandingUi"));
				num++;
			}
		}
		if (num != 1)
		{
			throw new InvalidOperationException($"DMSE landing cancellation IL changed: {num}");
		}
		MethodInfo abandon = AccessTools.Method(typeof(GravshipUtility), "AbandonMap");
		MethodInfo[] array2 = (from m in T("WorldObject_TransferShip").GetMethods(AccessTools.allDeclared)
			where m.Name.Contains("b__") && m.ReturnType == typeof(void) && m.GetParameters().Length == 0 && PatchProcessor.GetCurrentInstructions(m).Any((CodeInstruction code) => code.Calls(abandon))
			select m).ToArray();
		if (array2.Length != 1)
		{
			throw new InvalidOperationException($"DMSE confirmed abandonment IL changed: {array2.Length}");
		}
		MpCompat.harmony.Patch(array2[0], new HarmonyMethod(typeof(DeadMansSwitchExpedition), "AbandonTransferUi"));
		MP.RegisterSyncMethod(typeof(DeadMansSwitchExpedition), "SyncAbandonTransferShip").SetVersion(13).CancelIfAnyArgNull();
		MpCompat.harmony.Patch(M("TransferLandingUtility", "GenerateSiteMap"), new HarmonyMethod(typeof(DeadMansSwitchExpedition), "ShipFactionBefore"), null, null, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "ShipFactionAfter"));
		MpCompat.harmony.Patch(M("ShipSeparationUtility", "Separate"), new HarmonyMethod(typeof(DeadMansSwitchExpedition), "ConsoleFactionBefore"), null, null, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "ShipFactionAfter"));
		MpCompat.harmony.Patch(M("ShipSeparationUtility", "PlaceOnShip"), new HarmonyMethod(typeof(DeadMansSwitchExpedition), "TransferClock"));
		ConstructorInfo original = T("TransferTakeoffCutscene").GetConstructors().Single();
		MpCompat.harmony.Patch(original, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "TakeoffComplete"));
		MP.RegisterSyncMethod(typeof(DeadMansSwitchExpedition), "SyncTransferLand").SetVersion(13).CancelIfAnyArgNull();
		MP.RegisterSyncMethod(typeof(DeadMansSwitchExpedition), "SyncClaimFlight").SetVersion(13).CancelIfAnyArgNull();
		MpCompat.harmony.Patch(M("WorldObject_TransferShip", "GetGizmos"), null, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "FlightGizmos"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(WorldObject), "GetGizmos"), null, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "TerminalGizmos"));
		MP.RegisterSyncMethod(M("ShipSeparationUtility", "DebugSeparateNow")).SetVersion(13).SetDebugOnly();
		InstallTerminalGuidance();
	}

	private static IDisposable ShipFaction(WorldObject ship)
	{
		if (!MP.IsInMultiplayer || ship == null || ship.Faction?.IsPlayer != true)
		{
			return null;
		}
		return (IDisposable)factionPush.Invoke(null, new object[1] { ship.Faction });
	}

	private static void ShipFactionBefore(WorldObject ship, out IDisposable __state)
	{
		__state = ShipFaction(ship);
	}

	private static void ConsoleFactionBefore(ThingComp console, out IDisposable __state)
	{
		__state = ((MP.IsInMultiplayer && console != null && console.parent.Faction?.IsPlayer == true) ? ((IDisposable)factionPush.Invoke(null, new object[1] { console.parent.Faction })) : null);
	}

	private static Exception ShipFactionAfter(Exception __exception, IDisposable __state)
	{
		__state?.Dispose();
		return __exception;
	}

	private static void RecordCommander(ThingComp __instance)
	{
		if (MP.IsInMultiplayer && MP.IsExecutingSyncCommand && Owned(__instance))
		{
			Commander value = commanders.GetValue(__instance, (ThingComp _) => new Commander());
			value.Player = (int)executingPlayer.GetValue(null);
			value.Name = PlayerName(value.Player);
		}
	}

	private static void CommanderExpose(ThingComp __instance)
	{
		Commander value = commanders.GetValue(__instance, (ThingComp _) => new Commander());
		Scribe_Values.Look(ref value.Player, "mpDmseCommander", -1);
		Scribe_Values.Look(ref value.Name, "mpDmseCommanderName");
	}

	private static void FlightCommander(WorldObject __instance, Thing console)
	{
		if (!(console is ThingWithComps { AllComps: var allComps }))
		{
			return;
		}
		ThingComp thingComp = allComps.FirstOrDefault((ThingComp c) => c.GetType() == T("CompTransferConsole"));
		if (thingComp != null)
		{
			Commander value = commanders.GetValue(thingComp, (ThingComp _) => new Commander());
			Commander value2 = flightCommanders.GetValue(__instance, (WorldObject _) => new Commander());
			value2.Player = value.Player;
			value2.Name = value.Name;
		}
	}

	private static void FlightCommanderExpose(WorldObject __instance)
	{
		Commander value = flightCommanders.GetValue(__instance, (WorldObject _) => new Commander());
		Scribe_Values.Look(ref value.Player, "mpDmseCommander", -1);
		Scribe_Values.Look(ref value.Name, "mpDmseCommanderName");
	}

	private static string PlayerName(int id)
	{
		object obj = Players.FirstOrDefault((object p) => (int)Get(p, "id") == id);
		if (obj == null)
		{
			return null;
		}
		return (string)Get(obj, "username");
	}

	private static bool CurrentCommander(Commander owner, int player)
	{
		if (owner.Name == null)
		{
			if (owner.Player >= 0)
			{
				return owner.Player == player;
			}
			return false;
		}
		return owner.Name == PlayerName(player);
	}

	private static bool OwnerOnline(Commander owner)
	{
		if (owner.Name == null)
		{
			return Players.Any((object p) => (int)Get(p, "id") == owner.Player);
		}
		return Players.Any((object p) => (string)Get(p, "username") == owner.Name);
	}

	private static bool OwnLandingUi(WorldObject ship)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (Session == null || ship?.Faction == null)
		{
			return false;
		}
		int local = (int)Get(Session, "playerId");
		if (Players.Any((object p) => (int)Get(p, "id") == local && (int)Get(p, "factionId") == ship.Faction.loadID))
		{
			return CurrentCommander(flightCommanders.GetValue(ship, (WorldObject _) => new Commander()), local);
		}
		return false;
	}

	private static IEnumerable<Gizmo> FlightGizmos(IEnumerable<Gizmo> __result, WorldObject __instance)
	{
		foreach (Gizmo item in __result)
		{
			yield return item;
		}
		if (MP.IsInMultiplayer && __instance.Faction == Faction.OfPlayer && __instance is MapParent { HasMap: not false } && !OwnerOnline(flightCommanders.GetValue(__instance, (WorldObject _) => new Commander())))
		{
			yield return new Command_Action
			{
				defaultLabel = "MpDmsClaimLanding".Translate(),
				defaultDesc = "MpDmsClaimLandingDesc".Translate(),
				action = () =>
				{
					SyncClaimFlight(__instance);
				}
			};
		}
	}

	private static void SyncClaimFlight(WorldObject ship)
	{
		if (ship == null || ship.Destroyed || ship.Faction != Faction.OfPlayer)
		{
			return;
		}
		Commander value = flightCommanders.GetValue(ship, (WorldObject _) => new Commander());
		if (OwnerOnline(value))
		{
			return;
		}
		value.Player = (int)executingPlayer.GetValue(null);
		value.Name = PlayerName(value.Player);
		if (!T("WorldObject_TransferShip").IsInstanceOfType(ship))
		{
			return;
		}
		object obj = Get(ship, "landingTarget");
		if (!(obj is PlanetTile))
		{
			return;
		}
		PlanetTile tile = (PlanetTile)obj;
		if (tile.Valid)
		{
			Map map = Find.Maps.FirstOrDefault((Map m) => m.Tile == tile);
			if (map != null)
			{
				M("GameComponent_TransferLanding", "Request").Invoke(null, new object[2] { ship, map });
			}
		}
	}

	private static IEnumerable<Gizmo> TerminalGizmos(IEnumerable<Gizmo> __result, WorldObject __instance)
	{
		foreach (Gizmo item in __result)
		{
			yield return item;
		}
		if (MP.IsInMultiplayer && T("ScorerProjectile_WorldObject").IsInstanceOfType(__instance) && __instance.Faction == Faction.OfPlayer && !OwnerOnline(flightCommanders.GetValue(__instance, (WorldObject _) => new Commander())))
		{
			yield return new Command_Action
			{
				defaultLabel = "MpDmsClaimLanding".Translate(),
				defaultDesc = "MpDmsClaimLandingDesc".Translate(),
				action = () =>
				{
					SyncClaimFlight(__instance);
				}
			};
		}
	}

	private static void TakeoffComplete(ref Map displayMap, ref Action onComplete, Map captureMap)
	{
		if (MP.IsInMultiplayer && onComplete != null)
		{
			Action obj = onComplete;
			onComplete = null;
			obj();
			Map obj2 = displayMap;
			if (obj2 != null && obj2.Disposed && captureMap != null)
			{
				displayMap = captureMap;
			}
		}
	}

	private static void TransferClock(Map ship, Map origin, object snap)
	{
		if (MP.IsInMultiplayer && ship != origin)
		{
			Thing[] array = ((IEnumerable)Get(snap, "things")).Cast<Thing>().ToArray();
			Thing[] source = (from object p in (IEnumerable)Get(snap, "pawns")
				select (Pawn)Get(p, "pawn")).Cast<Thing>().Concat(array).Distinct()
				.ToArray();
			HashSet<Pawn> direct = new HashSet<Pawn>(source.OfType<Pawn>());
			MethodInfo getClock = AccessTools.Method(AccessTools.TypeByName("Multiplayer.Client.Patches.TimestampFixer"), "GetPawnClockMap");
			List<(Pawn, Map)> list = (from p in source.OfType<IThingHolder>().SelectMany((IThingHolder h) => ThingOwnerUtility.GetAllThingsRecursively(h)).OfType<Pawn>()
					.Concat(direct)
					.Distinct()
				orderby Depth(p) descending, p.thingIDNumber
				select (pawn: p, source: direct.Contains(p) ? origin : ((Map)getClock.Invoke(null, new object[1] { p })))).ToList();
			moveThings.Invoke(null, new object[4] { list, ship, array, origin });
		}
		static int Depth(Thing thing)
		{
			int num = 0;
			for (IThingHolder parentHolder = thing.ParentHolder; parentHolder != null; parentHolder = parentHolder.ParentHolder)
			{
				num++;
			}
			return num;
		}
	}

	private static bool LandTransfer(WorldObject ship, Map dest, IntVec3 offset, bool moved)
	{
		if (!MP.IsInMultiplayer || landingNow)
		{
			return true;
		}
		if (MP.InInterface)
		{
			SyncTransferLand(ship, dest, offset, moved);
		}
		else
		{
			QueueTransferLand(ship, dest, offset, moved);
		}
		return false;
	}

	private static bool BeginLandingUi(WorldObject ship)
	{
		if (!MP.IsInMultiplayer || !MP.InInterface)
		{
			return true;
		}
		SyncBeginLanding(ship);
		return false;
	}

	private static bool AbandonTransferUi(WorldObject __instance)
	{
		if (!MP.IsInMultiplayer || !MP.InInterface)
		{
			return true;
		}
		SyncAbandonTransferShip(__instance);
		return false;
	}

	private static void SyncAbandonTransferShip(WorldObject ship)
	{
		if (ship == null || ship.Destroyed || !T("WorldObject_TransferShip").IsInstanceOfType(ship) || ship.Faction != Faction.OfPlayer || !(ship is MapParent { HasMap: not false } mapParent) || mapParent.Map.Disposed || (bool)Property(ship, "InFlight") || mapParent.Map.mapPawns.FreeColonistsSpawnedCount != 0)
		{
			return;
		}
		using (ShipFaction(ship))
		{
			GravshipUtility.AbandonMap(mapParent.Map);
		}
	}

	private static void SyncBeginLanding(WorldObject ship)
	{
		if (ship == null || ship.Destroyed || ship.Faction != Faction.OfPlayer || (bool)Property(ship, "InFlight"))
		{
			return;
		}
		Commander value = flightCommanders.GetValue(ship, (WorldObject _) => new Commander());
		int num = (int)executingPlayer.GetValue(null);
		if (OwnerOnline(value) && !CurrentCommander(value, num))
		{
			return;
		}
		value.Player = num;
		value.Name = PlayerName(num);
		using (ShipFaction(ship))
		{
			M("TransferLandingUtility", "BeginLanding").Invoke(null, new object[1] { ship });
		}
	}

	private static bool CancelLandingUi(WorldObject __instance)
	{
		if (!MP.IsInMultiplayer || !MP.InInterface)
		{
			return true;
		}
		SyncCancelLanding(__instance);
		return false;
	}

	private static void SyncCancelLanding(WorldObject ship)
	{
		WorldObject worldObject = ship;
		if (worldObject != null && !worldObject.Destroyed && ship.Faction == Faction.OfPlayer)
		{
			Set(ship, "landingTarget", PlanetTile.Invalid);
			Current.Game.GetComponent<DmsExpeditionLandings>().Pending.RemoveAll((DmsExpeditionLanding l) => l.Ship == ship);
		}
	}

	private static void SyncTransferLand(WorldObject ship, Map dest, IntVec3 offset, bool moved)
	{
		if (ship?.Faction == Faction.OfPlayer)
		{
			QueueTransferLand(ship, dest, offset, moved);
		}
	}

	private static void QueueTransferLand(WorldObject ship, Map dest, IntVec3 offset, bool moved)
	{
		WorldObject worldObject = ship;
		if (worldObject != null && !worldObject.Destroyed && ship is MapParent { HasMap: not false } && dest != null && !dest.Disposed && !(bool)Property(ship, "InFlight") && !((PlanetTile)Get(ship, "landingTarget") != dest.Tile))
		{
			List<DmsExpeditionLanding> pending = Current.Game.GetComponent<DmsExpeditionLandings>().Pending;
			if (!pending.Any((DmsExpeditionLanding l) => l.Ship == ship))
			{
				pending.Add(new DmsExpeditionLanding
				{
					Ship = ship,
					Destination = dest,
					Offset = offset,
					Moved = moved
				});
			}
		}
	}

	private static void TransferMapTick(Map map)
	{
		List<DmsExpeditionLanding> pending = Current.Game.GetComponent<DmsExpeditionLandings>().Pending;
		DmsExpeditionLanding[] array = (from l in pending
			where l.Destination == map
			orderby l.Ship?.ID ?? (-1)
			select l).ToArray();
		foreach (DmsExpeditionLanding dmsExpeditionLanding in array)
		{
			WorldObject ship = dmsExpeditionLanding.Ship;
			if (ship == null || ship.Destroyed || !(ship is MapParent { HasMap: not false } mapParent) || (PlanetTile)Get(ship, "landingTarget") != map.Tile)
			{
				pending.Remove(dmsExpeditionLanding);
				continue;
			}
			using (ShipFaction(ship))
			{
				object obj = M("TransferLandingUtility", "LandingCells").Invoke(null, new object[1] { mapParent.Map });
				if (!((AcceptanceReport)M("TransferLandingUtility", "CanLandAt").Invoke(null, new object[5] { map, obj, dmsExpeditionLanding.Offset, null, null })).Accepted)
				{
					M("GameComponent_TransferLanding", "Request").Invoke(null, new object[2] { ship, map });
					pending.Remove(dmsExpeditionLanding);
					continue;
				}
				landingNow = true;
				try
				{
					M("TransferLandingUtility", "Land").Invoke(null, new object[4] { ship, map, dmsExpeditionLanding.Offset, dmsExpeditionLanding.Moved });
					pending.Remove(dmsExpeditionLanding);
				}
				finally
				{
					landingNow = false;
				}
			}
		}
	}

	private static void InstallVisual()
	{
		MethodInfo[] array = new MethodInfo[5]
		{
			M("CompEffecterLaunch", "CompTick"),
			M("CompEffecterLaunch", "ThrowExhaust"),
			M("MapComponent_TransferBurn", "WarmUpSparks"),
			M("MapComponent_InterceptEffects", "AddInterception"),
			M("CompTurretFacingRenderer", "CompTick")
		};
		foreach (MethodInfo original in array)
		{
			MpCompat.harmony.Patch(original, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "VisualBefore")
			{
				priority = 0
			}, null, null, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "VisualAfter")
			{
				priority = 800
			});
		}
		MpCompat.harmony.Patch(M("CompEffecterLaunch", "ShouldShowEffecter"), null, null, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "IgnitionMapIl"));
		MpCompat.harmony.Patch(M("CompTurretFacingRenderer", "PostExposeData"), null, new HarmonyMethod(typeof(DeadMansSwitchExpedition), "TurretMotionExpose"));
		FieldInfo deadline = F("CompTurretFacingRenderer", "aimOverrideUntilTick");
		PatchingUtilities.RegisterTimestampFields(T("CompTurretFacingRenderer"), (object value, int offset) =>
		{
			int num = (int)deadline.GetValue(value);
			if (num != -1)
			{
				deadline.SetValue(value, num + offset);
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

	private static Map IgnitionMap(ThingComp comp)
	{
		if (!MP.IsInMultiplayer)
		{
			return Find.CurrentMap;
		}
		return comp.parent.MapHeld;
	}

	private static IEnumerable<CodeInstruction> IgnitionMapIl(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo getter = AccessTools.PropertyGetter(typeof(Find), "CurrentMap");
		int count = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(getter))
			{
				count++;
				yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
				yield return CodeInstruction.Call(typeof(DeadMansSwitchExpedition), "IgnitionMap");
			}
			else
			{
				yield return instruction;
			}
		}
		if (count != 1)
		{
			throw new InvalidOperationException($"DMSE saved ignition view condition changed: {count}");
		}
	}

	private static void TurretMotionExpose(object __instance)
	{
		int value = (int)Get(__instance, "ticksUntilIdleTurn");
		int value2 = (int)Get(__instance, "idleTurnTicksLeft");
		int value3 = (int)Get(__instance, "aimOverrideUntilTick");
		bool value4 = (bool)Get(__instance, "idleTurnClockwise");
		Vector3 value5 = (Vector3)Get(__instance, "aimOverridePos");
		Scribe_Values.Look(ref value, "mpDmseIdleTurnIn", 0);
		Scribe_Values.Look(ref value2, "mpDmseIdleTurnLeft", 0);
		Scribe_Values.Look(ref value4, "mpDmseIdleClockwise", defaultValue: false);
		Scribe_Values.Look(ref value3, "mpDmseAimUntil", -1);
		Scribe_Values.Look(ref value5, "mpDmseAimPosition");
		if (Scribe.mode == LoadSaveMode.LoadingVars)
		{
			Set(__instance, "ticksUntilIdleTurn", value);
			Set(__instance, "idleTurnTicksLeft", value2);
			Set(__instance, "idleTurnClockwise", value4);
			Set(__instance, "aimOverrideUntilTick", value3);
			Set(__instance, "aimOverridePos", value5);
		}
	}
}
