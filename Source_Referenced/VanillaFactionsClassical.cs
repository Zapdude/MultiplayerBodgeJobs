using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using UnityEngine;
using VFEC;
using VFEC.Buildings;
using VFEC.Comps;
using VFEC.Perks.Workers;
using VFEC.Senators;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("OskarPotocki.VFE.Classical")]
internal class VanillaFactionsClassical
{
	public VanillaFactionsClassical(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
		VanillaSeriesRegistration17.Append(MpCompat.RegisterLambdaDelegate(typeof(WorldComponent_RoadBuilding), "AddRoadGizmos", 1));
		MethodInfo lambda = MpMethodUtil.GetLambda(typeof(WorldComponent_RoadBuilding), "AddRoadGizmos", MethodType.Normal, null, 1);
		MpCompat.harmony.Patch(lambda, null, new HarmonyMethod(typeof(VanillaFactionsClassical), "StopTargeter"));
		VanillaSeriesRegistration17.Append(MpCompat.RegisterLambdaDelegate(typeof(ScorpionTurret), "GetGizmos", default(int)));
		VanillaSeriesRegistration17.Append(MpCompat.RegisterLambdaMethod(typeof(CompToggleHediff), "CompGetWornGizmosExtra", 1));
		VanillaSeriesRegistration17.Append(MpCompat.RegisterLambdaDelegate(typeof(VeniVidiVici), "AddGizmo", default(int)));
		VanillaSeriesRegistration17.Append(MpCompat.RegisterLambdaDelegate(typeof(WorldComponent_Senators), "MakeSenatorsGizmo", default(int)));
		MP.RegisterSyncWorker<Designator_InstallScorpion>(SyncInstallScorpion, null, isImplicit: false, shouldConstruct: true);
		DialogUtilities.InitializeDialogCloseSync(addPauseLock: true);
		ConstructorInfo original = AccessTools.Constructor(typeof(Dialog_SenatorInfo), new Type[3]
		{
			typeof(FactionExtension_SenatorInfo),
			typeof(List<SenatorInfo>),
			typeof(bool)
		});
		MpCompat.harmony.Patch(original, null, new HarmonyMethod(typeof(VanillaFactionsClassical), "PostDialogOpen"));
		PatchingUtilities.PatchPushPopRand(AccessTools.Method(typeof(Dialog_SenatorInfo), "DrawSenatorInfo"), new HarmonyMethod(typeof(VanillaFactionsClassical), "ReplaceSenatorButtons"));
		MP.RegisterSyncMethod(typeof(VanillaFactionsClassical), "SyncedQuestButton").SetVersion(17);
		MP.RegisterSyncMethod(typeof(VanillaFactionsClassical), "SyncedBribeButton").SetVersion(17);
	}

	private static void LatePatch()
	{
		VanillaSeriesRegistration17.Append(MpCompat.RegisterLambdaMethod(typeof(Beacon), "LightCommand", default(int)));
		MP.RegisterSyncMethod(typeof(WorldComponent_Senators), "CheckInit").SetVersion(17);
	}

	private static void StopTargeter()
	{
		if (!MP.IsInMultiplayer || MP.InInterface || MP.IsExecutingSyncCommandIssuedBySelf)
		{
			Find.WorldTargeter.StopTargeting();
		}
	}

	private static void SyncInstallScorpion(SyncWorker sync, ref Designator_InstallScorpion designator)
	{
		sync.Bind(ref ((Designator_Place)(object)designator).placingRot);
		if (sync.isWriting)
		{
			sync.Write(designator.GiveJobTo);
			sync.Write<Scorpion>(designator.Scorpion);
		}
		else
		{
			designator.GiveJobTo = sync.Read<Pawn>();
			designator.Scorpion = sync.Read<Scorpion>();
		}
	}

