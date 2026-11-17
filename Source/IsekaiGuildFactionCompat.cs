using System;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Multiplayer.API;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Poupun.GuildFactionAddon")]
public class IsekaiGuildFactionCompat
{
	private static Type dialogGuildQuestBoardType;

	private static Type guildQuestBoardWorldCompType;

	private static Type guildFactionMidsaveSpawnerType;

	private static PropertyInfo entriesProperty;

	private static FieldInfo entriesField;

	private static MethodInfo acceptEntryMethod;

	private static MethodInfo tryRefreshMethod;

	private static FieldInfo lastRefreshDayField;

	private static FieldInfo entryRankField;

	private static FieldInfo entryKindField;

	private static FieldInfo entryXpField;

	private static FieldInfo entrySilverField;

	private static FieldInfo entryBountyField;

	[ThreadStatic]
	private static int rosterHealingDepth;

	private static bool _suppressTryRefresh;

	private static bool _suppressAcceptEntry;

	public IsekaiGuildFactionCompat(ModContentPack mod)
	{
		IsekaiDeferredExecution.Setup(mod);
		Type type = AccessTools.TypeByName("GuildFactionAddon.GuildRosterWorldComponent");
		Type type2 = AccessTools.TypeByName("GuildFactionAddon.GuildRosterGenerator");
		MethodInfo methodInfo = AccessTools.DeclaredMethod(type, "HealExistingRoster");
		MethodInfo methodInfo2 = AccessTools.DeclaredMethod(type2, "EquipUniformGear");
		if (methodInfo != null && methodInfo2 != null)
		{
			MpCompat.harmony.Patch(methodInfo, new HarmonyMethod(typeof(IsekaiGuildFactionCompat), "HealRosterPrefix"), null, null, new HarmonyMethod(typeof(IsekaiGuildFactionCompat), "HealRosterFinalizer"));
			MpCompat.harmony.Patch(methodInfo2, new HarmonyMethod(typeof(IsekaiGuildFactionCompat), "EquipUniformPrefix"));
		}
		dialogGuildQuestBoardType = AccessTools.TypeByName("GuildFactionAddon.Dialog_GuildQuestBoard");
		guildQuestBoardWorldCompType = AccessTools.TypeByName("GuildFactionAddon.GuildQuestBoardWorldComponent");
		guildFactionMidsaveSpawnerType = AccessTools.TypeByName("GuildFactionAddon.GuildFactionMidsaveSpawner");
		if (dialogGuildQuestBoardType == null || guildQuestBoardWorldCompType == null)
		{
			Log.Warning("[IsekaiMP] GuildFactionAddon types could not be resolved — patches skipped.");
			return;
		}
		entriesProperty = AccessTools.Property(guildQuestBoardWorldCompType, "Entries");
		entriesField = AccessTools.Field(guildQuestBoardWorldCompType, "entries");
		acceptEntryMethod = AccessTools.DeclaredMethod(dialogGuildQuestBoardType, "AcceptEntry");
		tryRefreshMethod = AccessTools.DeclaredMethod(guildQuestBoardWorldCompType, "TryRefresh");
		lastRefreshDayField = AccessTools.Field(guildQuestBoardWorldCompType, "lastRefreshDay");
		Type type3 = AccessTools.TypeByName("GuildFactionAddon.GuildQuestBoardEntry");
		entryRankField = AccessTools.Field(type3, "rank");
		entryKindField = AccessTools.Field(type3, "pawnKindDefName");
		entryXpField = AccessTools.Field(type3, "xpReward");
		entrySilverField = AccessTools.Field(type3, "silverReward");
		entryBountyField = AccessTools.Field(type3, "isBounty");
		if (lastRefreshDayField == null || entryRankField == null || entryKindField == null || entryXpField == null || entrySilverField == null || entryBountyField == null)
		{
			throw new MissingFieldException("Guild quest-board snapshot fields could not be resolved.");
		}
		if (acceptEntryMethod != null)
		{
			MpCompat.harmony.Patch(acceptEntryMethod, new HarmonyMethod(typeof(IsekaiGuildFactionCompat), "AcceptEntryPrefix")
			{
				priority = 800,
				before = new string[1] { "Poupun.IsekaiCreaturesAddon" }
			});
			MP.RegisterSyncMethod(typeof(IsekaiGuildFactionCompat), "SyncedAcceptQuestEntry");
			MP.RegisterSyncMethod(typeof(IsekaiGuildFactionCompat), "SyncedAcceptQuestEntryOnMap").SetVersion(11);
		}
		if (tryRefreshMethod != null)
		{
			MpCompat.harmony.Patch(tryRefreshMethod, new HarmonyMethod(typeof(IsekaiGuildFactionCompat), "TryRefreshPrefix"));
			MethodInfo methodInfo3 = AccessTools.DeclaredMethod(typeof(World), "WorldTick");
			if (methodInfo3 != null)
			{
				MpCompat.harmony.Patch(methodInfo3, null, new HarmonyMethod(typeof(IsekaiGuildFactionCompat), "WorldTickPostfix"));
			}
		}
		if (guildFactionMidsaveSpawnerType != null && AccessTools.DeclaredMethod(guildFactionMidsaveSpawnerType, "SpawnFactionAndSettlement") != null)
		{
			MP.RegisterSyncMethod(guildFactionMidsaveSpawnerType, "SpawnFactionAndSettlement");
		}
	}

