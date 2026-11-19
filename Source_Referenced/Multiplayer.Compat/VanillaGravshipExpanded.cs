using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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

	private static ISyncField oxygenTargetValuePctField;

	private static Action<PlanetTile> closeGravshipSession;

	private static Func<PlanetTile, bool> hasGravshipSession;

	private static MethodInfo mpCompMethod;

	private static FieldInfo factionDataField;

	private static FieldInfo areaManagerField;

	public VanillaGravshipExpanded(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
		PatchingUtilities.PatchPushPopRand("VanillaGravshipExpanded.Projectile_Gauss:DrawAt", (HarmonyMethod)null);
		PatchingUtilities.PatchPushPopRand("VanillaGravshipExpanded.Projectile_JavelinRocket:DrawAt", (HarmonyMethod)null);
	}

	private static void LatePatch()
	{
		MP.RegisterSyncMethod(typeof(Building_TargetingTerminal), "LinkTo");
		MP.RegisterSyncMethod(typeof(Building_TargetingTerminal), "Unlink");
		MP.RegisterSyncMethod(typeof(Building_GravshipTurret), "LinkTo");
		MP.RegisterSyncMethod(typeof(Building_GravshipTurret), "Unlink");
		MP.RegisterSyncMethod(typeof(CompWorldArtillery), "StartAttack");
		MP.RegisterSyncMethod(typeof(CompWorldArtillery), "Reset");
		PatchingUtilities.PatchCancelInInterface(new string[1] { "VanillaGravshipExpanded.Building_TurretGun_OrderAttack_Patch:Prefix" });
		PatchingUtilities.PatchCancelInInterface(new string[1] { "VanillaGravshipExpanded.Building_TurretGun_ResetForcedTarget_Patch:Prefix" });
		MpCompat.RegisterLambdaDelegate(typeof(Building_GravshipBlackBox), "GetGizmos", new int[1]);
		MpCompat.RegisterLambdaMethod(typeof(Building_SealantPopper), "GetGizmos", new int[1] { 1 });
		MpCompat.RegisterLambdaMethod(typeof(Building_Agrocell), "GetGizmos", new int[1]);
		MP.RegisterSyncMethod(typeof(CompGravheatAbsorber), "AbsorbGravheat");
		PatchingUtilities.PatchCancelInInterfaceSetResultToTrue(new string[1] { "VanillaGravshipExpanded.MainTabWindow_Research_DoBeginResearch_Patch:Prefix" });
		PatchingUtilities.PatchCancelInInterfaceSetResultToTrue(new string[1] { "VanillaGravshipExpanded.ResearchManager_SetCurrentProject_Patch:Prefix" });
		PatchingUtilities.PatchCancelInInterface(new string[1] { "VanillaGravshipExpanded.ResearchManager_StopProject_Patch:Prefix" });
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(Dialog_ConfigureVacuumRequirement), "SetSelectedVacCheckpointsTo"), new HarmonyMethod(typeof(VanillaGravshipExpanded), "PreSetSelectedVacCheckpoints"));
		MP.RegisterSyncMethod(typeof(VanillaGravshipExpanded), "SyncedSetVacCheckpoints").SetContext(SyncContext.MapSelected);
		maintenanceThresholdSync = MP.RegisterSyncField(typeof(World_ExposeData_Patch), "maintenanceThreshold");
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(Window_SetDesiredMaintenance), "DoWindowContents"), new HarmonyMethod(typeof(VanillaGravshipExpanded), "PreMaintenanceDoWindowContents"), new HarmonyMethod(typeof(VanillaGravshipExpanded), "PostMaintenanceDoWindowContents"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(Window_RenameAsteroid), "DoWindowContents"), new HarmonyMethod(typeof(VanillaGravshipExpanded), "PreRenameDoWindowContents"), new HarmonyMethod(typeof(VanillaGravshipExpanded), "PostRenameDoWindowContents"));
		MP.RegisterSyncMethod(typeof(VanillaGravshipExpanded), "SyncedRenameAsteroid");
		MpCompat.RegisterLambdaDelegate(typeof(Building_Gravlift), "GetGizmos", new int[1] { 2 });
		MethodInfo lambda = MpMethodUtil.GetLambda(typeof(Building_VacBarrier_Recolorable), "GetGizmos", MethodType.Normal, (Type[])null, 2);
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
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(WorldComponent_GravshipController_TakeoffEnded_Patch), "Prefix"), null, new HarmonyMethod(typeof(VanillaGravshipExpanded), "PostTakeoffEndedPatch"));
		MP.RegisterSyncMethod(typeof(VanillaGravshipExpanded), "SyncedSettleTile");
		MP.RegisterSyncMethod(typeof(VanillaGravshipExpanded), "SyncedAbandonTile");
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(Dialog_NamePlayerGravship), "Named"), new HarmonyMethod(typeof(VanillaGravshipExpanded), "PreGravshipNamed"));
		MP.RegisterSyncMethod(typeof(VanillaGravshipExpanded), "SyncedGravshipNamed");
		MP.RegisterSyncMethod(AccessTools.PropertySetter(typeof(CompApparelOxygenProvider), "AutomaticRechargeEnabled"));
		oxygenTargetValuePctField = MP.RegisterSyncField(typeof(Gizmo_OxygenProvider), "targetValuePct").SetBufferChanges();
		AccessTools.Field(oxygenTargetValuePctField.GetType(), "targetType").SetValue(oxygenTargetValuePctField, typeof(Gizmo_OxygenProvider));
		MP.RegisterSyncWorker<Gizmo_Slider>(SyncOxygenGizmo, typeof(Gizmo_OxygenProvider));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(Gizmo_Slider), "GizmoOnGUI"), new HarmonyMethod(typeof(VanillaGravshipExpanded), "PreOxygenGizmoOnGUI"), new HarmonyMethod(typeof(VanillaGravshipExpanded), "PostOxygenGizmoOnGUI"));
		mpCompMethod = AccessTools.Method(AccessTools.TypeByName("Multiplayer.Client.Extensions"), "MpComp");
		factionDataField = AccessTools.Field(AccessTools.TypeByName("Multiplayer.Client.MultiplayerMapComp"), "factionData");
		areaManagerField = AccessTools.Field(AccessTools.TypeByName("Multiplayer.Client.FactionMapData"), "areaManager");
		MpCompat.harmony.Patch(AccessTools.Method(AccessTools.TypeByName("Multiplayer.Client.MapSetup"), "InitNewFactionData"), null, new HarmonyMethod(typeof(VanillaGravshipExpanded), "PostMapSetupInitNewFactionData"));
		RegisterLandingLaunchInfoSync();
	}

	private static void RegisterLandingLaunchInfoSync()
	{
		Type landingPatch = AccessTools.TypeByName("VanillaGravshipExpanded.WorldComponent_GravshipController_LandingEnded_Patch");
		if (landingPatch == null)
		{
			Log.Error("[MPCompat] VGE landing info: LandingEnded patch type not found; extended launch info will not sync and the landing may diverge.");
			return;
		}

		try
		{
			MP.RegisterSyncMethod(typeof(VanillaGravshipExpanded), nameof(SyncExtendedLaunchInfo)).ExposeParameter(1);
		}
		catch (Exception e)
		{
			Log.Error($"[MPCompat] VGE landing info: failed to register SyncExtendedLaunchInfo. The landing will diverge.\n{e}");
			return;
		}

		MpCompat.harmony.Patch(
			AccessTools.Method(typeof(GravshipLandingMarker), "BeginLanding"),
			new HarmonyMethod(typeof(VanillaGravshipExpanded), nameof(BeforeBeginLanding)) { priority = 800 });

		MethodInfo postfix = AccessTools.DeclaredMethod(landingPatch, "Postfix");
		if (postfix == null)
		{
			Log.Error("[MPCompat] VGE landing info: LandingEnded Postfix not found; the missing-info guard is not installed.");
			return;
		}

		MpCompat.harmony.Patch(postfix,
			new HarmonyMethod(typeof(VanillaGravshipExpanded), nameof(BeforeLandingPostfix)));
		Log.Message("[MPCompat] VGE landing info sync installed.");
	}

	private static bool BeforeBeginLanding(GravshipLandingMarker __instance)
	{
		if (!MP.IsInMultiplayer || !MP.InInterface)
			return true;

		Gravship gravship = __instance?.gravship;
		Building_GravEngine engine = gravship?.Engine;
		LaunchInfo launchInfo = engine?.launchInfo;
		ExtendedLaunchInfo info = launchInfo?.ExtendedInfo(createIfMissing: false);
		if (info == null)
		{
			Log.Error($"[MPCompat] VGE landing info: no ExtendedLaunchInfo for {gravship?.Label ?? "unknown gravship"} at marker {__instance?.Tile}; clients will run the landing postfix without it.");
			return true;
		}

		SyncExtendedLaunchInfo(engine, info);
		return true;
	}

	public static void SyncExtendedLaunchInfo(Building_GravEngine engine, ExtendedLaunchInfo info)
	{
		if (engine == null || info == null)
		{
			Log.Error($"[MPCompat] VGE landing info: invalid payload engine={engine}, info={info}; the landing will diverge.");
			return;
		}

		LaunchInfo launchInfo = engine.launchInfo;
		if (launchInfo == null)
		{
			Log.Error($"[MPCompat] VGE landing info: {engine} has no LaunchInfo; the extended info was not applied.");
			return;
		}

		LaunchInfo_ExposeData_Patch.extendedLaunchInfos[launchInfo] = info;
		Log.Message($"[MPCompat] VGE landing info applied for {engine} (source tile {info.launchSourceTile}).");
	}

	private static bool BeforeLandingPostfix((Gravship gravship, Dictionary<LandingOutcomeDef, float> outcomes) __0)
	{
		LaunchInfo launchInfo = __0.gravship?.Engine?.launchInfo;
		ExtendedLaunchInfo info = launchInfo?.ExtendedInfo(createIfMissing: false);
		if (info == null)
		{
			Log.Error("[MPCompat] VGE landing postfix skipped: no extended launch info reached this client. " +
			          "The landing would NRE in TryTriggerLaunchBoon and diverge; fix the landing info sync.");
			return false;
		}

		return true;
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
		if (!__instance.ritual.def.IsGravshipLaunch())
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

	private static void PostTakeoffEndedPatch(WorldComponent_GravshipController __0)
	{
		if (!MP.IsInMultiplayer || Find.WindowStack.Count == 0 || !(Find.WindowStack.Windows[Find.WindowStack.Count - 1] is Dialog_MessageBox dialog_MessageBox))
		{
			return;
		}
		MapParent mapParent = __0.map?.Parent;
		if (mapParent == null)
		{
			return;
		}
		if (dialog_MessageBox.buttonBAction != null)
		{
			dialog_MessageBox.buttonBAction = () =>
			{
				SyncedSettleTile(mapParent);
			};
		}
		if (dialog_MessageBox.buttonAAction != null)
		{
			dialog_MessageBox.buttonAAction = () =>
			{
				SyncedAbandonTile(mapParent);
			};
		}
	}

	private static void SyncedSettleTile(MapParent mapParent)
	{
		CloseMapDecisionDialog();
		if (mapParent?.Map != null && (bool)mapParent.CanBeSettled)
		{
			SettleInExistingMapUtility.Settle(mapParent.Map);
		}
	}

	private static void SyncedAbandonTile(MapParent mapParent)
	{
		CloseMapDecisionDialog();
		if (mapParent?.Map == null)
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

	private static void CloseMapDecisionDialog()
	{
		for (int num = Find.WindowStack.Windows.Count - 1; num >= 0; num--)
		{
			if (Find.WindowStack.Windows[num] is Dialog_MessageBox dialog_MessageBox)
			{
				dialog_MessageBox.Close();
			}
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
		if (MP.IsInMultiplayer && !(__instance.GetType() != typeof(Gizmo_OxygenProvider)))
		{
			MP.WatchBegin();
			oxygenTargetValuePctField.Watch(__instance);
		}
	}

	private static void PostOxygenGizmoOnGUI(Gizmo_Slider __instance)
	{
		if (MP.IsInMultiplayer && !(__instance.GetType() != typeof(Gizmo_OxygenProvider)))
		{
			MP.WatchEnd();
		}
	}

	private static void SyncOxygenGizmo(SyncWorker sync, ref Gizmo_Slider gizmo)
	{
		if (sync.isWriting)
		{
			sync.Write((ThingComp)((Gizmo_OxygenProvider)gizmo).oxygenProvider);
			return;
		}
		CompApparelOxygenProvider compApparelOxygenProvider = (CompApparelOxygenProvider)sync.Read<ThingComp>();
		gizmo = (Gizmo_Slider)GetOxygenConfigurationGizmo(compApparelOxygenProvider);
	}

	// The donor VGE build cached the gizmo in the oxygenConfigurationGizmo field. The installed
	// build has no such field and creates it in GetConfigurationGizmo instead, so use the field
	// when it exists and fall back to the factory.
	private static Gizmo GetOxygenConfigurationGizmo(CompApparelOxygenProvider provider)
	{
		FieldInfo cached = AccessTools.Field(typeof(CompApparelOxygenProvider), "oxygenConfigurationGizmo");
		if (cached != null)
		{
			return (Gizmo)cached.GetValue(provider);
		}
		return (Gizmo)AccessTools.Method(typeof(CompApparelOxygenProvider), "GetConfigurationGizmo").Invoke(provider, null);
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
			if (selectedObject is Building_VacBarrier_Recolorable barrier)
			{
				SyncedSetBarrierColor(barrier, colorClipboard.Value.r, colorClipboard.Value.g, colorClipboard.Value.b);
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
				SyncedSetBarrierColor(array[i], color.r, color.g, color.b);
			}
		}
		return false;
	}

	private static void SyncedSetBarrierColor(Thing barrier, float r, float g, float b)
	{
		if (barrier is Building_VacBarrier_Recolorable building_VacBarrier_Recolorable)
		{
			building_VacBarrier_Recolorable.barrierColor = new Color(r, g, b);
			building_VacBarrier_Recolorable.Notify_ColorChanged();
		}
	}

	private static void PostMapSetupInitNewFactionData(Map map, Faction f)
	{
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
				areaManager.areas.Add(new Area_BuildVacBarrierRoof(areaManager));
			}
		}
	}
}
