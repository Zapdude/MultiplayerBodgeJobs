using System;
using System.Collections;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Owlchemist.SmartFarming")]
internal class SmartFarming
{
	private static IDictionary compCache;

	private static AccessTools.FieldRef<object, IDictionary> growZoneRegistryField;

	public SmartFarming(ModContentPack mod)
	{
		Type type = AccessTools.TypeByName("SmartFarming.Mod_SmartFarming");
		compCache = AccessTools.StaticFieldRefAccess<IDictionary>(type, "compCache");
		type = AccessTools.TypeByName("SmartFarming.MapComponent_SmartFarming");
		growZoneRegistryField = AccessTools.FieldRefAccess<IDictionary>(type, "growZoneRegistry");
		type = AccessTools.TypeByName("SmartFarming.ZoneData");
		MpCompat.RegisterLambdaDelegate(type, "Init", 3, 5, 6, 8);
		MP.RegisterSyncMethod(type, "SwitchSowMode");
		MP.RegisterSyncMethod(type, "SwitchPriority");
		MP.RegisterSyncMethod(type, "MergeZones");
		MP.RegisterSyncWorker<object>(SyncZoneData, type);
	}

	private static void SyncZoneData(SyncWorker sync, ref object zoneData)
	{
		if (sync.isWriting)
		{
			int? obj = null;
			object instance = compCache[Find.CurrentMap.uniqueID];
			foreach (DictionaryEntry item in growZoneRegistryField(instance))
			{
				if (item.Value == zoneData)
				{
					obj = (int)item.Key;
					break;
				}
			}
			sync.Write(obj);
			if (obj.HasValue)
			{
				sync.Write(Find.CurrentMap.uniqueID);
			}
		}
		else
		{
			int? num = sync.Read<int?>();
			if (num.HasValue)
			{
				object instance2 = compCache[sync.Read<int>()];
				IDictionary dictionary = growZoneRegistryField(instance2);
				zoneData = dictionary[num.Value];
			}
		}
	}
}
