using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

public static class DialogUtilities
{
	private static bool isCloseSyncInitialized = false;

	private static bool isPauseLockInitialized = false;

	private static readonly List<Window> OpenDialogsCloseSync = new List<Window>();

	private static readonly List<Window> OpenPauseLockDialogs = new List<Window>();

	public static void RegisterDialogCloseSync(Type type, bool addPauseLock = false)
	{
		if (type == null)
		{
			return;
		}
		InitializeDialogCloseSync(addPauseLock);
		HarmonyMethod postfix = (addPauseLock ? new HarmonyMethod(typeof(DialogUtilities), "PostDialogOpen_CloseSync_PauseLock") : new HarmonyMethod(typeof(DialogUtilities), "PostDialogOpen_CloseSync"));
		foreach (ConstructorInfo declaredConstructor in AccessTools.GetDeclaredConstructors(type))
		{
			MpCompat.harmony.Patch(declaredConstructor, null, postfix);
		}
	}

	public static void RegisterPauseLock(Type type)
	{
		if (type == null)
		{
			return;
		}
		InitializeDialogPauseLock();
		foreach (ConstructorInfo declaredConstructor in AccessTools.GetDeclaredConstructors(type))
		{
			MpCompat.harmony.Patch(declaredConstructor, null, new HarmonyMethod(typeof(DialogUtilities), "PostDialogOpen_PauseLock"));
		}
	}

	public static void InitializeDialogCloseSync(bool addPauseLock)
	{
		if (addPauseLock)
		{
			InitializeDialogPauseLock();
		}
		if (!isCloseSyncInitialized)
		{
			MP.RegisterSyncMethod(typeof(DialogUtilities), "SyncedTryCloseDialog");
			MpCompat.harmony.Patch(AccessTools.Method(typeof(WindowStack), "TryRemove", new Type[2]
			{
				typeof(Window),
				typeof(bool)
			}), new HarmonyMethod(typeof(DialogUtilities), "PreTryRemove"));
			isCloseSyncInitialized = true;
		}
	}

	public static void InitializeDialogPauseLock()
	{
		if (!isPauseLockInitialized)
		{
			MP.RegisterPauseLock(IsPausingDialogOpen);
			isPauseLockInitialized = true;
		}
	}

	private static bool IsPausingDialogOpen(Map _)
	{
		if (!OpenPauseLockDialogs.Any())
		{
			return false;
		}
		OpenPauseLockDialogs.RemoveAll((Window x) => !(x?.IsOpen ?? false));
		return OpenPauseLockDialogs.Any();
	}

	public static void PostDialogOpen_CloseSync(Window __instance)
	{
		if (MP.IsInMultiplayer)
		{
			OpenDialogsCloseSync.AddDistinct(__instance);
		}
	}

	public static void PostDialogOpen_PauseLock(Window __instance)
	{
		if (MP.IsInMultiplayer)
		{
			OpenPauseLockDialogs.AddDistinct(__instance);
		}
	}

	public static void PostDialogOpen_CloseSync_PauseLock(Window __instance)
	{
		if (MP.IsInMultiplayer)
		{
			OpenDialogsCloseSync.AddDistinct(__instance);
			OpenPauseLockDialogs.AddDistinct(__instance);
		}
	}

	private static bool PreTryRemove(Window window, bool doCloseSound)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (MP.IsExecutingSyncCommand)
		{
			OpenDialogsCloseSync.Remove(window);
			OpenPauseLockDialogs.Remove(window);
			return true;
		}
		int num = OpenDialogsCloseSync.IndexOf(window);
		if (num < 0)
		{
			OpenPauseLockDialogs.Remove(window);
			return true;
		}
		SyncedTryCloseDialog(num, doCloseSound);
		return false;
	}

	private static void SyncedTryCloseDialog(int index, bool doCloseSound)
	{
		if (index >= 0 && OpenDialogsCloseSync.Count > index)
		{
			Window window = OpenDialogsCloseSync[index];
			OpenDialogsCloseSync.RemoveAt(index);
			Find.WindowStack.TryRemove(window, doCloseSound);
		}
	}
}
