using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

internal static class RandGuard
{
	internal enum RngUse
	{
		Presentation,
		ReadOnlyQuery,
		StableInitialization,
		Simulation
	}

	private struct LocalState
	{
		internal long Token;

		internal bool Native;
	}

	private const string HarmonyId = "local.mpdesyncfix.randguard";

	private const int PawnInitializationPurpose = 1230000945;

	internal static readonly string[] IsekaiLevelingTargets = new string[62]
	{
		"IsekaiLeveling.Patches.Patch_SkillLearnXP|Postfix", "IsekaiLeveling.IsekaiComponent|GainXP", "IsekaiLeveling.IsekaiComponent|ProcessXPGain", "IsekaiLeveling.IsekaiComponent|FlushPendingXP", "IsekaiLeveling.IsekaiComponent|ProcessDeferredLevelUps", "IsekaiLeveling.IsekaiComponent|DevAddLevel", "IsekaiLeveling.UI.IsekaiAnimations|*", "IsekaiLeveling.PawnStatGenerator|InitializePawnStats", "IsekaiLeveling.PawnStatGenerator|InitializeWithStartingLevel", "IsekaiLeveling.PawnStatGenerator|RollRankForPawn",
		"IsekaiLeveling.PawnStatGenerator|RollStandardRank", "IsekaiLeveling.PawnStatGenerator|RollRankWithBoost", "IsekaiLeveling.PawnStatGenerator|GenerateStatsForRank", "IsekaiLeveling.PawnStatGenerator|DistributeRandom", "IsekaiLeveling.PawnStatGenerator|SyncToRankTrait", "IsekaiLeveling.PawnStatGenerator|UpdateRankTraitFromStats", "IsekaiLeveling.CompUseEffect_SummonHero|DoEffect", "IsekaiLeveling.CompUseEffect_IsekaiTrait|DoEffect", "IsekaiLeveling.CompUseEffect_StarFragment|DoEffect", "IsekaiLeveling.CompUseEffect_ManaCore|DoEffect",
		"IsekaiLeveling.CompUseEffect_Respec|DoEffect", "IsekaiLeveling.WorldBoss.IncidentWorker_WorldBoss|CanFireNowSub", "IsekaiLeveling.WorldBoss.IncidentWorker_WorldBoss|TryExecuteWorker", "IsekaiLeveling.Quests.IsekaiHuntTracker|GameComponentTick", "IsekaiLeveling.Quests.IsekaiHuntTracker|TrySpawnDailyHuntQuest", "IsekaiLeveling.Quests.IsekaiHuntTracker|CheckForStuckQuests", "IsekaiLeveling.Quests.IsekaiHuntTracker|RegisterHunt", "IsekaiLeveling.Quests.IsekaiHuntTracker|OnCreatureKilledByQuest", "IsekaiLeveling.Quests.IsekaiHuntSpawnChecker|MapComponentTick", "IsekaiLeveling.Quests.IncidentWorker_IsekaiHunt|CanFireNowSub",
		"IsekaiLeveling.Quests.IncidentWorker_IsekaiHunt|TryExecuteWorker", "IsekaiLeveling.Quests.IncidentWorker_IsekaiHunt|CreateHuntQuest", "IsekaiLeveling.Quests.IncidentWorker_IsekaiHunt|DetermineRankForColony", "IsekaiLeveling.Quests.IncidentWorker_IsekaiHunt|SelectCreatureForRank", "IsekaiLeveling.Quests.IncidentWorker_IsekaiHunt|SelectHostilePawnForRank", "IsekaiLeveling.Quests.IncidentWorker_IsekaiHunt|GetHostileFactionForBounty", "IsekaiLeveling.Quests.IncidentWorker_IsekaiHunt|GenerateLootRewards", "IsekaiLeveling.Quests.IncidentWorker_IsekaiHunt|ApplyForgeEnhancementsForRank", "IsekaiLeveling.Quests.IncidentWorker_IsekaiHunt|ForceBountyPawnRank", "IsekaiLeveling.Quests.IncidentWorker_IsekaiHunt|CreateHuntSite",
		"IsekaiLeveling.Quests.IncidentWorker_IsekaiHunt|TryFindHuntSiteTile", "IsekaiLeveling.Quests.QuestPart_IsekaiWorldHunt|CreateSiteOnWorldMap", "IsekaiLeveling.Quests.QuestPart_IsekaiWorldHunt|SpawnCreatureOnSite", "IsekaiLeveling.Quests.QuestPart_IsekaiWorldHunt|PostQuestAdded", "IsekaiLeveling.Quests.QuestPart_IsekaiWorldHunt|OnMapGenerated", "IsekaiLeveling.Quests.QuestPart_IsekaiWorldHunt|TrySpawnOnExistingMap", "IsekaiLeveling.Quests.QuestPart_IsekaiWorldHunt|GetPackSizeForRank", "IsekaiLeveling.Quests.QuestPart_IsekaiLocalHunt|SpawnCreatureOnMap", "IsekaiLeveling.Quests.QuestPart_IsekaiLocalHunt|FindValidSpawnMap", "IsekaiLeveling.Quests.QuestPart_IsekaiLocalHunt|TryResolvePendingSpawns",
		"IsekaiLeveling.Quests.Patch_Pawn_Kill_HuntTracker|Postfix", "IsekaiLeveling.Patches.Patch_AssignRankTraitAfterGeneration|Postfix", "IsekaiLeveling.Patches.Patch_MapFinalizeInit_SpawnHuntCreature|Postfix", "IsekaiLeveling.Patches.Patch_ForgeBuildingGizmos|Postfix", "IsekaiLeveling.Patches.Patch_ForgeBuildingFloatMenu|Postfix", "IsekaiLeveling.Forge.ForgeUtility|AttemptRefinement", "IsekaiLeveling.Forge.CompForgeEnhancement|TryAddRune", "IsekaiLeveling.Forge.CompForgeEnhancement|RemoveRuneAt", "IsekaiLeveling.MobRanking.MobRankUtility|ApplyRandomVariation", "IsekaiLeveling.MobRanking.MobRankUtility|GenerateEliteName",
		"IsekaiLeveling.MobRanking.MobRankRewards|OnMobKilled", "IsekaiLeveling.MobRanking.MobRankRewards|SpawnManaCoreDrops"
	};

