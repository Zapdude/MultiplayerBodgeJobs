using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EccentricDefenseGrid;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Aelanna.EccentricTech.DefenseGrid")]
internal class EccentricTechDefenseGrid
{
	private static ISyncField autoReload;

	private static ISyncField ordnanceDef;

	public EccentricTechDefenseGrid(ModContentPack mod)
	{
		Type type = AccessTools.TypeByName("EccentricProjectiles.EffectMapComponent");
		PatchingUtilities.PatchPushPopRand((IEnumerable<MethodBase>)new MethodInfo[2]
		{
			AccessTools.Method(type, "MapComponentTick"),
			AccessTools.Method(type, "CreateEffect")
		}, (HarmonyMethod)null);
		Type type2 = AccessTools.TypeByName("EccentricDefenseGrid.CompArtilleryDesignator");
		MP.RegisterSyncMethod(type2, "NextDesignationMode");
		MP.RegisterSyncMethod(type2, "SetDesignationMode");
		MP.RegisterSyncMethod(type2, "SetOrdnanceIndex");
		MpCompat.RegisterLambdaDelegate(type2, "DoArtilleryTargeting", new int[1]);
		type2 = AccessTools.TypeByName("EccentricDefenseGrid.DefenseGridNetwork");
		MP.RegisterSyncMethod(type2, "NextDesignationMode");
		MP.RegisterSyncMethod(type2, "SetDesignationMode");
		MP.RegisterSyncMethod(type2, "SetSelectedOrdnance");
		MpCompat.RegisterLambdaDelegate(type2, "DoArtilleryTargeting", new int[1]);
		MP.RegisterSyncWorker<object>(SyncDefenseGridNetwork, type2);
		type2 = AccessTools.TypeByName("EccentricDefenseGrid.OrdnanceCount");
		MP.RegisterSyncWorker<object>(SyncOrdnanceCount, type2, isImplicit: false, shouldConstruct: true);
		type2 = AccessTools.TypeByName("EccentricDefenseGrid.DefenseGridMapComponent");
		MP.RegisterSyncMethod(type2, "RecountOrdnance");
		type2 = AccessTools.TypeByName("EccentricDefenseGrid.CompArtilleryMissileLauncher");
		MpCompat.RegisterLambdaMethod(type2, "CompGetGizmosExtra", new int[2] { 0, 1 });
		type2 = AccessTools.TypeByName("EccentricDefenseGrid.CompDefenseProjector");
		MP.RegisterSyncMethod(type2, "ApplyRadius").SetContext(SyncContext.MapSelected);
		MP.RegisterSyncMethod(type2, "ApplyColor").SetContext(SyncContext.MapSelected);
		Extensions.SetDebugOnly((IEnumerable<ISyncMethod>)MpCompat.RegisterLambdaMethod(type2, "CompGetGizmosExtra", new int[1] { 1 }));
		type2 = AccessTools.TypeByName("EccentricDefenseGrid.CompDefenseGenerator");
		Extensions.SetDebugOnly(Enumerable.TakeLast<ISyncMethod>((IEnumerable<ISyncMethod>)MpCompat.RegisterLambdaMethod(type2, "CompGetGizmosExtra", new int[7] { 2, 4, 6, 8, 10, 11, 12 }), 2));
		type2 = AccessTools.TypeByName("EccentricDefenseGrid.CompDefenseHeatsink");
		Extensions.SetDebugOnly((IEnumerable<ISyncMethod>)MpCompat.RegisterLambdaMethod(type2, "CompGetGizmosExtra", new int[3] { 0, 1, 2 }));
		Type targetType = AccessTools.TypeByName("EccentricDefenseGrid.OrdnanceSlot");
		MP.RegisterSyncWorker<OrdnanceSlot>(SyncOrdnanceSlot, targetType);
		Type type3 = AccessTools.TypeByName("EccentricDefenseGrid.ITab_OrdnanceStorage");
		MpCompat.harmony.Patch(AccessTools.Method(type3, "FillTab"), new HarmonyMethod(typeof(EccentricTechDefenseGrid), "PreFillTab"), new HarmonyMethod(typeof(EccentricTechDefenseGrid), "PostFillTab"));
		Extensions.SetContext((IEnumerable<ISyncDelegate>)MpCompat.RegisterLambdaDelegate(type3, "DrawOrdnance_GenerateMenu", new int[2] { 0, 1 }), SyncContext.MapSelected);
		type3 = AccessTools.TypeByName("EccentricDefenseGrid.OrdnanceSlot");
		autoReload = MP.RegisterSyncField(type3, "autoReload");
		ordnanceDef = MP.RegisterSyncField(type3, "def");
		type3 = AccessTools.TypeByName("EccentricDefenseGrid.CompArtilleryMissileLauncher");
		MP.RegisterSyncWorker<CompArtilleryMissileLauncher>(SyncCompArtilleryMissileLauncher, type3);
	}

