using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using VanillaGravshipExpanded;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("vanillaexpanded.gravship")]
internal class VanillaGravshipExpanded
{
	private static ISyncField maintenanceThresholdSync;

	private static string cachedAsteroidName;

	private static bool inSyncedTileSelectedFlow;

	private static ISyncField oxygenRechargeThresholdField;

	private static Action<PlanetTile> closeGravshipSession;

	private static Func<PlanetTile, bool> hasGravshipSession;

	private static MethodInfo mpCompMethod;

	private static FieldInfo factionDataField;

	private static FieldInfo areaManagerField;

	private static FieldInfo landingConfirmationMarker;

	private static Func<object, bool> landingPresentationAllowed;

	private static Func<bool> hasPendingTakeoff;

	private static Action<Map, Building_GravEngine, Dialog_MessageBox> handleMapDecision;

	private static Func<MapParent, bool> wasManagedMapDecision;

	public VanillaGravshipExpanded(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
		PatchingUtilities.PatchPushPopRand("VanillaGravshipExpanded.Projectile_Gauss:DrawAt");
		PatchingUtilities.PatchPushPopRand("VanillaGravshipExpanded.Projectile_JavelinRocket:DrawAt");
	}

	private static void LatePatch()
	{
		PatchAsyncClocks();
		PatchLandingMarkerStores();
		MP.RegisterSyncMethod(typeof(Building_TargetingTerminal), "LinkTo");
		MP.RegisterSyncMethod(typeof(Building_TargetingTerminal), "Unlink");
		MP.RegisterSyncMethod(typeof(Building_GravshipTurret), "LinkTo");
		MP.RegisterSyncMethod(typeof(Building_GravshipTurret), "Unlink");
		MP.RegisterSyncMethod(typeof(CompWorldArtillery), "StartAttack");
		MP.RegisterSyncMethod(typeof(CompWorldArtillery), "Reset");
		PatchingUtilities.PatchCancelInInterface("VanillaGravshipExpanded.Building_TurretGun_OrderAttack_Patch:Prefix");
		PatchingUtilities.PatchCancelInInterface("VanillaGravshipExpanded.Building_TurretGun_ResetForcedTarget_Patch:Prefix");
		MpCompat.RegisterLambdaDelegate(typeof(Building_GravshipBlackBox), "GetGizmos", default(int));
		MpCompat.RegisterLambdaMethod(typeof(Building_SealantPopper), "GetGizmos", 1);
		MpCompat.RegisterLambdaMethod(typeof(Building_Agrocell), "GetGizmos", default(int));
		MP.RegisterSyncMethod(typeof(CompGravheatAbsorber), "AbsorbGravheat");
		PatchingUtilities.PatchCancelInInterfaceSetResultToTrue("VanillaGravshipExpanded.MainTabWindow_Research_DoBeginResearch_Patch:Prefix");
		PatchingUtilities.PatchCancelInInterfaceSetResultToTrue("VanillaGravshipExpanded.ResearchManager_SetCurrentProject_Patch:Prefix");
		PatchingUtilities.PatchCancelInInterface("VanillaGravshipExpanded.ResearchManager_StopProject_Patch:Prefix");
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(Dialog_ConfigureVacuumRequirement), "SetSelectedVacCheckpointsTo"), new HarmonyMethod(typeof(VanillaGravshipExpanded), "PreSetSelectedVacCheckpoints"));
		MP.RegisterSyncMethod(typeof(VanillaGravshipExpanded), "SyncedSetVacCheckpoints").SetContext(SyncContext.MapSelected);
		maintenanceThresholdSync = MP.RegisterSyncField(typeof(World_ExposeData_Patch), "maintenanceThreshold");
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(Window_SetDesiredMaintenance), "DoWindowContents"), new HarmonyMethod(typeof(VanillaGravshipExpanded), "PreMaintenanceDoWindowContents"), new HarmonyMethod(typeof(VanillaGravshipExpanded), "PostMaintenanceDoWindowContents"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(Window_RenameAsteroid), "DoWindowContents"), new HarmonyMethod(typeof(VanillaGravshipExpanded), "PreRenameDoWindowContents"), new HarmonyMethod(typeof(VanillaGravshipExpanded), "PostRenameDoWindowContents"));
		MP.RegisterSyncMethod(typeof(VanillaGravshipExpanded), "SyncedRenameAsteroid");
		MpCompat.RegisterLambdaDelegate(typeof(Building_Gravlift), "GetGizmos", 2);
		MethodInfo lambda = MpMethodUtil.GetLambda(typeof(Building_VacBarrier_Recolorable), "GetGizmos", MethodType.Normal, null, 2);
		MpCompat.harmony.Patch(lambda, new HarmonyMethod(typeof(VanillaGravshipExpanded), "PrePasteBarrierColor"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(Dialog_VacBarrierColorPicker), "SaveColor"), new HarmonyMethod(typeof(VanillaGravshipExpanded), "PreVacBarrierSaveColor"));
		MP.RegisterSyncMethod(typeof(VanillaGravshipExpanded), "SyncedSetBarrierColor");
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(Command_Ritual), "ProcessInput"), new HarmonyMethod(typeof(VanillaGravshipExpanded), "PreCommandRitualProcessInput_SyncIfGravshipLaunch"));
		MP.RegisterSyncMethod(typeof(VanillaGravshipExpanded), "SyncedShowRitualBeginWindow");
		Type type = AccessTools.TypeByName("Multiplayer.Client.Persistent.GravshipTravelUtils");
		MethodInfo methodInfo = AccessTools.DeclaredMethod(type, "CloseSessionAt");
		if (methodInfo != null)
		{
			closeGravshipSession = (Action<PlanetTile>)Delegate.CreateDelegate(typeof(Action<PlanetTile>), methodInfo);
		}
		MethodInfo methodInfo2 = AccessTools.DeclaredMethod(type, "HasSessionAt");
		if (methodInfo2 != null)
		{
			hasGravshipSession = (Func<PlanetTile, bool>)Delegate.CreateDelegate(typeof(Func<PlanetTile, bool>), methodInfo2);
		}
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(Precept_Ritual), "ShowRitualBeginWindow"), new HarmonyMethod(typeof(VanillaGravshipExpanded), "PreShowRitualClearStaleState")
		{
			priority = 800
		});
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(SettlementProximityGoodwillUtility), "CheckConfirmSettle"), new HarmonyMethod(typeof(VanillaGravshipExpanded), "PreCheckConfirmSettle"));
		MP.RegisterSyncMethod(typeof(VanillaGravshipExpanded), "SyncedGravshipTileSelected");
		MP.RegisterSyncMethod(typeof(GravshipUtility_PreLaunchConfirmation_Patch), "ExecuteGravshipLaunch");
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(GravshipUtility_PreLaunchConfirmation_Patch), "ExecuteGravshipLaunch"), new HarmonyMethod(typeof(VanillaGravshipExpanded), "PreExecuteGravshipLaunchCloseDialog"));
		PatchTakeoffDecision();
		MP.RegisterSyncMethod(typeof(VanillaGravshipExpanded), "SyncedSettleTile");
		MP.RegisterSyncMethod(typeof(VanillaGravshipExpanded), "SyncedAbandonTile");
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(Dialog_NamePlayerGravship), "Named"), new HarmonyMethod(typeof(VanillaGravshipExpanded), "PreGravshipNamed"));
		MP.RegisterSyncMethod(typeof(VanillaGravshipExpanded), "SyncedGravshipNamed");
		MP.RegisterSyncMethod(typeof(CompApparelOxygenProvider), "AutomaticRechargeEnabled");
		oxygenRechargeThresholdField = MP.RegisterSyncField(typeof(CompApparelOxygenProvider), "rechargeAtCharges").SetBufferChanges();
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(Gizmo_Slider), "GizmoOnGUI"), new HarmonyMethod(typeof(VanillaGravshipExpanded), "PreOxygenGizmoOnGUI"), new HarmonyMethod(typeof(VanillaGravshipExpanded), "PostOxygenGizmoOnGUI"));
		mpCompMethod = AccessTools.Method(AccessTools.TypeByName("Multiplayer.Client.Extensions"), "MpComp");
		factionDataField = AccessTools.Field(AccessTools.TypeByName("Multiplayer.Client.MultiplayerMapComp"), "factionData");
		areaManagerField = AccessTools.Field(AccessTools.TypeByName("Multiplayer.Client.FactionMapData"), "areaManager");
		MpCompat.harmony.Patch(AccessTools.Method(AccessTools.TypeByName("Multiplayer.Client.MapSetup"), "InitNewFactionData"), null, new HarmonyMethod(typeof(VanillaGravshipExpanded), "PostMapSetupInitNewFactionData"));
	}

	private static void PatchAsyncClocks()
	{
		PatchingUtilities.SetupAsyncTime();
		PatchingUtilities.RegisterTimestampFields(typeof(CompGravheatAbsorber), (object obj, int offset) =>
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0006: Unknown result type (might be due to invalid IL or missing references)
			CompGravheatAbsorber val = (CompGravheatAbsorber)obj;
			val.cooldownEndTick += offset;
		});
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(CompHeatManager), "TryApplyHeatToShip", new Type[2]
		{
			typeof(float),
			typeof(bool)
		}), new HarmonyMethod(typeof(VanillaGravshipExpanded), "BeforeApplyHeat"));
		PatchingUtilities.RegisterTimestampFields(typeof(CompScannerCluster), (object obj, int offset) =>
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0006: Unknown result type (might be due to invalid IL or missing references)
			//IL_000c: Expected Obj, but got Unknown
			//IL_0014: Expected Obj, but got Unknown
			((CompScanner)(CompScannerCluster)obj).lastScanTick += offset;
		});
		PatchingUtilities.RegisterTimestampFields(typeof(CompScannerCluster_BaseModule), (object obj, int offset) =>
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0006: Unknown result type (might be due to invalid IL or missing references)
			CompScannerCluster_BaseModule val = (CompScannerCluster_BaseModule)obj;
			val.lastScanTick += (float)offset;
		});
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(CompScannerCluster_OrbitalScannerModule), "CompTick"), null, null, new HarmonyMethod(typeof(VanillaGravshipExpanded), "KeepOrbitalSignalValidity"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(WorldComponent_GravshipController_LandingEnded_Patch), "Prefix"), null, null, new HarmonyMethod(typeof(VanillaGravshipExpanded), "ReadAnchorLandingClock"));
	}

	private static void PatchTakeoffDecision()
	{
		Type type = AccessTools.TypeByName("Multiplayer.Client.Patches.GravshipCutsceneSync");
		MethodInfo methodInfo = ((type == null) ? null : AccessTools.DeclaredProperty(type, "HasPendingTakeoff")?.GetGetMethod());
		if (methodInfo == null || !methodInfo.IsStatic || methodInfo.ReturnType != typeof(bool))
		{
			throw new MissingMethodException("Multiplayer.Client.Patches.GravshipCutsceneSync.HasPendingTakeoff");
		}
		hasPendingTakeoff = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), methodInfo);
		Type type2 = AccessTools.TypeByName("Multiplayer.Client.Persistent.GravshipMapDecisions");
		MethodInfo methodInfo2 = AccessTools.DeclaredMethod(type2, "HandleDialog", new Type[3]
		{
			typeof(Map),
			typeof(Building_GravEngine),
			typeof(Dialog_MessageBox)
		});
		MethodInfo methodInfo3 = AccessTools.DeclaredMethod(type2, "WasManaged", new Type[1] { typeof(MapParent) });
		if (methodInfo2 == null || !methodInfo2.IsStatic || methodInfo2.ReturnType != typeof(void) || methodInfo3 == null || !methodInfo3.IsStatic || methodInfo3.ReturnType != typeof(bool))
		{
			throw new MissingMethodException("Multiplayer.Client.Persistent.GravshipMapDecisions native dialog contract");
		}
		handleMapDecision = (Action<Map, Building_GravEngine, Dialog_MessageBox>)Delegate.CreateDelegate(typeof(Action<Map, Building_GravEngine, Dialog_MessageBox>), methodInfo2);
		wasManagedMapDecision = (Func<MapParent, bool>)Delegate.CreateDelegate(typeof(Func<MapParent, bool>), methodInfo3);
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(WorldComponent_GravshipController_TakeoffEnded_Patch), "Prefix"), new HarmonyMethod(typeof(VanillaGravshipExpanded), "BeforeTakeoffDecision"), null, new HarmonyMethod(typeof(VanillaGravshipExpanded), "RouteNativeMapDecision"));
	}

	private static IEnumerable<CodeInstruction> RouteNativeMapDecision(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo add = AccessTools.DeclaredMethod(typeof(WindowStack), "Add", new Type[1] { typeof(Window) });
		int matches = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(add))
			{
				matches++;
				CodeInstruction codeInstruction = new CodeInstruction(OpCodes.Ldarg_0);
				codeInstruction.labels.AddRange(instruction.labels);
				codeInstruction.blocks.AddRange(instruction.blocks);
				instruction.labels.Clear();
				instruction.blocks.Clear();
				yield return codeInstruction;
				instruction.opcode = OpCodes.Call;
				instruction.operand = AccessTools.DeclaredMethod(typeof(VanillaGravshipExpanded), "AddOwnedMapDecision");
			}
			yield return instruction;
		}
		if (matches != 2)
		{
			throw new InvalidOperationException("Unexpected VGE takeoff map-decision window count: " + matches);
		}
	}

	private static void AddOwnedMapDecision(WindowStack stack, Window window, WorldComponent_GravshipController controller)
	{
		if (!MP.IsInMultiplayer)
		{
			stack.Add(window);
			return;
		}
		if (!(window is Dialog_MessageBox arg) || controller?.map == null || controller.gravship?.Engine == null)
		{
			throw new InvalidOperationException("VGE map decision lost its departure map or engine");
		}
		handleMapDecision(controller.map, controller.gravship.Engine, arg);
	}

	private static bool BeforeTakeoffDecision(WorldComponent_GravshipController __0, out int __state)
	{
		__state = -1;
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if ((!MP.IsExecutingSyncCommand && hasPendingTakeoff()) || __0?.map == null || __0.gravship == null)
		{
			return false;
		}
		__state = Find.WindowStack.Count;
		return true;
	}

	private static void PatchLandingMarkerStores()
	{
		Type typeFromHandle = typeof(WorldComponent_GravshipController_WorldComponentOnGUI_Patch);
		MethodInfo methodInfo = AccessTools.DeclaredMethod(typeFromHandle, "TryBeginLanding", new Type[2]
		{
			typeof(GravshipLandingMarker),
			typeof(WorldComponent_GravshipController)
		});
		MethodInfo lambda = MpMethodUtil.GetLambda(typeFromHandle, "TryBeginLanding", MethodType.Normal, null, 3);
		if (methodInfo == null || methodInfo.ReturnType != typeof(void) || !methodInfo.IsStatic || lambda.ReturnType != typeof(void) || lambda.GetParameters().Length != 0 || AccessTools.Field(lambda.DeclaringType, "marker")?.FieldType != typeof(GravshipLandingMarker) || AccessTools.Field(lambda.DeclaringType, "controller")?.FieldType != typeof(WorldComponent_GravshipController))
		{
			throw new InvalidOperationException("VGE landing confirmation contract changed");
		}
		Type type = AccessTools.TypeByName("Multiplayer.Client.Persistent.GravshipUiOwnership");
		MethodInfo methodInfo2 = ((type == null) ? null : AccessTools.DeclaredMethod(type, "LandingPresentationAllowed", new Type[1] { typeof(object) }));
		if (methodInfo2 == null || !methodInfo2.IsStatic || methodInfo2.ReturnType != typeof(bool))
		{
			throw new MissingMethodException("Multiplayer.Client.Persistent.GravshipUiOwnership.LandingPresentationAllowed");
		}
		landingConfirmationMarker = AccessTools.Field(lambda.DeclaringType, "marker");
		landingPresentationAllowed = (Func<object, bool>)Delegate.CreateDelegate(typeof(Func<object, bool>), methodInfo2);
		HarmonyMethod transpiler = new HarmonyMethod(typeof(VanillaGravshipExpanded), "KeepSharedLandingMarker");
		MpCompat.harmony.Patch(methodInfo, null, null, transpiler);
		MpCompat.harmony.Patch(lambda, new HarmonyMethod(typeof(VanillaGravshipExpanded), "PreLandingConfirmation"), null, transpiler);
	}

	private static bool PreLandingConfirmation(object __instance)
	{
		if (!MP.IsExecutingSyncCommand)
		{
			return landingPresentationAllowed(landingConfirmationMarker.GetValue(__instance));
		}
		return true;
	}

	private static IEnumerable<CodeInstruction> KeepSharedLandingMarker(IEnumerable<CodeInstruction> instructions, MethodBase original)
	{
		Type type = AccessTools.TypeByName("Multiplayer.Client.Persistent.GravshipUiOwnership");
		MethodInfo methodInfo = ((type == null) ? null : AccessTools.DeclaredMethod(type, "SetLandingMarkerFromUi", new Type[2]
		{
			typeof(WorldComponent_GravshipController),
			typeof(GravshipLandingMarker)
		}));
		if (methodInfo == null || !methodInfo.IsStatic || methodInfo.ReturnType != typeof(void))
		{
			throw new MissingMethodException("Multiplayer.Client.Persistent.GravshipUiOwnership.SetLandingMarkerFromUi");
		}
		FieldInfo markerField = AccessTools.Field(typeof(WorldComponent_GravshipController), "landingMarker");
		List<CodeInstruction> list = instructions.ToList();
		CodeInstruction[] array = list.Where((CodeInstruction code) => code.opcode == OpCodes.Stfld && object.Equals(code.operand, markerField)).ToArray();
		if (array.Length != 1)
		{
			throw new InvalidOperationException($"Expected one VGE landing marker store in {original}, found {array.Length}");
		}
		array[0].opcode = OpCodes.Call;
		array[0].operand = methodInfo;
		return list;
	}

	private static int LandingTicks(WorldComponent_GravshipController controller)
	{
		if (!MP.IsInMultiplayer || !PatchingUtilities.IsAsyncTime)
		{
			return GenTicks.TicksGame;
		}
		PatchingUtilities.TimeSnapshot? andSetFromMap = PatchingUtilities.TimeSnapshot.GetAndSetFromMap(controller.map);
		try
		{
			return GenTicks.TicksGame;
		}
		finally
		{
			andSetFromMap?.Set();
		}
	}

	private static IEnumerable<CodeInstruction> ReadAnchorLandingClock(IEnumerable<CodeInstruction> instructions)
	{
		int matches = 0;
		MethodInfo getter = AccessTools.PropertyGetter(typeof(GenTicks), "TicksGame");
		foreach (CodeInstruction code in instructions)
		{
			if (code.Calls(getter))
			{
				CodeInstruction codeInstruction = new CodeInstruction(OpCodes.Ldarg_0);
				codeInstruction.labels.AddRange(code.labels);
				code.labels.Clear();
				codeInstruction.blocks.AddRange(code.blocks.Where((ExceptionBlock b) => b.blockType != ExceptionBlockType.EndExceptionBlock));
				code.blocks.RemoveAll((ExceptionBlock b) => b.blockType != ExceptionBlockType.EndExceptionBlock);
				yield return codeInstruction;
				code.opcode = OpCodes.Call;
				code.operand = AccessTools.Method(typeof(VanillaGravshipExpanded), "LandingTicks");
				matches++;
			}
			yield return code;
		}
		if (matches != 1)
		{
			throw new InvalidOperationException($"Expected one anchor landing clock read, found {matches}");
		}
	}

	private static IEnumerable<CodeInstruction> KeepOrbitalSignalValidity(IEnumerable<CodeInstruction> instructions)
	{
		int matches = 0;
		FieldInfo field = AccessTools.Field(typeof(CompOrbitalScanner), "locateSignalTick");
		List<CodeInstruction> all = instructions.ToList();
		for (int i = 0; i < all.Count; i++)
		{
			CodeInstruction codeInstruction = all[i];
			if (codeInstruction.LoadsField(field) && i + 1 < all.Count && all[i + 1].LoadsConstant(0L))
			{
				codeInstruction.opcode = OpCodes.Call;
				codeInstruction.operand = AccessTools.PropertyGetter(typeof(CompOrbitalScanner), "IsLocating");
				matches++;
			}
			yield return codeInstruction;
		}
		if (matches != 1)
		{
			throw new InvalidOperationException($"Expected one orbital module marker read, found {matches}");
		}
	}

	private static void BeforeApplyHeat(CompHeatManager __instance)
	{
		if (!MP.IsInMultiplayer)
		{
			return;
		}
		Map map = ((ThingComp)(object)__instance).parent.Map;
		if (map == null)
		{
			return;
		}
		PatchingUtilities.TimeSnapshot? andSetFromMap = PatchingUtilities.TimeSnapshot.GetAndSetFromMap(map);
		try
		{
			int ticksGame = Find.TickManager.TicksGame;
			if (__instance.cachedShipRooms == null || ticksGame - __instance.roomCacheTick < 0 || !__instance.cachedShipRooms.All((Room room) => room.Map == map))
			{
				__instance.cachedShipRooms = null;
				__instance.roomCacheTick = ticksGame - 61;
			}
		}
		finally
		{
			andSetFromMap?.Set();
		}
	}

	private static bool PreSetSelectedVacCheckpoints(float resistance, bool allowDrafted)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (MP.IsExecutingSyncCommand)
		{
			return true;
		}
		SyncedSetVacCheckpoints(resistance, allowDrafted);
		return false;
	}

	private static void SyncedSetVacCheckpoints(float resistance, bool allowDrafted)
	{
		Dialog_ConfigureVacuumRequirement.SetSelectedVacCheckpointsTo(resistance, allowDrafted);
	}

	private static void PreMaintenanceDoWindowContents()
	{
		if (MP.IsInMultiplayer)
		{
			MP.WatchBegin();
			maintenanceThresholdSync.Watch();
		}
	}

	private static void PostMaintenanceDoWindowContents()
	{
		if (MP.IsInMultiplayer)
		{
			MP.WatchEnd();
		}
	}

	private static void PreRenameDoWindowContents(Window_RenameAsteroid __instance)
	{
		if (MP.IsInMultiplayer)
		{
			cachedAsteroidName = (__instance.worldObject as SpaceMapParent)?.Name;
		}
	}

	private static void PostRenameDoWindowContents(Window_RenameAsteroid __instance)
	{
		if (MP.IsInMultiplayer && __instance.worldObject is SpaceMapParent spaceMapParent && !(cachedAsteroidName == spaceMapParent.Name))
		{
			string name = spaceMapParent.Name;
			spaceMapParent.Name = cachedAsteroidName;
			SyncedRenameAsteroid(spaceMapParent, name);
		}
	}

	private static void SyncedRenameAsteroid(WorldObject worldObject, string name)
	{
		if (worldObject is SpaceMapParent spaceMapParent)
		{
			spaceMapParent.Name = name;
		}
	}

	private static void PreShowRitualClearStaleState()
	{
		if (MP.IsInMultiplayer && !inSyncedTileSelectedFlow)
		{
			Dialog_BeginRitual_ShowRitualBeginWindow_Patch.ClearLaunchState();
		}
	}

	private static bool PreCommandRitualProcessInput_SyncIfGravshipLaunch(Command_Ritual __instance)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (MP.IsExecutingSyncCommand)
		{
			return true;
		}
		if (!GravshipHelper.IsGravshipLaunch(__instance.ritual.def))
		{
			return true;
		}
		SyncedShowRitualBeginWindow(__instance.ritual, __instance.targetInfo);
		return false;
	}

	private static void SyncedShowRitualBeginWindow(Precept_Ritual ritual, TargetInfo targetInfo)
	{
		ritual.ShowRitualBeginWindow(targetInfo);
	}

	private static bool PreCheckConfirmSettle(PlanetTile tile, Building_GravEngine gravEngine)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (gravEngine == null || Dialog_BeginRitual_ShowRitualBeginWindow_Patch.state == null)
		{
			return true;
		}
		if (!MP.IsExecutingSyncCommand)
		{
			SyncedGravshipTileSelected(gravEngine, tile);
		}
		return false;
	}

	private static void SyncedGravshipTileSelected(Building_GravEngine gravEngine, PlanetTile tile)
	{
		GravshipLaunchState state = Dialog_BeginRitual_ShowRitualBeginWindow_Patch.state;
		if (state != null && (hasGravshipSession == null || hasGravshipSession(gravEngine.Map.Tile)))
		{
			Find.World.renderer.wantedMode = WorldRenderMode.None;
			Find.TilePicker.StopTargetingInt();
			closeGravshipSession?.Invoke(gravEngine.Map.Tile);
			inSyncedTileSelectedFlow = true;
			try
			{
				SettlementProximityGoodwillUtility_CheckConfirmSettle_Patch.OnGravshipTileSelected(gravEngine, tile, state);
			}
			finally
			{
				inSyncedTileSelectedFlow = false;
			}
			Dialog_BeginRitual_ShowRitualBeginWindow_Patch.ClearLaunchState();
		}
	}

	private static void PreExecuteGravshipLaunchCloseDialog()
	{
		if (!MP.IsInMultiplayer || !MP.IsExecutingSyncCommand)
		{
			return;
		}
		string rawText = "ConfirmGravEngineLaunch".Translate().RawText;
		for (int num = Find.WindowStack.Windows.Count - 1; num >= 0; num--)
		{
			if (Find.WindowStack.Windows[num] is Dialog_MessageBox dialog_MessageBox && dialog_MessageBox.text.RawText.StartsWith(rawText))
			{
				dialog_MessageBox.Close();
				break;
			}
		}
	}

	private static void SyncedSettleTile(MapParent mapParent)
	{
		if ((!MP.IsInMultiplayer || !wasManagedMapDecision(mapParent)) && mapParent?.Map != null && (bool)mapParent.CanBeSettled)
		{
			SettleInExistingMapUtility.Settle(mapParent.Map);
		}
	}

	private static void SyncedAbandonTile(MapParent mapParent)
	{
		if ((MP.IsInMultiplayer && wasManagedMapDecision(mapParent)) || mapParent?.Map == null)
		{
			return;
		}
		Map map = mapParent.Map;
		if (mapParent is Settlement settlement && settlement.Faction == Faction.OfPlayer)
		{
			mapParent.Abandon(wasGravshipLaunch: false);
			return;
		}
		mapParent.ShouldRemoveMapNow(out var alsoRemoveWorldObject);
		Current.Game.DeinitAndRemoveMap(map, notifyPlayer: false);
		if (!mapParent.Destroyed && (alsoRemoveWorldObject || mapParent.forceRemoveWorldObjectWhenMapRemoved))
		{
			mapParent.Destroy();
		}
	}

	private static bool PreGravshipNamed(Dialog_NamePlayerGravship __instance, string s)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (MP.IsExecutingSyncCommand)
		{
			return true;
		}
		Building_GravEngine engine = __instance.engine;
		if (engine != null)
		{
			SyncedGravshipNamed(engine, s);
		}
		return false;
	}

	private static void SyncedGravshipNamed(Building_GravEngine engine, string name)
	{
		for (int num = Find.WindowStack.Windows.Count - 1; num >= 0; num--)
		{
			if (Find.WindowStack.Windows[num] is Dialog_NamePlayerGravship dialog_NamePlayerGravship)
			{
				((Dialog_GiveName)dialog_NamePlayerGravship).Named(name);
				break;
			}
		}
	}

	private static void PreOxygenGizmoOnGUI(Gizmo_Slider __instance)
	{
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Expected Obj, but got Unknown
		if (MP.IsInMultiplayer && !(__instance.GetType() != typeof(Gizmo_OxygenProvider)))
		{
			MP.WatchBegin();
			oxygenRechargeThresholdField.Watch(((Gizmo_OxygenProvider)__instance).oxygenProvider);
			__instance.targetValuePct = ((Gizmo_Slider)(Gizmo_OxygenProvider)__instance).Target;
		}
	}

	private static void PostOxygenGizmoOnGUI(Gizmo_Slider __instance)
	{
		if (MP.IsInMultiplayer && !(__instance.GetType() != typeof(Gizmo_OxygenProvider)))
		{
			MP.WatchEnd();
		}
	}

	private static bool PrePasteBarrierColor()
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		Color? colorClipboard = Building_VacBarrier_Recolorable.ColorClipboard;
		if (!colorClipboard.HasValue)
		{
			Messages.Message("ClipboardInvalidColor".Translate(), MessageTypeDefOf.RejectInput, historical: false);
			return false;
		}
		foreach (object selectedObject in Find.Selector.SelectedObjects)
		{
			Building_VacBarrier_Recolorable val = (Building_VacBarrier_Recolorable)((selectedObject is Building_VacBarrier_Recolorable) ? selectedObject : null);
			if (val != null)
			{
				SyncedSetBarrierColor((Thing)(object)val, colorClipboard.Value.r, colorClipboard.Value.g, colorClipboard.Value.b);
			}
		}
		return false;
	}

	private static bool PreVacBarrierSaveColor(Dialog_VacBarrierColorPicker __instance, Color color)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		Building_VacBarrier_Recolorable[] extraVacBarriers = __instance.extraVacBarriers;
		if (extraVacBarriers != null)
		{
			Building_VacBarrier_Recolorable[] array = extraVacBarriers;
			for (int i = 0; i < array.Length; i++)
			{
				SyncedSetBarrierColor((Thing)(object)array[i], color.r, color.g, color.b);
			}
		}
		return false;
	}

	private static void SyncedSetBarrierColor(Thing barrier, float r, float g, float b)
	{
		Building_VacBarrier_Recolorable val = (Building_VacBarrier_Recolorable)(object)((barrier is Building_VacBarrier_Recolorable) ? barrier : null);
		if (val != null)
		{
			val.barrierColor = new Color(r, g, b);
			((Thing)(object)val).Notify_ColorChanged();
		}
	}

	private static void PostMapSetupInitNewFactionData(Map map, Faction f)
	{
		//IL_007e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0088: Expected Obj, but got Unknown
		object obj = mpCompMethod?.Invoke(null, new object[1] { map });
		if (obj == null || !(factionDataField?.GetValue(obj) is IDictionary dictionary))
		{
			return;
		}
		object obj2 = dictionary[f.loadID];
		if (obj2 != null)
		{
			AreaManager areaManager = (AreaManager)(areaManagerField?.GetValue(obj2));
			if (areaManager != null && areaManager.Get<Area_BuildVacBarrierRoof>() == null)
			{
				areaManager.areas.Add((Area)new Area_BuildVacBarrierRoof(areaManager));
			}
		}
	}
}
