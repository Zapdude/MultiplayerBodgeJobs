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

internal static class IsekaiAsyncQuests
{
	private sealed class Scope(object owner, bool world)
	{
		internal readonly object Owner = owner;

		internal readonly bool World = world;
	}

	private sealed class HuntGeneration
	{
		internal Scope Clock;
	}

	private static bool installed;

	private static Type localHunt;

	private static Type worldHunt;

	private static Type heroVisit;

	private static Type creatureQuest;

	private static FieldInfo localMap;

	private static FieldInfo heroMap;

	private static FieldInfo creatureMap;

	private static FieldInfo creatureWorld;

	private static FieldInfo templateWorld;

	private static FieldInfo multiplayerGame;

	private static FieldInfo worldTime;

	private static MethodInfo mapTime;

	private static MethodInfo mapPre;

	private static MethodInfo mapPost;

	private static MethodInfo worldPre;

	private static MethodInfo worldPost;

	private static MethodInfo mapPaused;

	private static MethodInfo localSafety;

	private static MethodInfo resolvePending;

	private static MethodInfo heroTick;

	private static MethodInfo creatureTick;

	private static MethodInfo creaturePendingCleanup;

	private static AccessTools.FieldRef<object, Map> localMapValue;

	private static AccessTools.FieldRef<object, Map> heroMapValue;

	private static AccessTools.FieldRef<object, Map> creatureMapValue;

	private static AccessTools.FieldRef<object, Map> pollingOwnerMap;

	private static AccessTools.FieldRef<object, bool> creatureWorldValue;

	private static Func<object, bool> isMapPaused;

	[ThreadStatic]
	private static Map pollingMap;

	[ThreadStatic]
	private static Stack<HuntGeneration> huntGenerations;

	private static bool Active
	{
		get
		{
			if (MP.IsInMultiplayer)
			{
				return PatchingUtilities.IsAsyncTime;
			}
			return false;
		}
	}

	internal static bool AsyncActive => Active;

