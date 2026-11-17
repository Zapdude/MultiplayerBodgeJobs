using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("MaxZiCode.dws.ZiTools")]
public class ZiTools
{
	private static class MapMarksManager_Marks_Patch
	{
		private static object preDontSync;

		public static void Prefix()
		{
			preDontSync = DontSync.GetValue(null);
			DontSync.SetValue(null, true);
		}

		public static void Finalizer()
		{
			DontSync.SetValue(null, preDontSync);
		}
	}

	private static object FavChange;

	private static MethodInfo GetObjectsDatabase;

	private static PropertyInfo PropFavList;

	private static FieldInfo UnitsDict;

	private static FieldInfo DontSync;

	private static PropertyInfo PropLabel;

	public ZiTools(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
	}

	private void LatePatch()
	{
		DontSync = AccessTools.Field(AccessTools.TypeByName("Multiplayer.Client.Multiplayer"), "dontSync");
		Type type = AccessTools.TypeByName("ZiTools.ZiTools_GameComponent");
		GetObjectsDatabase = AccessTools.Method(type, "GetObjectsDatabase");
		type = AccessTools.TypeByName("ZiTools.ObjectsDatabase");
		PropFavList = AccessTools.Property(type, "UnitsInFavourites");
		UnitsDict = AccessTools.Field(type, "unitsDict");
		type = AccessTools.TypeByName("ZiTools.ObjectSeeker_Window");
		MpCompat.harmony.Patch(AccessTools.Method(type, "DoWindowContents"), null, new HarmonyMethod(typeof(ZiTools), "PostDoWindowContents"));
		MpCompat.harmony.Patch(AccessTools.Method(type, "DrawObjectsList"), null, new HarmonyMethod(typeof(ZiTools), "PostDrawObjectsList"));
		type = AccessTools.TypeByName("ZiTools.MapMarksManager");
		MpCompat.harmony.Patch(AccessTools.Method(type, "SetMarks"), new HarmonyMethod(typeof(MapMarksManager_Marks_Patch), "Prefix"), null, null, new HarmonyMethod(typeof(MapMarksManager_Marks_Patch), "Finalizer"));
		MpCompat.harmony.Patch(AccessTools.Method(type, "RemoveMarks"), new HarmonyMethod(typeof(MapMarksManager_Marks_Patch), "Prefix"), null, null, new HarmonyMethod(typeof(MapMarksManager_Marks_Patch), "Finalizer"));
		type = AccessTools.TypeByName("ZiTools.DBUnit");
		PropLabel = AccessTools.Property(type, "Label");
		MP.RegisterSyncMethod(typeof(ZiTools), "SyncedRemoveFromFav");
		MP.RegisterSyncMethod(typeof(ZiTools), "SyncedAddToFav");
	}

	public static void PostDrawObjectsList(ref IExposable favChange)
	{
		if (favChange != null)
		{
			FavChange = favChange;
		}
	}

	public static void PostDoWindowContents()
	{
		if (FavChange == null)
		{
			return;
		}
		IList favList = GetFavList();
		if (favList == null)
		{
			Log.Warning("ZiTools have no Favlist now");
			FavChange = null;
			return;
		}
		if (!favList.Contains(FavChange))
		{
			favList.Add(FavChange);
			SyncedRemoveFromFav(PropLabel.GetValue(FavChange) as string);
		}
		else
		{
			favList.Remove(FavChange);
			SyncedAddToFav(PropLabel.GetValue(FavChange) as string);
		}
		FavChange = null;
	}

	private static object GetUnitByLabel(string label)
	{
		object obj = GetObjectsDatabase.Invoke(null, null);
		foreach (object value in (UnitsDict.GetValue(obj) as IDictionary).Values)
		{
			if (PropLabel.GetValue(value) as string == label)
			{
				return value;
			}
		}
		return null;
	}

	private static IList GetFavList()
	{
		object obj = GetObjectsDatabase.Invoke(null, null);
		return PropFavList?.GetValue(obj) as IList;
	}

	private static void SyncedRemoveFromFav(string favChangeLabel)
	{
		object unitByLabel = GetUnitByLabel(favChangeLabel);
		if (unitByLabel != null)
		{
			GetFavList().Remove(unitByLabel);
		}
	}

	private static void SyncedAddToFav(string favChangeLabel)
	{
		object unitByLabel = GetUnitByLabel(favChangeLabel);
		if (unitByLabel != null)
		{
			GetFavList().Add(unitByLabel);
		}
	}
}
