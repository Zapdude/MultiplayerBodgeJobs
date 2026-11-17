using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.ExceptionServices;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

internal static class VanillaChristmasDistribution
{
	private sealed class AnnualRun
	{
		internal Game Game;

		internal GameComponent Manager;

		internal List<Map> Maps;

		internal List<Gift> Plan = new List<Gift>();

		internal int Start;

		internal int PresentIndex;

		internal int RemainderIndex;

		internal bool Capturing;

		internal Gift Delivery;
	}

	private sealed class SpawnFrame
	{
		internal SpawnFrame Previous;

		internal Gift Gift;
	}

	private sealed class MapScope : IDisposable
	{
		private readonly object owner;

		private readonly Map previous;

		private bool disposed;

		internal MapScope(Map map)
		{
			owner = mapOwner(map) ?? throw new InvalidOperationException("Christmas recipient has no multiplayer clock");
			previous = tickingMap();
			tickingMap() = map;
			try
			{
				mapPre(owner);
			}
			catch
			{
				tickingMap() = previous;
				throw;
			}
		}

		public void Dispose()
		{
			if (disposed)
			{
				return;
			}
			disposed = true;
			try
			{
				mapPost(owner);
			}
			finally
			{
				tickingMap() = previous;
			}
		}
	}

	internal sealed class Gift : IExposable
	{
		internal int MapId;

		internal int Count;

		internal QualityCategory Quality;

		internal ThingDef CrackerDef;

		internal bool IsCracker;

		internal bool Completed;

		internal Thing Attempt;

		internal bool Delivered
		{
			get
			{
				if (!Completed)
				{
					return Attempt?.Spawned ?? false;
				}
				return true;
			}
		}

		public void ExposeData()
		{
			Scribe_Values.Look(ref MapId, "mapId", -1);
			Scribe_Values.Look(ref Count, "count", 1);
			Scribe_Values.Look(ref Quality, "quality", QualityCategory.Awful);
			Scribe_Values.Look(ref IsCracker, "isCracker", defaultValue: false);
			Scribe_Defs.Look(ref CrackerDef, "crackerDef");
			bool value = Delivered;
			Scribe_Values.Look(ref value, "delivered", defaultValue: false);
			if (Scribe.mode == LoadSaveMode.LoadingVars)
			{
				Completed = value;
			}
		}
	}

	internal sealed class DistributionState : IExposable
	{
		internal int LastPayoutYear = int.MinValue;

		internal int NextMapId = -1;

		internal List<Gift> Pending = new List<Gift>();

		public void ExposeData()
		{
			Scribe_Values.Look(ref LastPayoutYear, "lastPayoutYear", int.MinValue);
			Scribe_Values.Look(ref NextMapId, "nextMapId", -1);
			Scribe_Collections.Look(ref Pending, "pending", LookMode.Deep);
			if (Scribe.mode == LoadSaveMode.PostLoadInit)
			{
				if (Pending == null)
				{
					Pending = new List<Gift>();
				}
				Pending.RemoveAll((Gift g) => g == null || g.Delivered || g.Count <= 0);
			}
		}
	}

	private static AccessTools.FieldRef<GameComponent, int> favor;

	private static Action<object, Map> convert;

	private static Action<object, Map, QualityCategory> spawnPresent;

	private static Action<object, Map, ThingDef, int> spawnCrackers;

	private static Func<Map, object> mapOwner;

	private static Action<object> mapPre;

	private static Action<object> mapPost;

	private static Func<Faction> spectatorFaction;

	private static AccessTools.FieldRef<Map> tickingMap;

	[ThreadStatic]
	private static AnnualRun active;

	[ThreadStatic]
	private static SpawnFrame spawning;

	internal static bool Installed { get; private set; }

