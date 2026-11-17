using System;
using HarmonyLib;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Dubwise.DubsMintMenus")]
internal class DubsMintMenu
{
	public DubsMintMenu(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(() =>
		{
			Patch("DubsMintMenus.MP_Util");
		});
	}

	public static void Patch(string name)
	{
		Type type = AccessTools.TypeByName(name);
		Type type2 = AccessTools.TypeByName("Multiplayer.Client.SyncMethod");
		AccessTools.Field(type, "RegisterRef").SetValue(null, AccessTools.Method(type2, "Register"));
		Type type3 = AccessTools.TypeByName("Multiplayer.Client.SyncFieldUtil");
		AccessTools.Field(type, "FieldWatchPrefixRef").SetValue(null, AccessTools.Method(type3, "FieldWatchPrefix"));
		AccessTools.Field(type, "FieldWatchPostfixRef").SetValue(null, AccessTools.Method(type3, "FieldWatchPostfix"));
		Type type4 = AccessTools.TypeByName("Multiplayer.Client.SyncField");
		AccessTools.Field(type, "WatchRef").SetValue(null, AccessTools.Method(type4, "Watch"));
		AccessTools.Field(type, "SetBufferChangesRef").SetValue(null, AccessTools.Method(type4, "SetBufferChanges"));
		Type type5 = AccessTools.TypeByName("Multiplayer.Client.Sync");
		AccessTools.Field(type, "FieldRef").SetValue(null, AccessTools.Method(type5, "Field", new Type[2]
		{
			typeof(Type),
			typeof(string)
		}));
		AccessTools.Method(type, "asshai").Invoke(null, Array.Empty<object>());
	}
}
