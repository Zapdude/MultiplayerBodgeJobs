using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.Sound;

namespace Multiplayer.Compat;

[MpCompatFor("OskarPotocki.VFE.Medieval2")]
public class VanillaFactionsMedieval2
{
	private static Type barterDealType;

	private static MethodInfo tryExecuteMethod;

	private static AccessTools.FieldRef<object, List<Tradeable>> barterDealTradeablesPlayerField;

	private static AccessTools.FieldRef<object, List<Tradeable>> barterDealTradeablesTraderField;

	private static Type dialogBarterType;

	private static bool inStockGeneration;

	private static AccessTools.FieldRef<CompForbiddable, bool> forbiddenIntField;

	public VanillaFactionsMedieval2(ModContentPack mod)
	{
		PatchingUtilities.PatchPushPopRand("VFEMedieval.JobDriver_PlayArchery:ThrowObjectAt");
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod("VFEMedieval.CompRallyAbility:ActivateAbility"));
		MpCompat.RegisterLambdaMethod("VFEMedieval.Building_Apiary", "GetGizmos", 0, 1).SetDebugOnly();
		LongEventHandler.ExecuteWhenFinished(LatePatchBarter);
	}

	private static void LatePatchBarter()
	{
		barterDealType = AccessTools.TypeByName("VFEMedieval.BarterDeal");
		tryExecuteMethod = AccessTools.DeclaredMethod(barterDealType, "TryExecute");
		barterDealTradeablesPlayerField = AccessTools.FieldRefAccess<List<Tradeable>>(barterDealType, "tradeablesPlayer");
		barterDealTradeablesTraderField = AccessTools.FieldRefAccess<List<Tradeable>>(barterDealType, "tradeablesTrader");
		dialogBarterType = AccessTools.TypeByName("VFEMedieval.Dialog_Barter");
		forbiddenIntField = AccessTools.FieldRefAccess<CompForbiddable, bool>("forbiddenInt");
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod("VFEMedieval.MerchantGuild:RegenerateStock"), new HarmonyMethod(typeof(VanillaFactionsMedieval2), "PreRegenerateStock"), null, null, new HarmonyMethod(typeof(VanillaFactionsMedieval2), "PostRegenerateStock"));
		MpCompat.harmony.Patch(AccessTools.DeclaredPropertySetter(typeof(CompForbiddable), "Forbidden"), new HarmonyMethod(typeof(VanillaFactionsMedieval2), "PreSetForbidden"));
		MpCompat.harmony.Patch(typeof(StorageSettings).GetMethod("CopyFrom", BindingFlags.Instance | BindingFlags.Public, null, new Type[1] { typeof(StorageSettings) }, null), new HarmonyMethod(typeof(VanillaFactionsMedieval2), "PreCopyFrom"));
		MpCompat.harmony.Patch(tryExecuteMethod, new HarmonyMethod(typeof(VanillaFactionsMedieval2), "PreTryExecute"));
		MP.RegisterSyncMethod(typeof(VanillaFactionsMedieval2), "SyncedBarterExecute");
	}

	private static void PreRegenerateStock()
	{
		if (MP.IsInMultiplayer)
		{
			inStockGeneration = true;
		}
	}

	private static void PostRegenerateStock(Exception __exception)
	{
		inStockGeneration = false;
	}

	private static bool PreSetForbidden(CompForbiddable __instance, bool value)
	{
		if (!inStockGeneration)
		{
			return true;
		}
		forbiddenIntField(__instance) = value;
		return false;
	}

	private static bool PreCopyFrom(StorageSettings __instance, StorageSettings other)
	{
		if (!inStockGeneration)
		{
			return true;
		}
		__instance.filter.CopyAllowancesFrom(other.filter);
		return false;
	}

	private static bool PreTryExecute(object __instance, ref bool actuallyTraded)
	{
		if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
		{
			return true;
		}
		List<Tradeable> list = barterDealTradeablesPlayerField(__instance);
		List<Tradeable> list2 = barterDealTradeablesTraderField(__instance);
		int[] array = new int[list.Count];
		for (int i = 0; i < list.Count; i++)
		{
			array[i] = list[i].CountToTransfer;
		}
		int[] array2 = new int[list2.Count];
		for (int j = 0; j < list2.Count; j++)
		{
			array2[j] = list2[j].CountToTransfer;
		}
		SyncedBarterExecute(TradeSession.playerNegotiator, (WorldObject)TradeSession.trader, array, array2);
		actuallyTraded = true;
		return false;
	}

	private static void SyncedBarterExecute(Pawn negotiator, WorldObject traderObj, int[] playerCounts, int[] traderCounts)
	{
		if (negotiator != null && traderObj != null)
		{
			TradeSession.SetupWith((ITrader)traderObj, negotiator, giftMode: false);
			object obj = Activator.CreateInstance(barterDealType);
			List<Tradeable> list = barterDealTradeablesPlayerField(obj);
			for (int i = 0; i < playerCounts.Length && i < list.Count; i++)
			{
				list[i].AdjustTo(playerCounts[i]);
			}
			List<Tradeable> list2 = barterDealTradeablesTraderField(obj);
			for (int j = 0; j < traderCounts.Length && j < list2.Count; j++)
			{
				list2[j].AdjustTo(traderCounts[j]);
			}
			object[] array = new object[1] { false };
			tryExecuteMethod.Invoke(obj, array);
			if ((bool)array[0])
			{
				SoundDefOf.ExecuteTrade.PlayOneShotOnCamera();
				negotiator.GetCaravan()?.RecacheInventory();
			}
			Find.WindowStack.TryRemove(dialogBarterType);
		}
	}
}
