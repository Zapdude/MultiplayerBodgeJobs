using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("RI.RimImmortal.Core")]
public class RimImmortalCore
{
	private static Type messageDialogType;

	private static FieldInfo pawnField;

	private static FieldInfo targetField;

	private static FieldInfo upgradeSpotField;

	public RimImmortalCore(ModContentPack mod)
	{
		Harmony harmony = MpCompat.harmony;
		MP.RegisterSyncDelegate(AccessTools.TypeByName("WhoXiuXian.Abilities.CompAbilityEffect_ToggleHediff"), "<>c__DisplayClass8_0", "<GetGizmos>b__2");
		MP.RegisterSyncDelegate(AccessTools.TypeByName("RIRitualFramework.Building_Upgrade"), "<>c__DisplayClass5_0", "<GetFloatMenuOptions>b__0");
		messageDialogType = AccessTools.TypeByName("RIRitualFramework.MessageDialog");
		pawnField = AccessTools.Field(messageDialogType, "pawn");
		targetField = AccessTools.Field(messageDialogType, "target");
		upgradeSpotField = AccessTools.Field(messageDialogType, "upgradeSpot");
		harmony.Patch(AccessTools.Method(typeof(WindowStack), "Add"), new HarmonyMethod(typeof(RimImmortalCore), "WindowStackAddPrefix"));
		MP.RegisterSyncWorker<Window>(SyncMessageDialog, messageDialogType, isImplicit: true);
		MP.RegisterSyncMethod(messageDialogType, "StartRIRitual");
	}

	private static bool WindowStackAddPrefix(Window window)
	{
		if (!MP.IsExecutingSyncCommandIssuedBySelf && window != null && window.GetType() == messageDialogType)
		{
			return false;
		}
		return true;
	}

	private static void SyncMessageDialog(SyncWorker sync, ref Window dialog)
	{
		if (sync.isWriting)
		{
			Pawn obj = (Pawn)pawnField.GetValue(dialog);
			LocalTargetInfo obj2 = (LocalTargetInfo)targetField.GetValue(dialog);
			LocalTargetInfo obj3 = (LocalTargetInfo)upgradeSpotField.GetValue(dialog);
			sync.Write(obj);
			sync.Write(obj2);
			sync.Write(obj3);
		}
		else
		{
			Pawn pawn = sync.Read<Pawn>();
			LocalTargetInfo localTargetInfo = sync.Read<LocalTargetInfo>();
			LocalTargetInfo localTargetInfo2 = sync.Read<LocalTargetInfo>();
			dialog = (Window)Activator.CreateInstance(messageDialogType, pawn, localTargetInfo, localTargetInfo2, true);
		}
	}
}
