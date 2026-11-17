using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("CptOhu.CorruptionCore")]
public class CorruptionCore
{
	private static FastInvokeHandler pawnSoulITabSoulToShowGetter;

	private static AccessTools.FieldRef<object, object> compSoulFavourTrackerField;

	private static AccessTools.FieldRef<object, object> compSoulPrayerTrackerField;

	private static ConstructorInfo setPawnPantheonDialogConstructor;

	private static AccessTools.FieldRef<object, ThingComp> setPawnPantheonDialogSoulField;

	private static AccessTools.FieldRef<object, IList> soulFavourTrackerFavoursField;

	private static AccessTools.FieldRef<object, ThingComp> prayerTrackerCompSoulField;

	private static ISyncField prayerTrackerAllowPrayingSync;

	private static ISyncField prayerTrackerShowPrayerSync;

	private static AccessTools.FieldRef<object, float> favourProgressFavourValueField;

	public CorruptionCore(ModContentPack mod)
	{
		Type type = AccessTools.TypeByName("Corruption.Core.Soul.ITab_Pawn_Soul");
		pawnSoulITabSoulToShowGetter = HarmonyLib.MethodInvoker.GetHandler(AccessTools.PropertyGetter(type, "SoulToShow"));
		MP.RegisterSyncMethod(typeof(CorruptionCore), "SyncFavourValue");
		MpCompat.harmony.Patch(AccessTools.Method(type, "FillTab"), new HarmonyMethod(typeof(CorruptionCore), "PreFillTab"), new HarmonyMethod(typeof(CorruptionCore), "PostFillTab"));
		type = AccessTools.TypeByName("Corruption.Core.Soul.CompSoul");
		compSoulFavourTrackerField = AccessTools.FieldRefAccess<object>(type, "FavourTracker");
		compSoulPrayerTrackerField = AccessTools.FieldRefAccess<object>(type, "PrayerTracker");
		type = AccessTools.TypeByName("Corruption.Core.Dialog_SetPawnPantheon");
		setPawnPantheonDialogConstructor = AccessTools.DeclaredConstructor(type);
		setPawnPantheonDialogSoulField = AccessTools.FieldRefAccess<ThingComp>(type, "soul");
		MP.RegisterSyncMethod(type, "SelectionChanged");
		MP.RegisterSyncWorker<object>(SyncDialogSetPawnPantheon, type);
		type = AccessTools.TypeByName("Corruption.Core.Soul.Soul_FavourTracker");
		soulFavourTrackerFavoursField = AccessTools.FieldRefAccess<IList>(type, "Favours");
		type = AccessTools.TypeByName("Corruption.Core.Gods.Pawn_PrayerTracker");
		prayerTrackerCompSoulField = AccessTools.FieldRefAccess<ThingComp>(type, "compSoul");
		prayerTrackerAllowPrayingSync = MP.RegisterSyncField(type, "AllowPraying");
		prayerTrackerShowPrayerSync = MP.RegisterSyncField(type, "ShowPrayer");
		MP.RegisterSyncWorker<object>(SyncPawnPrayerTracker, type);
		MpCompat.harmony.Patch(AccessTools.Method(type, "AdvancePrayer"), new HarmonyMethod(typeof(CorruptionCore), "PreAdvancePrayer"), new HarmonyMethod(typeof(CorruptionCore), "PostAdvancePrayer"));
		type = AccessTools.TypeByName("Corruption.Core.Soul.FavourProgress");
		favourProgressFavourValueField = AccessTools.FieldRefAccess<float>(type, "favourValue");
	}

	private static void PreFillTab(ITab __instance, ref object[] __state)
	{
		if (!MP.IsInMultiplayer)
		{
			return;
		}
		__state = new object[2];
		object obj = pawnSoulITabSoulToShowGetter(__instance);
		if (obj != null)
		{
			MP.WatchBegin();
			object target = compSoulPrayerTrackerField(obj);
			prayerTrackerAllowPrayingSync.Watch(target);
			prayerTrackerShowPrayerSync.Watch(target);
			__state[0] = true;
			object instance = compSoulFavourTrackerField(obj);
			IList list = soulFavourTrackerFavoursField(instance);
			float[] array = new float[list.Count];
			for (int i = 0; i < list.Count; i++)
			{
				array[i] = favourProgressFavourValueField(list[i]);
			}
			__state[1] = array;
		}
		else
		{
			__state[0] = false;
		}
	}

	private static void PostFillTab(ITab __instance, ref object[] __state)
	{
		if (!MP.IsInMultiplayer || !(bool)__state[0])
		{
			return;
		}
		MP.WatchEnd();
		object obj = pawnSoulITabSoulToShowGetter(__instance);
		object instance = compSoulFavourTrackerField(obj);
		float[] array = (float[])__state[1];
		IList list = soulFavourTrackerFavoursField(instance);
		for (int i = 0; i < list.Count; i++)
		{
			float num = favourProgressFavourValueField(list[i]);
			float num2 = array[i];
			if (num != num2)
			{
				favourProgressFavourValueField(list[i]) = num2;
				SyncFavourValue((ThingComp)obj, i, num);
			}
		}
	}

	private static void SyncFavourValue(ThingComp souldComp, int favourIndex, float value)
	{
		object instance = compSoulFavourTrackerField(souldComp);
		IList list = soulFavourTrackerFavoursField(instance);
		favourProgressFavourValueField(list[favourIndex]) = value;
	}

	private static void SyncDialogSetPawnPantheon(SyncWorker sync, ref object window)
	{
		if (sync.isWriting)
		{
			ThingComp obj = setPawnPantheonDialogSoulField(window);
			sync.Write(obj);
			return;
		}
		ThingComp thingComp = sync.Read<ThingComp>();
		window = setPawnPantheonDialogConstructor.Invoke(new object[2] { thingComp, null });
	}

	private static void SyncPawnPrayerTracker(SyncWorker sync, ref object tracker)
	{
		if (sync.isWriting)
		{
			sync.Write(prayerTrackerCompSoulField(tracker));
		}
		else
		{
			tracker = compSoulPrayerTrackerField(sync.Read<ThingComp>());
		}
	}

	private static void PreAdvancePrayer(object __instance)
	{
		if (MP.IsInMultiplayer)
		{
			Rand.PushState(prayerTrackerCompSoulField(__instance).parent.GetHashCode() ^ Find.TickManager.TicksGame);
		}
	}

	private static void PostAdvancePrayer()
	{
		if (MP.IsInMultiplayer)
		{
			Rand.PopState();
		}
	}
}
