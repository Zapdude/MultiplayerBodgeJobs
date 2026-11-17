using System;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Syrus.HMSChoice")]
internal class HealerMechSerumChoice
{
	private static Type hediffSelectionDialogType;

	private static AccessTools.FieldRef<object, Hediff> dialogSelectedHediffField;

	public HealerMechSerumChoice(ModContentPack mod)
	{
		MP.RegisterSyncMethod(typeof(HealerMechSerumChoice), "SyncedSetHediff");
		hediffSelectionDialogType = AccessTools.TypeByName("HMSChoice.Dialog_HediffSelection");
		dialogSelectedHediffField = AccessTools.FieldRefAccess<Hediff>(hediffSelectionDialogType, "SelectedHediff");
		MP.RegisterSyncMethod(AccessTools.Method(hediffSelectionDialogType, "Close", new Type[1] { typeof(Hediff) }));
		MP.RegisterSyncWorker<Window>(SyncHediffDialog, hediffSelectionDialogType);
		MpCompat.harmony.Patch(AccessTools.Method(hediffSelectionDialogType, "DoWindowContents"), new HarmonyMethod(typeof(HealerMechSerumChoice), "PreDoWindowContents"), new HarmonyMethod(typeof(HealerMechSerumChoice), "PostDoWindowContents"));
		DialogUtilities.RegisterDialogCloseSync(hediffSelectionDialogType, addPauseLock: true);
	}

	private static void PreDoWindowContents(Hediff ___SelectedHediff, ref Hediff __state)
	{
		if (MP.IsInMultiplayer)
		{
			__state = ___SelectedHediff;
		}
	}

	private static void PostDoWindowContents(ref Hediff ___SelectedHediff, Hediff __state)
	{
		if (MP.IsInMultiplayer && ___SelectedHediff != __state)
		{
			SyncedSetHediff(___SelectedHediff);
			___SelectedHediff = __state;
		}
	}

	private static void SyncedSetHediff(Hediff hediff)
	{
		Window window = Find.WindowStack.Windows.FirstOrDefault((Window x) => x.GetType() == hediffSelectionDialogType);
		if (window != null)
		{
			dialogSelectedHediffField(window) = hediff;
		}
	}

	private static void SyncHediffDialog(SyncWorker sync, ref Window window)
	{
		if (!sync.isWriting)
		{
			window = Find.WindowStack.Windows.FirstOrDefault((Window x) => x.GetType() == hediffSelectionDialogType);
		}
	}
}