	internal static readonly string[] GuildFactionAddonTargets = new string[3] { "GuildFactionAddon.GuildRosterWorldComponent|TryGenerateRosterOnce", "GuildFactionAddon.GuildRosterWorldComponent|HealExistingRoster", "GuildFactionAddon.GuildRosterGenerator|GenerateRoster" };

	internal static readonly string[] IsekaiCreaturesTargets = new string[17]
	{
		"IsekaiCreatures.Quests.GuildBoard_RollDailyEntries_Patch|*", "IsekaiCreatures.Quests.GuildBoard_AcceptEntry_Patch|*", "IsekaiCreatures.Quests.GuildQuestBoardPatchShared|RollRank", "IsekaiCreatures.Quests.GuildQuestBoardPatchShared|BuildBoardEntry", "IsekaiCreatures.Quests.IncidentWorker_IsekaiCreatureQuest|CanFireNowSub", "IsekaiCreatures.Quests.IncidentWorker_IsekaiCreatureQuest|TryExecuteWorker", "IsekaiCreatures.Quests.IncidentWorker_IsekaiCreatureQuest|RollColonyQuestRank", "IsekaiCreatures.Quests.IncidentWorker_IsekaiCreatureQuest|PickEligibleTemplate", "IsekaiCreatures.Quests.IncidentWorker_IsekaiCreatureQuest|PickTemplateNearRank", "IsekaiCreatures.Quests.IncidentWorker_IsekaiCreatureQuest|BuildAndPostQuest",
		"IsekaiCreatures.Quests.IncidentWorker_IsekaiCreatureQuest|ResolveMaxRank", "IsekaiCreatures.Quests.IncidentWorker_IsekaiCreatureQuest|FindWorldQuestTile", "IsekaiCreatures.Quests.IsekaiCreaturesSiteSpawnChecker|MapComponentTick", "IsekaiCreatures.Quests.IsekaiLeveling_HuntTryExecute_AddonInjectPatch|*", "IsekaiCreatures.Pawn_Kill_WeaponDrop_Patch|*", "IsekaiCreatures.Pawn_Kill_LegendarySpawnPatch|*", "IsekaiCreatures.IsekaiWeaponDrops|*"
	};

	internal static readonly string[] GravshipTargets = new string[2] { "VanillaGravshipExpanded.QuestNode_Root_MechanoidSignal_Expanded|TestRunInt", "VanillaGravshipExpanded.QuestNode_Root_MechanoidSignal_Expanded|RunInt" };

