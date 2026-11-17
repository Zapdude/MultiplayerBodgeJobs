using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using UnityEngine;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("VanillaStorytellersExpanded.WinstonWave")]
internal class VanillaStorytellersWinstonWaves
{
	private static Type chooseRewardDialogType;

	private static AccessTools.FieldRef<object, Def> choosenRewardField;

	private static AccessTools.FieldRef<object, bool> nextRaidInfoSentField;

	private static bool ignoreCall;

	public VanillaStorytellersWinstonWaves(ModContentPack mod)
	{
		PatchingUtilities.PatchSystemRand("VSEWW.Window_ChooseReward:DoWindowContents", patchPushPop: false);
		Type type = (chooseRewardDialogType = AccessTools.TypeByName("VSEWW.Window_ChooseReward"));
		choosenRewardField = AccessTools.FieldRefAccess<Def>(type, "choosenReward");
		DialogUtilities.RegisterPauseLock(type);
		MpCompat.harmony.Patch(AccessTools.DeclaredConstructor(type, new Type[3]
		{
			typeof(int),
			typeof(float),
			typeof(Map)
		}), null, new HarmonyMethod(AccessTools.DeclaredMethod(typeof(VanillaStorytellersWinstonWaves), "PostRewardDialogCreated")));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, "DoWindowContents"), new HarmonyMethod(AccessTools.DeclaredMethod(typeof(VanillaStorytellersWinstonWaves), "PreRewardDoWindowContents")));
		type = AccessTools.TypeByName("VSEWW.RewardDef");
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, "DrawCard"), null, null, new HarmonyMethod(typeof(VanillaStorytellersWinstonWaves), "ReplaceButton"));
		MP.RegisterSyncMethod(typeof(VanillaStorytellersWinstonWaves), "SyncedChooseReward");
		type = AccessTools.TypeByName("VSEWW.MapComponent_Winston");
		MethodInfo methodInfo = AccessTools.DeclaredMethod(type, "StartRaid");
		MP.RegisterSyncMethod(methodInfo);
		MpCompat.harmony.Patch(methodInfo, new HarmonyMethod(typeof(VanillaStorytellersWinstonWaves), "StopDuplicateRaidStart"));
		type = AccessTools.TypeByName("VSEWW.NextRaidInfo");
		nextRaidInfoSentField = AccessTools.FieldRefAccess<bool>(type, "sent");
	}

	private static void PostRewardDialogCreated(Window __instance, IList ___rewards)
	{
		if (!MP.IsInMultiplayer || (___rewards != null && ___rewards.Count > 0))
		{
			return;
		}
		try
		{
			ignoreCall = true;
			__instance.DoWindowContents(Rect.zero);
		}
		finally
		{
			ignoreCall = false;
		}
	}

	private static bool PreRewardDoWindowContents(Window __instance, IList ___rewards, Def ___choosenReward)
	{
		if (!MP.IsInMultiplayer || ignoreCall)
		{
			return true;
		}
		if (___choosenReward != null)
		{
			__instance.Close();
			return false;
		}
		if (___rewards == null || ___rewards.Count <= 0)
		{
			__instance.Close();
			return false;
		}
		return true;
	}

	private static bool StopDuplicateRaidStart(object ___nextRaidInfo)
	{
		if (___nextRaidInfo != null)
		{
			return !nextRaidInfoSentField(___nextRaidInfo);
		}
		return false;
	}

	private static bool InjectedButton(Rect rect, string label, bool drawBackground, bool doMouseoverSound, bool active, TextAnchor? anchor, Def instance)
	{
		if (Widgets.ButtonText(rect, label, drawBackground, doMouseoverSound, active, anchor))
		{
			if (!MP.IsInMultiplayer)
			{
				return true;
			}
			SyncedChooseReward(instance);
		}
		return false;
	}

	private static void SyncedChooseReward(Def rewardDef)
	{
		Window window = Find.WindowStack.windows.FirstOrDefault((Window w) => w.GetType() == chooseRewardDialogType);
		if (window != null)
		{
			choosenRewardField(window) = rewardDef;
			window.Close();
		}
	}

	private static IEnumerable<CodeInstruction> ReplaceButton(IEnumerable<CodeInstruction> instr)
	{
		MethodInfo target = AccessTools.Method(typeof(Widgets), "ButtonText", new Type[6]
		{
			typeof(Rect),
			typeof(string),
			typeof(bool),
			typeof(bool),
			typeof(bool),
			typeof(TextAnchor?)
		});
		MethodInfo replacement = AccessTools.Method(typeof(VanillaStorytellersWinstonWaves), "InjectedButton");
		int replacedCount = 0;
		foreach (CodeInstruction ci in instr)
		{
			if (ci.opcode == OpCodes.Call && ci.operand is MethodInfo methodInfo && methodInfo == target)
			{
				yield return new CodeInstruction(OpCodes.Ldarg_0);
				ci.operand = replacement;
				replacedCount++;
			}
			yield return ci;
		}
		if (replacedCount != 1)
		{
			Log.Warning($"Patched incorrect number of button calls inside of RewardDef.DrawCard: patched {replacedCount} methods, expected {1}");
		}
	}
}
