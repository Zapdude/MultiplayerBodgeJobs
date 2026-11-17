using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

internal static class IsekaiQuestMapOrigins
{
	private sealed class GenerationScope(Map map, GenerationScope previous)
	{
		internal readonly Map Map = map;

		internal readonly GenerationScope Previous = previous;
	}

	private sealed class PayoutScope(QuestPart_IsekaiMapOrigin origin, PayoutScope previous)
	{
		internal readonly Map Map = origin?.originMap;

		internal readonly bool Marked = origin != null;

		internal readonly Game Game = Current.Game;

		internal readonly PayoutScope Previous = previous;
	}

	private sealed class RewardScope(Map map, bool restricted, RewardScope previous)
	{
		internal readonly Map Map = map;

		internal readonly bool Restricted = restricted;

		internal readonly Game Game = Current.Game;

		internal readonly RewardScope Previous = previous;
	}

	private static bool installed;

	private static Type localHunt;

	private static Type worldHunt;

	private static Type heroVisit;

	private static Type recruitment;

	private static Type creatureQuest;

	private static Type worldBoss;

	private static FieldInfo creatureWorld;

	private static FieldInfo creatureMap;

	private static FieldInfo activeHunts;

	private static FieldInfo huntQuestId;

	[ThreadStatic]
	private static GenerationScope generation;

	[ThreadStatic]
	private static PayoutScope payout;

	[ThreadStatic]
	private static RewardScope reward;

