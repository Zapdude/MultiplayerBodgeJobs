using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using VanillaGravshipExpanded2;
using Verse;

namespace Multiplayer.Compat.Workshop17.Bodies;

public class TransportersArrivalAction_HellpodStrike_Body
{
	public static bool get_GeneratesMap(TransportersArrivalAction_HellpodStrike __instance)
	{
		return true;
	}

	public static void ExposeData(TransportersArrivalAction_HellpodStrike __instance)
	{
		((TransportersArrivalAction)__instance).ExposeData();
		Scribe_References.Look<MapParent>(ref __instance.mapParent, "mapParent", false);
		Scribe_References.Look<Faction>(ref __instance.launchingFaction, "launchingFaction", false);
		Scribe_Defs.Look<PawnsArrivalModeDef>(ref __instance.arrivalMode, "arrivalMode");
	}

	public static FloatMenuAcceptanceReport StillValid(TransportersArrivalAction_HellpodStrike __instance, IEnumerable<IThingHolder> pods, PlanetTile destinationTile)
	{
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0040: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		if (__instance.mapParent == null || ((WorldObject)__instance.mapParent).Destroyed || ((WorldObject)__instance.mapParent).Tile != destinationTile || __instance.arrivalMode == null)
		{
			return ((FloatMenuAcceptanceReport)(false));
		}
		FloatMenuAcceptanceReport val = ((TransportersArrivalAction)__instance).StillValid(pods, destinationTile);
		if (!((FloatMenuAcceptanceReport)(val)))
		{
			return val;
		}
		return ((FloatMenuAcceptanceReport)(true));
	}

	public static bool ShouldUseLongEvent(TransportersArrivalAction_HellpodStrike __instance, List<ActiveTransporterInfo> pods, PlanetTile tile)
	{
		if (__instance.mapParent != null)
		{
			return !__instance.mapParent.HasMap;
		}
		return true;
	}

	public static void Arrived(TransportersArrivalAction_HellpodStrike __instance, List<ActiveTransporterInfo> transporters, PlanetTile tile)
	{
		//IL_000a: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_008d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0092: Unknown result type (might be due to invalid IL or missing references)
		//IL_007a: Unknown result type (might be due to invalid IL or missing references)
		//IL_007f: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b9: Unknown result type (might be due to invalid IL or missing references)
		IntVec3 val = Find.World.info.initialMapSize;
		if (((WorldObject)__instance.mapParent).def.overrideMapSize.HasValue)
		{
			val = ((WorldObject)__instance.mapParent).def.overrideMapSize.Value;
		}
		Map orGenerateMap = GetOrGenerateMapUtility.GetOrGenerateMap(((WorldObject)__instance.mapParent).Tile, val, ((WorldObject)__instance.mapParent).def, (IEnumerable<GenStepWithParams>)null, false);
		IntVec3 val2 = default;
		if (__instance.arrivalMode == PawnsArrivalModeDefOf.CenterDrop)
		{
			if (!DropCellFinder.TryFindRaidDropCenterClose(out val2, orGenerateMap, true, true, true, -1))
			{
				val2 = DropCellFinder.FindRaidDropCenterDistant(orGenerateMap, false, true);
			}
		}
		else
		{
			val2 = DropCellFinder.FindRaidDropCenterDistant(orGenerateMap, false, !DropPodUtility.IsShuttle(transporters));
		}
		foreach (ActiveTransporterInfo transporter in transporters)
		{
			IntVec3 val3 = CellFinder.RandomEdgeCell(orGenerateMap);
			ProjectileUtil.LaunchWarpod(transporter, InternalDefOf.VGE_WarpodIncoming, __instance.launchingFaction, orGenerateMap, val3, val2);
		}
	}
}