	private static bool QuestButtonReplacement(Rect rect, string label, bool drawBackground, bool doMouseoverSounds, bool active, TextAnchor? overrideTextAnchor, AllSenatorInfo senatorInfo, Dialog_SenatorInfo dialog)
	{
		bool flag = Widgets.ButtonText(rect, label, drawBackground, doMouseoverSounds, active, overrideTextAnchor);
		if (!MP.IsInMultiplayer || !flag)
		{
			return flag;
		}
		SyncedQuestButton(((SenatorInfo)senatorInfo).Pawn, dialog.Faction, dialog.Caravan);
		return false;
	}

	private static bool BribeButtonReplacement(Rect rect, string label, bool drawBackground, bool doMouseoverSounds, bool active, TextAnchor? overrideTextAnchor, AllSenatorInfo senatorInfo, Dialog_SenatorInfo dialog)
	{
		bool flag = Widgets.ButtonText(rect, label, drawBackground, doMouseoverSounds, active, overrideTextAnchor);
		if (!MP.IsInMultiplayer || !flag)
		{
			return flag;
		}
		SyncedBribeButton(((SenatorInfo)senatorInfo).Pawn, dialog.Faction, dialog.Caravan);
		return false;
	}

	private static SenatorInfo ResolveSenator(Pawn pawn, Faction faction, Caravan caravan)
	{
		if (pawn == null || faction == null || caravan == null || caravan.Destroyed || caravan.Faction != Faction.OfPlayer)
		{
			return null;
		}
		WorldComponent_Senators instance = WorldComponent_Senators.Instance;
		if (instance == null || !instance.SenatorInfo.TryGetValue(faction, out var value))
		{
			return null;
		}
		return value.Find((SenatorInfo info) => info.Pawn == pawn && !info.Favored);
	}

	private static void SyncedQuestButton(Pawn pawn, Faction faction, Caravan caravan)
	{
		SenatorInfo val = ResolveSenator(pawn, faction, caravan);
		if (val == null)
		{
			return;
		}
		if (val.Quest != null && (val.Quest.State == QuestState.Ongoing || val.Quest.State == QuestState.NotYetAccepted))
		{
			if (MP.IsExecutingSyncCommandIssuedBySelf)
			{
				Messages.Message("VFEC.UI.AlreadyQuest".Translate(), MessageTypeDefOf.RejectInput, historical: false);
			}
			return;
		}
		Slate slate = default;
		Quest quest = SenatorQuests.GenerateQuestFor(SenatorQuests.GetValidQuestsFrom(pawn, ref slate), slate, val, faction);
		if (quest != null)
		{
			val.Quest = quest;
			Find.QuestManager.Add(quest);
			QuestUtility.SendLetterQuestAvailable(quest);
			RefreshSenatorWindows(pawn, faction, val);
		}
		else if (MP.IsExecutingSyncCommandIssuedBySelf)
		{
			Messages.Message("VFEC.UI.NoQuestsAvailable".Translate(), MessageTypeDefOf.RejectInput, historical: false);
		}
	}

	private static float BribeCost()
	{
		return 1000f + 0.05f * (float)WorldComponent_Senators.Instance.NumBribes * 0.5f * Find.WorldObjects.Settlements.Where((Settlement s) => s.HasMap && s.Map.IsPlayerHome).Sum((Settlement s) => s.Map.wealthWatcher.WealthTotal);
	}