	internal static void Install()
	{
		if (installed)
		{
			return;
		}
		localHunt = RequiredType("IsekaiLeveling.Quests.QuestPart_IsekaiLocalHunt");
		worldHunt = RequiredType("IsekaiLeveling.Quests.QuestPart_IsekaiWorldHunt");
		heroVisit = AccessTools.TypeByName("GuildFactionAddon.QuestPart_GuildHeroVisit");
		recruitment = AccessTools.TypeByName("GuildFactionAddon.QuestPart_GuildRecruitmentDuel");
		creatureQuest = AccessTools.TypeByName("IsekaiCreatures.Quests.QuestPart_IsekaiCreatureQuestSpawn");
		worldBoss = AccessTools.TypeByName("IsekaiLeveling.WorldBoss.QuestPart_WorldBoss");
		PatchGeneration("IsekaiLeveling.Quests.IncidentWorker_IsekaiHunt", "CreateHuntQuest", "BeginHomeGeneration");
		if (heroVisit != null)
		{
			PatchGeneration("GuildFactionAddon.IncidentWorker_GuildHeroVisit", "CreateVisitQuest", "BeginHomeGeneration");
		}
		if (recruitment != null)
		{
			PatchGeneration("GuildFactionAddon.IncidentWorker_GuildRecruitmentDuel", "CreateRecruitmentQuest", "BeginHomeGeneration");
		}
		if (worldBoss != null)
		{
			PatchGeneration("IsekaiLeveling.WorldBoss.IncidentWorker_WorldBoss", "CreateWorldBossQuest", "BeginHomeGeneration");
		}
		if (creatureQuest != null)
		{
			creatureWorld = RequiredField(creatureQuest, "isWorldQuest");
			creatureMap = RequiredField(creatureQuest, "targetMap");
			PatchGeneration("IsekaiCreatures.Quests.IncidentWorker_IsekaiCreatureQuest", "BuildAndPostQuest", "BeginCreatureGeneration");
			MpCompat.harmony.Patch(RequiredMethod(creatureQuest, "TryPostChainedQuest"), Patch("KeepChainOrigin"));
		}
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(QuestManager), "Add", new Type[1] { typeof(Quest) }) ?? throw new MissingMethodException(typeof(QuestManager).FullName, "Add"), Patch("AttachOrigin"));
		MpCompat.harmony.Patch(RequiredMethod(localHunt, "FindValidSpawnMap"), Patch("ResolveOriginSpawnMap"));
		Type[] array = new Type[2] { heroVisit, recruitment };
		foreach (Type type in array)
		{
			if (type != null)
			{
				MpCompat.harmony.Patch(RequiredMethod(type, "ResolveMap"), Patch("ResolveOriginSpawnMap"));
			}
		}
		Type type2 = RequiredType("IsekaiLeveling.Quests.IsekaiHuntTracker");
		activeHunts = RequiredField(type2, "activeHunts");
		huntQuestId = RequiredField(activeHunts.FieldType.GetGenericArguments()[1], "questId");
		int num = 0;
		foreach (MethodInfo declaredMethod in AccessTools.GetDeclaredMethods(type2))
		{
			if (declaredMethod.Name == "OnCreatureKilledByQuest")
			{
				MpCompat.harmony.Patch(declaredMethod, Patch("BeginQuestPayout"), null, null, Patch("EndPayout"));
				num++;
			}
		}
		if (num != 2)
		{
			throw new InvalidOperationException("Isekai quest payout overload contract changed");
		}
		MpCompat.harmony.Patch(RequiredMethod(type2, "OnCreatureKilled"), Patch("BeginTrackedPayout"), null, null, Patch("EndPayout"));
		string[] array2 = new string[2] { "AwardSilver", "AwardLoot" };
		foreach (string name in array2)
		{
			MpCompat.harmony.Patch(RequiredMethod(type2, name), Patch("BeginReward"), null, null, Patch("EndReward"));
		}
		MpCompat.harmony.Patch(RequiredMethod(RequiredType("IsekaiLeveling.Quests.GuildDropZoneUtility"), "TryFindDropZone"), Patch("FindOriginDropZone"));
		RequiredMethod(RequiredType("Multiplayer.Client.Comp.MultiplayerAsyncQuest"), "RegisterQuestMapResolver").Invoke(null, new object[2]
		{
			typeof(QuestPart_IsekaiMapOrigin),
			(Func<QuestPart, Map>)((QuestPart part) =>
			{
				QuestPart_IsekaiMapOrigin questPart_IsekaiMapOrigin = (QuestPart_IsekaiMapOrigin)part;
				return (!questPart_IsekaiMapOrigin.world) ? questPart_IsekaiMapOrigin.originMap : null;
			})
		});
		installed = true;
	}

	private static Type RequiredType(string name)
	{
		return AccessTools.TypeByName(name) ?? throw new TypeLoadException(name);
	}

	private static FieldInfo RequiredField(Type type, string name)
	{
		return AccessTools.Field(type, name) ?? throw new MissingFieldException(type.FullName, name);
	}

	private static MethodInfo RequiredMethod(Type type, string name)
	{
		return AccessTools.DeclaredMethod(type, name) ?? throw new MissingMethodException(type.FullName, name);
	}

	private static HarmonyMethod Patch(string method)
	{
		return new HarmonyMethod(typeof(IsekaiQuestMapOrigins), method);
	}

	private static void PatchGeneration(string type, string method, string prefix)
	{
		MpCompat.harmony.Patch(RequiredMethod(RequiredType(type), method), Patch(prefix), null, null, Patch("EndGeneration"));
	}

	private static void BeginHomeGeneration(Map homeMap, ref GenerationScope __state)
	{
		BeginGeneration(homeMap, ref __state);
	}

	private static void BeginCreatureGeneration(Map map, ref GenerationScope __state)
	{
		BeginGeneration(map, ref __state);
	}

	private static void BeginGeneration(Map map, ref GenerationScope state)
	{
		if (MP.IsInMultiplayer)
		{
			state = new GenerationScope(map, generation);
			generation = state;
		}
	}

	private static void EndGeneration(GenerationScope __state)
	{
		if (__state != null)
		{
			generation = __state.Previous;
		}
	}

	internal static QuestPart_IsekaiMapOrigin Origin(Quest quest)
	{
		if (quest == null)
		{
			return null;
		}
		foreach (QuestPart item in quest.PartsListForReading)
		{
			if (item is QuestPart_IsekaiMapOrigin result)
			{
				return result;
			}
		}
		return null;
	}

	private static void AttachOrigin(Quest __0)
	{
		if (!MP.IsInMultiplayer || generation?.Map == null || __0 == null || Origin(__0) != null)
		{
			return;
		}
		bool flag = false;
		bool flag2 = false;
		foreach (QuestPart item in __0.PartsListForReading)
		{
			Type type = item.GetType();
			if (type == worldHunt || type == worldBoss)
			{
				flag = true;
				flag2 = true;
			}
			else if (type == creatureQuest)
			{
				flag = true;
				flag2 |= (bool)creatureWorld.GetValue(item);
			}
			else if (type == localHunt || type == heroVisit || type == recruitment)
			{
				flag = true;
			}
		}
		if (flag)
		{
			__0.AddPart(new QuestPart_IsekaiMapOrigin
			{
				originMap = generation.Map,
				world = flag2
			});
		}
	}

	private static bool LoadedHome(Map map)
	{
		if (map != null && !map.Disposed && map.IsPlayerHome)
		{
			return Find.Maps.Contains(map);
		}
		return false;
	}

	private static bool ResolveOriginSpawnMap(QuestPart __instance, ref Map __result)
	{
		object obj;
		if (MP.IsInMultiplayer)
		{
			QuestPart_IsekaiMapOrigin questPart_IsekaiMapOrigin = Origin(__instance?.quest);
			if (questPart_IsekaiMapOrigin != null)
			{
				Map originMap = questPart_IsekaiMapOrigin.originMap;
				if (LoadedHome(originMap))
				{
					MapPawns mapPawns = originMap.mapPawns;
					if (mapPawns != null && mapPawns.FreeColonistsCount > 0)
					{
						obj = originMap;
						goto IL_0049;
					}
				}
				obj = null;
				goto IL_0049;
			}
		}
		return true;
		IL_0049:
		__result = (Map)obj;
		return false;
	}

	private static bool KeepChainOrigin(QuestPart __instance)
	{
		if (MP.IsInMultiplayer)
		{
			QuestPart_IsekaiMapOrigin questPart_IsekaiMapOrigin = Origin(__instance?.quest);
			if (questPart_IsekaiMapOrigin != null)
			{
				if (!LoadedHome(questPart_IsekaiMapOrigin.originMap))
				{
					return false;
				}
				creatureMap.SetValue(__instance, questPart_IsekaiMapOrigin.originMap);
				return true;
			}
		}
		return true;
	}

	private static void BeginReward(Map questSiteMap, ref RewardScope __state)
	{
		if (MP.IsInMultiplayer)
		{
			PayoutScope payoutScope = ((payout?.Game == Current.Game) ? payout : null);
			bool restricted = (payoutScope != null && payoutScope.Marked) || LoadedHome(questSiteMap);
			__state = new RewardScope((payoutScope != null && payoutScope.Marked) ? payoutScope.Map : questSiteMap, restricted, reward);
			reward = __state;
		}
	}

	private static void EndReward(RewardScope __state)
	{
		if (__state != null)
		{
			reward = __state.Previous;
		}
	}

	private static bool FindOriginDropZone(ref Map map, ref IntVec3 cell, ref bool __result)
	{
		if (!MP.IsInMultiplayer || reward == null || reward.Game != Current.Game || !reward.Restricted)
		{
			return true;
		}
		map = null;
		cell = IntVec3.Invalid;
		__result = false;
		Map map2 = reward.Map;
		if (!LoadedHome(map2))
		{
			return false;
		}
		ThingDef namedSilentFail = DefDatabase<ThingDef>.GetNamedSilentFail("Isekai_GuildDropZone");
		List<Thing> list = ((namedSilentFail == null) ? null : map2.listerThings?.ThingsOfDef(namedSilentFail));
		if (list == null || list.Count == 0)
		{
			return false;
		}
		map = map2;
		cell = list[0].Position;
		__result = true;
		return false;
	}

	private static void BeginQuestPayout(object __instance, Pawn creature, Quest quest, ref PayoutScope __state)
	{
		BeginPayout((creature == null) ? (TrackedQuest(__instance, null, first: true) ?? quest) : quest, ref __state);
	}

	private static void BeginTrackedPayout(object __instance, Pawn creature, ref PayoutScope __state)
	{
		BeginPayout(TrackedQuest(__instance, creature, first: false), ref __state);
	}

	private static void BeginPayout(Quest quest, ref PayoutScope state)
	{
		if (MP.IsInMultiplayer)
		{
			state = new PayoutScope(Origin(quest), payout);
			payout = state;
		}
	}

	private static void EndPayout(PayoutScope __state)
	{
		if (__state != null)
		{
			payout = __state.Previous;
		}
	}

	private static Quest TrackedQuest(object tracker, Pawn creature, bool first)
	{
		if (!MP.IsInMultiplayer || tracker == null || !(activeHunts.GetValue(tracker) is IDictionary dictionary))
		{
			return null;
		}
		object obj = null;
		if (creature != null)
		{
			obj = (dictionary.Contains(creature.thingIDNumber) ? dictionary[creature.thingIDNumber] : null);
		}
		else if (first)
		{
			{
				IDictionaryEnumerator dictionaryEnumerator = dictionary.GetEnumerator();
				try
				{
					if (dictionaryEnumerator.MoveNext())
					{
						obj = dictionaryEnumerator.Value;
					}
				}
				finally
				{
					IDisposable disposable = dictionaryEnumerator as IDisposable;
					if (disposable != null)
					{
						disposable.Dispose();
					}
				}
			}
		}
		if (obj == null)
		{
			return null;
		}
		int num = (int)huntQuestId.GetValue(obj);
		if (num < 0 || Find.QuestManager == null)
		{
			return null;
		}
		foreach (Quest item in Find.QuestManager.QuestsListForReading)
		{
			if (item.id == num)
			{
				return item;
			}
		}
		return null;
	}
}
