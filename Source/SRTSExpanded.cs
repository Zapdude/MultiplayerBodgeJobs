using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("smashphil.srtsexpanded")]
[MpCompatFor("smashphil.neceros.srtsexpanded")]
[MpCompatFor("Nephlite.Carryalls")]
[MpCompatFor("Azazellz.TransportShuttleStandalone")]
internal class SRTSExpanded
{
	private static FastInvokeHandler tryLaunchMethod;

	private static AccessTools.FieldRef<object, Caravan> caravanField;

	private static ISyncField bombTypeSync;

	private static FastInvokeHandler boardShuttleMethod;

	private static AccessTools.FieldRef<ThingComp, List<Pawn>> tmpAllowedPawnsField;

	private static Type designatorAddToCarryallType;

	private static AccessTools.FieldRef<Designator, ThingComp> designatorLaunchableField;

	public SRTSExpanded(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(() =>
		{
			DelayedPatch(mod.PackageId);
		});
		MpCompat.RegisterLambdaDelegate("SRTS.StartUp", "LaunchAndBombGizmosPassthrough", 3);
		MP.RegisterSyncWorker<Pair<IntVec3, IntVec3>>(SyncIntVec3Pair, typeof(Pair<IntVec3, IntVec3>));
		Type type = AccessTools.TypeByName("SRTS.CompBombFlyer");
		MP.RegisterSyncMethod(type, "TryLaunchBombRun");
		bombTypeSync = MP.RegisterSyncField(type, "bombType");
		foreach (MethodInfo lambdum in MpMethodUtil.GetLambda(type, "CompGetGizmosExtra", MethodType.Normal, null, 1, 2))
		{
			MpCompat.harmony.Patch(lambdum, new HarmonyMethod(typeof(SRTSExpanded), "PreSyncBombType"), new HarmonyMethod(typeof(SRTSExpanded), "PostSyncBombType"));
		}
	}

	private static void DelayedPatch(string modId)
	{
		Type type = AccessTools.TypeByName("SRTS.CompLaunchableSRTS");
		caravanField = AccessTools.FieldRefAccess<Caravan>(type, "carr");
		MethodInfo methodInfo = AccessTools.Method(type, "TryLaunch");
		tryLaunchMethod = HarmonyLib.MethodInvoker.GetHandler(methodInfo);
		MpCompat.harmony.Patch(methodInfo, new HarmonyMethod(typeof(SRTSExpanded), "PreTryLaunch"));
		MP.RegisterSyncMethod(typeof(SRTSExpanded), "SyncedLaunch").ExposeParameter(2);
		if (modId == "Nephlite.Carryalls".ToLower())
		{
			PatchCarryalls();
		}
	}

	private static void PreSyncBombType(ThingComp __instance)
	{
		if (MP.IsInMultiplayer)
		{
			MP.WatchBegin();
			bombTypeSync.Watch(__instance);
		}
	}

	private static void PostSyncBombType()
	{
		if (MP.IsInMultiplayer)
		{
			MP.WatchEnd();
		}
	}

	private static bool PreTryLaunch(ThingComp __instance, int destinationTile, TransportersArrivalAction arrivalAction, Caravan cafr = null)
	{
		if (!MP.InInterface)
		{
			return true;
		}
		Caravan caravanFieldValue = caravanField(__instance);
		SyncedLaunch(__instance, destinationTile, arrivalAction, cafr, caravanFieldValue);
		return false;
	}

	private static void SyncedLaunch(ThingComp compLaunchableSrts, int destinationTile, TransportersArrivalAction arrivalAction, Caravan caravanMethodParameter, Caravan caravanFieldValue)
	{
		caravanField(compLaunchableSrts) = caravanFieldValue;
		tryLaunchMethod(compLaunchableSrts, destinationTile, arrivalAction, caravanMethodParameter);
	}

	private static void SyncIntVec3Pair(SyncWorker sync, ref Pair<IntVec3, IntVec3> pair)
	{
		if (sync.isWriting)
		{
			sync.Write(pair.First);
			sync.Write(pair.Second);
		}
		else
		{
			pair = new Pair<IntVec3, IntVec3>(sync.Read<IntVec3>(), sync.Read<IntVec3>());
		}
	}

	private static void PatchCarryalls()
	{
		Type type = AccessTools.TypeByName("SRTS.CompLaunchableSRTS");
		tmpAllowedPawnsField = AccessTools.FieldRefAccess<List<Pawn>>(type, "tmpAllowedPawns");
		MpCompat.RegisterLambdaMethod(type, "CompGetGizmosExtra", 0, 1);
		MpCompat.RegisterLambdaDelegate(type, "CompFloatMenuOptions", default(int));
		MethodInfo lambda = MpMethodUtil.GetLambda(type, "CompMultiSelectFloatMenuOptions");
		boardShuttleMethod = HarmonyLib.MethodInvoker.GetHandler(lambda);
		MpCompat.harmony.Patch(lambda, new HarmonyMethod(typeof(SRTSExpanded), "PreAddPawns"));
		MP.RegisterSyncMethod(typeof(SRTSExpanded), "SyncedAddPawns");
		type = (designatorAddToCarryallType = AccessTools.TypeByName("SRTS.Designator_AddToCarryall"));
		designatorLaunchableField = AccessTools.FieldRefAccess<ThingComp>(type, "launchable");
		MP.RegisterSyncWorker<Designator>(SyncAddToCarryallDesignator, type);
	}

	private static bool PreAddPawns(ThingComp __instance, List<Pawn> ___tmpAllowedPawns)
	{
		if (!MP.InInterface)
		{
			return true;
		}
		SyncedAddPawns(__instance, ___tmpAllowedPawns);
		return false;
	}

	private static void SyncedAddPawns(ThingComp comp, List<Pawn> pawns)
	{
		ref List<Pawn> reference = ref tmpAllowedPawnsField(comp);
		List<Pawn> list = pawns;
		List<Pawn> list2 = reference;
		reference = list;
		pawns = list2;
		boardShuttleMethod(comp);
		reference = pawns;
	}

	private static void SyncAddToCarryallDesignator(SyncWorker sync, ref Designator designator)
	{
		if (sync.isWriting)
		{
			sync.Write(designatorLaunchableField(designator));
			return;
		}
		designator = (Designator)Activator.CreateInstance(designatorAddToCarryallType, sync.Read<ThingComp>());
	}
}
