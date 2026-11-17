using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using VanillaGravshipExpanded2;
using Verse;

namespace Multiplayer.Compat.Workshop17.Bodies;

public class TransportersArrivalAction_WarpodStrike_Body
{
	public static bool get_GeneratesMap(TransportersArrivalAction_WarpodStrike __instance)
	{
		return false;
	}

	public static void ExposeData(TransportersArrivalAction_WarpodStrike __instance)
	{
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		((TransportersArrivalAction)__instance).ExposeData();
		Scribe_References.Look<MapParent>(ref __instance.mapParent, "mapParent", false);
		Scribe_Values.Look<IntVec3>(ref __instance.cell, "cell", default(IntVec3), false);
		Scribe_References.Look<Faction>(ref __instance.launchingFaction, "launchingFaction", false);
	}

	public static FloatMenuAcceptanceReport StillValid(TransportersArrivalAction_WarpodStrike __instance, IEnumerable<IThingHolder> pods, PlanetTile destinationTile)
	{
		//IL_004e: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		//IL_0056: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Unknown result type (might be due to invalid IL or missing references)
		if (__instance.mapParent == null || ((WorldObject)__instance.mapParent).Destroyed || ((WorldObject)__instance.mapParent).Tile != destinationTile || !__instance.mapParent.HasMap || !GenGrid.InBounds(__instance.cell, __instance.mapParent.Map))
		{
			return ((FloatMenuAcceptanceReport)(false));
		}
		return ((TransportersArrivalAction)__instance).StillValid(pods, destinationTile);
	}

	public static void Arrived(TransportersArrivalAction_WarpodStrike __instance, List<ActiveTransporterInfo> transporters, PlanetTile tile)
	{
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		foreach (ActiveTransporterInfo transporter in transporters)
		{
			Map map = __instance.mapParent.Map;
			IntVec3 val = CellFinder.RandomEdgeCell(map);
			ProjectileUtil.LaunchWarpod(transporter, InternalDefOf.VGE_WarpodIncoming, __instance.launchingFaction, map, val, __instance.cell);
		}
	}
}
