using System;
using System.Collections;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("MemeGoddess.SearchAndDestroy")]
public class SearchAndDestroy
{
	private static FastInvokeHandler searchAndDestroyInstance;

	private static FastInvokeHandler extendedDataStorageGetter;

	private static AccessTools.FieldRef<object, IDictionary> storeField;

	public SearchAndDestroy(ModContentPack mod)
	{
		MpCompat.RegisterLambdaDelegate("SearchAndDestroy.Harmony.Pawn_DraftController_GetGizmos", "CreateGizmo_SearchAndDestroy_Melee", 1);
		MpCompat.RegisterLambdaDelegate("SearchAndDestroy.Harmony.Pawn_DraftController_GetGizmos", "CreateGizmo_SearchAndDestroy_Ranged", 1);
		Type targetType = AccessTools.TypeByName("SearchAndDestroy.Storage.ExtendedPawnData");
		MP.RegisterSyncWorker<object>(SyncExtendedPawnData, targetType);
		Type type = AccessTools.TypeByName("SearchAndDestroy.Base");
		searchAndDestroyInstance = MethodInvoker.GetHandler(AccessTools.PropertyGetter(type, "Instance"));
		extendedDataStorageGetter = MethodInvoker.GetHandler(AccessTools.PropertyGetter(type, "ExtendedDataStorage"));
		storeField = AccessTools.FieldRefAccess<IDictionary>("SearchAndDestroy.Storage.ExtendedDataStorage:_store");
	}

	public static void SyncExtendedPawnData(SyncWorker sync, ref object pawnData)
	{
		object target = searchAndDestroyInstance(null);
		object instance = extendedDataStorageGetter(target);
		IDictionary dictionary = storeField(instance);
		if (sync.isWriting)
		{
			bool flag = false;
			foreach (DictionaryEntry item in dictionary)
			{
				if (item.Value == pawnData)
				{
					sync.Write((int)item.Key);
					flag = true;
					break;
				}
			}
			if (!flag)
			{
				sync.Write(int.MinValue);
			}
		}
		else
		{
			int num = sync.Read<int>();
			if (num != int.MinValue)
			{
				pawnData = dictionary[num];
			}
		}
	}
}
