using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.QuestGen;
using UnityEngine;
using VFED;
using VFEEmpire;
using Verse;
using Verse.Sound;

namespace Multiplayer.Compat;

[MpCompatFor("OskarPotocki.VFE.Deserters")]
public class VanillaFactionsDeserters
{
	private static float nextRecacheTime = 0f;

	private static int currentApproachIndex = -1;

	public VanillaFactionsDeserters(ModContentPack mod)
	{
		MpCompat.RegisterLambdaMethod(typeof(Building_BombPackDeployed), "GetGizmos", new int[1]);
		MpCompat.RegisterLambdaMethod(typeof(Building_RemoteTrap), "GetGizmos", new int[1]);
		MpCompat.RegisterLambdaMethod(typeof(CompDeployable), "CompGetWornGizmosExtra", new int[1]);
		MP.RegisterSyncMethod(typeof(CompIntelExtract), "AddDesignation");
		MpCompat.RegisterLambdaDelegate(typeof(CompIntelExtract), "CompFloatMenuOptions", new int[1]);
		MpCompat.RegisterLambdaMethod(typeof(CompIntelScraper), "CompGetGizmosExtra", new int[2] { 0, 1 });
		MP.RegisterSyncMethod(typeof(CompInvisibilityEngulfer), "Activate");
		MP.RegisterSyncMethod(typeof(ShipJob_WaitSendable), "SendAway");
		MP.RegisterSyncDialogNodeTree(typeof(IncidentWorker_ImperialPatrol), "TryExecuteWorker");
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(Dialog_DeserterNetwork), "DoWindowContents"), new HarmonyMethod(typeof(VanillaFactionsDeserters), "PreDeserterNetworkDraw"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(DeserterTabWorker_Services), "DrawService"), null, null, new HarmonyMethod(typeof(VanillaFactionsDeserters), "ReplaceServicePurchaseButton"));
		MP.RegisterSyncMethod(typeof(VanillaFactionsDeserters), "SyncedPurchaseContraband");
		MP.RegisterSyncMethod(typeof(VanillaFactionsDeserters), "SyncedPurchaseContrabandRushedDelivery");
		MP.RegisterSyncMethod(typeof(VanillaFactionsDeserters), "SyncedPurchaseQuest").CancelIfAnyArgNull();
		MP.RegisterSyncMethod(typeof(VanillaFactionsDeserters), "SyncedPurchaseService");
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(DesertersUIUtility), "DoPurchaseButton"), new HarmonyMethod(typeof(VanillaFactionsDeserters), "PreDoPurchaseButton"));
		MpCompat.harmony.Patch(MpMethodUtil.GetLambda(typeof(WorldComponent_Deserters), "CommFloatMenuOption", MethodType.Normal, (Type[])null, 0), new HarmonyMethod(typeof(VanillaFactionsDeserters), "PreTryOpenDeserterDialog"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(GameComponentUtility), "FinalizeInit"), null, new HarmonyMethod(typeof(VanillaFactionsDeserters), "EnsureDeserterStuffPreGenerated"));
		MP.RegisterSyncMethod(typeof(WorldComponent_Deserters), "InitializePlots");
		MP.RegisterSyncMethod(typeof(VanillaFactionsDeserters), "SyncedAcceptPlot");
		LongEventHandler.ExecuteWhenFinished(() =>
		{
			MpCompat.harmony.Patch(MpMethodUtil.GetLambda(typeof(DeserterTabWorker_Plots), "DoMainPart", MethodType.Normal, (Type[])null, 0), new HarmonyMethod(typeof(VanillaFactionsDeserters), "PreAcceptFinalQuest"));
			MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(DeserterTabWorker_Plots), "DoMainPart"), new HarmonyMethod(typeof(VanillaFactionsDeserters), "ResetApproachIndex"), null, new HarmonyMethod(typeof(VanillaFactionsDeserters), "ReplaceAcceptPlotButton"));
		});
		MP.RegisterSyncMethod(typeof(WorldComponent_Deserters), "EnsureQuestListFilled");
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(WorldComponent_Deserters), "InitializePlots"), new HarmonyMethod(typeof(VanillaFactionsDeserters), "EnsurePlotsGeneratedWithoutRepeats"));
	}

	private static bool PreTryOpenDeserterDialog(Pawn ___negotiator)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		Find.WindowStack.Add(new Dialog_DeserterNetwork(___negotiator.MapHeld));
		return false;
	}

	private static void PreDeserterNetworkDraw(ref int ___TotalIntel, ref int ___TotalCriticalIntel, Map ___Map)
	{
		if (Time.realtimeSinceStartup >= nextRecacheTime)
		{
			RecacheDialogIntel(ref ___TotalIntel, ref ___TotalCriticalIntel, ___Map);
		}
	}

	private static void RecacheDialogIntel(Dialog_DeserterNetwork dialog)
	{
		RecacheDialogIntel(ref dialog.TotalIntel, ref dialog.TotalCriticalIntel, dialog.Map);
	}

	private static void RecacheDialogIntel(ref int totalIntel, ref int totalCriticalIntel, Map map)
	{
		(int, int) intelCountForMap = GetIntelCountForMap(map);
		totalIntel = intelCountForMap.Item1;
		totalCriticalIntel = intelCountForMap.Item2;
		nextRecacheTime = Time.realtimeSinceStartup + 1.5f;
	}

	private static (int intel, int criticalIntel) GetIntelCountForMap(Map map)
	{
		int num = 0;
		int num2 = 0;
		foreach (IntVec3 item in Building_OrbitalTradeBeacon.AllPowered(map).SelectMany((Building_OrbitalTradeBeacon x) => x.TradeableCells).Distinct())
		{
			foreach (Thing thing in item.GetThingList(map))
			{
				if (thing.def == VFED_DefOf.VFED_Intel)
				{
					num += thing.stackCount;
				}
				if (thing.def == VFED_DefOf.VFED_CriticalIntel)
				{
					num2 += thing.stackCount;
				}
			}
		}
		return (intel: num, criticalIntel: num2);
	}

	private static bool TrySpendIntel(int intelCost, bool useCritical, Map map)
	{
		if (useCritical)
		{
			return TrySpendIntel(0, intelCost, map);
		}
		return TrySpendIntel(intelCost, 0, map);
	}

	private static bool TrySpendIntel(int normalIntelCost, int criticalIntelCost, Map map)
	{
		var (num, num2) = GetIntelCountForMap(map);
		if (num < normalIntelCost)
		{
			if (MP.IsExecutingSyncCommandIssuedBySelf)
			{
				Messages.Message("VFED.NotEnough".Translate(VFED_DefOf.VFED_Intel.LabelCap, normalIntelCost, num), MessageTypeDefOf.RejectInput, historical: false);
			}
			return false;
		}
		if (num2 < criticalIntelCost)
		{
			if (MP.IsExecutingSyncCommandIssuedBySelf)
			{
				Messages.Message("VFED.NotEnough".Translate(VFED_DefOf.VFED_CriticalIntel.LabelCap, criticalIntelCost, num2), MessageTypeDefOf.RejectInput, historical: false);
			}
			return false;
		}
		TradeUtility.LaunchThingsOfType(VFED_DefOf.VFED_Intel, normalIntelCost, map, null);
		TradeUtility.LaunchThingsOfType(VFED_DefOf.VFED_CriticalIntel, criticalIntelCost, map, null);
		Dialog_DeserterNetwork dialog_DeserterNetwork = Find.WindowStack.WindowOfType<Dialog_DeserterNetwork>();
		if (dialog_DeserterNetwork != null && dialog_DeserterNetwork.Map == map)
		{
			RecacheDialogIntel(dialog_DeserterNetwork);
		}
		return true;
	}

	private static List<(ThingDef thing, int count)> ShoppingCartToList()
	{
		return ContrabandManager.ShoppingCart.Select((KeyValuePair<(ThingDef, ContrabandExtension), int> x) => (x.Key.Item1, x.Key.Item2.countMult * x.Value)).ToList();
	}

	private static bool PreDoPurchaseButton(Rect inRect, string text, int intelCost, int criticalIntelCost, Dialog_DeserterNetwork parent, bool forceDisabled, ref bool __result)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if ((intelCost > parent.TotalIntel || criticalIntelCost > parent.TotalCriticalIntel) | forceDisabled)
		{
			GUI.color = Color.grey;
		}
		if (Widgets.ButtonText(inRect, text) && !forceDisabled)
		{
			RecacheDialogIntel(ref parent.TotalIntel, ref parent.TotalCriticalIntel, parent.Map);
			if (intelCost <= parent.TotalIntel && criticalIntelCost <= parent.TotalCriticalIntel)
			{
				SoundDefOf.ExecuteTrade.PlayOneShotOnCamera();
				if (text == "VFED.Purchase".Translate())
				{
					SyncedPurchaseContraband(intelCost, criticalIntelCost, parent.Map, ShoppingCartToList(), ContrabandManager.TotalAmount);
				}
				else if (text == "VFED.RushDelivery".Translate())
				{
					SyncedPurchaseContrabandRushedDelivery(intelCost, criticalIntelCost, parent.Map, ShoppingCartToList());
				}
				else if (text == "VFED.Activate".Translate())
				{
					SyncedPurchaseQuest(intelCost, criticalIntelCost, parent.Map, (parent.curTab.Worker as DeserterTabWorker_Services)?.selected);
				}
				else
				{
					Log.Error("Deserter network dialog - unsupported purchase button. Button's text: " + text);
				}
			}
		}
		GUI.color = Color.white;
		__result = false;
		return false;
	}

	private static void EnsureDeserterStuffPreGenerated()
	{
		WorldComponent_Deserters.Instance.EnsureQuestListFilled();
		WorldComponent_Deserters.Instance.InitializePlots();
	}

	private static void SyncedPurchaseContraband(int intelCost, int criticalIntelCost, Map targetMap, List<(ThingDef def, int count)> shoppingCart, int totalAmount)
	{
		if (!TrySpendIntel(intelCost, criticalIntelCost, targetMap))
		{
			return;
		}
		Slate slate = new Slate();
		slate.Set("delayTicks", Utilities.ReceiveTimeRange(totalAmount).RandomInRange.DaysToTicks());
		slate.Set("availableTime", Utilities.SiteExistTime(totalAmount).DaysToTicks());
		List<ThingDef> list = new List<ThingDef>();
		foreach (var item2 in shoppingCart)
		{
			var (item, num) = item2;
			while (num-- > 0)
			{
				list.Add(item);
			}
		}
		slate.Set("itemStashThings", list);
		QuestUtility.GenerateQuestAndMakeAvailable(VFED_DefOf.VFED_DeadDrop, slate);
		if (MP.IsExecutingSyncCommandIssuedBySelf)
		{
			ContrabandManager.ClearCart();
		}
	}

	private static void SyncedPurchaseContrabandRushedDelivery(int intelCost, int criticalIntelCost, Map targetMap, List<(ThingDef def, int count)> shoppingCart)
	{
		if (!TrySpendIntel(intelCost, criticalIntelCost, targetMap))
		{
			return;
		}
		List<List<Thing>> list = new List<List<Thing>>();
		List<Thing> list2 = new List<Thing>();
		foreach (var item in shoppingCart)
		{
			var (thingDef, num) = item;
			while (num-- > 0)
			{
				list2.Add(ThingMaker.MakeThing(thingDef, thingDef.MadeFromStuff ? GenStuff.DefaultStuffFor(thingDef) : null).TryMakeMinified());
				if (list2.Count > 10)
				{
					list.Add(list2);
					list2 = new List<Thing>();
				}
			}
		}
		list.Add(list2);
		IntVec3 intVec = DropCellFinder.TradeDropSpot(targetMap);
		DropPodUtility.DropThingGroupsNear(intVec, targetMap, list, 110, instaDrop: false, leaveSlag: false, canRoofPunch: false, forbid: false, allowFogged: false, canTransfer: false, EmpireUtility.Deserters);
		Messages.Message("VFED.ContrabandArrived".Translate(), new TargetInfo(intVec, targetMap), MessageTypeDefOf.PositiveEvent);
		if (MP.IsExecutingSyncCommandIssuedBySelf)
		{
			ContrabandManager.ClearCart();
		}
	}

	private static bool EnsurePlotsGeneratedWithoutRepeats(WorldComponent_Deserters __instance)
	{
		return __instance.PlotMissions.NullOrEmpty();
	}

	private static void ResetApproachIndex()
	{
		currentApproachIndex = -1;
	}

	private static bool ReplacedAcceptPlotButton(Rect rect, string label, bool drawBackground, bool doMouseoverSound, bool active, TextAnchor? overrideTextAnchor, DeserterTabWorker_Plots instance)
	{
		currentApproachIndex++;
		bool flag = Widgets.ButtonText(rect, label, drawBackground, doMouseoverSound, active, overrideTextAnchor);
		if (!MP.IsInMultiplayer || !flag)
		{
			return flag;
		}
		SyncedAcceptPlot(instance.selectedPlot.quest, currentApproachIndex, ((DeserterTabWorker)instance).Parent.Map);
		return false;
	}

	private static bool PreAcceptFinalQuest(DeserterTabWorker_Plots __instance)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		SyncedAcceptPlot(__instance.selectedPlot.quest, -1, ((DeserterTabWorker)__instance).Parent.Map);
		return false;
	}

	private static void SyncedAcceptPlot(Quest quest, int approachIndex, Map map)
	{
		QuestPart_ApproachChoices questPart_ApproachChoices = quest.PartsListForReading.OfType<QuestPart_ApproachChoices>().FirstOrDefault();
		if (questPart_ApproachChoices != null)
		{
			if (approachIndex < 0 || approachIndex >= questPart_ApproachChoices.choices.Count)
			{
				Log.Error($"Received approach index out of bounds (index={approachIndex}, count={questPart_ApproachChoices.choices.Count}) for quest {quest}");
				return;
			}
			QuestPart_ApproachChoices.Choice choice = questPart_ApproachChoices.choices[approachIndex];
			if (!TrySpendIntel(choice.info.intelCost, choice.info.useCriticalIntel, map))
			{
				return;
			}
			questPart_ApproachChoices.Choose(choice);
		}
		quest.hidden = false;
		quest.hiddenInUI = false;
		quest.Accept(null);
		if (MP.IsExecutingSyncCommandIssuedBySelf)
		{
			Find.WindowStack.WindowOfType<Dialog_DeserterNetwork>()?.Close();
			MainButtonDefOf.Quests.Worker.Activate();
			((MainTabWindow_Quests)MainButtonDefOf.Quests.TabWindow).Select(quest);
		}
	}

	private static IEnumerable<CodeInstruction> ReplaceAcceptPlotButton(IEnumerable<CodeInstruction> instr, MethodBase baseMethod)
	{
		MethodInfo target = AccessTools.DeclaredMethod(typeof(Widgets), "ButtonText", new Type[6]
		{
			typeof(Rect),
			typeof(string),
			typeof(bool),
			typeof(bool),
			typeof(bool),
			typeof(TextAnchor?)
		});
		MethodInfo replacement = AccessTools.DeclaredMethod(typeof(VanillaFactionsDeserters), "ReplacedAcceptPlotButton");
		int replacedCount = 0;
		bool lookForButtonMethod = false;
		foreach (CodeInstruction ci in instr)
		{
			if (lookForButtonMethod)
			{
				if ((ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt) && ci.operand is MethodInfo methodInfo && methodInfo == target)
				{
					ci.opcode = OpCodes.Call;
					ci.operand = replacement;
					replacedCount++;
					yield return new CodeInstruction(OpCodes.Ldarg_0);
					lookForButtonMethod = false;
				}
			}
			else if (ci.opcode == OpCodes.Ldstr && ci.operand is string text && text == "VFED.Select")
			{
				lookForButtonMethod = true;
			}
			yield return ci;
		}
		if (replacedCount != 1)
		{
			string arg = ((baseMethod.DeclaringType?.Namespace).NullOrEmpty() ? baseMethod.Name : (baseMethod.DeclaringType.Name + ":" + baseMethod.Name));
			Log.Warning($"Patched incorrect number of Widgets.ButtonText calls (patched {replacedCount}, expected {1}) for method {arg}");
		}
	}

	private static void SyncedPurchaseQuest(int intelCost, int criticalIntelCost, Map targetMap, Quest selected)
	{
		if (selected.acceptanceTick == -1 && TrySpendIntel(intelCost, criticalIntelCost, targetMap))
		{
			selected.hidden = false;
			selected.hiddenInUI = false;
			WorldComponent_Deserters.Instance.ServiceQuests.Remove(selected);
			WorldComponent_Deserters.Instance.EnsureQuestListFilled();
			selected.Accept(null);
			if (MP.IsExecutingSyncCommandIssuedBySelf)
			{
				Find.WindowStack.WindowOfType<Dialog_DeserterNetwork>()?.Close();
				MainButtonDefOf.Quests.Worker.Activate();
				((MainTabWindow_Quests)MainButtonDefOf.Quests.TabWindow).Select(selected);
			}
		}
	}

	private static bool ServicePurchaseButtonReplacement(Rect inRect, bool doMouseoverSound, DeserterServiceDef serviceDef, DeserterTabWorker instance)
	{
		bool flag = Widgets.ButtonInvisible(inRect, doMouseoverSound);
		if (!MP.IsInMultiplayer || !flag)
		{
			return flag;
		}
		RecacheDialogIntel(instance.Parent);
		if (instance.Parent.HasIntel(Mathf.FloorToInt((float)serviceDef.intelCost * WorldComponent_Deserters.Instance.VisibilityLevel.intelCostModifier), serviceDef.useCriticalIntel))
		{
			SyncedPurchaseService(instance.Parent.Map, serviceDef, WorldComponent_Deserters.Instance.VisibilityLevel.intelCostModifier);
		}
		return false;
	}

	private static void SyncedPurchaseService(Map map, DeserterServiceDef def, float currentCostModifier)
	{
		if (TrySpendIntel(Mathf.FloorToInt((float)def.intelCost * currentCostModifier), def.useCriticalIntel, map))
		{
			def.worker.Call();
		}
	}

	private static IEnumerable<CodeInstruction> ReplaceServicePurchaseButton(IEnumerable<CodeInstruction> instr, MethodBase baseMethod)
	{
		MethodInfo target = AccessTools.DeclaredMethod(typeof(Widgets), "ButtonInvisible", new Type[2]
		{
			typeof(Rect),
			typeof(bool)
		});
		MethodInfo replacement = AccessTools.DeclaredMethod(typeof(VanillaFactionsDeserters), "ServicePurchaseButtonReplacement");
		int replacedCount = 0;
		foreach (CodeInstruction ci in instr)
		{
			if ((ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt) && ci.operand is MethodInfo methodInfo && methodInfo == target)
			{
				ci.opcode = OpCodes.Call;
				ci.operand = replacement;
				replacedCount++;
				yield return new CodeInstruction(OpCodes.Ldarg_2);
				yield return new CodeInstruction(OpCodes.Ldarg_0);
			}
			yield return ci;
		}
		if (replacedCount != 1)
		{
			string arg = ((baseMethod.DeclaringType?.Namespace).NullOrEmpty() ? baseMethod.Name : (baseMethod.DeclaringType.Name + ":" + baseMethod.Name));
			Log.Warning($"Patched incorrect number of Widgets.ButtonInvisible calls (patched {replacedCount}, expected {1}) for method {arg}");
		}
	}
}
