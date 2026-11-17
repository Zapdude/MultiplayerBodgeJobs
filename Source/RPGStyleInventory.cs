using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Sandy.RPGStyleInventory")]
[MpCompatFor("Nykot.RPGStyleInventory")]
[MpCompatFor("Sandy.RPGStyleInventory.avilmask.Revamped")]
internal class RPGStyleInventory
{
	private static Type tabType;

	public RPGStyleInventory(ModContentPack mod)
	{
		tabType = AccessTools.TypeByName("Sandy_Detailed_RPG_Inventory.Sandy_Detailed_RPG_GearTab");
		MP.RegisterSyncWorker<ITab_Pawn_Gear>(SyncITab, tabType);
		string[] array = new string[2] { "InterfaceDrop", "InterfaceIngest" };
		foreach (string name in array)
		{
			MethodInfo methodInfo = AccessTools.DeclaredMethod(tabType, name);
			if (methodInfo != null)
			{
				MP.RegisterSyncMethod(methodInfo).SetContext(SyncContext.MapSelected);
			}
		}
		if (mod.PackageId == "Sandy.RPGStyleInventory.avilmask.Revamped".ToLower())
		{
			MpCompat.RegisterLambdaDelegate(tabType, "PopupMenu", 0, 1);
		}
	}

	private static void SyncITab(SyncWorker sync, ref ITab_Pawn_Gear gearITab)
	{
		if (!sync.isWriting)
		{
			gearITab = Activator.CreateInstance(tabType) as ITab_Pawn_Gear;
		}
	}
}