	private static void SyncedBribeButton(Pawn pawn, Faction faction, Caravan caravan)
	{
		SenatorInfo val = ResolveSenator(pawn, faction, caravan);
		if (val == null || !val.CanBribe)
		{
			return;
		}
		int remaining = Mathf.CeilToInt(BribeCost());
		if (!CaravanInventoryUtility.HasThings(caravan, ThingDefOf.Silver, remaining))
		{
			if (MP.IsExecutingSyncCommandIssuedBySelf)
			{
				Messages.Message("VFEC.UI.NotEnoughMoney".Translate(), MessageTypeDefOf.RejectInput, historical: false);
			}
			return;
		}
		if (Rand.Chance(0.15f))
		{
			val.CanBribe = false;
			RefreshSenatorWindows(pawn, faction, val);
			if (MP.IsExecutingSyncCommandIssuedBySelf)
			{
				Messages.Message("VFEC.UI.BribeReject".Translate(pawn.Name.ToStringFull), MessageTypeDefOf.RejectInput);
			}
			return;
		}
		CaravanInventoryUtility.TakeThings(caravan, (Thing thing) =>
		{
			if (thing.def != ThingDefOf.Silver)
			{
				return 0;
			}
			int num = Mathf.Min(remaining, thing.stackCount);
			remaining -= num;
			return num;
		}).ForEach((Thing t) =>
		{
			t.Destroy();
		});
		val.Favored = true;
		WorldComponent_Senators instance = WorldComponent_Senators.Instance;
		instance.NumBribes++;
		WorldComponent_Senators.Instance.GainFavorOf(pawn, faction);
		RefreshSenatorWindows(pawn, faction, val);
	}

	private static void RefreshSenatorWindows(Pawn pawn, Faction faction, SenatorInfo saved)
	{
		foreach (Dialog_SenatorInfo item in Find.WindowStack.Windows.OfType<Dialog_SenatorInfo>())
		{
			if (item.Faction == faction)
			{
				AllSenatorInfo val = item.senatorInfo.Find((AllSenatorInfo info) => ((SenatorInfo)info).Pawn == pawn);
				if (val != null)
				{
					((SenatorInfo)val).Quest = saved.Quest;
					((SenatorInfo)val).Favored = saved.Favored;
					((SenatorInfo)val).CanBribe = saved.CanBribe;
					item.moneyNeeded = BribeCost();
				}
			}
		}
	}

	private static IEnumerable<CodeInstruction> ReplaceSenatorButtons(IEnumerable<CodeInstruction> instr)
	{
		MethodInfo targetMethod = AccessTools.Method(typeof(Widgets), "ButtonText", new Type[6]
		{
			typeof(Rect),
			typeof(string),
			typeof(bool),
			typeof(bool),
			typeof(bool),
			typeof(TextAnchor?)
		});
		MethodInfo replacementMethod = null;
		int patchedCount = 0;
		foreach (CodeInstruction ci in instr)
		{
			if (ci.opcode == OpCodes.Ldstr)
			{
				MethodInfo methodInfo;
				switch (ci.operand as string)
				{
				case "VFEC.UI.ReQuest":
					methodInfo = AccessTools.Method(typeof(VanillaFactionsClassical), "QuestButtonReplacement");
					goto IL_0157;
				case "VFEC.UI.Bribe":
					methodInfo = AccessTools.Method(typeof(VanillaFactionsClassical), "BribeButtonReplacement");
					goto IL_0157;
				default:
					methodInfo = replacementMethod;
					goto IL_0157;
				case null:
					break;
					IL_0157:
					replacementMethod = methodInfo;
					goto IL_022e;
				}
			}
			if (replacementMethod != null && ci.opcode == OpCodes.Call && ci.operand is MethodInfo methodInfo2 && methodInfo2 == targetMethod)
			{
				ci.operand = replacementMethod;
				replacementMethod = null;
				yield return new CodeInstruction(OpCodes.Ldarg_1);
				yield return new CodeInstruction(OpCodes.Ldarg_0);
				patchedCount++;
			}
			goto IL_022e;
			IL_022e:
			yield return ci;
		}
		switch (patchedCount)
		{
		case 0:
			Log.Warning("Failed to patch Vanilla Factions - Classical senator buttons");
			break;
		case 1:
			Log.Warning("Failed to fully patch Vanilla Factions - Classical senator buttons (only single patch was applied)");
			break;
		}
	}

	private static void PostDialogOpen(Window __instance, bool canInteract)
	{
		if (MP.IsInMultiplayer & canInteract)
		{
			DialogUtilities.PostDialogOpen_CloseSync_PauseLock(__instance);
		}
	}
}