	private static void HealRosterPrefix(out int __state)
	{
		__state = rosterHealingDepth;
		if (MP.IsInMultiplayer)
		{
			rosterHealingDepth++;
		}
	}

	private static Exception HealRosterFinalizer(Exception __exception, int __state)
	{
		rosterHealingDepth = __state;
		return __exception;
	}

	private static bool EquipUniformPrefix(Pawn pawn)
	{
		if (MP.IsInMultiplayer && rosterHealingDepth != 0 && pawn?.apparel != null)
		{
			return pawn.apparel.WornApparel.Count == 0;
		}
		return true;
	}

	private static bool TryRefreshPrefix(ref bool __result)
	{
		if (!MP.IsInMultiplayer || _suppressTryRefresh)
		{
			return true;
		}
		__result = false;
		return false;
	}

	private static void WorldTickPostfix()
	{
		if (!MP.IsInMultiplayer || Find.TickManager == null || Find.TickManager.TicksGame % 250 != 0)
		{
			return;
		}
		WorldComponent worldComponent = Find.World?.GetComponent(guildQuestBoardWorldCompType);
		if (worldComponent == null || tryRefreshMethod == null)
		{
			return;
		}
		_suppressTryRefresh = true;
		try
		{
			tryRefreshMethod.Invoke(worldComponent, new object[1] { false });
		}
		finally
		{
			_suppressTryRefresh = false;
		}
	}

	private static bool AcceptEntryPrefix(object entry)
	{
		if (!MP.IsInMultiplayer || _suppressAcceptEntry || entry == null)
		{
			return true;
		}
		if (!MP.InInterface || MP.IsExecutingSyncCommand)
		{
			return true;
		}
		Map currentMap = Find.CurrentMap;
		if (!IsekaiQuestMaps.IsValidManualMap(currentMap))
		{
			return false;
		}
		WorldComponent worldComponent = Find.World?.GetComponent(guildQuestBoardWorldCompType);
		if (worldComponent == null)
		{
			return false;
		}
		IList list = ((entriesProperty != null) ? ((IList)entriesProperty.GetValue(worldComponent)) : ((IList)(entriesField?.GetValue(worldComponent))));
		if (list == null)
		{
			return false;
		}
		int num = list.IndexOf(entry);
		if (num >= 0)
		{
			SyncedAcceptQuestEntryOnMap(num, BoardSnapshot(worldComponent, list), currentMap);
			return false;
		}
		return false;
	}

	private static void SyncedAcceptQuestEntryOnMap(int index, string expectedBoard, Map map)
	{
		if (!IsekaiQuestMaps.IsValidManualMap(map))
		{
			return;
		}
		WorldComponent worldComponent = Find.World?.GetComponent(guildQuestBoardWorldCompType);
		if (worldComponent == null)
		{
			return;
		}
		IList list = ((entriesProperty != null) ? ((IList)entriesProperty.GetValue(worldComponent)) : ((IList)(entriesField?.GetValue(worldComponent))));
		if (list == null || index < 0 || index >= list.Count || !string.Equals(expectedBoard, BoardSnapshot(worldComponent, list), StringComparison.Ordinal))
		{
			return;
		}
		using (IsekaiQuestMaps.AcceptOnMap(map))
		{
			SyncedAcceptQuestEntry(index, expectedBoard);
		}
	}

	private static void SyncedAcceptQuestEntry(int index, string expectedBoard)
	{
		WorldComponent worldComponent = Find.World?.GetComponent(guildQuestBoardWorldCompType);
		if (worldComponent == null)
		{
			return;
		}
		IList list = ((entriesProperty != null) ? ((IList)entriesProperty.GetValue(worldComponent)) : ((IList)(entriesField?.GetValue(worldComponent))));
		if (list == null || index < 0 || index >= list.Count || !string.Equals(expectedBoard, BoardSnapshot(worldComponent, list), StringComparison.Ordinal))
		{
			return;
		}
		object obj = list[index];
		_suppressAcceptEntry = true;
		try
		{
			acceptEntryMethod?.Invoke(null, new object[1] { obj });
		}
		finally
		{
			_suppressAcceptEntry = false;
		}
	}

	private static string BoardSnapshot(object boardComp, IList entries)
	{
		StringBuilder stringBuilder = new StringBuilder();
		AppendNumber(stringBuilder, (int)lastRefreshDayField.GetValue(boardComp));
		AppendNumber(stringBuilder, entries.Count);
		foreach (object entry in entries)
		{
			if (entry == null)
			{
				stringBuilder.Append("null;");
				continue;
			}
			AppendNumber(stringBuilder, Convert.ToInt32(entryRankField.GetValue(entry), CultureInfo.InvariantCulture));
			string text = (string)entryKindField.GetValue(entry);
			AppendNumber(stringBuilder, text?.Length ?? (-1));
			stringBuilder.Append(text).Append(';');
			AppendNumber(stringBuilder, BitConverter.SingleToInt32Bits((float)entryXpField.GetValue(entry)));
			AppendNumber(stringBuilder, BitConverter.SingleToInt32Bits((float)entrySilverField.GetValue(entry)));
			stringBuilder.Append(((bool)entryBountyField.GetValue(entry)) ? '1' : '0').Append(';');
		}
		return stringBuilder.ToString();
	}

	private static void AppendNumber(StringBuilder builder, int value)
	{
		builder.Append(value.ToString(CultureInfo.InvariantCulture)).Append(';');
	}
}
