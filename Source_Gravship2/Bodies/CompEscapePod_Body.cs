using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using VanillaGravshipExpanded2;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Verse.Sound;

namespace Multiplayer.Compat.Workshop17.Bodies;

public class CompEscapePod_Body
{
	public static ThingOwner get_SearchableContents(CompEscapePod __instance)
	{
		return (ThingOwner)(object)__instance.heldPawn;
	}

	public static Pawn get_Occupant(CompEscapePod __instance)
	{
		if (!((ThingOwner)__instance.heldPawn).Any)
		{
			return null;
		}
		return __instance.heldPawn[0];
	}

	public static bool get_CanSetAutoRebuild(CompEscapePod __instance)
	{
		if ((((Thing)((ThingComp)__instance).parent).Faction == Faction.OfPlayer || __instance.isCurrentlyStolen) && ((BuildableDef)((Thing)((ThingComp)__instance).parent).def).blueprintDef != null)
		{
			return ((BuildableDef)((Thing)((ThingComp)__instance).parent).def).IsResearchFinished;
		}
		return false;
	}

	public static bool get_IsCurrentPlanetLayerSupported(CompEscapePod __instance)
	{
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		//IL_0055: Unknown result type (might be due to invalid IL or missing references)
		Map map = ((Thing)((ThingComp)__instance).parent).Map;
		object obj;
		PlanetTile tile;
		if (map == null)
		{
			obj = null;
		}
		else
		{
			tile = map.Tile;
			PlanetLayer layer = tile.Layer;
			obj = ((layer != null) ? layer.Def : null);
		}
		if (obj != null && __instance.Props.layerWhitelist != null)
		{
			List<PlanetLayerDef> layerWhitelist = __instance.Props.layerWhitelist;
			tile = ((Thing)((ThingComp)__instance).parent).Map.Tile;
			return layerWhitelist.Contains(tile.LayerDef);
		}
		return false;
	}