	internal static void Install()
	{
		if (!Installed)
		{
			Type type = AccessTools.TypeByName("VanillaChristmasExpanded.FestiveFavorManager") ?? throw new TypeLoadException("VanillaChristmasExpanded.FestiveFavorManager");
			favor = AccessTools.FieldRefAccess<GameComponent, int>(AccessTools.DeclaredField(type, "festiveFavor"));
			MethodInfo method = Required(type, "ConvertFavorToPresents");
			MethodInfo methodInfo = Required(type, "SpawnPresent");
			MethodInfo methodInfo2 = Required(type, "SpawnCrackers");
			convert = Bridge<Action<object, Map>>(method);
			spawnPresent = Bridge<Action<object, Map, QualityCategory>>(methodInfo);
			spawnCrackers = Bridge<Action<object, Map, ThingDef, int>>(methodInfo2);
			Type type2 = AccessTools.TypeByName("Multiplayer.Client.AsyncTimeComp") ?? throw new TypeLoadException("Multiplayer.Client.AsyncTimeComp");
			mapOwner = AccessTools.MethodDelegate<Func<Map, object>>(AccessTools.DeclaredMethod("Multiplayer.Client.Extensions:AsyncTime"));
			mapPre = Bridge<Action<object>>(Required(type2, "PreContext"));
			mapPost = Bridge<Action<object>>(Required(type2, "PostContext"));
			tickingMap = AccessTools.StaticFieldRefAccess<Map>(AccessTools.DeclaredField(type2, "tickingMap"));
			Type type3 = AccessTools.TypeByName("Multiplayer.Client.Multiplayer");
			Func<object> worldComp = AccessTools.MethodDelegate<Func<object>>(AccessTools.PropertyGetter(type3, "WorldComp"));
			FieldInfo spectator = AccessTools.DeclaredField(AccessTools.TypeByName("Multiplayer.Client.MultiplayerWorldComp"), "spectatorFaction");
			spectatorFaction = () =>
			{
				object obj = worldComp();
				return (obj == null) ? null : ((Faction)spectator.GetValue(obj));
			};
			MpCompat.harmony.Patch(Required(type, "GameComponentTick"), null, transpiler: Hook("AnnualCall"), postfix: Hook("ContinuePending"));
			MpCompat.harmony.Patch(methodInfo, Hook("CapturePresent"), null, Hook("TrackPlacement"), Hook("AfterSpawn"));
			MpCompat.harmony.Patch(methodInfo2, Hook("CaptureCrackers"), null, Hook("TrackPlacement"), Hook("AfterSpawn"));
			Installed = true;
		}
	}

	private static MethodInfo Required(Type type, string name)
	{
		return AccessTools.DeclaredMethod(type, name) ?? throw new MissingMethodException(type.FullName, name);
	}

	private static HarmonyMethod Hook(string name)
	{
		return new HarmonyMethod(typeof(VanillaChristmasDistribution), name);
	}

	private static T Bridge<T>(MethodInfo method) where T : Delegate
	{
		MethodInfo method2 = typeof(T).GetMethod("Invoke");
		Type[] array = (from p in method2.GetParameters()
			select p.ParameterType).ToArray();
		DynamicMethod dynamicMethod = new DynamicMethod("Christmas_" + method.Name, method2.ReturnType, array, typeof(VanillaChristmasDistribution), skipVisibility: true);
		ILGenerator iLGenerator = dynamicMethod.GetILGenerator();
		iLGenerator.Emit(OpCodes.Ldarg_0);
		iLGenerator.Emit(OpCodes.Castclass, method.DeclaringType);
		for (int num = 1; num < array.Length; num++)
		{
			iLGenerator.Emit(OpCodes.Ldarg, num);
		}
		iLGenerator.Emit(OpCodes.Call, method);
		iLGenerator.Emit(OpCodes.Ret);
		return dynamicMethod.CreateDelegate<T>();
	}

