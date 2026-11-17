using System;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat.Mods;

[MpCompatFor("Aelanna.EccentricTech.Furniture")]
internal class EccentricTechFurniture
{
	private static Type typeCompFabricator;

	private static ISyncField targetCount;

	private static ISyncField pushProductsToNetwork;

	public EccentricTechFurniture(ModContentPack mod)
	{
		typeCompFabricator = AccessTools.TypeByName("EccentricFurniture.CompFabricator");
		MP.RegisterSyncWorker<ThingComp>(SyncCompFabricator, typeCompFabricator);
		targetCount = MP.RegisterSyncField(typeCompFabricator, "targetCount");
		pushProductsToNetwork = MP.RegisterSyncField(typeCompFabricator, "pushProductsToNetwork");
		Type type = AccessTools.TypeByName("EccentricFurniture.ITab_Fabricator");
		MpCompat.harmony.Patch(AccessTools.Method(type, "FillTab"), new HarmonyMethod(new Action<ITab>(PreFillTab)), new HarmonyMethod(new Action(PostFillTab)));
		MP.RegisterSyncMethod(typeCompFabricator, "SetActiveRecipe");
		MpCompat.RegisterLambdaDelegate(type, "DoRepeatModeFloatMenu", 0, 1, 2);
		MpCompat.RegisterLambdaDelegate(type, "DoConfirmCancelDialog", default(int));
		Type parentType = AccessTools.TypeByName("EccentricFurniture.Building_CeilingLight");
		MpCompat.RegisterLambdaMethod(parentType, "GetGizmos", 1);
		MpCompat.RegisterLambdaMethod(parentType, "DoAdjustHeightDialog", 1);
		MpCompat.RegisterLambdaMethod(AccessTools.TypeByName("EccentricFurniture.Building_SmartTable"), "GetGizmos", 1);
		MpCompat.RegisterLambdaMethod(AccessTools.TypeByName("EccentricFurniture.Building_StorageWithStackLimit"), "CreateCachedGizmos", 2);
	}

	private static void SyncCompFabricator(SyncWorker sync, ref ThingComp comp)
	{
		if (sync.isWriting)
		{
			sync.Write(comp.parent);
			return;
		}
		foreach (ThingComp allComp in sync.Read<ThingWithComps>().AllComps)
		{
			if (allComp.GetType() == typeCompFabricator)
			{
				comp = allComp;
			}
		}
	}

	private static void PreFillTab(ITab __instance)
	{
		if (!MP.IsInMultiplayer)
		{
			return;
		}
		ThingWithComps obj = __instance.SelThing as ThingWithComps;
		ThingComp target = null;
		foreach (ThingComp allComp in obj.AllComps)
		{
			if (allComp.GetType() == typeCompFabricator)
			{
				target = allComp;
			}
		}
		MP.WatchBegin();
		targetCount.Watch(target);
		pushProductsToNetwork.Watch(target);
	}

	private static void PostFillTab()
	{
		if (MP.IsInMultiplayer)
		{
			MP.WatchEnd();
		}
	}
}
