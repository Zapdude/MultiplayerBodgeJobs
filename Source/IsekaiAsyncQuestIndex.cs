using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

internal static class IsekaiAsyncQuestIndex
{
	internal sealed class Record(Quest quest, long order, long born)
	{
		internal readonly Quest Quest = quest;

		internal readonly long Order = order;

		internal readonly long Born = born;

		internal readonly int Epoch = epoch;

		internal long Removed;

		internal int PartCount;

		internal bool Registered = true;

		internal readonly List<Part> Parts = new List<Part>();

		internal readonly List<Map> Maps = new List<Map>();
	}

	internal readonly struct Part(QuestPart value, int kind, Map map)
	{
		internal readonly QuestPart Value = value;

		internal readonly int Kind = kind;

		internal readonly Map Map = map;
	}

	private sealed class MapRecords
	{
		internal readonly List<Record> All = new List<Record>();

		internal readonly List<Record> Heroes = new List<Record>();
	}

	internal sealed class Work
	{
		private readonly List<Record> quests = new List<Record>();

		internal readonly List<QuestPart> Parts = new List<QuestPart>();

		internal Map Map;

		internal int Mask;

		internal int Epoch;

		internal long Start;

		internal long Limit;

		internal long Cursor;

		internal int position;

		internal bool Next(out Quest quest)
		{
			if (position == quests.Count)
			{
				quest = null;
				return false;
			}
			Record record = quests[position++];
			Cursor = record.Order;
			RefreshRecord(record, force: false);
			Parts.Clear();
			foreach (Part part in record.Parts)
			{
				if ((part.Kind & Mask) != 0)
				{
					Parts.Add(part.Value);
				}
			}
			quest = record.Quest;
			return true;
		}

		internal void Add(Record record)
		{
			if (record.Order <= Cursor || record.Order > Limit || record.Born > Start || (record.Removed != 0L && record.Removed <= Start) || Epoch != record.Epoch)
			{
				return;
			}
			bool flag = false;
			foreach (Part part in record.Parts)
			{
				if (part.Map == Map && (part.Kind & Mask) != 0)
				{
					flag = true;
					break;
				}
			}
			if (flag)
			{
				int i;
				for (i = position; i < quests.Count && quests[i].Order < record.Order; i++)
				{
				}
				if (i >= quests.Count || quests[i] != record)
				{
					quests.Insert(i, record);
				}
			}
		}

		internal void Release()
		{
			quests.Clear();
			Parts.Clear();
			Map = null;
			if (work[depth - 1] != this)
			{
				throw new InvalidOperationException("Isekai quest polling must release in reverse order");
			}
			depth--;
			if (depth == 0)
			{
				removedDuringPoll.Clear();
			}
		}
	}

	internal const int Hero = 1;

	internal const int Hunt = 2;

	internal const int Creature = 4;

	private static QuestManager manager;

	private static Game game;

	private static long order;

	private static long version;

	private static int epoch;

	private static readonly Dictionary<Quest, Record> records = new Dictionary<Quest, Record>(ReferenceEqualityComparer.Instance);

	private static readonly List<Record> relevant = new List<Record>();

	private static readonly Dictionary<Map, MapRecords> maps = new Dictionary<Map, MapRecords>(ReferenceEqualityComparer.Instance);

	private static readonly Dictionary<Quest, List<Record>> removedDuringPoll = new Dictionary<Quest, List<Record>>(ReferenceEqualityComparer.Instance);

	private static readonly HashSet<Map> removedMaps = new HashSet<Map>(ReferenceEqualityComparer.Instance);

	[ThreadStatic]
	private static List<Work> work;

	[ThreadStatic]
	private static int depth;

