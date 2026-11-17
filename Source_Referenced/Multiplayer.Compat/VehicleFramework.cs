using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using JetBrains.Annotations;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using SmashTools;
using SmashTools.Targeting;
using UnityEngine;
using Vehicles;
using Vehicles.Rendering;
using Vehicles.World;
using Verse;
using Verse.Sound;

namespace Multiplayer.Compat;

[MpCompatFor("SmashPhil.VehicleFramework")]
public class VehicleFramework
{
	[MpCompatRequireMod("SmashPhil.VehicleFramework")]
	private class LoadVehicleCargoSession : ExposableSession, ISessionWithTransferables, ISessionWithCreationRestrictions
	{
		public static LoadVehicleCargoSession drawingSession;

		public static bool allowedToRecacheTransferables;

		private VehiclePawn vehicle;

		public List<TransferableOneWay> transferables = new List<TransferableOneWay>();

		public bool uiDirty;

		public bool widgetDirty;

		public override Map Map => ((Thing)(object)vehicle).Map;

		[UsedImplicitly]
		public LoadVehicleCargoSession(Map map)
			: base(map)
		{
		}

		public LoadVehicleCargoSession(Map map, VehiclePawn vehicle)
			: base(map)
		{
			this.vehicle = vehicle;
			AddItems();
		}

		public void AddItems()
		{
			//IL_0006: Unknown result type (might be due to invalid IL or missing references)
			//IL_000c: Expected Obj, but got Unknown
			Dialog_LoadCargo val = new Dialog_LoadCargo(vehicle);
			try
			{
				allowedToRecacheTransferables = true;
				uiDirty = true;
				widgetDirty = true;
				val.CalculateAndRecacheTransferables();
				transferables = val.transferables;
			}
			finally
			{
				allowedToRecacheTransferables = false;
			}
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_References.Look<VehiclePawn>(ref vehicle, "vehicle", false);
			Scribe_Collections.Look(ref transferables, "transferables", LookMode.Deep);
		}

		public override bool IsCurrentlyPausing(Map map)
		{
			return map == Map;
		}

		private void OpenWindow(bool sound = true)
		{
			Dialog_LoadCargo val = PrepareDummyDialog();
			if (!sound)
			{
				((Window)(object)val).soundAppear = null;
			}
			Find.WindowStack.Add((Window)(object)val);
			uiDirty = true;
			widgetDirty = true;
		}

		private Dialog_LoadCargo PrepareDummyDialog()
		{
			//IL_0006: Unknown result type (might be due to invalid IL or missing references)
			//IL_000b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0018: Expected Obj, but got Unknown
			return new Dialog_LoadCargo(vehicle)
			{
				transferables = transferables
			};
		}

		public void Accept()
		{
			vehicle.cargoToLoad = transferables.Where((TransferableOneWay t) => t.CountToTransfer > 0).ToList();
			ComponentCache.GetCachedMapComponent<VehicleReservationManager>(((Thing)(object)vehicle).Map).RegisterLister(vehicle, "LoadVehicle");
			Remove();
		}

		public void Reset()
		{
			SoundDefOf.Tick_Low.PlayOneShotOnCamera();
			transferables.ForEach((TransferableOneWay t) =>
			{
				t.CountToTransfer = 0;
			});
			uiDirty = true;
		}

		public void PackInstantly()
		{
			SoundDefOf.Tick_High.PlayOneShotOnCamera();
			foreach (TransferableOneWay transferable in transferables)
			{
				List<Thing> things = transferable.things;
				int countToTransfer = transferable.CountToTransfer;
				TransferableUtility.Transfer(things, countToTransfer, (Thing t, IThingHolder _) =>
				{
					vehicle.AddOrTransfer(t);
				});
			}
			Remove();
		}

		public void SetToSendEverything()
		{
			PrepareDummyDialog().SetToSendEverything();
			uiDirty = true;
		}

		public void Remove()
		{
			MP.GetLocalSessionManager(Map).RemoveSession(this);
		}

		public static bool TryOpenLoadVehicleCargoDialog(VehiclePawn vehicle)
		{
			if (((Thing)(object)vehicle)?.Map == null)
			{
				return false;
			}
			LoadVehicleCargoSession firstOfType = MP.GetLocalSessionManager(((Thing)(object)vehicle).Map).GetFirstOfType<LoadVehicleCargoSession>();
			if (firstOfType == null)
			{
				return false;
			}
			firstOfType.OpenWindow();
			return true;
		}

		public static void CreateLoadVehicleCargoSession(VehiclePawn vehicle)
		{
			if (vehicle == null)
			{
				Log.Error("Trying to create LoadVehicleCargoSession for a null vehicle.");
				return;
			}
			if (((Thing)(object)vehicle).Map == null)
			{
				Log.Error(string.Format("Trying to create {0} for a vehicle with null map. Vehicle={1}", "LoadVehicleCargoSession", vehicle));
				return;
			}
			ISessionManager localSessionManager = MP.GetLocalSessionManager(((Thing)(object)vehicle).Map);
			LoadVehicleCargoSession loadVehicleCargoSession = localSessionManager.GetFirstOfType<LoadVehicleCargoSession>();
			if (loadVehicleCargoSession == null)
			{
				loadVehicleCargoSession = new LoadVehicleCargoSession(((Thing)(object)vehicle).Map, vehicle);
				if (!localSessionManager.AddSession(loadVehicleCargoSession))
				{
					loadVehicleCargoSession = null;
				}
			}
			if (loadVehicleCargoSession == null)
			{
				Log.Error("Couldn't get or create LoadVehicleCargoSession");
			}
			else if (MP.IsExecutingSyncCommandIssuedBySelf)
			{
				loadVehicleCargoSession.OpenWindow();
			}
		}

		public override FloatMenuOption GetBlockingWindowOptions(ColonistBar.Entry entry)
		{
			return new FloatMenuOption("MpVehicleCargoLoadingSession".Translate(), () =>
			{
				Session.SwitchToMapOrWorld(Map);
				OpenWindow();
			});
		}

		public Transferable GetTransferableByThingId(int thingId)
		{
			return transferables.Find((TransferableOneWay tr) => tr.things.Any((Thing t) => t.thingIDNumber == thingId));
		}

		public void Notify_CountChanged(Transferable tr)
		{
			uiDirty = true;
		}

		public bool CanExistWith(Session other)
		{
			return !(other is LoadVehicleCargoSession);
		}
	}

	[MpCompatRequireMod("SmashPhil.VehicleFramework")]
	private class FlyingVehicleTargetedLandingSession : ExposableSession, ISessionWithCreationRestrictions
	{
		private List<VehiclePawn> vehicles = new List<VehiclePawn>();

		public override Map Map { get; }

		public override bool IsSessionValid => !vehicles.NullOrEmpty();

		private FlyingVehicleTargetedLandingSession(Map map)
			: base(map)
		{
			Map = map;
		}

		public override bool IsCurrentlyPausing(Map map)
		{
			return map == Map;
		}

		public override FloatMenuOption GetBlockingWindowOptions(ColonistBar.Entry entry)
		{
			if (entry.map != Map)
			{
				return null;
			}
			return new FloatMenuOption("MpVehicleAerialLandingSession".Translate(), () =>
			{
				if (!IsSessionValid)
				{
					Remove();
				}
				else
				{
					Session.SwitchToMapOrWorld(Map);
					if (vehicles.Count == 1)
					{
						StartVehicleLandingTargeter(vehicles[0]);
					}
					else
					{
						SetupVehicleListFloatMenu();
					}
				}
			});
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Deep.Look(ref vehicles, "vehicles", this);
		}

		public bool CanExistWith(Session other)
		{
			return !(other is FlyingVehicleTargetedLandingSession);
		}

		public void VehicleArrival(VehiclePawn vehicle, LocalTargetInfo target, Rot4 rot)
		{
			VehicleArrivalById(((Thing)(object)vehicle).thingIDNumber, target, rot);
		}

		public void VehicleArrivalById(int vehicleId, LocalTargetInfo target, Rot4 rot)
		{
			//IL_0075: Unknown result type (might be due to invalid IL or missing references)
			//IL_007a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0097: Expected Obj, but got Unknown
			VehiclePawn val = vehicles.Find((VehiclePawn v) => ((Thing)(object)v).thingIDNumber == vehicleId);
			if (val == null)
			{
				return;
			}
			if (((Thing)(object)val).Spawned)
			{
				vehicles.Remove(val);
				Log.Error(string.Format("{0} contained a vehicle that it should no longer contain, sessionID={1}, vehicle={2}", "FlyingVehicleTargetedLandingSession", SessionId, val));
				return;
			}
			VehicleSkyfaller_Arriving val2 = (VehicleSkyfaller_Arriving)ThingMaker.MakeThing(val.CompVehicleLauncher.Props.skyfallerIncoming);
			((VehicleSkyfaller)val2).vehicle = val;
			GenSpawn.Spawn((Thing)val2, target.Cell, Map, rot);
			vehicles.Remove(val);
			if (((BaseTargeter)LandingTargeter.Instance).vehicle == val)
			{
				((BaseTargeter)LandingTargeter.Instance).StopTargeting();
			}
			if (!IsSessionValid)
			{
				Remove();
			}
		}

