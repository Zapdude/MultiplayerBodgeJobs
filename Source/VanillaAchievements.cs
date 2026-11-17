using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("vanillaexpanded.achievements")]
internal class VanillaAchievements
{
	private static MethodInfo purchaseRewardMethod;

	private static MethodInfo refundPointsMethod;

	private static MethodInfo tryExecuteEventMethod;

	private static AccessTools.FieldRef<IList> rewardDatabase;

	private static Type windowType;

	private static bool usedReward;

	public VanillaAchievements(ModContentPack mod)
	{
		Type type = AccessTools.TypeByName("AchievementsExpanded.AchievementReward");
		purchaseRewardMethod = AccessTools.Method(type, "PurchaseReward");
		refundPointsMethod = AccessTools.Method(type, "RefundPoints");
		tryExecuteEventMethod = AccessTools.Method(type, "TryExecuteEvent");
		rewardDatabase = AccessTools.StaticFieldRefAccess<IList>(AccessTools.Field(typeof(DefDatabase<>).MakeGenericType(type), "defsList"));
		windowType = AccessTools.TypeByName("AchievementsExpanded.MainTabWindow_Achievements");
		MethodInfo[] array = new MethodInfo[2]
		{
			AccessTools.Method(windowType, "DrawSidePanel"),
			AccessTools.DeclaredMethod(AccessTools.TypeByName("AchievementsExpanded.TraderTracker"), "Trigger")
		};
		foreach (MethodInfo original in array)
		{
			MpCompat.harmony.Patch(original, null, null, new HarmonyMethod(typeof(VanillaAchievements), "Transpiler"));
		}
		MP.RegisterSyncMethod(typeof(VanillaAchievements), "SyncedCalls");
	}

	private static bool PurchaseReward(Def reward)
	{
		usedReward = true;
		SyncedCalls(rewardDatabase().IndexOf(reward));
		return false;
	}

	private static void SyncedCalls(int index)
	{
		object obj = rewardDatabase()[index];
		if ((bool)purchaseRewardMethod.Invoke(obj, Array.Empty<object>()))
		{
			if (!(bool)tryExecuteEventMethod.Invoke(obj, Array.Empty<object>()))
			{
				refundPointsMethod.Invoke(obj, Array.Empty<object>());
			}
			else if (usedReward)
			{
				Find.WindowStack.Windows.FirstOrDefault((Window x) => x.GetType() == windowType)?.Close(doCloseSound: false);
			}
		}
		usedReward = false;
	}

	private static float SaferGetPriceFor(Tradeable tradeable, TradeAction action)
	{
		if (MP.IsInMultiplayer && tradeable.AnyThing == null)
		{
			return 0f;
		}
		return tradeable.GetPriceFor(action);
	}

	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instr)
	{
		MethodInfo methodInfo = AccessTools.Method(typeof(Tradeable), "GetPriceFor");
		foreach (CodeInstruction item in instr)
		{
			if (item.opcode == OpCodes.Callvirt)
			{
				MethodInfo methodInfo2 = (MethodInfo)item.operand;
				if (methodInfo2 == purchaseRewardMethod)
				{
					item.opcode = OpCodes.Call;
					item.operand = AccessTools.Method(typeof(VanillaAchievements), "PurchaseReward");
					break;
				}
				if (methodInfo2 == methodInfo)
				{
					item.opcode = OpCodes.Call;
					item.operand = AccessTools.Method(typeof(VanillaAchievements), "SaferGetPriceFor");
					break;
				}
			}
		}
		return instr;
	}
}
