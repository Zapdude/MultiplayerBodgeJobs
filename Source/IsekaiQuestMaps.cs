using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

internal static class IsekaiQuestMaps
{
	private sealed class AutomaticSelection
	{
		internal Game Game;

		internal AutomaticSelection Previous;

		internal Map Map;

		internal int Faction;

		internal long Revision;

		internal bool Committed;
	}

	private sealed class ManualSelection(Game game, Map map, ManualSelection previous) : IDisposable
	{
		internal readonly Game Game = game;

		internal readonly Map Map = map;

		private bool disposed;

		public void Dispose()
		{
			if (!disposed)
			{
				disposed = true;
				manual = previous;
			}
		}
	}

	private static bool installed;

	private static Func<Map, bool> isOtherPlayersMap;

	[ThreadStatic]
	private static AutomaticSelection automatic;

	[ThreadStatic]
	private static ManualSelection manual;

	internal static void Install()
	{
		if (!installed)
		{
			isOtherPlayersMap = AccessTools.MethodDelegate<Func<Map, bool>>(AccessTools.DeclaredMethod(AccessTools.TypeByName("Multiplayer.Client.Factions.QuestMapFilter") ?? throw new TypeLoadException("Multiplayer.Client.Factions.QuestMapFilter"), "IsOtherPlayersMap"));
			PatchAutomatic("IsekaiLeveling.Quests.IsekaiHuntTracker", "TrySpawnDailyHuntQuest");
			Type type = AccessTools.TypeByName("GuildFactionAddon.GuildIncidentScheduler");
			if ((object)type != null)
			{
				PatchAutomatic(type.FullName, "TryFire");
			}
			PatchManual("GuildFactionAddon.Dialog_GuildQuestBoard", "AcceptEntry");
			PatchManual("IsekaiCreatures.Quests.GuildBoard_AcceptEntry_Patch", "Prefix");
			IsekaiQuestMapOrigins.Install();
			installed = true;
		}
	}

	private static HarmonyMethod Hook(string name)
	{
		return new HarmonyMethod(typeof(IsekaiQuestMaps), name);
	}

	private static void PatchAutomatic(string typeName, string name)
	{
		MethodInfo original = AccessTools.DeclaredMethod(AccessTools.TypeByName(typeName) ?? throw new TypeLoadException(typeName), name) ?? throw new MissingMethodException(typeName, name);
		MpCompat.harmony.Patch(original, new HarmonyMethod(typeof(IsekaiQuestMaps), "BeginAutomatic")
		{
			priority = 800
		}, null, Hook("AutomaticTranspiler"), new HarmonyMethod(typeof(IsekaiQuestMaps), "EndAutomatic")
		{
			priority = 0
		});
	}