	private static IEnumerable<CodeInstruction> AnnualCall(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
	{
		MethodInfo original = Required(__originalMethod.DeclaringType, "ConvertFavorToPresents");
		int changed = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(original))
			{
				changed++;
				yield return new CodeInstruction(instruction)
				{
					opcode = OpCodes.Call,
					operand = AccessTools.DeclaredMethod(typeof(VanillaChristmasDistribution), "AnnualPayout")
				};
			}
			else
			{
				yield return instruction;
			}
		}
		if (changed != 1)
		{
			throw new InvalidOperationException("Christmas annual conversion call changed: " + changed);
		}
	}

	private static bool LivePlayerMap(Map map)
	{
		if (map != null && !map.Disposed && Current.Game != null && Find.Maps.Contains(map))
		{
			Faction parentFaction = map.ParentFaction;
			if (parentFaction != null && parentFaction.IsPlayer && map.ParentFaction != spectatorFaction())
			{
				return mapOwner(map) != null;
			}
		}
		return false;
	}

	internal static List<Map> EligibleMaps()
	{
		List<Map> source = (from m in Find.Maps.Where(LivePlayerMap)
			orderby m.uniqueID
			select m).ToList();
		List<Map> list = source.Where((Map m) => m.wasSpawnedViaGravShipLanding || (m.Parent?.def.canBePlayerHome ?? false)).ToList();
		if (list.Count != 0)
		{
			return list;
		}
		if (!ModsConfig.OdysseyActive)
		{
			return list;
		}
		return source.Where(GravshipUtility.PlayerHasGravEngine).ToList();
	}

	private static int SuccessorIndex(List<Map> maps, int id)
	{
		int num = maps.FindIndex((Map m) => m.uniqueID >= id);
		if (num >= 0)
		{
			return num;
		}
		return 0;
	}

	private static void AnnualPayout(GameComponent manager, Map reference)
	{
		if (!MP.IsInMultiplayer)
		{
			convert(manager, reference);
		}
		else
		{
			if (MP.InInterface || Current.Game == null || active != null || reference == null || favor(manager) <= 0)
			{
				return;
			}
			VanillaChristmasDistributionComponent component = Current.Game.GetComponent<VanillaChristmasDistributionComponent>();
			DistributionState state = component.State;
			int num = GenDate.Year(Find.TickManager.TicksAbs, Find.WorldGrid.LongLatOf(reference.Tile).x);
			if (state.LastPayoutYear == num)
			{
				return;
			}
			List<Map> list = EligibleMaps();
			if (list.Count == 0)
			{
				return;
			}
			AnnualRun annualRun = new AnnualRun
			{
				Game = Current.Game,
				Manager = manager,
				Maps = list,
				Start = SuccessorIndex(list, state.NextMapId),
				Capturing = true
			};
			int num2 = favor(manager);
			active = annualRun;
			try
			{
				try
				{
					convert(manager, reference);
					if (Current.Game != annualRun.Game)
					{
						throw new InvalidOperationException("Game changed during Christmas reward planning");
					}
				}
				catch
				{
					favor(manager) = num2;
					throw;
				}
				state.Pending.AddRange(annualRun.Plan);
				state.LastPayoutYear = num;
				state.NextMapId = list[(annualRun.Start + 1) % list.Count].uniqueID;
			}
			finally
			{
				active = null;
			}
			Drain(manager, component);
		}
	}

	private static bool CapturePresent(GameComponent __instance, Map map, QualityCategory quality, out SpawnFrame __state)
	{
		__state = EnterSpawn(__instance, map, cracker: false);
		AnnualRun annualRun = active;
		if (annualRun == null || !annualRun.Capturing || annualRun.Manager != __instance || annualRun.Game != Current.Game)
		{
			return true;
		}
		int index = (annualRun.Start + annualRun.PresentIndex++) % annualRun.Maps.Count;
		annualRun.Plan.Add(new Gift
		{
			MapId = annualRun.Maps[index].uniqueID,
			Quality = quality,
			Count = 1
		});
		return false;
	}

	private static bool CaptureCrackers(GameComponent __instance, Map map, ThingDef crackerDef, int stackSize, out SpawnFrame __state)
	{
		__state = EnterSpawn(__instance, map, cracker: true);
		AnnualRun annualRun = active;
		if (annualRun == null || !annualRun.Capturing || annualRun.Manager != __instance || annualRun.Game != Current.Game)
		{
			return true;
		}
		int count = annualRun.Maps.Count;
		int num = stackSize / count;
		int num2 = stackSize % count;
		int num3 = (annualRun.Start + annualRun.RemainderIndex) % count;
		for (int i = 0; i < count; i++)
		{
			int num4 = num + ((i < num2) ? 1 : 0);
			if (num4 > 0)
			{
				annualRun.Plan.Add(new Gift
				{
					MapId = annualRun.Maps[(num3 + i) % count].uniqueID,
					CrackerDef = crackerDef,
					Count = num4,
					IsCracker = true
				});
			}
		}
		annualRun.RemainderIndex = (annualRun.RemainderIndex + num2) % count;
		return false;
	}

	private static SpawnFrame EnterSpawn(GameComponent manager, Map map, bool cracker)
	{
		AnnualRun annualRun = active;
		if (annualRun == null || annualRun.Capturing || annualRun.Game != Current.Game)
		{
			return null;
		}
		SpawnFrame spawnFrame = new SpawnFrame
		{
			Previous = spawning
		};
		if (spawning == null && annualRun.Manager == manager)
		{
			Gift delivery = annualRun.Delivery;
			if (delivery != null && delivery.MapId == map?.uniqueID && delivery.IsCracker == cracker)
			{
				spawnFrame.Gift = delivery;
			}
		}
		spawning = spawnFrame;
		return spawnFrame;
	}

	private static void AfterSpawn(SpawnFrame __state)
	{
		if (__state != null)
		{
			spawning = __state.Previous;
		}
	}

	private static void ContinuePending(GameComponent __instance)
	{
		if (MP.IsInMultiplayer && !MP.InInterface && Current.Game != null && active == null && Find.TickManager.TicksGame % 2500 == 0)
		{
			VanillaChristmasDistributionComponent component = Current.Game.GetComponent<VanillaChristmasDistributionComponent>();
			if (component.State.Pending.Count != 0)
			{
				Drain(__instance, component);
			}
		}
	}

	private static void Drain(GameComponent manager, VanillaChristmasDistributionComponent component)
	{
		int ticksGame = Find.TickManager.TicksGame;
		if (component.LastPumpTick == ticksGame)
		{
			return;
		}
		component.LastPumpTick = ticksGame;
		DistributionState state = component.State;
		AnnualRun annualRun = new AnnualRun
		{
			Game = Current.Game,
			Manager = manager
		};
		ExceptionDispatchInfo exceptionDispatchInfo = null;
		active = annualRun;
		try
		{
			Gift[] array = state.Pending.ToArray();
			foreach (Gift gift in array)
			{
				if (gift.Delivered || gift.Count <= 0)
				{
					continue;
				}
				List<Map> list = EligibleMaps();
				if (list.Count == 0)
				{
					break;
				}
				if (gift.IsCracker && gift.CrackerDef == null)
				{
					continue;
				}
				Map map = list.FirstOrDefault((Map m) => m.uniqueID == gift.MapId) ?? list[SuccessorIndex(list, gift.MapId)];
				gift.MapId = map.uniqueID;
				annualRun.Delivery = gift;
				try
				{
					using (new MapScope(map))
					{
						if (gift.IsCracker)
						{
							spawnCrackers(manager, map, gift.CrackerDef, gift.Count);
						}
						else
						{
							spawnPresent(manager, map, gift.Quality);
						}
					}
					gift.Completed = true;
				}
				catch (Exception source)
				{
					if (exceptionDispatchInfo == null)
					{
						exceptionDispatchInfo = ExceptionDispatchInfo.Capture(source);
					}
				}
				finally
				{
					Gift gift2 = gift;
					gift2.Completed |= gift.Attempt?.Spawned ?? false;
					gift.Attempt = null;
					annualRun.Delivery = null;
				}
				if (Current.Game != annualRun.Game)
				{
					break;
				}
			}
		}
		finally
		{
			state.Pending.RemoveAll((Gift g) => g.Delivered || g.Count <= 0);
			active = null;
		}
		exceptionDispatchInfo?.Throw();
	}

	private static IEnumerable<CodeInstruction> TrackPlacement(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo original = AccessTools.DeclaredMethod(typeof(GenSpawn), "Spawn", new Type[4]
		{
			typeof(Thing),
			typeof(IntVec3),
			typeof(Map),
			typeof(WipeMode)
		});
		int changed = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(original))
			{
				changed++;
				yield return new CodeInstruction(instruction)
				{
					opcode = OpCodes.Call,
					operand = AccessTools.DeclaredMethod(typeof(VanillaChristmasDistribution), "TrackedSpawn")
				};
			}
			else
			{
				yield return instruction;
			}
		}
		if (changed != 1)
		{
			throw new InvalidOperationException("Christmas placement call changed: " + changed);
		}
	}

	private static Thing TrackedSpawn(Thing thing, IntVec3 cell, Map map, WipeMode wipeMode)
	{
		AnnualRun annualRun = active;
		Gift gift = ((annualRun == null || annualRun.Capturing || annualRun.Game != Current.Game) ? null : spawning?.Gift);
		if (gift != null)
		{
			gift.Attempt = thing;
		}
		try
		{
			return GenSpawn.Spawn(thing, cell, map, wipeMode);
		}
		finally
		{
			if (gift != null && thing.Spawned)
			{
				gift.Completed = true;
			}
		}
	}
}
