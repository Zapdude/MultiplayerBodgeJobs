using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("rabiosus.vfautoparking")]
public class AutomaticParking
{
	private static Type parkingControllerCompType;

	private static AccessTools.FieldRef<ThingComp, object> parkingControllerCompControllerField;

	private static AccessTools.FieldRef<object, Pawn> parkingControllerVehicleField;

	public AutomaticParking(ModContentPack mod)
	{
		MpCompatPatchLoader.LoadPatch(this);
		parkingControllerCompControllerField = AccessTools.FieldRefAccess<object>(parkingControllerCompType = AccessTools.TypeByName("VehicleAutoParking.Core.CompParkingController"), "parkingController");
		Type type = AccessTools.TypeByName("VehicleAutoParking.Core.ParkingController");
		parkingControllerVehicleField = AccessTools.FieldRefAccess<Pawn>(type, "vehicle");
		MP.RegisterSyncMethod(type, "SaveParkingPosition");
		MP.RegisterSyncMethod(type, "ResetParkingPosition");
		MP.RegisterSyncMethod(type, "MoveToParkingPosition");
	}

	[MpCompatSyncWorker("VehicleAutoParking.Core.ParkingController")]
	private static void SyncParkingController(SyncWorker sync, ref object controller)
	{
		if (sync.isWriting)
		{
			sync.Write(parkingControllerVehicleField(controller));
			return;
		}
		ThingComp thingComp = sync.Read<Pawn>().AllComps.FirstOrDefault((ThingComp c) => parkingControllerCompType.IsInstanceOfType(c));
		if (thingComp != null)
		{
			controller = parkingControllerCompControllerField(thingComp);
		}
		else
		{
			Log.Error("A vehicle is missing CompParkingController.");
		}
	}
}
