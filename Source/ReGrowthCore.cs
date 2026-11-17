using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("ReGrowth.BOTR.Core")]
public class ReGrowthCore
{
	private static Type smartFarmingCompType;

	private static FieldInfo compCacheField;

	private static FieldInfo growZoneRegistryField;

	public ReGrowthCore(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
		MpCompatPatchLoader.LoadPatch(this);
		MpCompat.RegisterLambdaMethod("ReGrowthCore.CompLeavesSpawnerBase", "CompGetGizmosExtra", default(int)).SetDebugOnly();
		PatchingUtilities.PatchPushPopRand("ReGrowthCore.WeatherOverlay_FogMotes:TickOverlay");
		PatchingUtilities.PatchPushPopRand("ReGrowthCore.SplashesUtility:ProcessSplashes");
		PatchingUtilities.PatchPushPopRand("ReGrowthCore.Patch_DoCellSteadyEffects:ColdGlow");
		MpCompat.RegisterLambdaDelegate("ReGrowthCore.AbandonedCamp", "SetupCampGizmo", default(int));
		smartFarmingCompType = AccessTools.TypeByName("ReGrowthCore.MapComponent_SmartFarming");
		compCacheField = AccessTools.Field(AccessTools.TypeByName("ReGrowthCore.ReGrowthCore_SmartFarming"), "compCache");
		growZoneRegistryField = AccessTools.Field(smartFarmingCompType, "growZoneRegistry");
		MP.RegisterSyncWorker<object>(SyncZoneData, AccessTools.TypeByName("ReGrowthCore.ZoneData"));
		MP.RegisterSyncWorker<object>(SyncSmartFarmingComp, smartFarmingCompType);
		MpCompat.RegisterLambdaDelegate("ReGrowthCore.ZoneData", "Init", 0, 1, 3, 5, 6, 8);
		MpCompat.RegisterLambdaDelegate("ReGrowthCore.Patch_GetGizmos", "GetMultiZoneGizmos", 1, 2, 4).SetContext(SyncContext.MapSelected);
		Type type = AccessTools.TypeByName("ReGrowthCore.CaravanCamp");
		HarmonyMethod postfix = new HarmonyMethod(typeof(ReGrowthCore), "PostCaravanCompTick");
		MethodInfo methodInfo = AccessTools.DeclaredMethod(type, "Tick");
		MethodInfo methodInfo2 = AccessTools.DeclaredMethod(type, "TickInterval");
		if (methodInfo != null)
		{
			MpCompat.harmony.Patch(methodInfo, null, postfix);
		}
		if (methodInfo2 != null)
		{
			MpCompat.harmony.Patch(methodInfo2, null, postfix);
		}
	}

	private static void SyncSmartFarmingComp(SyncWorker sync, ref object comp)
	{
		if (sync.isWriting)
		{
			sync.Write(((MapComponent)comp).map);
			return;
		}
		comp = sync.Read<Map>()?.components.FirstOrDefault((MapComponent c) => c.GetType() == smartFarmingCompType);
	}

	private static void SyncZoneData(SyncWorker sync, ref object zoneData)
	{
		if (sync.isWriting)
		{
			Map map = null;
			int obj = -1;
			foreach (DictionaryEntry item in (IDictionary)compCacheField.GetValue(null))
			{
				foreach (DictionaryEntry item2 in (IDictionary)growZoneRegistryField.GetValue(item.Value))
				{
					if (item2.Value == zoneData)
					{
						map = ((MapComponent)item.Value).map;
						obj = (int)item2.Key;
						break;
					}
				}
				if (map != null)
				{
					break;
				}
			}
			sync.Write(map);
			sync.Write(obj);
			return;
		}
		Map map2 = sync.Read<Map>();
		int num = sync.Read<int>();
		if (map2 != null)
		{
			MapComponent obj2 = map2.components.FirstOrDefault((MapComponent c) => c.GetType() == smartFarmingCompType);
			IDictionary dictionary = (IDictionary)growZoneRegistryField.GetValue(obj2);
			zoneData = dictionary[num];
		}
	}

	private static void LatePatch()
	{
		PatchingUtilities.PatchPushPopRand("ReGrowthCore.DevilDust_Tornado:ThrowDevilDustPuff");
	}

	private static void PostCaravanCompTick(MapParent __instance)
	{
		Faction faction = __instance.Faction;
		if (faction != null && faction.IsPlayer && (!__instance.HasMap || __instance.ShouldRemoveMapNow(out var _)))
		{
			__instance.SetFaction(null);
		}
	}
}