	private static void PreFillTab(ITab __instance, ref CompArtilleryMissileLauncher __state)
	{
		if (MP.IsInMultiplayer)
		{
			Building building = __instance.SelThing as Building;
			__state = ((ThingWithComps)building).GetComp<CompArtilleryMissileLauncher>();
			MP.WatchBegin();
			__state.slots.ForEach((OrdnanceSlot slot) =>
			{
				autoReload.Watch(slot);
				ordnanceDef.Watch(slot);
			});
		}
	}

	private static void PostFillTab(CompArtilleryMissileLauncher __state)
	{
		if (MP.IsInMultiplayer)
		{
			MP.WatchEnd();
		}
	}

	private static void SyncOrdnanceSlot(SyncWorker sync, ref OrdnanceSlot obj)
	{
		if (sync.isWriting)
		{
			sync.Write(obj == null);
			if (obj == null)
			{
				return;
			}
		}
		else if (sync.Read<bool>())
		{
			obj = null;
			return;
		}
		Thing singleSelectedThing = Find.Selector.SingleSelectedThing;
		CompArtilleryMissileLauncher val = ((singleSelectedThing != null) ? singleSelectedThing.TryGetComp<CompArtilleryMissileLauncher>() : null);
		if (val != null)
		{
			if (sync.isWriting)
			{
				sync.Write(val.slots.IndexOf(obj));
				return;
			}
			int num = sync.Read<int>();
			if (num != -1)
			{
				obj = val.slots[num];
				return;
			}
		}
		if (sync.isWriting)
		{
			sync.Write(obj.autoReload);
			sync.Write((ThingWithComps)(object)obj.ordnance);
			sync.Write(((Def)(object)obj.def).defName);
		}
		else
		{
			obj.autoReload = sync.Read<bool>();
			OrdnanceSlot obj2 = obj;
			ThingWithComps thingWithComps = sync.Read<ThingWithComps>();
			obj2.ordnance = (Ordnance)(object)((thingWithComps is Ordnance) ? thingWithComps : null);
			obj.def = DefDatabase<OrdnanceDef>.GetNamed(sync.Read<string>());
		}
	}

	private static void SyncOrdnanceCount(SyncWorker sync, ref object ordnanceCount)
	{
		object obj = ordnanceCount;
		OrdnanceCount val = (OrdnanceCount)((obj is OrdnanceCount) ? obj : null);
		if (sync.isWriting)
		{
			sync.Write(((Def)(object)val.def).defName);
			sync.Write(val.count);
		}
		else
		{
			val.def = DefDatabase<OrdnanceDef>.GetNamed(sync.Read<string>());
			val.count = sync.Read<int>();
		}
	}

	private static void SyncDefenseGridNetwork(SyncWorker sync, ref object network)
	{
		object obj = network;
		DefenseGridNetwork val = (DefenseGridNetwork)((obj is DefenseGridNetwork) ? obj : null);
		if (sync.isWriting)
		{
			sync.Write(((MapComponent)(object)val.mapComponent).map);
			sync.Write(val.id);
			return;
		}
		Map map = sync.Read<Map>();
		int id = sync.Read<int>();
		network = map.GetComponent<DefenseGridMapComponent>().Networks.Find((DefenseGridNetwork net) => net.id == id);
	}

	private static void SyncCompArtilleryMissileLauncher(SyncWorker sync, ref CompArtilleryMissileLauncher compArtilleryMissileLauncher)
	{
		if (sync.isWriting)
		{
			sync.Write(((ThingComp)(object)compArtilleryMissileLauncher).parent);
		}
		else
		{
			compArtilleryMissileLauncher = sync.Read<ThingWithComps>().GetComp<CompArtilleryMissileLauncher>();
		}
	}
}