	internal static RngUse Policy(MethodBase method)
	{
		string text = method.DeclaringType?.FullName;
		if (text == "IsekaiLeveling.PawnStatGenerator" && method.Name == "InitializePawnStats")
		{
			return RngUse.StableInitialization;
		}
		switch (text)
		{
		case "IsekaiLeveling.UI.IsekaiAnimations":
		case "IsekaiLeveling.Patches.Patch_ForgeBuildingGizmos":
		case "IsekaiLeveling.Patches.Patch_ForgeBuildingFloatMenu":
			return RngUse.Presentation;
		default:
		{
			string name = method.Name;
			if ((name == "CanFireNowSub" || name == "TestRunInt") ? true : false)
			{
				return RngUse.ReadOnlyQuery;
			}
			return RngUse.Simulation;
		}
		}
	}

	internal static void Install(string[] targets)
	{
		try
		{
			SessionServices.Install();
			Harmony harmony = new Harmony("local.mpdesyncfix.randguard");
			int num = 0;
			int num2 = 0;
			int[] array = new int[4];
			int num3 = 0;
			List<string> list = new List<string>();
			foreach (string text in targets)
			{
				int num4 = text.IndexOf('|');
				if (num4 < 0)
				{
					continue;
				}
				string text2 = text.Substring(0, num4);
				string text3 = text.Substring(num4 + 1);
				Type type = AccessTools.TypeByName(text2);
				if (type == null)
				{
					num3++;
					list.Add(text2 + " (type not loaded)");
					continue;
				}
				List<MethodBase> list2 = new List<MethodBase>();
				if (text3 == "*")
				{
					foreach (MethodInfo declaredMethod in AccessTools.GetDeclaredMethods(type))
					{
						if (!declaredMethod.IsAbstract && !declaredMethod.ContainsGenericParameters)
						{
							string name = declaredMethod.Name;
							bool flag = ((name == ".ctor" || name == ".cctor") ? true : false);
							if (!flag && !declaredMethod.Name.StartsWith("get_") && !declaredMethod.Name.StartsWith("set_"))
							{
								list2.Add(declaredMethod);
							}
						}
					}
				}
				else
				{
					foreach (MethodInfo declaredMethod2 in AccessTools.GetDeclaredMethods(type))
					{
						if (declaredMethod2.Name == text3 && !declaredMethod2.IsAbstract)
						{
							list2.Add(declaredMethod2);
						}
					}
				}
				if (list2.Count == 0)
				{
					num3++;
					list.Add(text2 + "." + text3 + " (method not found)");
					continue;
				}
				foreach (MethodBase item in list2)
				{
					try
					{
						RngUse rngUse = Policy(item);
						array[(int)rngUse]++;
						if (rngUse == RngUse.Simulation)
						{
							num2++;
							continue;
						}
						bool flag2 = rngUse == RngUse.StableInitialization;
						harmony.Patch(item, new HarmonyMethod(typeof(RandGuard), flag2 ? "BeforePawnInitialization" : "BeforeLocal")
						{
							priority = 0
						}, null, null, new HarmonyMethod(typeof(RandGuard), flag2 ? "AfterPawnInitialization" : "AfterLocal")
						{
							priority = 800
						});
						num++;
					}
					catch (Exception ex)
					{
						num3++;
						list.Add(item.Name + " (patch failed: " + ex.GetType().Name + ")");
					}
				}
			}
			Log.Message($"[RandGuard] Scoped {num}; native simulation {num2}; skipped {num3}. Policies: presentation={array[0]}, query={array[1]}, initialization={array[2]}, simulation={array[3]}.");
			if (list.Count > 0)
			{
				Log.Message($"[RandGuard:{targets.GetHashCode():x8}] Skipped: {string.Join("; ", list)}");
			}
		}
		catch (Exception value)
		{
			Log.Error($"[RandGuard] Install exception: {value}");
		}
	}

	private static void BeforeLocal(out LocalState __state)
	{
		__state = default;
		if (MP.IsInMultiplayer && !SessionServices.BeginLocalOnly(out __state.Token))
		{
			Rand.PushState();
			__state.Native = true;
		}
	}

	private static Exception AfterLocal(Exception __exception, LocalState __state)
	{
		if (__state.Token != 0L)
		{
			SessionServices.EndLocalOnly(__state.Token);
		}
		else if (__state.Native)
		{
			Rand.PopState();
		}
		return __exception;
	}

	private static void BeforePawnInitialization(Pawn __0, out bool __state)
	{
		__state = false;
		if (MP.IsInMultiplayer && __0 != null && __0.thingIDNumber > 0 && Find.World != null)
		{
			Rand.PushState(Gen.HashCombineInt(Gen.HashCombineInt(Find.World.ConstantRandSeed, __0.thingIDNumber), 1230000945));
			__state = true;
		}
	}

	private static Exception AfterPawnInitialization(Exception __exception, bool __state)
	{
		if (__state)
		{
			Rand.PopState();
		}
		return __exception;
	}
}
