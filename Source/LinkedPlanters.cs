using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("rswallen.linkedplanters")]
public class LinkedPlanters
{
	private static Type gizmoCacheType;

	private static AccessTools.FieldRef<object, Building_PlantGrower> gizmoCacheRootField;

	public LinkedPlanters(ModContentPack mod)
	{
		Type type = AccessTools.TypeByName("LinkedPlanters.GrowerGroup");
		type = (gizmoCacheType = AccessTools.Inner(type, "GizmoCache"));
		gizmoCacheRootField = AccessTools.FieldRefAccess<Building_PlantGrower>(type, "root");
		MP.RegisterSyncWorker<object>(SyncGrowerGroupGizmoCache, type);
		foreach (MethodInfo lambdum in MpMethodUtil.GetLambda(type, null, MethodType.Constructor, new Type[1] { typeof(Building_PlantGrower) }, 0, 2))
		{
			MP.RegisterSyncDelegate(type, lambdum.DeclaringType.Name, lambdum.Name).SetContext(SyncContext.MapSelected);
		}
	}

	private static void SyncGrowerGroupGizmoCache(SyncWorker sync, ref object obj)
	{
		if (sync.isWriting)
		{
			sync.Write(gizmoCacheRootField(obj));
			return;
		}
		obj = Activator.CreateInstance(gizmoCacheType, sync.Read<Building_PlantGrower>());
	}
}