	internal static void Install(Type coreQuest)
	{
		Patch(typeof(QuestManager), "Add", "Added", typeof(Quest));
		Patch(typeof(QuestManager), "Remove", "Removed", typeof(Quest));
		Patch(typeof(Quest), "AddPart", "QuestEdited", typeof(QuestPart));
		Patch(typeof(Quest), "RemovePart", "QuestEdited", typeof(QuestPart));
		Patch(typeof(Quest), "End", "QuestEdited", typeof(QuestEndOutcome), typeof(bool), typeof(bool));
		Patch(typeof(Quest), "CleanupQuestParts", "QuestEdited");
		Patch(coreQuest, "CacheQuest", "Cached", typeof(Quest));
		Patch(coreQuest, "Reset", "Reset");
		Patch(typeof(Game), "FinalizeInit", "Reset");
		Patch(typeof(MapDeiniter), "Deinit", "MapRemoved", typeof(Map), typeof(bool));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(Quest), "QuestTick"), null, null, new HarmonyMethod(typeof(IsekaiAsyncQuestIndex), "ObservePartClear"));
	}

	private static void Patch(Type type, string name, string postfix, params Type[] args)
	{
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, name, args) ?? throw new MissingMethodException(type.FullName, name), null, new HarmonyMethod(typeof(IsekaiAsyncQuestIndex), postfix));
	}

	internal static void Reset()
	{
		if (depth != 0)
		{
			foreach (Record value in records.Values)
			{
				value.Registered = false;
				value.Removed = ++version;
				RememberRemoved(value);
			}
		}
		manager = null;
		game = null;
		records.Clear();
		relevant.Clear();
		maps.Clear();
		removedMaps.Clear();
		if (depth == 0)
		{
			removedDuringPoll.Clear();
		}
		order = (version = 0L);
		epoch++;
	}

	private static void Ensure(QuestManager current)
	{
		if (manager == current && game == Current.Game && records.Count == current.QuestsListForReading.Count)
		{
			return;
		}
		Reset();
		manager = current;
		game = Current.Game;
		foreach (Quest item in current.QuestsListForReading)
		{
			Register(item);
		}
	}

	private static void Register(Quest quest)
	{
		if (quest != null && !records.ContainsKey(quest))
		{
			Record record = new Record(quest, ++order, ++version);
			records.Add(quest, record);
			RefreshRecord(record, force: true);
		}
	}

	private static void Added(QuestManager __instance, Quest quest)
	{
		if (IsekaiAsyncQuests.AsyncActive && manager == __instance && game == Current.Game)
		{
			if (__instance.Contains(quest))
			{
				Register(quest);
			}
			RefreshRemoved(quest);
		}
	}

	private static void Removed(QuestManager __instance, Quest quest)
	{
		if (manager == __instance && quest != null && !__instance.Contains(quest) && records.Remove(quest, out var value))
		{
			Unlink(value);
			relevant.Remove(value);
			value.Registered = false;
			value.Removed = ++version;
			if (depth != 0)
			{
				RememberRemoved(value);
			}
		}
	}

	private static void RememberRemoved(Record record)
	{
		if (!removedDuringPoll.TryGetValue(record.Quest, out var value))
		{
			removedDuringPoll.Add(record.Quest, value = new List<Record>());
		}
		value.Add(record);
	}

	private static void Cached(Quest quest)
	{
		Touch(quest);
	}

	private static void QuestEdited(Quest __instance)
	{
		Touch(__instance, force: true);
	}

	internal static void PartLoaded(QuestPart part)
	{
		LoadSaveMode mode = Scribe.mode;
		if ((mode == LoadSaveMode.LoadingVars || mode == LoadSaveMode.PostLoadInit) ? true : false)
		{
			Touch(part?.quest, force: true);
		}
	}

	internal static void Touch(Quest quest, bool force = false)
	{
		if (quest != null)
		{
			if (manager != null && game == Current.Game && records.TryGetValue(quest, out var value))
			{
				RefreshRecord(value, force);
			}
			RefreshRemoved(quest);
		}
	}

	private static void RefreshRemoved(Quest quest)
	{
		if (quest == null || depth == 0 || !removedDuringPoll.TryGetValue(quest, out var value))
		{
			return;
		}
		foreach (Record item in value)
		{
			RefreshRecord(item, force: true);
		}
	}

	private static void MapRemoved(Map map)
	{
		if (map != null)
		{
			removedMaps.Add(map);
			maps.Remove(map);
		}
	}

	private static void Unlink(Record record)
	{
		foreach (Map map in record.Maps)
		{
			if (maps.TryGetValue(map, out var value))
			{
				value.All.Remove(record);
				value.Heroes.Remove(record);
				if (value.All.Count == 0)
				{
					maps.Remove(map);
				}
			}
		}
		record.Maps.Clear();
	}

	private static void Insert(List<Record> list, Record record)
	{
		int num = list.Count;
		while (num > 0 && list[num - 1].Order > record.Order)
		{
			num--;
		}
		list.Insert(num, record);
	}

	private static void RefreshRecord(Record record, bool force)
	{
		List<QuestPart> partsListForReading = record.Quest.PartsListForReading;
		bool flag = force || record.PartCount != partsListForReading.Count;
		if (!flag)
		{
			foreach (Part part in record.Parts)
			{
				if (part.Map != IsekaiAsyncQuests.OwnerMap(part.Value))
				{
					flag = true;
					break;
				}
			}
		}
		if (!flag)
		{
			return;
		}
		Unlink(record);
		record.Parts.Clear();
		record.PartCount = partsListForReading.Count;
		foreach (QuestPart item in partsListForReading)
		{
			int num = IsekaiAsyncQuests.PartKind(item);
			if (num == 0)
			{
				continue;
			}
			Map map = IsekaiAsyncQuests.OwnerMap(item);
			record.Parts.Add(new Part(item, num, map));
			if (record.Registered && record.Epoch == epoch && map != null && !removedMaps.Contains(map) && !record.Maps.Contains(map))
			{
				record.Maps.Add(map);
				if (!maps.TryGetValue(map, out var value))
				{
					maps.Add(map, value = new MapRecords());
				}
				Insert(value.All, record);
			}
		}
		if (record.Registered && record.Epoch == epoch)
		{
			if (record.Parts.Count == 0)
			{
				relevant.Remove(record);
			}
			else if (!relevant.Contains(record))
			{
				relevant.Add(record);
			}
			foreach (Map map2 in record.Maps)
			{
				foreach (Part part2 in record.Parts)
				{
					if (part2.Map == map2 && part2.Kind == 1)
					{
						Insert(maps[map2].Heroes, record);
						break;
					}
				}
			}
		}
		for (int i = 0; i < depth; i++)
		{
			work[i].Add(record);
		}
	}

	internal static Work Begin(QuestManager current, Map map, int tick)
	{
		Ensure(current);
		for (int num = relevant.Count - 1; num >= 0; num--)
		{
			RefreshRecord(relevant[num], force: false);
		}
		if (IsekaiAsyncQuestIndex.work == null)
		{
			IsekaiAsyncQuestIndex.work = new List<Work>();
		}
		if (depth == IsekaiAsyncQuestIndex.work.Count)
		{
			IsekaiAsyncQuestIndex.work.Add(new Work());
		}
		Work work = IsekaiAsyncQuestIndex.work[depth++];
		work.Map = map;
		work.Mask = 1 | ((tick % 250 == 0) ? 2 : 0) | ((tick % 60 == 0) ? 4 : 0);
		work.Start = version;
		work.Limit = order;
		work.Cursor = 0L;
		work.Epoch = epoch;
		work.position = 0;
		try
		{
			if (map != null && maps.TryGetValue(map, out var value))
			{
				foreach (Record item in (work.Mask == 1) ? value.Heroes : value.All)
				{
					work.Add(item);
				}
			}
			return work;
		}
		catch
		{
			work.Release();
			throw;
		}
	}

	private static void ClearParts(List<QuestPart> parts, Quest quest)
	{
		parts.Clear();
		Touch(quest, force: true);
	}

	private static IEnumerable<CodeInstruction> ObservePartClear(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo clear = AccessTools.DeclaredMethod(typeof(List<QuestPart>), "Clear");
		int count = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(clear))
			{
				yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
				instruction.opcode = OpCodes.Call;
				instruction.operand = AccessTools.DeclaredMethod(typeof(IsekaiAsyncQuestIndex), "ClearParts");
				count++;
			}
			yield return instruction;
		}
		if (count != 1)
		{
			throw new InvalidOperationException("Native historical quest-part clear contract changed: " + count);
		}
	}
}