		public void Remove()
		{
			MP.GetLocalSessionManager(Map).RemoveSession(this);
		}

		public static void HandleTargetedVehicleArrival(VehiclePawn vehicle, Map map)
		{
			MP.GetLocalSessionManager(map).GetOrAddSession(new FlyingVehicleTargetedLandingSession(map)).vehicles.AddDistinct(vehicle);
		}

		private void SetupVehicleListFloatMenu()
		{
			List<FloatMenuOption> list = new List<FloatMenuOption>();
			foreach (VehiclePawn vehicle in vehicles)
			{
				string label = ((!vehicle.Nameable || ((Pawn)(object)vehicle).Name == null) ? ((string)((Def)(object)vehicle.VehicleDef).LabelCap) : $"{((Def)(object)vehicle.VehicleDef).LabelCap} - {((Pawn)(object)vehicle).Name}");
				list.Add(new FloatMenuOption(label, () =>
				{
					StartVehicleLandingTargeter(vehicle);
				}));
			}
			Find.WindowStack.Add(new FloatMenu(list, "MpVehiclesWaitingToLand"));
		}

		private void StartVehicleLandingTargeter(VehiclePawn vehicle)
		{
			bool flag = false;
			if (((ThingDef)(object)vehicle.VehicleDef).rotatable)
			{
				LaunchProtocolProperties landingProperties = vehicle.CompVehicleLauncher.launchProtocol.LandingProperties;
				flag = landingProperties == null || !landingProperties.forcedRotation.HasValue;
			}
			LandingTargeter.Instance.BeginTargeting(vehicle, Map, (Action<LocalTargetInfo, Rot4>)((LocalTargetInfo target, Rot4 rot) =>
			{
				VehicleArrival(vehicle, target, rot);
			}), (Func<LocalTargetInfo, bool>)null, (Action)null, (Action)null, (Texture2D)null, flag, false);
		}
	}

	private static Type mpTransferableReferenceType;

	private static ISyncField syncTradeableCount;

	private static bool shouldSyncInInterface = false;

	private static ISyncField showAllCargoItemsField;

	private static AccessTools.FieldRef<object, VehiclePawn> vehiclePawnInnerClassParentField;

	private static RoadType localRoadType = (RoadType)1;

	private static readonly AccessTools.FieldRef<Gizmo_RefuelableFuelTravel, CompFueledTravel> fuelGizmoRefuelableField = AccessTools.FieldRefAccess<Gizmo_RefuelableFuelTravel, CompFueledTravel>("refuelable");

	private static readonly AccessTools.FieldRef<VehicleArrivalAction, VehiclePawn> arrivalActionVehicleField = AccessTools.FieldRefAccess<VehicleArrivalAction, VehiclePawn>("vehicle");

	public VehicleFramework(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
	}

