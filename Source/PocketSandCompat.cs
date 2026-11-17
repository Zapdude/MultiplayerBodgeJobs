using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("usagirei.pocketsand")]
public class PocketSandCompat
{
	public PocketSandCompat(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LateLoad);
	}

	private static void LateLoad()
	{
		Type type = AccessTools.TypeByName("PocketSand.PawnExtensions");
		MP.RegisterSyncMethod(type, "EquipFromInventory");
		MP.RegisterSyncMethod(type, "DropFromInventory");
	}
}
