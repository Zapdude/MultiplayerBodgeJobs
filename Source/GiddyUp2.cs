using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Owlchemist.GiddyUp")]
public class GiddyUp2
{
	private static FastInvokeHandler transferableAdjustTo;

	private static AccessTools.FieldRef<object, Pawn> extendedPawnDataPawn;

	private static FastInvokeHandler getGUData;

	private static AccessTools.FieldRef<object, object> parentClass;

	private static AccessTools.FieldRef<object, TransferableOneWay> transferableField;

	private static AccessTools.FieldRef<object, Area> designatorSelectedArea;

	private static AccessTools.FieldRef<object, string> designatorAreaLabel;

	public GiddyUp2(ModContentPack mod)
	{
		MpCompat.RegisterLambdaDelegate("GiddyUp.Harmony.Patch_PawnGetGizmos", "Postfix", default(int));
		MP.RegisterSyncMethod(AccessTools.TypeByName("GiddyUpRideAndRoll.Harmony.Pawn_GetGizmos"), "PawnEndCurrentJob");
		MpCompat.RegisterLambdaDelegate("GiddyUp.Harmony.FloatMenuUtility", "AddMountingOptions", 0, 1, 2);
		Type type = AccessTools.TypeByName("GiddyUpCaravan.Harmony.Patch_TransferableOneWayWidget");
		MP.RegisterSyncMethod(type, "SelectMountRider");
		MP.RegisterSyncMethod(type, "ClearMountRider");
		MethodInfo lambda = MpMethodUtil.GetLambda(type, "HandleAnimal");
		transferableField = AccessTools.FieldRefAccess<TransferableOneWay>(lambda.DeclaringType, "trad");
		MpCompat.harmony.Patch(lambda, new HarmonyMethod(typeof(GiddyUp2), "WatchTranferableCount"));
		lambda = MpMethodUtil.GetLambda(type, "HandleAnimal", MethodType.Normal, null, 1);
		parentClass = AccessTools.FieldRefAccess<object>(lambda.DeclaringType, "CS$<>8__locals1");
		MpCompat.harmony.Patch(lambda, new HarmonyMethod(typeof(GiddyUp2), "PreSetRider"));
		MP.RegisterSyncMethod(AccessTools.TypeByName("GiddyUpRideAndRoll.PawnColumnWorker_Mountable_Colonists"), "SetValue");
		MP.RegisterSyncMethod(AccessTools.TypeByName("GiddyUpRideAndRoll.PawnColumnWorker_Mountable_Slaves"), "SetValue");
		Type type2 = AccessTools.TypeByName("GiddyUp.ExtendedPawnData");
		extendedPawnDataPawn = AccessTools.FieldRefAccess<Pawn>(type2, "pawn");
		MP.RegisterSyncWorker<object>(SyncExtendedPawnData, type2);
		getGUData = HarmonyLib.MethodInvoker.GetHandler(AccessTools.DeclaredMethod("GiddyUp.StorageUtility:GetGUData"));
		transferableAdjustTo = HarmonyLib.MethodInvoker.GetHandler(AccessTools.DeclaredMethod("Multiplayer.Client.SyncFields:TransferableAdjustTo"));
		type2 = AccessTools.TypeByName("GiddyUp.Designator_GU");
		designatorSelectedArea = AccessTools.FieldRefAccess<Area>(type2, "selectedArea");
		designatorAreaLabel = AccessTools.FieldRefAccess<string>(type2, "areaLabel");
		MP.RegisterSyncWorker<Designator>(SyncGiddyUpDesignator, type2, isImplicit: true, shouldConstruct: true);
	}

	private static void WatchTranferableCount(TransferableOneWay ___trad)
	{
		transferableAdjustTo(null, ___trad);
	}

	private static void PreSetRider(object __instance)
	{
		WatchTranferableCount(transferableField(parentClass(__instance)));
	}

	private static void SyncExtendedPawnData(SyncWorker sync, ref object extendedPawnData)
	{
		if (sync.isWriting)
		{
			sync.Write(extendedPawnDataPawn(extendedPawnData));
			return;
		}
		Pawn pawn = sync.Read<Pawn>();
		extendedPawnData = getGUData(null, pawn);
	}

	private static void SyncGiddyUpDesignator(SyncWorker sync, ref Designator designator)
	{
		if (sync.isWriting)
		{
			sync.Write(designatorSelectedArea(designator));
			sync.Write(designatorAreaLabel(designator));
		}
		else
		{
			designatorSelectedArea(designator) = sync.Read<Area>();
			designatorAreaLabel(designator) = sync.Read<string>();
		}
	}
}
