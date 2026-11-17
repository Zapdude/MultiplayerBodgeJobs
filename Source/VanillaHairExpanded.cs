using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("VanillaExpanded.VHE")]
internal class VanillaHairExpanded
{
	private static Type changeHairstyleDialogType;

	private static List<HairDef> orderedHairstyleDefs;

	private static List<BeardDef> orderedBeardDefs;

	private static ISyncField newHairstyleComboSync;

	private static FieldInfo hairDefField;

	private static FieldInfo beardDefField;

	private static FieldInfo hairColourField;

	public VanillaHairExpanded(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
	}

	public void LatePatch()
	{
		changeHairstyleDialogType = AccessTools.TypeByName("VanillaHairExpanded.Dialog_ChangeHairstyle");
		orderedHairstyleDefs = (List<HairDef>)AccessTools.Field(changeHairstyleDialogType, "orderedHairDefs").GetValue(null);
		orderedBeardDefs = (List<BeardDef>)AccessTools.Field(changeHairstyleDialogType, "orderedBeardDefs").GetValue(null);
		newHairstyleComboSync = MP.RegisterSyncField(AccessTools.Field(changeHairstyleDialogType, "newHairBeardCombo"));
		MP.RegisterSyncMethod(changeHairstyleDialogType, "SetHairstyle");
		MP.RegisterSyncWorker<Window>(SyncDialog, changeHairstyleDialogType);
		MP.RegisterSyncMethod(typeof(VanillaHairExpanded), "SyncedTryRemoveWindow");
		MpCompat.harmony.Patch(AccessTools.Method(changeHairstyleDialogType, "DoWindowContents"), new HarmonyMethod(typeof(VanillaHairExpanded), "PreDoWindowContents"), new HarmonyMethod(typeof(VanillaHairExpanded), "PostDoWindowContents"));
		Type type = AccessTools.Inner(changeHairstyleDialogType, "HairBeardCombination");
		MP.RegisterSyncWorker<object>(SyncHairstyleCombination, type);
		hairDefField = AccessTools.Field(type, "hairDef");
		beardDefField = AccessTools.Field(type, "beardDef");
		hairColourField = AccessTools.Field(type, "hairColour");
		MpCompat.harmony.Patch(AccessTools.Method(typeof(WindowStack), "TryRemove", new Type[2]
		{
			typeof(Window),
			typeof(bool)
		}), new HarmonyMethod(typeof(VanillaHairExpanded), "PreTryRemoveWindow"));
		DialogUtilities.RegisterDialogCloseSync(changeHairstyleDialogType, addPauseLock: true);
	}

	private static void PreDoWindowContents(Window __instance)
	{
		if (MP.IsInMultiplayer)
		{
			MP.WatchBegin();
			newHairstyleComboSync.Watch(__instance);
		}
	}

	private static void PostDoWindowContents()
	{
		if (MP.IsInMultiplayer)
		{
			MP.WatchEnd();
		}
	}

	private static bool PreTryRemoveWindow(Window window)
	{
		if (!MP.InInterface || window.GetType() != changeHairstyleDialogType)
		{
			return true;
		}
		SyncedTryRemoveWindow();
		return false;
	}

	private static void SyncedTryRemoveWindow()
	{
		Find.WindowStack.TryRemove(changeHairstyleDialogType);
	}

	private static void SyncHairstyleCombination(SyncWorker sync, ref object obj)
	{
		if (sync.isWriting)
		{
			sync.Write(((Color?)hairColourField?.GetValue(obj)) ?? Color.black);
			sync.Write(orderedHairstyleDefs?.IndexOf(hairDefField.GetValue(obj) as HairDef) ?? (-1));
			sync.Write(orderedBeardDefs?.IndexOf(beardDefField.GetValue(obj) as BeardDef) ?? (-1));
			return;
		}
		hairColourField.SetValue(obj, sync.Read<Color>());
		int num = sync.Read<int>();
		if (num >= 0)
		{
			hairDefField.SetValue(obj, orderedHairstyleDefs[num]);
		}
		num = sync.Read<int>();
		if (num >= 0)
		{
			beardDefField.SetValue(obj, orderedBeardDefs[num]);
		}
	}

	private static void SyncDialog(SyncWorker sync, ref Window dialog)
	{
		if (!sync.isWriting && Find.WindowStack.IsOpen(changeHairstyleDialogType))
		{
			dialog = Find.WindowStack.Windows.First((Window x) => x.GetType() == changeHairstyleDialogType);
		}
	}
}
