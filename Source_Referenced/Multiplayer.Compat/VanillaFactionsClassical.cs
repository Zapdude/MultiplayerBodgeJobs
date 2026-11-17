using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
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
		MpCompat.RegisterLambdaDelegate(typeof(WorldComponent_RoadBuilding), "AddRoadGizmos", new int[1] { 1 });
		MethodInfo lambda = MpMethodUtil.GetLambda(typeof(WorldComponent_RoadBuilding), "AddRoadGizmos", MethodType.Normal, (Type[])null, 1);
		MpCompat.harmony.Patch(lambda, null, new HarmonyMethod(typeof(VanillaFactionsClassical), "StopTargeter"));
		MpCompat.RegisterLambdaDelegate(typeof(ScorpionTurret), "GetGizmos", new int[1]);
		MpCompat.RegisterLambdaMethod(typeof(CompToggleHediff), "CompGetWornGizmosExtra", new int[1] { 1 });
		MpCompat.RegisterLambdaDelegate(typeof(VeniVidiVici), "AddGizmo", new int[1]);
		MpCompat.RegisterLambdaDelegate(typeof(WorldComponent_Senators), "MakeSenatorsGizmo", new int[1]);
		MP.RegisterSyncWorker<Designator_InstallScorpion>(SyncInstallScorpion, null, isImplicit: false, shouldConstruct: true);
		DialogUtilities.InitializeDialogCloseSync(true);
		ConstructorInfo original = AccessTools.Constructor(typeof(Dialog_SenatorInfo), new Type[3]
		{
			typeof(FactionExtension_SenatorInfo),
			typeof(List<SenatorInfo>),
			typeof(bool)
		});
		MpCompat.harmony.Patch(original, null, new HarmonyMethod(typeof(VanillaFactionsClassical), "PostDialogOpen"));
		PatchingUtilities.PatchPushPopRand((MethodBase)AccessTools.Method(typeof(Dialog_SenatorInfo), "DrawSenatorInfo"), new HarmonyMethod(typeof(VanillaFactionsClassical), "ReplaceSenatorButtons"));
		MP.RegisterSyncMethod(typeof(VanillaFactionsClassical), "SyncedQuestButton");
		MP.RegisterSyncMethod(typeof(VanillaFactionsClassical), "SyncedBribeButton");
	}

	private static void LatePatch()
	{
		MpCompat.RegisterLambdaMethod(typeof(Beacon), "LightCommand", new int[1]);
		MP.RegisterSyncMethod(typeof(WorldComponent_Senators), "CheckInit");
	}

	private static void StopTargeter()
	{
		Find.WorldTargeter.StopTargeting();
	}

	private static void SyncInstallScorpion(SyncWorker sync, ref Designator_InstallScorpion designator)
	{
		sync.Bind(ref ((Designator_Place)designator).placingRot);
		if (sync.isWriting)
		{
			sync.Write(designator.GiveJobTo);
			sync.Write(designator.Scorpion);
		}
		else
		{
			designator.GiveJobTo = sync.Read<Pawn>();
			designator.Scorpion = sync.Read<Scorpion>();
		}
	}

	private static bool QuestButtonReplacement(Rect rect, string label, bool drawBackground, bool doMouseoverSounds, bool active, TextAnchor? overrideTextAnchor, Dialog_SenatorInfo.AllSenatorInfo senatorInfo, Dialog_SenatorInfo dialog)
	{
		bool flag = Widgets.ButtonText(rect, label, drawBackground, doMouseoverSounds, active, overrideTextAnchor);
		if (!MP.IsInMultiplayer || !flag)
		{
			return flag;
		}
		int num = dialog.senatorInfo.IndexOf(senatorInfo);
		if (num >= 0)
		{
			SyncedQuestButton(num);
		}
		return false;
	}

	private static bool BribeButtonReplacement(Rect rect, string label, bool drawBackground, bool doMouseoverSounds, bool active, TextAnchor? overrideTextAnchor, Dialog_SenatorInfo.AllSenatorInfo senatorInfo, Dialog_SenatorInfo dialog)
	{
		bool flag = Widgets.ButtonText(rect, label, drawBackground, doMouseoverSounds, active, overrideTextAnchor);
		if (!MP.IsInMultiplayer || !flag)
		{
			return flag;
		}
		int num = dialog.senatorInfo.IndexOf(senatorInfo);
		if (num >= 0)
		{
			SyncedBribeButton(num);
		}
		return false;
	}

	private static void SyncedQuestButton(int senatorDataIndex)
	{
		if (senatorDataIndex < 0)
		{
			return;
		}
		Dialog_SenatorInfo dialog_SenatorInfo = Find.WindowStack.WindowOfType<Dialog_SenatorInfo>();
		List<Dialog_SenatorInfo.AllSenatorInfo> list = dialog_SenatorInfo?.senatorInfo;
		if (list == null || senatorDataIndex >= list.Count)
		{
			return;
		}
		Dialog_SenatorInfo.AllSenatorInfo allSenatorInfo = list[senatorDataIndex];
		if (allSenatorInfo.Quest != null)
		{
			QuestState state = allSenatorInfo.Quest.State;
			if (state == QuestState.Ongoing || state == QuestState.NotYetAccepted)
			{
				if (MP.IsExecutingSyncCommandIssuedBySelf)
				{
					Messages.Message("VFEC.UI.AlreadyQuest".Translate(), MessageTypeDefOf.RejectInput, historical: false);
				}
				return;
			}
		}
		SenatorInfo senatorInfo = WorldComponent_Senators.Instance.InfoFor(allSenatorInfo.Pawn, dialog_SenatorInfo.Faction);
		Quest quest = SenatorQuests.GenerateQuestFor(SenatorQuests.GetValidQuestsFrom(allSenatorInfo.Pawn, out var slate), slate, senatorInfo, dialog_SenatorInfo.Faction);
		if (quest != null)
		{
			allSenatorInfo.Quest = (senatorInfo.Quest = quest);
			Find.QuestManager.Add(quest);
			QuestUtility.SendLetterQuestAvailable(quest);
		}
		else if (MP.IsExecutingSyncCommandIssuedBySelf)
		{
			Messages.Message("VFEC.UI.NoQuestsAvailable".Translate(), MessageTypeDefOf.RejectInput, historical: false);
		}
	}

	private static void SyncedBribeButton(int senatorDataIndex)
	{
		if (senatorDataIndex < 0)
		{
			return;
		}
		Dialog_SenatorInfo dialog_SenatorInfo = Find.WindowStack.WindowOfType<Dialog_SenatorInfo>();
		List<Dialog_SenatorInfo.AllSenatorInfo> list = dialog_SenatorInfo?.senatorInfo;
		if (list == null || senatorDataIndex >= list.Count)
		{
			return;
		}
		Dialog_SenatorInfo.AllSenatorInfo allSenatorInfo = list[senatorDataIndex];
		if (!CaravanInventoryUtility.HasThings(dialog_SenatorInfo.Caravan, ThingDefOf.Silver, Mathf.CeilToInt(dialog_SenatorInfo.moneyNeeded)))
		{
			Messages.Message("VFEC.UI.NotEnoughMoney".Translate(), MessageTypeDefOf.RejectInput, historical: false);
			return;
		}
		if (Rand.Chance(0.15f))
		{
			Messages.Message("VFEC.UI.BribeReject".Translate(allSenatorInfo.Pawn.Name.ToStringFull), MessageTypeDefOf.RejectInput);
			allSenatorInfo.CanBribe = false;
			WorldComponent_Senators.Instance.InfoFor(allSenatorInfo.Pawn, dialog_SenatorInfo.Faction).CanBribe = false;
			return;
		}
		int remaining = Mathf.CeilToInt(dialog_SenatorInfo.moneyNeeded);
		CaravanInventoryUtility.TakeThings(dialog_SenatorInfo.Caravan, (Thing thing) =>
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
		allSenatorInfo.Favored = true;
		WorldComponent_Senators.Instance.NumBribes++;
		dialog_SenatorInfo.moneyNeeded = 1000f + 0.05f * (float)WorldComponent_Senators.Instance.NumBribes * 0.5f * Find.WorldObjects.Settlements.Where((Settlement s) => s.HasMap && s.Map.IsPlayerHome).Sum((Settlement s) => s.Map.wealthWatcher.WealthTotal);
		WorldComponent_Senators.Instance.GainFavorOf(allSenatorInfo.Pawn, dialog_SenatorInfo.Faction);
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