	internal static void Install()
	{
		if (installed)
		{
			return;
		}
		PatchingUtilities.SetupAsyncTime();
		Type type = RequiredType("Multiplayer.Client.Comp.MultiplayerAsyncQuest");
		Type type2 = RequiredType("Multiplayer.Client.AsyncTimeComp");
		Type type3 = RequiredType("Multiplayer.Client.AsyncTime.AsyncWorldTimeComp");
		multiplayerGame = RequiredField(RequiredType("Multiplayer.Client.Multiplayer"), "game");
		worldTime = RequiredField(RequiredType("Multiplayer.Client.MultiplayerGame"), "asyncWorldTimeComp");
		mapTime = RequiredMethod(RequiredType("Multiplayer.Client.Extensions"), "AsyncTime");
		mapPre = RequiredMethod(type2, "PreContext");
		mapPost = RequiredMethod(type2, "PostContext");
		worldPre = RequiredMethod(type3, "PreContextForCallback");
		worldPost = RequiredMethod(type3, "PostContext");
		mapPaused = AccessTools.PropertyGetter(type2, "Paused") ?? throw new MissingMemberException(type2.FullName, "Paused");
		pollingOwnerMap = AccessTools.FieldRefAccess<Map>(type2, "map");
		DynamicMethod dynamicMethod = new DynamicMethod("Isekai_ReadMapPause", typeof(bool), new Type[1] { typeof(object) }, typeof(IsekaiAsyncQuests), skipVisibility: true);
		ILGenerator iLGenerator = dynamicMethod.GetILGenerator();
		iLGenerator.Emit(OpCodes.Ldarg_0);
		iLGenerator.Emit(OpCodes.Castclass, type2);
		iLGenerator.Emit(OpCodes.Call, mapPaused);
		iLGenerator.Emit(OpCodes.Ret);
		isMapPaused = (Func<object, bool>)dynamicMethod.CreateDelegate(typeof(Func<object, bool>));
		localHunt = RequiredType("IsekaiLeveling.Quests.QuestPart_IsekaiLocalHunt");
		worldHunt = RequiredType("IsekaiLeveling.Quests.QuestPart_IsekaiWorldHunt");
		heroVisit = AccessTools.TypeByName("GuildFactionAddon.QuestPart_GuildHeroVisit");
		creatureQuest = AccessTools.TypeByName("IsekaiCreatures.Quests.QuestPart_IsekaiCreatureQuestSpawn");
		localMap = RequiredField(localHunt, "targetMap");
		localMapValue = AccessTools.FieldRefAccess<Map>(localHunt, "targetMap");
		MethodInfo methodInfo = RequiredMethod(type, "RegisterQuestMapResolver");
		methodInfo.Invoke(null, new object[2]
		{
			localHunt,
			(Func<QuestPart, Map>)((QuestPart part) => (Map)localMap.GetValue(part))
		});
		methodInfo.Invoke(null, new object[2]
		{
			worldHunt,
			(Func<QuestPart, Map>)((QuestPart _) => (Map)null)
		});
		Type type4 = AccessTools.TypeByName("IsekaiLeveling.WorldBoss.QuestPart_WorldBoss");
		if (type4 != null)
		{
			methodInfo.Invoke(null, new object[2]
			{
				type4,
				(Func<QuestPart, Map>)((QuestPart _) => (Map)null)
			});
			MpCompat.harmony.Patch(RequiredMethod(RequiredType("IsekaiLeveling.WorldBoss.IncidentWorker_WorldBoss"), "CreateWorldBossQuest"), Method("WorldReadClock"), null, null, Method("RestoreClock"));
			MpCompat.harmony.Patch(RequiredMethod(type4, "SpawnBossOnSite"), Method("BeginMapOperation"), null, null, Method("RestoreClock"));
		}
		MethodInfo original = RequiredMethod(RequiredType("IsekaiLeveling.Quests.IncidentWorker_IsekaiHunt"), "CreateHuntQuest");
		MpCompat.harmony.Patch(original, Method("BeginHuntGeneration"), null, Method("UseResolvedHuntClock"), new HarmonyMethod(typeof(IsekaiAsyncQuests), "EndHuntGeneration")
		{
			priority = 801
		});
		localSafety = RequiredMethod(localHunt, "CheckSafetyNet");
		resolvePending = RequiredMethod(localHunt, "TryResolvePendingSpawns");
		MpCompat.harmony.Patch(localSafety, Method("LocalPollAllowed"));
		MpCompat.harmony.Patch(resolvePending, Method("PendingPollAllowed"));
		MpCompat.harmony.Patch(RequiredMethod(localHunt, "FindValidSpawnMap"), null, Method("FilterPendingSpawnMap"));
		if (heroVisit != null)
		{
			heroMap = RequiredField(heroVisit, "targetMap");
			heroMapValue = AccessTools.FieldRefAccess<Map>(heroVisit, "targetMap");
			methodInfo.Invoke(null, new object[2]
			{
				heroVisit,
				(Func<QuestPart, Map>)((QuestPart part) => (Map)heroMap.GetValue(part))
			});
			heroTick = RequiredMethod(heroVisit, "Tick");
			MpCompat.harmony.Patch(heroTick, Method("LocalPollAllowed"));
			PatchRead(AccessTools.PropertyGetter(heroVisit, "DescriptionPart"), "PartReadClock");
			MpCompat.harmony.Patch(RequiredMethod(RequiredType("GuildFactionAddon.IncidentWorker_GuildHeroVisit"), "CreateVisitQuest"), Method("BeginHeroGeneration"), null, null, Method("RestoreClock"));
			Type type5 = RequiredType("GuildFactionAddon.GuildQuestBoardWorldComponent");
			PatchRead(AccessTools.PropertyGetter(type5, "CurrentDay"), "WorldReadClock");
			PatchRead(AccessTools.PropertyGetter(type5, "TicksUntilNextRefresh"), "WorldReadClock");
			PatchRead(RequiredMethod(RequiredType("GuildFactionAddon.GuildHeroVisitGameComponent"), "ForceVisitDueNow"), "WorldReadClock");
		}
		Type type6 = AccessTools.TypeByName("GuildFactionAddon.QuestPart_GuildRecruitmentDuel");
		if (type6 != null)
		{
			FieldInfo recruitmentMap = RequiredField(type6, "targetMap");
			methodInfo.Invoke(null, new object[2]
			{
				type6,
				(Func<QuestPart, Map>)((QuestPart part) => (Map)recruitmentMap.GetValue(part))
			});
			MpCompat.harmony.Patch(RequiredMethod(RequiredType("GuildFactionAddon.IncidentWorker_GuildRecruitmentDuel"), "CreateRecruitmentQuest"), Method("BeginHeroGeneration"), null, null, Method("RestoreClock"));
		}
		if (creatureQuest != null)
		{
			creatureMap = RequiredField(creatureQuest, "targetMap");
			creatureWorld = RequiredField(creatureQuest, "isWorldQuest");
			creatureMapValue = AccessTools.FieldRefAccess<Map>(creatureQuest, "targetMap");
			creatureWorldValue = AccessTools.FieldRefAccess<bool>(creatureQuest, "isWorldQuest");
			methodInfo.Invoke(null, new object[2]
			{
				creatureQuest,
				(Func<QuestPart, Map>)((QuestPart part) => (!(bool)creatureWorld.GetValue(part)) ? ((Map)creatureMap.GetValue(part)) : null)
			});
			creatureTick = RequiredMethod(creatureQuest, "PolledTick");
			creaturePendingCleanup = AccessTools.PropertyGetter(creatureQuest, "HasPendingSiteCleanup");
			MpCompat.harmony.Patch(creatureTick, Method("CreaturePollAllowed"));
			MethodInfo methodInfo2 = RequiredMethod(RequiredType("IsekaiCreatures.Quests.IncidentWorker_IsekaiCreatureQuest"), "BuildAndPostQuest");
			templateWorld = RequiredField(methodInfo2.GetParameters()[0].ParameterType, "isWorldQuest");
			MpCompat.harmony.Patch(methodInfo2, Method("BeginCreatureGeneration"), null, null, Method("RestoreClock"));
		}
		MpCompat.harmony.Patch(RequiredMethod(type2, "QuestManagerTickAsyncTime"), Method("MapPollStarting"), Method("PollLocalQuests"));
		IsekaiAsyncQuestIndex.Install(type);
		Type[] array = new Type[3] { localHunt, heroVisit, creatureQuest };
		foreach (Type type7 in array)
		{
			if (type7 != null)
			{
				MethodInfo methodInfo3 = AccessTools.DeclaredMethod(type7, "ExposeData");
				if ((object)methodInfo3 != null)
				{
					MpCompat.harmony.Patch(methodInfo3, null, Method("PartLoaded"));
				}
			}
		}
		IsekaiQuestMaps.Install();
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

	private static HarmonyMethod Method(string name)
	{
		return new HarmonyMethod(typeof(IsekaiAsyncQuests), name);
	}

	private static void PatchRead(MethodInfo method, string prefix)
	{
		if (method == null)
		{
			throw new MissingMethodException(prefix);
		}
		MpCompat.harmony.Patch(method, Method(prefix), null, null, Method("RestoreClock"));
	}

	private static Scope Enter(Map map)
	{
		if (!Active)
		{
			return null;
		}
		object obj = ((map == null) ? worldTime.GetValue(multiplayerGame.GetValue(null)) : mapTime.Invoke(null, new object[1] { map }));
		if (obj == null)
		{
			return null;
		}
		((map == null) ? worldPre : mapPre).Invoke(obj, null);
		return new Scope(obj, map == null);
	}

	private static void RestoreClock(Scope __state)
	{
		if (__state != null)
		{
			(__state.World ? worldPost : mapPost).Invoke(__state.Owner, null);
		}
	}

	internal static int PartKind(QuestPart part)
	{
		Type type = part?.GetType();
		if (!(type == heroVisit) || !(type != null))
		{
			if (!(type == localHunt) || !(type != null))
			{
				if (!(type == creatureQuest) || !(type != null))
				{
					return 0;
				}
				return 4;
			}
			return 2;
		}
		return 1;
	}

	private static void PartLoaded(QuestPart __instance)
	{
		IsekaiAsyncQuestIndex.PartLoaded(__instance);
	}

	internal static Map OwnerMap(QuestPart part)
	{
		if (part == null)
		{
			return null;
		}
		if (part.GetType() == localHunt)
		{
			return localMapValue(part);
		}
		if (part.GetType() == heroVisit)
		{
			return heroMapValue(part);
		}
		if (part.GetType() == creatureQuest)
		{
			if (!creatureWorldValue(part))
			{
				return creatureMapValue(part);
			}
			return null;
		}
		return null;
	}

	private static void BeginHeroGeneration(Map homeMap, ref Scope __state)
	{
		__state = Enter(homeMap);
	}

	private static void BeginMapOperation(Map map, ref Scope __state)
	{
		if (map != null)
		{
			__state = Enter(map);
		}
	}

	private static void BeginCreatureGeneration(object template, Map map, ref Scope __state)
	{
		if (template != null && map != null)
		{
			__state = Enter(((bool)templateWorld.GetValue(template)) ? null : map);
		}
	}

	private static void WorldReadClock(ref Scope __state)
	{
		__state = Enter(null);
	}

	private static void PartReadClock(QuestPart __instance, ref Scope __state)
	{
		__state = Enter(OwnerMap(__instance));
	}

	private static void BeginHuntGeneration(ref HuntGeneration __state)
	{
		if (Active)
		{
			__state = new HuntGeneration();
			(huntGenerations ?? (huntGenerations = new Stack<HuntGeneration>())).Push(__state);
		}
	}

	private static Quest MakeHuntInResolvedClock(bool world, Map homeMap)
	{
		if (Active)
		{
			Stack<HuntGeneration> stack = huntGenerations;
			if (stack != null && stack.Count > 0)
			{
				huntGenerations.Peek().Clock = Enter(world ? null : (homeMap ?? Find.AnyPlayerHomeMap));
			}
		}
		return Quest.MakeRaw();
	}

	private static void EndHuntGeneration(HuntGeneration __state)
	{
		if (__state == null)
		{
			return;
		}
		try
		{
			RestoreClock(__state.Clock);
		}
		finally
		{
			huntGenerations.Pop();
		}
	}

	private static IEnumerable<CodeInstruction> UseResolvedHuntClock(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
	{
		IList<LocalVariableInfo> list = __originalMethod.GetMethodBody()?.LocalVariables;
		List<CodeInstruction> list2 = instructions.ToList();
		if (list == null || list.Count == 0 || list[0].LocalType != typeof(bool) || list2.Count < 6 || list2[0].opcode != OpCodes.Ldarg_1 || list2[2].opcode != OpCodes.Clt || list2[4].opcode != OpCodes.Ceq || list2[5].opcode != OpCodes.Stloc_0)
		{
			throw new InvalidOperationException("Isekai hunt world/local flag contract changed");
		}
		MethodInfo makeRaw = AccessTools.DeclaredMethod(typeof(Quest), "MakeRaw");
		MethodInfo replacement = AccessTools.DeclaredMethod(typeof(IsekaiAsyncQuests), "MakeHuntInResolvedClock");
		int replaced = 0;
		foreach (CodeInstruction instruction in list2)
		{
			if (instruction.Calls(makeRaw))
			{
				yield return new CodeInstruction(OpCodes.Ldloc_0).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
				yield return new CodeInstruction(OpCodes.Ldarg_S, 4);
				instruction.opcode = OpCodes.Call;
				instruction.operand = replacement;
				replaced++;
			}
			yield return instruction;
		}
		if (replaced != 1)
		{
			throw new InvalidOperationException("Isekai hunt MakeRaw contract changed: " + replaced);
		}
	}

	private static bool LocalPollAllowed(QuestPart __instance)
	{
		if (Active)
		{
			if (pollingMap != null)
			{
				return OwnerMap(__instance) == pollingMap;
			}
			return false;
		}
		return true;
	}

	private static bool PendingPollAllowed()
	{
		if (Active)
		{
			return pollingMap != null;
		}
		return true;
	}

	private static bool CreaturePollAllowed(QuestPart __instance)
	{
		if (!Active)
		{
			return true;
		}
		Map map = OwnerMap(__instance);
		if (map != null)
		{
			return pollingMap == map;
		}
		return pollingMap == null;
	}

	private static void FilterPendingSpawnMap(QuestPart __instance, ref Map __result)
	{
		if (Active && pollingMap != null && (OwnerMap(__instance) != pollingMap || __result != pollingMap))
		{
			__result = null;
		}
	}

	private static void MapPollStarting(object __instance, ref bool __state)
	{
		__state = Active && !isMapPaused(__instance);
	}

	private static void PollLocalQuests(object __instance, bool __state)
	{
		if (!__state || Find.QuestManager == null)
		{
			return;
		}
		Map map = pollingOwnerMap(__instance);
		Map map2 = pollingMap;
		pollingMap = map;
		IsekaiAsyncQuestIndex.Work work = null;
		try
		{
			int ticksGame = Find.TickManager.TicksGame;
			if (ticksGame % 250 == 0)
			{
				resolvePending.Invoke(null, null);
			}
			work = IsekaiAsyncQuestIndex.Begin(Find.QuestManager, map, ticksGame);
			Quest quest;
			while (work.Next(out quest))
			{
				foreach (QuestPart part in work.Parts)
				{
					if (OwnerMap(part) != map)
					{
						continue;
					}
					try
					{
						if (part.GetType() == heroVisit && quest.State == QuestState.Ongoing)
						{
							heroTick.Invoke(part, null);
						}
						else if (part.GetType() == localHunt && ticksGame % 250 == 0 && quest.State == QuestState.Ongoing)
						{
							localSafety.Invoke(part, null);
						}
						else if (part.GetType() == creatureQuest && ticksGame % 60 == 0 && (!quest.Historical || (bool)creaturePendingCleanup.Invoke(part, null)))
						{
							creatureTick.Invoke(part, null);
						}
					}
					catch (Exception value)
					{
						Log.Warning($"[IsekaiMP] Local quest polling failed: {value}");
					}
				}
			}
		}
		finally
		{
			try
			{
				work?.Release();
			}
			finally
			{
				pollingMap = map2;
			}
		}
	}
}