	public static CompProperties_EscapePod get_Props(CompEscapePod __instance)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Expected Obj, but got Unknown
		return (CompProperties_EscapePod)((ThingComp)__instance).props;
	}

	public static void CompTick(CompEscapePod __instance)
	{
		((ThingComp)__instance).CompTick();
		VGE2_MapComponent mapComp = __instance.mapComp;
		if (mapComp != null && mapComp.EvacuationActive && __instance.Props.evacuationSustainerSound != null)
		{
			__instance.ActivateAndMaintainSustainer();
		}
		else if (__instance.evacuationSustainer != null)
		{
			__instance.evacuationSustainer.Cleanup();
			__instance.evacuationSustainer = null;
		}
	}

	public static void CompTickInterval(CompEscapePod __instance, int delta)
	{
		//IL_0081: Unknown result type (might be due to invalid IL or missing references)
		//IL_0086: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a1: Unknown result type (might be due to invalid IL or missing references)
		((ThingComp)__instance).CompTickInterval(delta);
		if (((ThingOwner)__instance.heldPawn).Any && ((Thing)((ThingComp)__instance).parent).Spawned)
		{
			if (Find.TickManager.TicksGame >= __instance.triggerOnTick)
			{
				__instance.Trigger();
				if (__instance.progressBarEffecter != null)
				{
					__instance.progressBarEffecter.Cleanup();
					__instance.progressBarEffecter = null;
				}
				return;
			}
			if (__instance.progressBarEffecter == null)
			{
				__instance.progressBarEffecter = EffecterDefOf.ProgressBar.Spawn();
			}
			__instance.progressBarEffecter.EffectTick(((TargetInfo)((Thing)(object)((ThingComp)__instance).parent)), TargetInfo.Invalid);
			MoteProgressBar mote = ((SubEffecter_ProgressBar)__instance.progressBarEffecter.children[0]).mote;
			mote.progress = 1f - (float)(__instance.triggerOnTick - Find.TickManager.TicksGame) / (float)__instance.Props.launchDuration;
			mote.offsetZ = -0.8f;
		}
		else if (__instance.progressBarEffecter != null)
		{
			__instance.progressBarEffecter.Cleanup();
			__instance.progressBarEffecter = null;
		}
	}

	public static void GetChildHolders(CompEscapePod __instance, List<IThingHolder> outChildren)
	{
		ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, (IList<Thing>)__instance.GetDirectlyHeldThings());
	}

	public static ThingOwner GetDirectlyHeldThings(CompEscapePod __instance)
	{
		return (ThingOwner)(object)__instance.heldPawn;
	}

	public static IEnumerable<Gizmo> CompGetGizmosExtra(CompEscapePod __instance)
	{
		foreach (Gizmo item in _003C_003En__0(__instance))
		{
			yield return item;
		}
		Faction faction = ((Thing)((ThingComp)__instance).parent).Faction;
		if (faction != null && faction.IsPlayer)
		{
			if (__instance.CanSetAutoRebuild)
			{
				yield return (Gizmo)new Command_Toggle
				{
					defaultLabel = ((string)(Translator.Translate("CommandAutoRebuild_Building"))),
					defaultDesc = ((string)(TranslatorFormattedStringExtensions.Translate("VGE_CommandAutoRebuild_EscapePod", NamedArgumentUtility.Named((object)((ThingComp)__instance).parent, "BUILDING")))),
					hotKey = KeyBindingDefOf.Misc3,
					icon = (Texture)(object)__instance.Props.AutoRebuildGizmo,
					isActive = () => _003CCompGetGizmosExtra_003Eb__22_0(__instance),
					toggleAction = () => _003CCompGetGizmosExtra_003Eb__22_1(__instance)
				};
			}
			if (__instance.mapComp != null && __instance.IsCurrentPlanetLayerSupported)
			{
				yield return (Gizmo)new Command_Toggle_NoInheritInteractions
				{
					defaultLabel = ((string)(__instance.mapComp.EvacuationActive ? Translator.Translate("VGE_EscapePod_CancelEvacuation") : Translator.Translate("VGE_EscapePod_Evacuation"))),
					defaultDesc = ((string)(Translator.Translate("VGE_EscapePod_EvacuationDesc"))),
					icon = (Texture)(object)__instance.Props.EvacuationGizmo,
					isActive = () => _003CCompGetGizmosExtra_003Eb__22_2(__instance),
					toggleAction = () => _003CCompGetGizmosExtra_003Eb__22_3(__instance),
					alsoClickIfOtherInGroupClicked = false
				};
			}
		}
	}

	public static IEnumerable<FloatMenuOption> CompFloatMenuOptions(CompEscapePod __instance, Pawn selPawn)
	{
		CompEscapePod val = __instance;
		foreach (FloatMenuOption item in _003C_003En__1(__instance, selPawn))
		{
			yield return item;
		}
		Faction faction = ((Thing)((ThingComp)__instance).parent).Faction;
		if (faction != null && !faction.IsPlayer)
		{
			yield break;
		}
		Map map = ((Thing)((ThingComp)__instance).parent).Map;
		object obj;
		if (map == null)
		{
			obj = null;
		}
		else
		{
			PlanetTile tile = map.Tile;
			obj = tile.Layer;
		}
		if (obj == null)
		{
			yield break;
		}
		Rand.PushState();
		PlanetTile val2;
		try
		{
			val2 = __instance.FindClosestValidTile();
		}
		finally
		{
			Rand.PopState();
		}
		FloatMenuOption val3 = new FloatMenuOption(((string)(Translator.Translate("VGE_EscapePod_Enter"))), (Action)(() =>
		{
			//IL_001b: Unknown result type (might be due to invalid IL or missing references)
			selPawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(InternalDefOf.VGE_EscapePod_Enter, ((LocalTargetInfo)((Thing)(object)((ThingComp)val).parent))), (JobTag?)((JobTag)0), false);
			val.ClaimIfNeeded();
		}), (MenuOptionPriority)4, (Action<Rect>)null, (Thing)null, 0f, (Func<Rect, bool>)null, (WorldObject)null, true, 0);
		FloatMenuOption insertPawn = null;
		FloatMenuOption insertCarriedPawn = null;
		if (!selPawn.IsMutant)
		{
			insertPawn = new FloatMenuOption(((string)(Translator.Translate("VGE_EscapePod_InsertPawn"))), (Action)(() =>
			{
				Find.Targeter.BeginTargeting(val.Props.insertPawnTargetingParameters, (Action<LocalTargetInfo>)((LocalTargetInfo target) =>
				{
					//IL_00b8: Unknown result type (might be due to invalid IL or missing references)
					//IL_00bd: Unknown result type (might be due to invalid IL or missing references)
					//IL_008f: Unknown result type (might be due to invalid IL or missing references)
					if (target.Thing != null && (target.Thing.Faction == null || target.Thing.Faction == Faction.OfPlayer) && (target.Pawn == null || ((Thing)target.Pawn).Faction == Faction.OfPlayer || target.Pawn.IsPrisonerOfColony || target.Pawn.IsSlaveOfColony))
					{
						if (selPawn == target.Pawn)
						{
							selPawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(InternalDefOf.VGE_EscapePod_Enter, ((LocalTargetInfo)((Thing)(object)((ThingComp)val).parent))), (JobTag?)((JobTag)0), false);
						}
						else
						{
							Job val9 = JobMaker.MakeJob(InternalDefOf.VGE_EscapePod_InsertPawn, ((LocalTargetInfo)((Thing)(object)((ThingComp)val).parent)), target);
							val9.count = 1;
							selPawn.jobs.TryTakeOrderedJob(val9, (JobTag?)((JobTag)0), false);
						}
						val.ClaimIfNeeded();
					}
				}), (Pawn)null, (Action)null, (Texture2D)null, true);
			}), (MenuOptionPriority)4, (Action<Rect>)null, (Thing)null, 0f, (Func<Rect, bool>)null, (WorldObject)null, true, 0);
			Pawn_CarryTracker carryTracker = selPawn.carryTracker;
			Thing val4 = ((carryTracker != null) ? carryTracker.CarriedThing : null);
			Pawn carriedPawn = (Pawn)(object)((val4 is Pawn) ? val4 : null);
			if (carriedPawn != null)
			{
				insertCarriedPawn = new FloatMenuOption(((string)(TranslatorFormattedStringExtensions.Translate("VGE_EscapePod_InsertCarriedPawn", NamedArgumentUtility.Named((object)carriedPawn, "PAWN")))), (Action)(() =>
				{
					//IL_0015: Unknown result type (might be due to invalid IL or missing references)
					//IL_0020: Unknown result type (might be due to invalid IL or missing references)
					Job val9 = JobMaker.MakeJob(InternalDefOf.VGE_EscapePod_InsertPawnDrafted, ((LocalTargetInfo)((Thing)(object)((ThingComp)val).parent)), ((LocalTargetInfo)((Thing)(object)carriedPawn)));
					val9.count = 1;
					selPawn.jobs.TryTakeOrderedJob(val9, (JobTag?)((JobTag)0), false);
					val.ClaimIfNeeded();
				}), (MenuOptionPriority)4, (Action<Rect>)null, (Thing)null, 0f, (Func<Rect, bool>)null, (WorldObject)null, true, 0);
			}
		}
		AcceptanceReport val5 = __instance.IsFloatMenuEnabled(selPawn, val2);
		if (!val5.Accepted)
		{
			val3.Disabled = true;
			FloatMenuOption val6 = val3;
			val6.Label = val6.Label + ": " + val5.Reason;
			if (insertPawn != null)
			{
				insertPawn.Disabled = true;
				FloatMenuOption val7 = insertPawn;
				val7.Label = val7.Label + ": " + val5.Reason;
			}
			if (insertCarriedPawn != null)
			{
				insertCarriedPawn.Disabled = true;
				FloatMenuOption val8 = insertCarriedPawn;
				val8.Label = val8.Label + ": " + val5.Reason;
			}
		}
		else
		{
			val3 = FloatMenuUtility.DecoratePrioritizedTask(val3, selPawn, ((LocalTargetInfo)((Thing)(object)((ThingComp)__instance).parent)), "ReservedBy", (ReservationLayerDef)null);
			if (insertPawn != null)
			{
				insertPawn = FloatMenuUtility.DecoratePrioritizedTask(insertPawn, selPawn, ((LocalTargetInfo)((Thing)(object)((ThingComp)__instance).parent)), "ReservedBy", (ReservationLayerDef)null);
			}
			if (insertCarriedPawn != null)
			{
				insertCarriedPawn = FloatMenuUtility.DecoratePrioritizedTask(insertCarriedPawn, selPawn, ((LocalTargetInfo)((Thing)(object)((ThingComp)__instance).parent)), "ReservedBy", (ReservationLayerDef)null);
			}
		}
		yield return val3;
		if (insertPawn != null)
		{
			yield return insertPawn;
		}
		if (insertCarriedPawn != null)
		{
			yield return insertCarriedPawn;
		}
	}

	public static AcceptanceReport IsFloatMenuEnabled(CompEscapePod __instance, Pawn pawn, PlanetTile tile)
	{
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_008b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0062: Unknown result type (might be due to invalid IL or missing references)
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_0079: Unknown result type (might be due to invalid IL or missing references)
		//IL_007e: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ae: Unknown result type (might be due to invalid IL or missing references)
		if (__instance.GetDirectlyHeldThings().Any)
		{
			return ((AcceptanceReport)(Translator.Translate("VGE_EscapePod_Occupied")));
		}
		if (!tile.Valid)
		{
			PlanetTile tile2 = ((Thing)((ThingComp)__instance).parent).Map.Tile;
			NamedArgument val = NamedArgumentUtility.Named((object)tile2.LayerDef.gerundLabel, "GERUND");
			tile2 = ((Thing)((ThingComp)__instance).parent).Map.Tile;
			return ((AcceptanceReport)(TranslatorFormattedStringExtensions.Translate("CannotPerformPlanetLayer", val, NamedArgumentUtility.Named((object)((Def)tile2.LayerDef).label, "LAYER"))));
		}
		if (!ReachabilityUtility.CanReach(pawn, ((LocalTargetInfo)((Thing)(object)((ThingComp)__instance).parent)), (PathEndMode)1, (Danger)3, false, false, (TraverseMode)0))
		{
			TaggedString val2 = Translator.Translate("NoPath");
			return ((AcceptanceReport)(val2.CapitalizeFirst()));
		}
		return ((AcceptanceReport)(true));
	}

	public static void Trigger(CompEscapePod __instance)
	{
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0081: Unknown result type (might be due to invalid IL or missing references)
		//IL_0088: Expected Obj, but got Unknown
		//IL_008a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0094: Expected Obj, but got Unknown
		//IL_00c8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ed: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f4: Expected Obj, but got Unknown
		//IL_0107: Unknown result type (might be due to invalid IL or missing references)
		//IL_0108: Unknown result type (might be due to invalid IL or missing references)
		//IL_012b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0135: Expected Obj, but got Unknown
		//IL_0117: Unknown result type (might be due to invalid IL or missing references)
		//IL_0121: Expected Obj, but got Unknown
		//IL_015e: Unknown result type (might be due to invalid IL or missing references)
		Map map = ((Thing)((ThingComp)__instance).parent).Map;
		IntVec3 position = ((Thing)((ThingComp)__instance).parent).Position;
		PlanetTile destinationTile = __instance.FindClosestValidTile();
		if (!destinationTile.Valid)
		{
			((ThingOwner)__instance.heldPawn).TryDropAll(position, map, (ThingPlaceMode)1, (Action<Thing, int>)null, (Predicate<IntVec3>)null, true);
			return;
		}
		Pawn occupant = __instance.Occupant;
		Lord lord = LordUtility.GetLord(occupant);
		if (lord != null)
		{
			lord.Notify_PawnLost(occupant, (PawnLostCondition)6, (DamageInfo?)null);
		}
		ThingOwner directlyHeldThings = __instance.GetDirectlyHeldThings();
		ActiveTransporter val = (ActiveTransporter)ThingMaker.MakeThing(__instance.Props.activeTransporterDef ?? ThingDefOf.ActiveDropPod, (ThingDef)null);
		val.Contents = new ActiveTransporterInfo();
		val.Contents.innerContainer.TryAddRangeOrTransfer((IEnumerable<Thing>)directlyHeldThings, true, true);
		val.Contents.sentTransporterDef = ((Thing)((ThingComp)__instance).parent).def;
		((Thing)val).Rotation = ((Thing)((ThingComp)__instance).parent).Rotation;
		FlyShipLeaving val2 = (FlyShipLeaving)SkyfallerMaker.MakeSkyfaller(__instance.Props.skyfallerLeaving ?? ThingDefOf.DropPodLeaving, (Thing)(object)val);
		val2.groupID = Find.UniqueIDsManager.GetNextTransporterGroupID();
		val2.destinationTile = destinationTile;
		if (__instance.isCurrentlyStolen)
		{
			val2.arrivalAction = (TransportersArrivalAction)new TransportersArrivalAction_DestroyOrPassToWorld();
		}
		else
		{
			val2.arrivalAction = (TransportersArrivalAction)new TransportersArrivalAction_EscapePod(((Thing)occupant).Faction);
		}
		val2.worldObjectDef = __instance.Props.worldObjectDef ?? WorldObjectDefOf.TravellingTransporters;
		((Thing)((ThingComp)__instance).parent).Destroy((DestroyMode)0);
		GenSpawn.Spawn((Thing)(object)val2, position, map, (WipeMode)0);
		__instance.CheckAutoRebuild(map);
	}

	public static void Enter(CompEscapePod __instance, Pawn pawn)
	{
		__instance.triggerOnTick = Find.TickManager.TicksGame + __instance.Props.launchDuration;
		bool flag = ((Thing)pawn).DeSpawnOrDeselect((DestroyMode)0);
		if (((Thing)pawn).holdingOwner != null)
		{
			((Thing)pawn).holdingOwner.TryTransferToContainer((Thing)(object)pawn, (ThingOwner)(object)__instance.heldPawn, true);
		}
		else
		{
			((ThingOwner)__instance.heldPawn).TryAdd((Thing)(object)pawn, true);
		}
		if (flag)
		{
			Find.Selector.Select((object)pawn, true, true);
		}
	}

	public static PlanetTile FindClosestValidTile(CompEscapePod __instance)
	{
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		if (!__instance.IsCurrentPlanetLayerSupported)
		{
			return PlanetTile.Invalid;
		}
		PlanetTile result = default;
		TileFinder.TryFindPassableTileWithTraversalDistance(((PlanetLayer)Find.WorldGrid.Surface).GetClosestTile_NewTemp(((Thing)((ThingComp)__instance).parent).Map.Tile, false), 0, int.MaxValue, out result, (Predicate<PlanetTile>)__instance.IsPlanetTileValid, true, (TileFinderMode)1, true, true);
		return result;
	}

	public static bool IsPlanetTileValid(CompEscapePod __instance, PlanetTile tile)
	{
		return true;
	}

	public static void PostSwapMap(CompEscapePod __instance)
	{
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		((ThingComp)__instance).PostSwapMap();
		((ThingOwner)__instance.heldPawn).TryDropAll(((Thing)((ThingComp)__instance).parent).Position, ((Thing)((ThingComp)__instance).parent).Map, (ThingPlaceMode)1, (Action<Thing, int>)null, (Predicate<IntVec3>)null, true);
	}

	public static void PostSpawnSetup(CompEscapePod __instance, bool respawningAfterLoad)
	{
		//IL_0071: Unknown result type (might be due to invalid IL or missing references)
		((ThingComp)__instance).PostSpawnSetup(respawningAfterLoad);
		if (__instance.IsCurrentPlanetLayerSupported)
		{
			__instance.mapComp = VGE2_MapComponent.GetCompFast(((Thing)((ThingComp)__instance).parent).Map);
			__instance.mapComp?.activeEscapePods.Add(__instance);
		}
		if (!respawningAfterLoad && !((Thing)((ThingComp)__instance).parent).BeingTransportedOnGravship)
		{
			__instance.autoRebuild = __instance.CanSetAutoRebuild && ((Area)((Thing)((ThingComp)__instance).parent).Map.areaManager.Home)[((Thing)((ThingComp)__instance).parent).Position];
		}
	}

	public static void PostDeSpawn(CompEscapePod __instance, Map map, DestroyMode mode = (DestroyMode)0)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0031: Invalid comparison between Unknown and I4
		//IL_003f: Unknown result type (might be due to invalid IL or missing references)
		((ThingComp)__instance).PostDeSpawn(map, mode);
		if (__instance.IsCurrentPlanetLayerSupported)
		{
			__instance.mapComp?.activeEscapePods.Remove(__instance);
			__instance.mapComp = null;
		}
		if ((int)mode != 1)
		{
			((ThingOwner)__instance.heldPawn).TryDropAll(((Thing)((ThingComp)__instance).parent).Position, map, (ThingPlaceMode)1, (Action<Thing, int>)null, (Predicate<IntVec3>)null, true);
		}
		if (__instance.progressBarEffecter != null)
		{
			__instance.progressBarEffecter.Cleanup();
			__instance.progressBarEffecter = null;
		}
	}

	public static void PostExposeData(CompEscapePod __instance)
	{
		//IL_0056: Unknown result type (might be due to invalid IL or missing references)
		//IL_005c: Invalid comparison between Unknown and I4
		((ThingComp)__instance).PostExposeData();
		Scribe_Deep.Look<ThingOwner<Pawn>>(ref __instance.heldPawn, "heldPawn", new object[1] { __instance });
		Scribe_Values.Look<int>(ref __instance.triggerOnTick, "triggerOnTick", 0, false);
		Scribe_Values.Look<bool>(ref __instance.autoRebuild, "autoRebuild", false, false);
		Scribe_Values.Look<bool>(ref __instance.isCurrentlyStolen, "isCurrentlyStolen", false, false);
		if ((int)Scribe.mode != 4)
		{
			return;
		}
		if (__instance.Occupant != null)
		{
			Faction faction = ((Thing)__instance.Occupant).Faction;
			if (faction == null || !faction.IsPlayer)
			{
				return;
			}
		}
		__instance.isCurrentlyStolen = false;
	}

	public static void CheckAutoRebuild(CompEscapePod __instance, Map map)
	{
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		//IL_0066: Unknown result type (might be due to invalid IL or missing references)
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		if (__instance.autoRebuild && __instance.CanSetAutoRebuild && map != null && ((AcceptanceReport)(GenConstruct.CanPlaceBlueprintAt((BuildableDef)(object)((Thing)((ThingComp)__instance).parent).def, ((Thing)((ThingComp)__instance).parent).Position, ((Thing)((ThingComp)__instance).parent).Rotation, map, false, (Thing)null, (Thing)null, ((Thing)((ThingComp)__instance).parent).Stuff, false, false, false))))
		{
			GenConstruct.PlaceBlueprintForBuild((BuildableDef)(object)((Thing)((ThingComp)__instance).parent).def, ((Thing)((ThingComp)__instance).parent).Position, map, ((Thing)((ThingComp)__instance).parent).Rotation, Faction.OfPlayer, ((Thing)((ThingComp)__instance).parent).Stuff, (Precept_ThingStyle)null, (ThingStyleDef)null, true);
		}
	}

	public static void ClaimIfNeeded(CompEscapePod __instance)
	{
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0040: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		if (!((Thing)((ThingComp)__instance).parent).def.CanHaveFaction || ((Thing)((ThingComp)__instance).parent).Faction == Faction.OfPlayer)
		{
			return;
		}
		((Thing)((ThingComp)__instance).parent).SetFaction(Faction.OfPlayer, (Pawn)null);
		CellRect val = GenAdj.OccupiedRect((Thing)(object)((ThingComp)__instance).parent);
		CellRect.Enumerator enumerator = val.GetEnumerator();
		try
		{
			while (enumerator.MoveNext())
			{
				FleckMaker.ThrowMetaPuffs(new TargetInfo(enumerator.Current, ((Thing)((ThingComp)__instance).parent).Map, false));
			}
		}
		finally
		{
			((IDisposable)enumerator/*cast due to constrained. prefix*/).Dispose();
		}
	}

	public static void ActivateAndMaintainSustainer(CompEscapePod __instance)
	{
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		if (__instance.evacuationSustainer == null || __instance.evacuationSustainer.Ended)
		{
			__instance.evacuationSustainer = SoundStarter.TrySpawnSustainer(__instance.Props.evacuationSustainerSound, SoundInfo.InMap(((TargetInfo)((Thing)(object)((ThingComp)__instance).parent)), (MaintenanceType)1));
		}
		__instance.evacuationSustainer.Maintain();
	}

	public static bool CompPreventClaimingBy(CompEscapePod __instance, Faction faction)
	{
		if (((Thing)__instance.Occupant).Faction == faction)
		{
			return ((ThingComp)__instance).CompPreventClaimingBy(faction);
		}
		return true;
	}

	public static bool _003CCompGetGizmosExtra_003Eb__22_0(CompEscapePod __instance)
	{
		return __instance.autoRebuild;
	}

	public static void _003CCompGetGizmosExtra_003Eb__22_1(CompEscapePod __instance)
	{
		__instance.autoRebuild = !__instance.autoRebuild;
		__instance.ClaimIfNeeded();
	}

	public static bool _003CCompGetGizmosExtra_003Eb__22_2(CompEscapePod __instance)
	{
		return __instance.mapComp.EvacuationActive;
	}

	public static void _003CCompGetGizmosExtra_003Eb__22_3(CompEscapePod __instance)
	{
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		if (__instance.mapComp.EvacuationActive)
		{
			__instance.mapComp.EvacuationActive = false;
		}
		else
		{
			Find.WindowStack.Add((Window)(object)Dialog_MessageBox.CreateConfirmation(Translator.Translate("VGE_EscapePod_EvacuationConfirmation"), (Action)(() => _003CCompGetGizmosExtra_003Eb__22_4(__instance)), true, (string)null, (WindowLayer)1));
		}
	}

	public static void _003CCompGetGizmosExtra_003Eb__22_4(CompEscapePod __instance)
	{
		__instance.mapComp.EvacuationActive = true;
		foreach (object selectedObject in Find.Selector.SelectedObjects)
		{
			ThingWithComps val = (ThingWithComps)((selectedObject is ThingWithComps) ? selectedObject : null);
			if (val != null)
			{
				CompEscapePod comp = val.GetComp<CompEscapePod>();
				if (comp != null)
				{
					comp.ClaimIfNeeded();
					comp.ActivateAndMaintainSustainer();
				}
			}
		}
	}

	public static IEnumerable<Gizmo> _003C_003En__0(CompEscapePod __instance)
	{
		return ((ThingComp)__instance).CompGetGizmosExtra();
	}

	public static IEnumerable<FloatMenuOption> _003C_003En__1(CompEscapePod __instance, Pawn selPawn)
	{
		return ((ThingComp)__instance).CompFloatMenuOptions(selPawn);
	}
}