	private static void LatePatch()
	{
		MpCompatPatchLoader.LoadPatch<VehicleFramework>();
		PatchingUtilities.PatchLongEventMarkers();
		MethodInfo[] source = new MethodInfo[3]
		{
			AccessTools.DeclaredMethod(typeof(PathingHelper), "RecalculatePerceivedPathCostAt"),
			AccessTools.DeclaredMethod(typeof(PathingHelper), "ThingAffectingRegionsOrientationChanged"),
			AccessTools.DeclaredMethod(typeof(PathingHelper), "ThingAffectingRegionsStateChange")
		};
		HarmonyMethod transpiler = new HarmonyMethod(typeof(VehicleFramework), "ReplaceThreadAvailable");
		foreach (MethodInfo item in source.Where((MethodInfo m) => m != null))
		{
			MpCompat.harmony.Patch(item, null, null, transpiler);
		}
		MpCompat.RegisterLambdaMethod(typeof(Building_Artillery), "GetGizmos", new int[2] { 0, 1 });
		MP.RegisterSyncMethod(typeof(VehiclePawn), "DisembarkAll");
		MpCompat.RegisterLambdaDelegate(typeof(VehiclePawn), "GetGizmos", new int[5] { 5, 8, 10, 14, 15 });
		MpCompat.harmony.Patch(MpMethodUtil.GetLambda(typeof(VehiclePawn), "GetGizmos", MethodType.Normal, (Type[])null, 13), new HarmonyMethod(typeof(VehicleFramework), "PreDisembarkSinglePawn"));
		MP.RegisterSyncMethod(typeof(VehicleFramework), "SyncedDisembarkPawn");
		MpCompat.RegisterLambdaMethod(typeof(VehicleIgnitionController), "GetGizmos", new int[1] { 1 });
		MP.RegisterSyncMethod(typeof(CompFueledTravel), "TargetFuelPercent");
		MP.RegisterSyncMethod(typeof(CompFueledTravel), "Refuel", new SyncType[1] { typeof(List<Thing>) });
		MP.RegisterSyncMethod(typeof(CompFueledTravel), "Refuel", new SyncType[1] { typeof(float) });
		MP.RegisterSyncMethod(typeof(CompFueledTravel), "ConsumeFuelFromInventory");
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(Gizmo_RefuelableFuelTravel), "ToggleAutoRefuel"), new HarmonyMethod(typeof(VehicleFramework), "PreToggleFuelSwitch"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(Gizmo_RefuelableFuelTravel), "ToggleCharging"), new HarmonyMethod(typeof(VehicleFramework), "PreToggleFuelSwitch"));
		MP.RegisterSyncMethod(typeof(VehicleFramework), "SyncedToggleFuelSwitch");
		Extensions.SetDebugOnly((IEnumerable<ISyncMethod>)MpCompat.RegisterLambdaMethod(typeof(CompFueledTravel), "DevModeGizmos", new int[3] { 0, 1, 2 }));
		Extensions.SetDebugOnly((IEnumerable<ISyncMethod>)MpCompat.RegisterLambdaMethod(typeof(CompFueledTravel), "CompCaravanGizmos", new int[2] { 0, 1 }));
		MP.RegisterSyncMethod(typeof(CompVehicleTurrets), "SetQuotaLevel");
		Extensions.SetDebugOnly((IEnumerable<ISyncDelegate>)MpCompat.RegisterLambdaDelegate(typeof(CompVehicleTurrets), "CompGetGizmosExtra", new int[1]));
		MP.RegisterSyncMethod(typeof(Command_CooldownAction), "FireTurret");
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(Command_CooldownAction), "GizmoOnGUI"), null, null, new HarmonyMethod(typeof(VehicleFramework), "ReplaceSetTargetCall"));
		MethodInfo lambda = MpMethodUtil.GetLambda(typeof(Command_TargeterCooldownAction), "FireTurret", MethodType.Normal, (Type[])null, 0);
		MP.RegisterSyncDelegate(typeof(Command_TargeterCooldownAction), lambda.DeclaringType.Name, lambda.Name);
		MpCompat.harmony.Patch(lambda, new HarmonyMethod(typeof(VehicleFramework), "PreSetTurretTarget"));
		PatchingUtilities.PatchCancelInInterface(new MethodBase[1] { AccessTools.DeclaredMethod(typeof(VehicleTurret), "StartTicking") });
		PatchingUtilities.PatchCancelInInterface(new MethodBase[1] { AccessTools.DeclaredMethod(typeof(VehicleTurret), "AlignToAngleRestricted") });
		MP.RegisterSyncMethod(typeof(VehicleTurret), "CycleFireMode");
		foreach (Type item2 in typeof(VehicleTurret).AllSubclasses().Concat(typeof(VehicleTurret)))
		{
			lambda = AccessTools.DeclaredMethod(item2, "SetTarget");
			if (lambda != null)
			{
				MP.RegisterSyncMethod(lambda);
				MpCompat.harmony.Patch(lambda, new HarmonyMethod(typeof(VehicleFramework), "CancelTurretSetTargetSync"));
			}
			MethodInfo methodInfo = AccessTools.DeclaredMethod(item2, "Reload", Array.Empty<Type>());
			if (methodInfo != null)
			{
				MP.RegisterSyncMethod(methodInfo);
			}
			MethodInfo methodInfo2 = AccessTools.DeclaredMethod(item2, "Reload", new Type[2]
			{
				typeof(ThingDef),
				typeof(bool)
			});
			if (methodInfo2 != null)
			{
				MP.RegisterSyncMethod(methodInfo2);
			}
			TrySyncDeclaredMethod(item2, "TryClearChamber");
			TrySyncDeclaredMethod(item2, "SwitchAutoTarget");
		}
		PatchingUtilities.PatchCancelInInterface(new MethodBase[1] { AccessTools.DeclaredMethod(typeof(VehicleTurret), "UpdateRotationLock") });
		MpCompat.RegisterLambdaDelegate(typeof(VehiclePawn), "GetFloatMenuOptions", new int[1]);
		PatchingUtilities.PatchPushPopRand((IEnumerable<string>)new string[2] { "Vehicles.Verb_ShootRealistic:InitTurretMotes", "Vehicles.VehicleTurret:InitTurretMotes" }, (HarmonyMethod)null);
		foreach (MethodInfo item3 in from m in AccessTools.GetDeclaredMethods(typeof(LaunchProtocol))
			where m.Name == "ThrowFleck"
			select m)
		{
			PatchingUtilities.PatchPushPopRand((MethodBase)item3, (HarmonyMethod)null);
		}
		MethodInfo methodInfo3 = AccessTools.DeclaredMethod(typeof(VehiclePawn), "DrawAt", new Type[3]
		{
			typeof(Vector3).MakeByRefType(),
			typeof(Rot8),
			typeof(float)
		});
		if (methodInfo3 != null)
		{
			MpCompat.harmony.Patch(methodInfo3, new HarmonyMethod(typeof(VehicleFramework), "PreRenderPawnInternal"), null, null, new HarmonyMethod(typeof(VehicleFramework), "PostRenderPawnInternal"));
		}
		MpCompat.RegisterLambdaMethod("Vehicles.VehiclePawn", "ChangeColor", new int[1]);
		MP.RegisterSyncMethod(typeof(LoadVehicleCargoSession), "CreateLoadVehicleCargoSession");
		MP.RegisterSyncMethod(typeof(LoadVehicleCargoSession), "Accept");
		MP.RegisterSyncMethod(typeof(LoadVehicleCargoSession), "Reset");
		MP.RegisterSyncMethod(typeof(LoadVehicleCargoSession), "PackInstantly");
		MP.RegisterSyncMethod(typeof(LoadVehicleCargoSession), "SetToSendEverything");
		MP.RegisterSyncMethod(typeof(LoadVehicleCargoSession), "Remove");
		showAllCargoItemsField = MP.RegisterSyncField(typeof(VehiclesModSettings), "showAllCargoItems").PostApply(PostShowAllCargoItemsChanged);
		MP.RegisterSyncWorker<VehiclesModSettings>(SyncVehicleSettings);
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(Dialog_LoadCargo), "DoWindowContents"), new HarmonyMethod(typeof(VehicleFramework), "PreDrawLoadCargo"), null, null, new HarmonyMethod(typeof(VehicleFramework), "FinalizeDrawLoadCargo"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(Dialog_LoadCargo), "SetToSendEverything"), new HarmonyMethod(typeof(VehicleFramework), "PreLoadCargoSetToSendEverything"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(Dialog_LoadCargo), "BottomButtons"), null, null, new HarmonyMethod(typeof(VehicleFramework), "ReplaceButtonsTranspiler"));
		lambda = MpMethodUtil.GetLambda(typeof(VehiclePawn), "GetGizmos", MethodType.Normal, (Type[])null, 6);
		vehiclePawnInnerClassParentField = AccessTools.FieldRefAccess<VehiclePawn>(lambda.DeclaringType, "<>4__this");
		MpCompat.harmony.Patch(lambda, new HarmonyMethod(typeof(VehicleFramework), "PreLoadCargoDialog"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(Dialog_LoadCargo), "CalculateAndRecacheTransferables"), new HarmonyMethod(typeof(VehicleFramework), "PreLoadCargoCalculateAndRecache"));
		Type[] array = new Type[1] { typeof(Dialog_LoadCargo) };
		foreach (Type type in array)
		{
			MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, "DoWindowContents", new Type[1] { typeof(Rect) }), null, new HarmonyMethod(typeof(VehicleFramework), "InsertSwitchToMap"));
		}
		foreach (Type item4 in typeof(ITab_Airdrop_Container).AllSubclasses().Concat(typeof(ITab_Airdrop_Container)))
		{
			TrySyncDeclaredMethod(item4, "InterfaceDrop")?.SetContext(SyncContext.MapSelected);
			TrySyncDeclaredMethod(item4, "InterfaceDropAll")?.SetContext(SyncContext.MapSelected);
		}
		lambda = AccessTools.DeclaredMethod(typeof(VehicleTabHelper_Passenger), "HandleDragEvent");
		MpCompat.harmony.Patch(lambda, new HarmonyMethod(typeof(VehicleFramework), "PreHandleDragEvent"));
		MP.RegisterSyncMethod(typeof(VehicleFramework), "SyncedHandleDragEvent");
		Type[] array2 = new Type[2]
		{
			typeof(Thing),
			typeof(AerialVehicleInFlight)
		};
		Type[] array3 = new Type[2]
		{
			typeof(TransferableImmutable),
			typeof(AerialVehicleInFlight)
		};
		lambda = MpMethodUtil.GetLambda(typeof(AerialVehicleAbandonOrBanishHelper), "TryAbandonOrBanishViaInterface", MethodType.Normal, array2, 0);
		MP.RegisterSyncDelegate(typeof(AerialVehicleAbandonOrBanishHelper), lambda.DeclaringType.Name, lambda.Name);
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(AerialVehicleAbandonOrBanishHelper), "TryAbandonOrBanishViaInterface", array2), null, null, new HarmonyMethod(typeof(VehicleFramework), "ReplaceVanillaBanishDialog"));
		MP.RegisterSyncMethod(typeof(VehicleFramework), "SyncedBanishPawn");
		lambda = MpMethodUtil.GetLambda(typeof(AerialVehicleAbandonOrBanishHelper), "TryAbandonOrBanishViaInterface", MethodType.Normal, array3, 0);
		MP.RegisterSyncDelegate(typeof(AerialVehicleAbandonOrBanishHelper), lambda.DeclaringType.Name, lambda.Name);
		lambda = MpMethodUtil.GetLambda(typeof(AerialVehicleAbandonOrBanishHelper), "TryAbandonSpecificCountViaInterface", MethodType.Normal, array2, 0);
		MP.RegisterSyncDelegate(typeof(AerialVehicleAbandonOrBanishHelper), lambda.DeclaringType.Name, lambda.Name);
		lambda = MpMethodUtil.GetLambda(typeof(AerialVehicleAbandonOrBanishHelper), "TryAbandonSpecificCountViaInterface", MethodType.Normal, array3, 0);
		MP.RegisterSyncDelegate(typeof(AerialVehicleAbandonOrBanishHelper), lambda.DeclaringType.Name, lambda.Name);
		Serializer<UpgradeNode, (UpgradeTreeDef, string)> serializer = Serializer.New((UpgradeNode upgrade, object target, object[] _) =>
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			return (target: ((CompUpgradeTree)target).Props.def, key: upgrade.key);
		}, ((UpgradeTreeDef target, string key) tuple) => tuple.target.GetNode(tuple.key));
		MP.RegisterSyncMethod(typeof(CompUpgradeTree), "StartUnlock").TransformArgument(0, serializer);
		MP.RegisterSyncMethod(typeof(CompUpgradeTree), "RemoveUnlock").TransformArgument(0, serializer);
		MP.RegisterSyncMethod(typeof(CompUpgradeTree), "ClearUpgrade");
		MP.RegisterSyncMethod(typeof(CompUpgradeTree), "FinishUnlock").TransformArgument(0, serializer).SetDebugOnly();
		MP.RegisterSyncMethod(typeof(CompUpgradeTree), "ResetUnlock").TransformArgument(0, serializer).SetDebugOnly();
		MP.RegisterSyncWorker<LaunchProtocol>(SyncLaunchProtocol, null, isImplicit: true);
		MP.RegisterSyncWorker<FlightNode>(SyncFlightNode);
		MP.RegisterSyncWorker<VehicleArrivalAction>(SyncVehicleArrivalAction, null, isImplicit: true);
		MP.RegisterSyncWorker<IArrivalAction>(SyncIArrivalAction, null, isImplicit: true);
		MP.RegisterSyncMethod(typeof(VehicleFramework), "SyncedLaunch");
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(CompVehicleLauncher), "Launch"), new HarmonyMethod(typeof(VehicleFramework), "PreLaunch"));
		MP.RegisterSyncMethod(typeof(VehicleFramework), "SyncedOrderFlyToTiles");
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(AerialVehicleInFlight), "OrderFlyToTiles"), new HarmonyMethod(typeof(VehicleFramework), "PreOrderFlyToTiles"));
		MP.RegisterSyncMethod(typeof(VehicleFramework), "SyncedCaravanLaunch");
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(VehicleCaravan), "Launch"), new HarmonyMethod(typeof(VehicleFramework), "PreCaravanLaunch"));
		MP.RegisterSyncMethod(typeof(VehicleFramework), "SyncedWorldVehicleFlyToMap");
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(LaunchProtocol), "StartTargetingLocalMap"), new HarmonyMethod(typeof(VehicleFramework), "PreStartTargetingLocalMap"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(VehicleSkyfaller_Leaving), "ExposeData"), null, new HarmonyMethod(typeof(VehicleFramework), "PostSkyfallerLeavingExposeData"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(CompVehicleLauncher), "Launch"), new HarmonyMethod(typeof(VehicleFramework), "PreLaunchSetArrivalVehicle"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(AerialVehicleInFlight), "OrderFlyToTiles"), new HarmonyMethod(typeof(VehicleFramework), "PreOrderFlySetArrivalVehicle"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(AerialVehicleInFlight), "MoveForward"), new HarmonyMethod(typeof(VehicleFramework), "PreMoveForwardFixArrivalVehicle"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(AerialVehicleInFlight), "ResumePathPostLoad"), new HarmonyMethod(typeof(VehicleFramework), "PreResumePathPostLoad"), new HarmonyMethod(typeof(VehicleFramework), "PostResumePathPostLoad"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(VehicleCaravan), "GetInspectString"), new HarmonyMethod(typeof(VehicleFramework), "CleanupDestroyedCaravans"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(AerialVehicleArrivalModeWorker_TargetedDrop), "VehicleArrived"), new HarmonyMethod(typeof(VehicleFramework), "PreTargetedDropVehicleArrival"));
		MpCompat.harmony.Patch(AccessTools.DeclaredPropertyGetter(typeof(MapPawns), "AnyPawnBlockingMapRemoval"), null, new HarmonyMethod(typeof(VehicleFramework), "PreventMapRemovalForLandingSessions")
		{
			after = new string[1] { "SmashPhil.VehicleFramework" }
		});
		MP.RegisterSyncMethod(typeof(FlyingVehicleTargetedLandingSession), "Remove");
		MP.RegisterSyncMethod(typeof(FlyingVehicleTargetedLandingSession), "VehicleArrivalById");
		MP.RegisterSyncWorker<VehiclePawn>(SyncVehiclePawn, null, isImplicit: true);
		MP.RegisterSyncWorker<CompFueledTravel>(SyncFueledTravelComp);
		MP.RegisterSyncWorker<object>(NoSync, typeof(ITab_Vehicle_Cargo), isImplicit: false, shouldConstruct: true);
		MP.RegisterSyncWorker<Command_Turret>(SyncCommandTurret, typeof(Command_Turret), isImplicit: true, shouldConstruct: true);
		MP.RegisterSyncWorker<VehicleComponent>(SyncVehicleComponent, null, isImplicit: true);
		MP.RegisterSyncWorker<VehicleTurret>(SyncVehicleTurret, null, isImplicit: true);
		MP.RegisterSyncWorker<VehicleIgnitionController>(SyncVehicleIgnitionController);
		MP.RegisterSyncWorker<VehicleRoleHandler>(SyncVehicleRoleHandler);
		MP.RegisterSyncWorker<AssignedSeat>(SyncAssignedSeat);
		mpTransferableReferenceType = AccessTools.TypeByName("Multiplayer.Client.Persistent.MpTransferableReference");
		syncTradeableCount = (ISyncField)AccessTools.DeclaredField("Multiplayer.Client.SyncFields:SyncTradeableCount").GetValue(null);
		FieldInfo fieldInfo = AccessTools.DeclaredField("Multiplayer.Client.RwSerialization:supportedThingHolders");
		if (fieldInfo == null)
		{
			Log.Error("Trying to access Multiplayer.Client.RwSerialization:supportedThingHolders failed, field is null.");
			return;
		}
		if (!fieldInfo.IsStatic)
		{
			Log.Error("Trying to access Multiplayer.Client.RwSerialization:supportedThingHolders failed, field is non-static.");
			return;
		}
		Type[] array4 = fieldInfo.GetValue(null) as Type[];
		if (array4 == null)
		{
			Log.Error(string.Format("Trying to access {0} failed, the value is null or not Type[]. Value={1}", "Multiplayer.Client.RwSerialization:supportedThingHolders", fieldInfo.GetValue(null)));
			return;
		}
		Array.Resize(ref array4, array4.Length + 1);
		array4[array4.Length - 1] = typeof(VehicleRoleHandler);
		fieldInfo.SetValue(null, array4);
		static ISyncMethod TrySyncDeclaredMethod(Type targetType, string targetMethodName)
		{
			MethodInfo methodInfo4 = AccessTools.DeclaredMethod(targetType, targetMethodName);
			if (methodInfo4 != null)
			{
				return MP.RegisterSyncMethod(methodInfo4);
			}
			return null;
		}
	}

	private static bool NoThreadInMp(VehiclePathingSystem mapping)
	{
		if (!MP.IsInMultiplayer)
		{
			return mapping.ThreadAvailable;
		}
		return false;
	}

	private static IEnumerable<CodeInstruction> ReplaceThreadAvailable(IEnumerable<CodeInstruction> instr, MethodBase baseMethod)
	{
		MethodInfo target = AccessTools.DeclaredPropertyGetter(typeof(VehiclePathingSystem), "ThreadAvailable");
		MethodInfo replacement = AccessTools.DeclaredMethod(typeof(VehicleFramework), "NoThreadInMp");
		bool replacedAnything = false;
		foreach (CodeInstruction item in instr)
		{
			if (item.Calls(target))
			{
				item.opcode = OpCodes.Call;
				item.operand = replacement;
				replacedAnything = true;
			}
			yield return item;
		}
		if (!replacedAnything)
		{
			string text = ((baseMethod.DeclaringType?.Namespace).NullOrEmpty() ? baseMethod.Name : (baseMethod.DeclaringType.Name + ":" + baseMethod.Name));
			Log.Warning("Failed to patch VehiclePathingSystem.ThreadAvailable calls for method " + text);
		}
	}

	private static bool PreToggleFuelSwitch(Gizmo_RefuelableFuelTravel __instance)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		SyncedToggleFuelSwitch(fuelGizmoRefuelableField(__instance));
		return false;
	}

	private static void SyncedToggleFuelSwitch(CompFueledTravel comp)
	{
		if (comp.Props.ElectricPowered)
		{
			if (!comp.Charging)
			{
				if (comp.TryConnectPower())
				{
					SoundDefOf.Tick_High.PlayOneShotOnCamera();
				}
				else
				{
					SoundDefOf.ClickReject.PlayOneShotOnCamera();
				}
			}
			else
			{
				comp.DisconnectPower();
				SoundDefOf.Tick_Low.PlayOneShotOnCamera();
			}
		}
		else
		{
			comp.allowAutoRefuel = !comp.allowAutoRefuel;
			(comp.allowAutoRefuel ? SoundDefOf.Tick_High : SoundDefOf.Tick_Low).PlayOneShotOnCamera();
		}
	}

	private static bool PreDisembarkSinglePawn(object __instance)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		FieldInfo field = __instance.GetType().GetField("currentPawn");
		object obj = (__instance.GetType().GetField("CS$<>8__locals1") ?? __instance.GetType().GetField("CS$<>8__locals2"))?.GetValue(__instance);
		object obj2 = (obj?.GetType().GetField("<>4__this"))?.GetValue(obj);
		VehiclePawn val = (VehiclePawn)((obj2 is VehiclePawn) ? obj2 : null);
		if (val != null && field?.GetValue(__instance) is Pawn pawn)
		{
			SyncedDisembarkPawn(val, pawn.thingIDNumber);
		}
		return false;
	}

	private static void SyncedDisembarkPawn(VehiclePawn vehicle, int pawnId)
	{
		foreach (VehicleRoleHandler handler in vehicle.handlers)
		{
			foreach (Pawn item in handler.thingOwner.InnerListForReading)
			{
				if (item.thingIDNumber == pawnId && item != null)
				{
					Pawn pawn = item;
					vehicle.DisembarkPawn(pawn);
					return;
				}
			}
		}
	}

	private static bool PreHandleDragEvent()
	{
		if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
		{
			return true;
		}
		if (Event.current.type != EventType.MouseUp || Event.current.button != 0)
		{
			return false;
		}
		SyncedHandleDragEvent(VehicleTabHelper_Passenger.draggedPawn, VehicleTabHelper_Passenger.hoveringOverPawn, VehicleTabHelper_Passenger.transferToHolder);
		VehicleTabHelper_Passenger.draggedPawn = null;
		return false;
	}

	private static void SyncedHandleDragEvent(Pawn dragged, Pawn hovering, IThingHolder holder)
	{
		Pawn draggedPawn = VehicleTabHelper_Passenger.draggedPawn;
		Pawn hoveringOverPawn = VehicleTabHelper_Passenger.hoveringOverPawn;
		IThingHolder transferToHolder = VehicleTabHelper_Passenger.transferToHolder;
		Event current = Event.current;
		try
		{
			VehicleTabHelper_Passenger.draggedPawn = dragged;
			VehicleTabHelper_Passenger.hoveringOverPawn = hovering;
			VehicleTabHelper_Passenger.transferToHolder = holder;
			Event.current = new Event
			{
				type = EventType.MouseUp,
				button = 0
			};
			VehicleTabHelper_Passenger.HandleDragEvent();
		}
		finally
		{
			VehicleTabHelper_Passenger.draggedPawn = draggedPawn;
			VehicleTabHelper_Passenger.hoveringOverPawn = hoveringOverPawn;
			VehicleTabHelper_Passenger.transferToHolder = transferToHolder;
			Event.current = current;
		}
	}

	private static void ReplacedShowBanishPawnConfirmationDialog(Pawn pawn, Action onConfirm, AerialVehicleInFlight aerialVehicle)
	{
		if (!MP.IsInMultiplayer)
		{
			PawnBanishUtility.ShowBanishPawnConfirmationDialog(pawn, onConfirm);
			return;
		}
		if (onConfirm != null)
		{
			Log.ErrorOnce("onConfirm was not null for ShowBanishPawnConfirmationDialog, MP Compat will likely need an update. There may be issues", -818484805);
		}
		Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(PawnBanishUtility.GetBanishPawnDialogText(pawn), () =>
		{
			SyncedBanishPawn(aerialVehicle, pawn.thingIDNumber);
		}, destructive: true));
	}

	private static void SyncedBanishPawn(AerialVehicleInFlight aerialVehicle, int pawnId)
	{
		if (aerialVehicle?.vehicle == null)
		{
			return;
		}
		List<Pawn> allPawnsAboard = aerialVehicle.vehicle.AllPawnsAboard;
		if (!allPawnsAboard.NullOrEmpty())
		{
			Pawn pawn = allPawnsAboard.Find((Pawn p) => p.thingIDNumber == pawnId);
			if (pawn != null)
			{
				PawnBanishUtility.Banish(pawn);
			}
		}
	}

	private static IEnumerable<CodeInstruction> ReplaceVanillaBanishDialog(IEnumerable<CodeInstruction> instr, MethodBase baseMethod)
	{
		MethodInfo target = AccessTools.DeclaredMethod(typeof(PawnBanishUtility), "ShowBanishPawnConfirmationDialog");
		MethodInfo replacement = AccessTools.DeclaredMethod(typeof(VehicleFramework), "ReplacedShowBanishPawnConfirmationDialog");
		int replacedCount = 0;
		foreach (CodeInstruction ci in instr)
		{
			if (ci.Calls(target))
			{
				ci.opcode = OpCodes.Call;
				ci.operand = replacement;
				replacedCount++;
				yield return new CodeInstruction(OpCodes.Ldarg_1);
			}
			yield return ci;
		}
		if (replacedCount != 1)
		{
			string arg = ((baseMethod.DeclaringType?.Namespace).NullOrEmpty() ? baseMethod.Name : (baseMethod.DeclaringType.Name + ":" + baseMethod.Name));
			Log.Warning($"Patched incorrect number of PawnBanishUtility.ShowBanishPawnConfirmationDialog calls (patched {replacedCount}, expected {1}) for method {arg}");
		}
	}

	private static bool CancelTurretSetTargetSync()
	{
		if (!shouldSyncInInterface)
		{
			return !MP.InInterface;
		}
		return true;
	}

	private static void SyncSetTarget(VehicleTurret turret, LocalTargetInfo target)
	{
		try
		{
			shouldSyncInInterface = true;
			turret.SetTarget(target);
		}
		finally
		{
			shouldSyncInInterface = false;
		}
	}

	private static IEnumerable<CodeInstruction> ReplaceSetTargetCall(IEnumerable<CodeInstruction> instr, MethodBase baseMethod)
	{
		MethodInfo target = AccessTools.DeclaredMethod(typeof(VehicleTurret), "SetTarget");
		MethodInfo replacement = AccessTools.DeclaredMethod(typeof(VehicleFramework), "SyncSetTarget");
		int replacedCount = 0;
		foreach (CodeInstruction item in instr)
		{
			if ((item.opcode == OpCodes.Call || item.opcode == OpCodes.Callvirt) && item.operand is MethodInfo methodInfo && methodInfo == target)
			{
				item.opcode = OpCodes.Call;
				item.operand = replacement;
				replacedCount++;
			}
			yield return item;
		}
		if (replacedCount != 1)
		{
			string arg = ((baseMethod.DeclaringType?.Namespace).NullOrEmpty() ? baseMethod.Name : (baseMethod.DeclaringType.Name + ":" + baseMethod.Name));
			Log.Warning($"Patched incorrect number of SetTarget calls (patched {replacedCount}, expected {1}) for method {arg}");
		}
	}

	private static void PreSetTurretTarget(VehicleTurret ___turret)
	{
		if (MP.IsInMultiplayer)
		{
			___turret.SetTarget(LocalTargetInfo.Invalid);
			___turret.AlignToAngleRestricted(0f);
		}
	}

	private static bool PreLaunch(CompVehicleLauncher __instance, TargetData<GlobalTargetInfo> targetData, IArrivalAction arrivalAction)
	{
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Expected Obj, but got Unknown
		if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
		{
			return true;
		}
		ExtractTargetData(targetData.targets, out var tileIds, out var layerIds, out var worldObjectIds);
		SyncedLaunch(((VehicleComp)__instance).Vehicle, tileIds, layerIds, worldObjectIds, (VehicleArrivalAction)arrivalAction);
		return false;
	}

	private static void SyncedLaunch(VehiclePawn vehicle, List<int> tileIds, List<int> layerIds, List<int> worldObjectIds, VehicleArrivalAction arrivalAction)
	{
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		if (arrivalAction != null)
		{
			arrivalActionVehicleField(arrivalAction) = vehicle;
		}
		vehicle.CompVehicleLauncher.Launch(ReconstructTargetData(tileIds, layerIds, worldObjectIds), (IArrivalAction)(object)arrivalAction);
	}

	private static bool PreCaravanLaunch(VehicleCaravan __instance, TargetData<GlobalTargetInfo> targetData, IArrivalAction arrivalAction)
	{
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Expected Obj, but got Unknown
		if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
		{
			return true;
		}
		ExtractTargetData(targetData.targets, out var tileIds, out var layerIds, out var worldObjectIds);
		SyncedCaravanLaunch(__instance, tileIds, layerIds, worldObjectIds, (VehicleArrivalAction)arrivalAction);
		return false;
	}

	private static void SyncedCaravanLaunch(VehicleCaravan caravan, List<int> tileIds, List<int> layerIds, List<int> worldObjectIds, VehicleArrivalAction arrivalAction)
	{
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		if (arrivalAction != null)
		{
			arrivalActionVehicleField(arrivalAction) = caravan.LeadVehicle;
		}
		caravan.Launch(ReconstructTargetData(tileIds, layerIds, worldObjectIds), (IArrivalAction)(object)arrivalAction);
	}

	private static bool PreStartTargetingLocalMap(VehiclePawn vehicle, TargetData<GlobalTargetInfo> targetData, MapParent mapParent, LocalTargetInfo landingCell, Rot4 rot)
	{
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
		{
			return true;
		}
		ExtractTargetData(targetData.targets, out var tileIds, out var layerIds, out var worldObjectIds);
		SyncedWorldVehicleFlyToMap(vehicle, tileIds, layerIds, worldObjectIds, mapParent, landingCell.Cell, rot);
		return false;
	}

	private static void SyncedWorldVehicleFlyToMap(VehiclePawn vehicle, List<int> tileIds, List<int> layerIds, List<int> worldObjectIds, MapParent mapParent, IntVec3 landingCell, Rot4 rot)
	{
		//IL_0004: Unknown result type (might be due to invalid IL or missing references)
		LaunchProtocol.StartTargetingLocalMap(vehicle, ReconstructTargetData(tileIds, layerIds, worldObjectIds), mapParent, (LocalTargetInfo)landingCell, rot);
	}

	private static void PostSkyfallerLeavingExposeData(VehicleSkyfaller_Leaving __instance)
	{
		Scribe_Deep.Look(ref __instance.arrivalAction, "arrivalAction");
	}

	private static void PreLaunchSetArrivalVehicle(CompVehicleLauncher __instance, IArrivalAction arrivalAction)
	{
		if (MP.IsInMultiplayer)
		{
			VehicleArrivalAction val = (VehicleArrivalAction)(object)((arrivalAction is VehicleArrivalAction) ? arrivalAction : null);
			if (val != null)
			{
				arrivalActionVehicleField(val) = ((VehicleComp)__instance).Vehicle;
			}
		}
	}

	private static void PreOrderFlySetArrivalVehicle(AerialVehicleInFlight __instance, IArrivalAction arrivalAction)
	{
		if (MP.IsInMultiplayer)
		{
			VehicleArrivalAction val = (VehicleArrivalAction)(object)((arrivalAction is VehicleArrivalAction) ? arrivalAction : null);
			if (val != null)
			{
				arrivalActionVehicleField(val) = __instance.vehicle;
			}
		}
	}

	private static bool PreOrderFlyToTiles(AerialVehicleInFlight __instance, List<FlightNode> flightPath, IArrivalAction arrivalAction)
	{
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Expected Obj, but got Unknown
		if (!MP.IsInMultiplayer || !MP.InInterface)
		{
			return true;
		}
		SyncedOrderFlyToTiles(__instance, flightPath, (VehicleArrivalAction)arrivalAction);
		return false;
	}

	private static void SyncedOrderFlyToTiles(AerialVehicleInFlight aerialVehicle, List<FlightNode> flightPath, VehicleArrivalAction arrivalAction)
	{
		if (arrivalAction != null)
		{
			arrivalActionVehicleField(arrivalAction) = aerialVehicle.vehicle;
		}
		aerialVehicle.OrderFlyToTiles(flightPath, (IArrivalAction)(object)arrivalAction);
	}

	private static void SyncIArrivalAction(SyncWorker sync, ref IArrivalAction action)
	{
		IArrivalAction obj = action;
		VehicleArrivalAction action2 = (VehicleArrivalAction)(object)((obj is VehicleArrivalAction) ? obj : null);
		SyncVehicleArrivalAction(sync, ref action2);
		action = (IArrivalAction)(object)action2;
	}

	private static void ExtractTargetData(IEnumerable<GlobalTargetInfo> targets, out List<int> tileIds, out List<int> layerIds, out List<int> worldObjectIds)
	{
		tileIds = new List<int>();
		layerIds = new List<int>();
		worldObjectIds = new List<int>();
		if (targets == null)
		{
			return;
		}
		foreach (GlobalTargetInfo target in targets)
		{
			tileIds.Add(target.Tile.tileId);
			layerIds.Add(target.Tile.Layer.LayerID);
			worldObjectIds.Add(target.HasWorldObject ? target.WorldObject.ID : (-1));
		}
	}

	private static TargetData<GlobalTargetInfo> ReconstructTargetData(List<int> tileIds, List<int> layerIds, List<int> worldObjectIds)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0089: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		TargetData<GlobalTargetInfo> result = new TargetData<GlobalTargetInfo>();
		for (int i = 0; i < tileIds.Count; i++)
		{
			int woId = worldObjectIds[i];
			if (woId >= 0)
			{
				WorldObject worldObject = Find.WorldObjects.AllWorldObjects.FirstOrDefault((WorldObject o) => o.ID == woId);
				if (worldObject != null)
				{
					result.targets.Add(new GlobalTargetInfo(worldObject));
					continue;
				}
			}
			result.targets.Add(new GlobalTargetInfo(new PlanetTile(tileIds[i], layerIds[i])));
		}
		return result;
	}

	private static void SyncVehicleArrivalAction(SyncWorker sync, ref VehicleArrivalAction action)
	{
		//IL_0083: Unknown result type (might be due to invalid IL or missing references)
		//IL_0089: Expected Obj, but got Unknown
		if (sync.isWriting)
		{
			bool flag = action == null;
			sync.Write(flag);
			if (!flag)
			{
				sync.Write(((object)action).GetType().FullName);
				sync.Write(((Thing)(object)arrivalActionVehicleField(action))?.thingIDNumber ?? (-1));
			}
		}
		else
		{
			if (sync.Read<bool>())
			{
				return;
			}
			string text = sync.Read<string>();
			sync.Read<int>();
			if (!string.IsNullOrEmpty(text))
			{
				Type type = AccessTools.TypeByName(text);
				if (!(type == null))
				{
					action = (VehicleArrivalAction)Activator.CreateInstance(type);
				}
			}
		}
	}

	private static void PreMoveForwardFixArrivalVehicle(AerialVehicleInFlight __instance)
	{
		if (MP.IsInMultiplayer)
		{
			FlightPath flightPath = __instance.flightPath;
			IArrivalAction obj = ((flightPath != null) ? flightPath.ArrivalAction : null);
			VehicleArrivalAction val = (VehicleArrivalAction)(object)((obj is VehicleArrivalAction) ? obj : null);
			if (val != null && arrivalActionVehicleField(val) == null && __instance.vehicle != null)
			{
				arrivalActionVehicleField(val) = __instance.vehicle;
			}
		}
	}

	private static void PreResumePathPostLoad(AerialVehicleInFlight __instance, ref (float transition, Vector3 position)? __state)
	{
		if (MP.IsInMultiplayer)
		{
			__state = (__instance.transition, __instance.position);
		}
	}

	private static void PostResumePathPostLoad(AerialVehicleInFlight __instance, (float transition, Vector3 position)? __state)
	{
		if (__state.HasValue)
		{
			__instance.transition = __state.Value.transition;
			__instance.position = __state.Value.position;
		}
	}

	private static void CleanupDestroyedCaravans(VehicleCaravan __instance)
	{
		if (!((WorldObject)(object)__instance).Destroyed)
		{
			return;
		}
		foreach (VehiclePawn vehicle in __instance.Vehicles)
		{
			AerialVehicleInFlight aerialVehicle = Ext_Vehicles.GetAerialVehicle((Pawn)(object)vehicle);
			if (aerialVehicle != null)
			{
				Find.WorldSelector.Select((WorldObject)(object)aerialVehicle);
			}
		}
		Find.WorldSelector.Deselect((WorldObject)(object)__instance);
	}

	private static void NoSync(SyncWorker sync, ref object target)
	{
	}

	private static void SyncVehiclePawn(SyncWorker sync, ref VehiclePawn vehicle)
	{
		if (sync.isWriting)
		{
			if (vehicle == null)
			{
				sync.Write(byte.MaxValue);
				return;
			}
			AerialVehicleInFlight aerialVehicle = Ext_Vehicles.GetAerialVehicle((Pawn)(object)vehicle);
			if (aerialVehicle == null)
			{
				sync.Write((byte)0);
				sync.Write((Pawn)(object)vehicle);
			}
			else
			{
				sync.Write((byte)1);
				sync.Write<AerialVehicleInFlight>(aerialVehicle);
				sync.Write(((Thing)(object)vehicle).thingIDNumber);
			}
			return;
		}
		byte b = sync.Read<byte>();
		switch (b)
		{
		case 0:
		{
			Pawn pawn = sync.Read<Pawn>();
			vehicle = (VehiclePawn)(object)((pawn is VehiclePawn) ? pawn : null);
			break;
		}
		case 1:
		{
			AerialVehicleInFlight val = sync.Read<AerialVehicleInFlight>();
			int id = sync.Read<int>();
			vehicle = val?.vehicle;
			if (vehicle == null && MP.TryGetThingById(id, out var value))
			{
				vehicle = (VehiclePawn)(object)((value is VehiclePawn) ? value : null);
			}
			break;
		}
		default:
			throw new Exception(string.Format("Trying to read {0}, but received an unsupported holder type ({1})", "VehiclePawn", b));
		case byte.MaxValue:
			break;
		}
	}

	private static void SyncFueledTravelComp(SyncWorker sync, ref CompFueledTravel fueledTravel)
	{
		if (sync.isWriting)
		{
			ThingWithComps parent = ((ThingComp)(object)fueledTravel).parent;
			sync.Write<VehiclePawn>((VehiclePawn)(object)((parent is VehiclePawn) ? parent : null));
		}
		else
		{
			VehiclePawn val = sync.Read<VehiclePawn>();
			fueledTravel = ((val != null) ? val.CompFueledTravel : null);
		}
	}

	private static void SyncVehicleComponent(SyncWorker sync, ref VehicleComponent comp)
	{
		if (sync.isWriting)
		{
			VehiclePawn vehicle = comp.vehicle;
			List<VehicleComponent> components = vehicle.statHandler.components;
			int num = components.IndexOf(comp);
			sync.Write(num);
			if (num >= 0)
			{
				sync.Write<VehiclePawn>(vehicle);
			}
			else
			{
				Log.Error($"Trying to write a VehicleComponent, but the vehicle this component belongs to does not contain it. Vehicle={vehicle}, compCount={components.Count}");
			}
			return;
		}
		int num2 = sync.Read<int>();
		if (num2 >= 0)
		{
			VehiclePawn val = sync.Read<VehiclePawn>();
			List<VehicleComponent> components2 = val.statHandler.components;
			if (num2 < components2.Count)
			{
				comp = components2[num2];
			}
			else
			{
				Log.Error($"Trying to read VehicleComponent, but we've received component with index out of range. Vehicle={val}, index={num2}, compCount={components2.Count}");
			}
		}
	}

	private static void SyncVehicleTurret(SyncWorker sync, ref VehicleTurret turret)
	{
		if (sync.isWriting)
		{
			sync.Write(turret.uniqueID);
			sync.Write<VehiclePawn>(turret.vehicle);
			return;
		}
		int id = sync.Read<int>();
		VehiclePawn val = sync.Read<VehiclePawn>();
		if (id >= -1)
		{
			int num = id;
			if (num == -1)
			{
				Log.Error($"Trying to read VehicleTurret, received an uninitialized turret. Vehicle={val}, id=-1");
				return;
			}
			if (val == null)
			{
				Log.Error($"Trying to read VehicleTurret, received a null parent vehicle. id={id}");
				return;
			}
			CompVehicleTurrets compVehicleTurrets = val.CompVehicleTurrets;
			if (val.CompVehicleTurrets == null)
			{
				Log.Error($"Trying to read VehicleTurret, the vehicle doesn't contain CompVehicleTurrets. Vehicle={val}, id={id}");
				return;
			}
			if (compVehicleTurrets.turrets == null)
			{
				Log.Error($"Trying to read VehicleTurret, but the CompVehicleTurrets contains a null list of turrets. Vehicle={val}, id={id}, comp={compVehicleTurrets}");
				return;
			}
			turret = compVehicleTurrets.turrets.FirstOrDefault((VehicleTurret t) => t.uniqueID == id);
			if (turret == null)
			{
				Log.Error($"Trying to read VehicleTurret, but the list of turrets does not contain the turret we're trying to read. Vehicle={val}, id={id}, comp={compVehicleTurrets}");
			}
		}
		else
		{
			Log.Warning($"Trying to read VehicleTurret, received a turret with local ID. This shouldn't happen. Vehicle={val}, id={id}");
		}
	}

	private static void SyncVehicleIgnitionController(SyncWorker sync, ref VehicleIgnitionController controller)
	{
		if (sync.isWriting)
		{
			sync.Write<VehiclePawn>(controller.vehicle);
			return;
		}
		VehiclePawn val = sync.Read<VehiclePawn>();
		controller = val?.ignition;
		if (controller == null)
		{
			Log.Error($"Trying to read VehicleIgnitionController, but the vehicle is missing it. Vehicle={val}");
		}
	}

	private static void SyncVehicleRoleHandler(SyncWorker sync, ref VehicleRoleHandler handler)
	{
		if (sync.isWriting)
		{
			sync.Write(handler.uniqueID);
			sync.Write<VehiclePawn>(handler.vehicle);
			return;
		}
		int id = sync.Read<int>();
		VehiclePawn val = sync.Read<VehiclePawn>();
		if (id >= -1)
		{
			int num = id;
			if (num == -1)
			{
				Log.Error($"Trying to read VehicleRoleHandler, received an uninitialized handler. Vehicle={val}, id=-1");
				return;
			}
			if (val == null)
			{
				Log.Error($"Trying to read VehicleRoleHandler, received a null parent vehicle. id={id}");
				return;
			}
			if (val.handlers == null)
			{
				Log.Error($"Trying to read VehicleRoleHandler, but the vehicle contains a null list of handlers. Vehicle={val}, id={id}");
				return;
			}
			handler = val.handlers.FirstOrDefault((VehicleRoleHandler t) => t.uniqueID == id);
			if (handler == null)
			{
				Log.Error($"Trying to read VehicleRoleHandler, but the list of handlers does not contain the handler we're trying to read. Vehicle={val}, id={id}");
			}
		}
		else
		{
			Log.Warning($"Trying to read VehicleRoleHandler, received handler with local ID. This shouldn't happen. Vehicle={val}, id={id}");
		}
	}

	private static void SyncCommandTurret(SyncWorker sync, ref Command_Turret command)
	{
		if (sync.isWriting)
		{
			sync.Write<VehiclePawn>(command.vehicle);
		}
		else
		{
			command.vehicle = sync.Read<VehiclePawn>();
		}
		SyncVehicleTurret(sync, ref command.turret);
	}

	private static void SyncAssignedSeat(SyncWorker sync, ref AssignedSeat seat)
	{
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Expected Obj, but got Unknown
		if (sync.isWriting)
		{
			sync.Write<VehicleRoleHandler>(seat.handler);
			return;
		}
		VehicleRoleHandler handler = sync.Read<VehicleRoleHandler>();
		seat = new AssignedSeat
		{
			handler = handler
		};
	}

	private static void SyncVehicleSettings(SyncWorker sync, ref VehiclesModSettings settings)
	{
		if (!sync.isWriting)
		{
			settings = VehicleMod.settings;
		}
	}

	private static void SyncLaunchProtocol(SyncWorker sync, ref LaunchProtocol launchProtocol)
	{
		if (sync.isWriting)
		{
			sync.Write<VehiclePawn>(launchProtocol?.vehicle);
			return;
		}
		VehiclePawn val = sync.Read<VehiclePawn>();
		launchProtocol = ((val == null) ? null : val.CompVehicleLauncher?.launchProtocol);
	}

	private static void SyncFlightNode(SyncWorker sync, ref FlightNode node)
	{
		SyncType type = typeof(FlightNode);
		type.expose = true;
		if (sync.isWriting)
		{
			sync.Write<FlightNode>(node, type);
		}
		else
		{
			node = sync.Read<FlightNode>(type);
		}
	}

	[MpCompatSyncWorker(typeof(Designator_AreaRoadExpand), shouldConstruct = true)]
	private static void SyncAreaRoadDesignator(SyncWorker sync, ref Designator_AreaRoadExpand designator)
	{
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		if (sync.isWriting)
		{
			sync.Write<RoadType>(localRoadType);
		}
		else
		{
			Designator_AreaRoad.roadType = sync.Read<RoadType>();
		}
	}

	private static void SetCurrentLoadCargoSessionState(LoadVehicleCargoSession session)
	{
		LoadVehicleCargoSession.drawingSession = session;
		MP.SetCurrentSessionWithTransferables(session);
	}

	private static void PreDrawLoadCargo(Dialog_LoadCargo __instance)
	{
		if (!MP.IsInMultiplayer)
		{
			return;
		}
		LoadVehicleCargoSession firstOfType = MP.GetLocalSessionManager(((Thing)(object)__instance.vehicle).Map).GetFirstOfType<LoadVehicleCargoSession>();
		if (firstOfType == null)
		{
			((Window)(object)__instance).Close(true);
			return;
		}
		SetCurrentLoadCargoSessionState(firstOfType);
		MP.WatchBegin();
		showAllCargoItemsField.Watch(VehicleMod.settings);
		if (firstOfType.uiDirty)
		{
			__instance.CountToTransferChanged();
			firstOfType.uiDirty = false;
		}
		if (firstOfType.widgetDirty)
		{
			__instance.transferables = firstOfType.transferables;
			__instance.itemsTransfer = new TransferableOneWayWidget(firstOfType.transferables, null, null, null, drawMass: true, IgnorePawnsInventoryMode.IgnoreIfAssignedToUnload, includePawnsMassInMassUsage: false, () => __instance.MassCapacity - __instance.MassUsage);
			firstOfType.widgetDirty = false;
		}
	}

	private static void FinalizeDrawLoadCargo()
	{
		if (LoadVehicleCargoSession.drawingSession != null)
		{
			MP.WatchEnd();
			SetCurrentLoadCargoSessionState(null);
		}
	}

	private static void PostShowAllCargoItemsChanged(object instances, object value)
	{
		foreach (Map map in Find.Maps)
		{
			MP.GetLocalSessionManager(map).GetFirstOfType<LoadVehicleCargoSession>()?.AddItems();
		}
	}

	private static bool ReplacedLoadCargoCancelButton(Rect rect, string label, bool drawBackground, bool doMouseoverSound, bool active, TextAnchor? overrideTextAnchor)
	{
		if (LoadVehicleCargoSession.drawingSession == null)
		{
			return DoButton();
		}
		Color color = GUI.color;
		try
		{
			GUI.color = new Color(1f, 0.3f, 0.35f);
			if (DoButton())
			{
				LoadVehicleCargoSession.drawingSession.Remove();
			}
		}
		finally
		{
			GUI.color = color;
		}
		return false;
		bool DoButton()
		{
			return Widgets.ButtonText(rect, label, drawBackground, doMouseoverSound, active, overrideTextAnchor);
		}
	}

	private static bool ReplacedLoadCargoResetButton(Rect rect, string label, bool drawBackground, bool doMouseoverSound, bool active, TextAnchor? overrideTextAnchor)
	{
		bool flag = Widgets.ButtonText(rect, label, drawBackground, doMouseoverSound, active, overrideTextAnchor);
		if (!flag || LoadVehicleCargoSession.drawingSession == null)
		{
			return flag;
		}
		LoadVehicleCargoSession.drawingSession.Reset();
		return false;
	}

	private static bool ReplacedLoadCargoAcceptButton(Rect rect, string label, bool drawBackground, bool doMouseoverSound, bool active, TextAnchor? overrideTextAnchor)
	{
		bool flag = Widgets.ButtonText(rect, label, drawBackground, doMouseoverSound, active, overrideTextAnchor);
		if (!flag || LoadVehicleCargoSession.drawingSession == null)
		{
			return flag;
		}
		LoadVehicleCargoSession.drawingSession.Accept();
		return false;
	}

	private static bool ReplacedLoadCargoPackInstantlyButton(Rect rect, string label, bool drawBackground, bool doMouseoverSound, bool active, TextAnchor? overrideTextAnchor)
	{
		bool flag = Widgets.ButtonText(rect, label, drawBackground, doMouseoverSound, active, overrideTextAnchor);
		if (!flag || LoadVehicleCargoSession.drawingSession == null)
		{
			return flag;
		}
		LoadVehicleCargoSession.drawingSession.PackInstantly();
		return false;
	}

	private static bool PreLoadCargoSetToSendEverything()
	{
		if (LoadVehicleCargoSession.drawingSession == null)
		{
			return true;
		}
		LoadVehicleCargoSession.drawingSession.SetToSendEverything();
		return false;
	}

	private static bool PreLoadCargoCalculateAndRecache()
	{
		if (MP.IsInMultiplayer)
		{
			return LoadVehicleCargoSession.allowedToRecacheTransferables;
		}
		return true;
	}

	private static bool PreLoadCargoDialog(object __instance)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		VehiclePawn vehicle = vehiclePawnInnerClassParentField(__instance);
		if (!LoadVehicleCargoSession.TryOpenLoadVehicleCargoDialog(vehicle))
		{
			LoadVehicleCargoSession.CreateLoadVehicleCargoSession(vehicle);
		}
		return false;
	}

	private static void PreventMapRemovalForLandingSessions(ref bool __result, Map ___map)
	{
		if (MP.IsInMultiplayer && !__result)
		{
			__result = MP.GetLocalSessionManager(___map).GetFirstOfType<FlyingVehicleTargetedLandingSession>() != null;
		}
	}

	private static bool PreTargetedDropVehicleArrival(VehiclePawn vehicle, Map map)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		FlyingVehicleTargetedLandingSession.HandleTargetedVehicleArrival(vehicle, map);
		return false;
	}

	private static void CreateAndSyncMpTransferableReference(ISessionWithTransferables session, Transferable transferable)
	{
		syncTradeableCount.Watch(Activator.CreateInstance(mpTransferableReferenceType, session, transferable));
	}

	private static IEnumerable<CodeInstruction> ReplaceButtonsTranspiler(IEnumerable<CodeInstruction> instr, MethodBase baseMethod)
	{
		MethodInfo target = AccessTools.DeclaredMethod(typeof(Widgets), "ButtonText", new Type[6]
		{
			typeof(Rect),
			typeof(string),
			typeof(bool),
			typeof(bool),
			typeof(bool),
			typeof(TextAnchor?)
		});
		Dictionary<string, MethodInfo> buttonReplacements = new Dictionary<string, MethodInfo>();
		if (baseMethod.DeclaringType == typeof(Dialog_LoadCargo))
		{
			int expected = 4;
			buttonReplacements.Add("CancelButton", AccessTools.DeclaredMethod(typeof(VehicleFramework), "ReplacedLoadCargoCancelButton"));
			buttonReplacements.Add("ResetButton", AccessTools.DeclaredMethod(typeof(VehicleFramework), "ReplacedLoadCargoResetButton"));
			buttonReplacements.Add("AcceptButton", AccessTools.DeclaredMethod(typeof(VehicleFramework), "ReplacedLoadCargoAcceptButton"));
			buttonReplacements.Add("Dev: Pack Instantly", AccessTools.DeclaredMethod(typeof(VehicleFramework), "ReplacedLoadCargoPackInstantlyButton"));
			int replacedCount = 0;
			MethodInfo currentButtonReplacement = null;
			foreach (CodeInstruction item in instr)
			{
				if (currentButtonReplacement != null)
				{
					if (item.Calls(target))
					{
						item.operand = currentButtonReplacement;
						currentButtonReplacement = null;
						replacedCount++;
					}
				}
				else if (item.opcode == OpCodes.Ldstr && item.operand is string key)
				{
					buttonReplacements.TryGetValue(key, out currentButtonReplacement);
				}
				yield return item;
			}
			if (replacedCount != expected)
			{
				string arg = ((baseMethod.DeclaringType?.Namespace).NullOrEmpty() ? baseMethod.Name : (baseMethod.DeclaringType.Name + ":" + baseMethod.Name));
				Log.Warning($"Patched incorrect number of Widgets.ButtonText calls (patched {replacedCount}, expected {expected}) for method {arg}");
			}
			yield break;
		}
		throw new Exception($"Trying to patch a method for unsupported type: {baseMethod.DeclaringType}");
	}

	private static void InsertSwitchToMap(Window __instance, Rect __0)
	{
		if (!MP.IsInMultiplayer)
		{
			return;
		}
		using (new TextBlock(GameFont.Tiny))
		{
			TaggedString taggedString = "MpCompatSwitchToMap".Translate();
			float num = taggedString.GetWidthCached() + 25f;
			if (Widgets.ButtonText(new Rect(__0.xMax - num, 5f, num, 24f), taggedString))
			{
				__instance.Close();
			}
		}
	}

	[MpCompatPostfix(typeof(VehiclePawn), "Tick", null, MethodType.Normal)]
	private static void PostVehicleTick(VehiclePawn __instance)
	{
		if (!MP.IsInMultiplayer || !((Thing)(object)__instance).Spawned)
		{
			return;
		}
		CompVehicleTurrets compVehicleTurrets = __instance.CompVehicleTurrets;
		if (compVehicleTurrets == null)
		{
			return;
		}
		foreach (VehicleTurret turret in compVehicleTurrets.turrets)
		{
			if (turret.IsTargetable || turret.attachedTo != null)
			{
				turret.UpdateRotationLock();
				turret.TurretRotation = Mathf.Repeat(turret.TurretRotation, 360f);
			}
		}
	}

	private static void PreTurretRotation(VehicleTurret __instance, ref float? __state)
	{
		if (MP.InInterface)
		{
			__state = __instance.TurretRotation;
		}
	}

	private static void PostTurretRotation(VehicleTurret __instance, float? __state)
	{
		if (__state.HasValue)
		{
			__instance.TurretRotation = __state.Value;
		}
	}

	[MpCompatPrefix(typeof(TurretTargeter), "Turret", null, MethodType.Getter)]
	private static bool PreTurretTargeterCurrentTurretGetter()
	{
		if (MP.IsInMultiplayer)
		{
			return MP.InInterface;
		}
		return true;
	}

	[MpCompatPrefix(typeof(VehicleTweener), "TweenedPos", null, MethodType.Getter)]
	private static bool PreTweenedPosGetter(VehicleTweener __instance, ref Vector3 __result)
	{
		if (!MP.IsInMultiplayer || MP.InInterface)
		{
			return true;
		}
		__result = __instance.TweenedPosRoot();
		return false;
	}

	private static void PreRenderPawnInternal(VehiclePawn __instance, ref (Rot4 rotation, float angle)? __state)
	{
		if (MP.InInterface)
		{
			__state = (((Thing)(object)__instance).Rotation, __instance.angle);
		}
	}

	private static void PostRenderPawnInternal(VehiclePawn __instance, ref (Rot4 rotation, float angle)? __state)
	{
		(Rot4, float)? tuple = __state;
		if (tuple.HasValue)
		{
			(Rot4, float) valueOrDefault = tuple.GetValueOrDefault();
			Rot4 item = valueOrDefault.Item1;
			float item2 = valueOrDefault.Item2;
			((Thing)(object)__instance).Rotation = item;
			__instance.angle = item2;
		}
	}

	[MpCompatPrefix(typeof(Designator_AreaRoad), "ProcessInput", 1, MethodType.Normal)]
	private static void StoreNewLocalRoadType(RoadType ___roadType)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		localRoadType = ___roadType;
	}

	[MpCompatPrefix(typeof(Designator_AreaRoad), "DesignateSingleCell", null, MethodType.Normal)]
	[MpCompatPrefix(typeof(Designator_AreaRoad), "CanDesignateCell", null, MethodType.Normal)]
	private static void RestoreLocalRoadType()
	{
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		if (MP.IsInMultiplayer && !MP.IsExecutingSyncCommand)
		{
			Designator_AreaRoad.roadType = localRoadType;
		}
	}

	private static bool ShouldExecuteWhenFinished()
	{
		if (!UnityData.IsInMainThread)
		{
			return false;
		}
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		return PatchingUtilities.AllowedToRunLongEvents;
	}

	[MpCompatTranspiler(typeof(UpgradeNode), "AddOverlays", null, MethodType.Normal)]
	private static IEnumerable<CodeInstruction> FixHostOverlayInit(IEnumerable<CodeInstruction> instr, MethodBase baseMethod)
	{
		MethodInfo methodInfo = AccessTools.DeclaredPropertyGetter(typeof(UnityData), "IsInMainThread");
		MethodInfo methodInfo2 = MpMethodUtil.MethodOf((Delegate)new Func<bool>(ShouldExecuteWhenFinished));
		return PatchingUtilities.ReplaceMethod(instr, (MethodBase)methodInfo, (MethodBase)methodInfo2, baseMethod, (Func<CodeInstruction, IEnumerable<CodeInstruction>>)null, (Func<CodeInstruction, IEnumerable<CodeInstruction>>)null, 1, (string)null, (string)null, (Func<CodeInstruction, bool>)null, (Func<CodeInstruction, bool>)null);
	}

	static VehicleFramework()
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
	}
}