	private static void PatchManual(string typeName, string name)
	{
		Type type = AccessTools.TypeByName(typeName);
		if ((object)type != null)
		{
			MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, name) ?? throw new MissingMethodException(typeName, name), null, null, Hook("ManualTranspiler"));
		}
	}

	internal static IDisposable AcceptOnMap(Map map)
	{
		return manual = new ManualSelection(Current.Game, map, manual);
	}

	internal static bool IsLiveMap(Map map)
	{
		if (map != null && !map.Disposed && Current.Game != null)
		{
			return Find.Maps.Contains(map);
		}
		return false;
	}

	internal static bool IsValidManualMap(Map map)
	{
		if (IsLiveMap(map) && map.IsPlayerHome)
		{
			Func<Map, bool> func = isOtherPlayersMap;
			if (func == null || !func(map))
			{
				MapPawns mapPawns = map.mapPawns;
				if (mapPawns == null)
				{
					return false;
				}
				return mapPawns.FreeColonistsSpawnedCount > 0;
			}
		}
		return false;
	}

	private static void BeginAutomatic(out AutomaticSelection __state)
	{
		__state = null;
		if (MP.IsInMultiplayer && Current.Game != null)
		{
			__state = new AutomaticSelection
			{
				Game = Current.Game,
				Previous = automatic
			};
			automatic = __state;
		}
	}

	private static Exception EndAutomatic(Exception __exception, AutomaticSelection __state)
	{
		if (__state != null)
		{
			automatic = __state.Previous;
		}
		return __exception;
	}

	private static Map OriginalFirst(IEnumerable<Map> maps, Predicate<Map> predicate)
	{
		if (maps != null)
		{
			foreach (Map map in maps)
			{
				if (predicate(map))
				{
					return map;
				}
			}
		}
		return null;
	}

	private static Map SelectAutomaticMap(IEnumerable<Map> maps, Predicate<Map> predicate)
	{
		AutomaticSelection automaticSelection = automatic;
		if (!MP.IsInMultiplayer || automaticSelection?.Game != Current.Game || Current.Game == null)
		{
			return OriginalFirst(maps, predicate);
		}
		List<Map> list = maps?.Where((Map map) =>
		{
			if (IsLiveMap(map))
			{
				Func<Map, bool> func = isOtherPlayersMap;
				if (func == null || !func(map))
				{
					return predicate(map);
				}
			}
			return false;
		}).OrderBy((Map map) => map.uniqueID).ToList();
		if (list == null || list.Count == 0)
		{
			return null;
		}
		if (automaticSelection.Map != null)
		{
			if (!list.Contains(automaticSelection.Map))
			{
				return null;
			}
			return automaticSelection.Map;
		}
		IsekaiQuestMapState component = Current.Game.GetComponent<IsekaiQuestMapState>();
		automaticSelection.Faction = Faction.OfPlayer?.loadID ?? (-1);
		automaticSelection.Revision = component.Revision(automaticSelection.Faction);
		int previousMap = component.LastMap(automaticSelection.Faction);
		for (AutomaticSelection previous = automaticSelection.Previous; previous != null; previous = previous.Previous)
		{
			if (previous.Game == automaticSelection.Game && previous.Map != null && previous.Faction == automaticSelection.Faction)
			{
				previousMap = previous.Map.uniqueID;
				break;
			}
		}
		automaticSelection.Map = list.FirstOrDefault((Map map) => map.uniqueID > previousMap) ?? list[0];
		return automaticSelection.Map;
	}

	private static bool ExecuteAutomatic(IncidentWorker worker, IncidentParms parms)
	{
		AutomaticSelection automaticSelection = automatic;
		bool flag = worker.TryExecute(parms);
		if (flag && MP.IsInMultiplayer && automaticSelection != null && automaticSelection.Game == Current.Game && !automaticSelection.Committed && automaticSelection.Map != null && parms.target == automaticSelection.Map && IsLiveMap(automaticSelection.Map))
		{
			Current.Game.GetComponent<IsekaiQuestMapState>().Commit(automaticSelection.Faction, automaticSelection.Map.uniqueID, automaticSelection.Revision);
			automaticSelection.Committed = true;
		}
		return flag;
	}

	private static Map SelectManualMap(IEnumerable<Map> maps, Predicate<Map> predicate)
	{
		if (!MP.IsInMultiplayer || manual == null)
		{
			return OriginalFirst(maps, predicate);
		}
		Map map = ((manual.Game == Current.Game) ? manual.Map : null);
		if (map == null || !IsValidManualMap(map) || !predicate(map) || maps == null || !maps.Contains(map))
		{
			return null;
		}
		return map;
	}

	private static bool IsMapFirst(MethodInfo method)
	{
		if (method.DeclaringType == typeof(GenCollection) && method.Name == "FirstOrDefault" && method.IsGenericMethod && method.GetGenericArguments().SequenceEqual(new Type[1] { typeof(Map) }, null) && method.GetParameters().Length == 2)
		{
			return method.GetParameters()[1].ParameterType == typeof(Predicate<Map>);
		}
		return false;
	}

	private static IEnumerable<CodeInstruction> AutomaticTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
	{
		return ReplaceSelectors(instructions, __originalMethod, automaticChoice: true);
	}

	private static IEnumerable<CodeInstruction> ManualTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
	{
		return ReplaceSelectors(instructions, __originalMethod, automaticChoice: false);
	}

	private static IEnumerable<CodeInstruction> ReplaceSelectors(IEnumerable<CodeInstruction> instructions, MethodBase method, bool automaticChoice)
	{
		int selectors = 0;
		int executions = 0;
		MethodInfo execute = AccessTools.DeclaredMethod(typeof(IncidentWorker), "TryExecute", new Type[1] { typeof(IncidentParms) });
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.operand is MethodInfo method2 && IsMapFirst(method2))
			{
				selectors++;
				instruction.opcode = OpCodes.Call;
				instruction.operand = AccessTools.DeclaredMethod(typeof(IsekaiQuestMaps), automaticChoice ? "SelectAutomaticMap" : "SelectManualMap");
			}
			else if (automaticChoice && instruction.Calls(execute))
			{
				executions++;
				instruction.opcode = OpCodes.Call;
				instruction.operand = AccessTools.DeclaredMethod(typeof(IsekaiQuestMaps), "ExecuteAutomatic");
			}
			yield return instruction;
		}
		if (selectors != 1 || (automaticChoice && executions != 1))
		{
			throw new InvalidOperationException($"Isekai quest-map selection contract changed in {method}: selectors={selectors}, executions={executions}");
		}
	}
}
