using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using VanillaGravshipExpanded2;
using Verse;

namespace Multiplayer.Compat.Workshop17.Bodies;

public class SpaceRebellionsUtility_Body
{
	public static bool CanInitiateRebellion(Pawn pawn)
	{
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0040: Unknown result type (might be due to invalid IL or missing references)
		if (((pawn != null) ? ((Thing)pawn).Map : null) == null)
		{
			return false;
		}
		if (SpaceRebellionsUtility.GetAllUsableEscapePods(((Thing)pawn).Map).Any())
		{
			return true;
		}
		PlanetTile closestTargetTransportPodTile = SpaceRebellionsUtility.GetClosestTargetTransportPodTile(((Thing)pawn).Map);
		int num = Find.WorldGrid.TraversalDistanceBetween(((Thing)pawn).Map.Tile, closestTargetTransportPodTile, true, int.MaxValue, true);
		return SpaceRebellionsUtility.GetAllUsableDropPods(((Thing)pawn).Map, closestTargetTransportPodTile.Layer, num).Any();
	}

	public static void GetAllValidPods(Map map, List<Thing> escapePods, List<Thing> dropPods)
	{
		//IL_004b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Unknown result type (might be due to invalid IL or missing references)
		//IL_005c: Unknown result type (might be due to invalid IL or missing references)
		if (map == null)
		{
			return;
		}
		if (escapePods != null)
		{
			escapePods.Clear();
			escapePods.AddRange((IEnumerable<Thing>)(from x in SpaceRebellionsUtility.GetAllUsableEscapePods(map)
				select ((ThingComp)x).parent));
		}
		if (dropPods != null)
		{
			if (dropPods != escapePods)
			{
				dropPods.Clear();
			}
			PlanetTile closestTargetTransportPodTile = SpaceRebellionsUtility.GetClosestTargetTransportPodTile(map);
			int num = Find.WorldGrid.TraversalDistanceBetween(map.Tile, closestTargetTransportPodTile, true, int.MaxValue, true);
			dropPods.AddRange((IEnumerable<Thing>)(from x in SpaceRebellionsUtility.GetAllUsableDropPods(map, closestTargetTransportPodTile.Layer, num)
				select ((ThingComp)x).parent));
		}
	}

	public static IEnumerable<CompEscapePod> GetAllUsableEscapePods(Map map)
	{
		for (int i = 0; i < SpaceRebellionsUtility.ValidEscapePods.Count; i++)
		{
			List<Thing> list = map.listerThings.ThingsOfDef(SpaceRebellionsUtility.ValidEscapePods[i]);
			if (list.Count > 0)
			{
				CompEscapePod val = ThingCompUtility.TryGetComp<CompEscapePod>(list[0]);
				if (val != null && SpaceRebellionsUtility.IsEscapePodUsable(val))
				{
					yield return val;
				}
			}
		}
	}

	public static IEnumerable<CompLaunchable> GetAllUsableDropPods(Map map, PlanetLayer targetLayer, int distance)
	{
		for (int i = 0; i < SpaceRebellionsUtility.ValidTransportPods.Count; i++)
		{
			List<Thing> list = map.listerThings.ThingsOfDef(SpaceRebellionsUtility.ValidTransportPods[i]);
			for (int j = 0; j < list.Count; j++)
			{
				CompLaunchable val = ThingCompUtility.TryGetComp<CompLaunchable>(list[j]);
				if (val != null && SpaceRebellionsUtility.IsTransportPodUsable(val, targetLayer, distance))
				{
					yield return val;
				}
			}
		}
	}

	public static bool IsEscapePodUsable(CompEscapePod escapePod)
	{
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		if (escapePod != null && escapePod.Occupant == null && escapePod.IsCurrentPlanetLayerSupported)
		{
			PlanetTile val = escapePod.FindClosestValidTile();
			return val.Valid;
		}
		return false;
	}

	public static bool IsTransportPodUsable(CompLaunchable transportPod, PlanetLayer targetLayer, int distance)
	{
		if (((transportPod != null) ? transportPod.Transporter : null) == null)
		{
			return false;
		}
		if (transportPod.FuelLevel < transportPod.Props.minFuelCost)
		{
			return false;
		}
		if (transportPod.RequiresFuelingPort && !transportPod.Refuelable.HasFuel)
		{
			return false;
		}
		return distance <= transportPod.MaxLaunchDistanceAtFuelLevel((float)distance, targetLayer);
	}

	public static PlanetTile GetClosestTargetTransportPodTile(Map map)
	{
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		PlanetTile result = default;
		TileFinder.TryFindPassableTileWithTraversalDistance(((PlanetLayer)Find.WorldGrid.Surface).GetClosestTile_NewTemp(map.Tile, false), 0, int.MaxValue, out result, (Predicate<PlanetTile>)null, true, (TileFinderMode)1, true, true);
		return result;
	}

	public static void LogSpaceRebellionData(Pawn p)
	{
		//IL_0107: Unknown result type (might be due to invalid IL or missing references)
		//IL_010c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0118: Unknown result type (might be due to invalid IL or missing references)
		//IL_011d: Unknown result type (might be due to invalid IL or missing references)
		if (((p != null) ? ((Thing)p).Map : null) != null)
		{
			StringBuilder stringBuilder = new StringBuilder($"Testing space prison escape for {p}.");
			stringBuilder.AppendLine();
			stringBuilder.Append("Pawn is");
			if (!p.IsPrisoner)
			{
				stringBuilder.Append(" not");
			}
			stringBuilder.AppendLine(" a prisoner.");
			stringBuilder.Append("Pawn is");
			BiomeDef biome = ((Thing)p).Map.Biome;
			if (biome == null || !biome.inVacuum)
			{
				stringBuilder.Append(" not");
			}
			stringBuilder.AppendLine(" in vacuum biome");
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder3 = stringBuilder2;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(28, 1, stringBuilder2);
			handler.AppendLiteral("There's ");
			handler.AppendFormatted(SpaceRebellionsUtility.GetAllUsableEscapePods(((Thing)p).Map).Count());
			handler.AppendLiteral(" usable escape pods.");
			stringBuilder3.AppendLine(ref handler);
			PlanetTile closestTargetTransportPodTile = SpaceRebellionsUtility.GetClosestTargetTransportPodTile(((Thing)p).Map);
			int num = Find.WorldGrid.TraversalDistanceBetween(((Thing)p).Map.Tile, closestTargetTransportPodTile, true, int.MaxValue, true);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder4 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(26, 1, stringBuilder2);
			handler.AppendLiteral("There's ");
			handler.AppendFormatted(SpaceRebellionsUtility.GetAllUsableDropPods(((Thing)p).Map, closestTargetTransportPodTile.Layer, num).Count());
			handler.AppendLiteral(" usable drop pods.");
			stringBuilder4.AppendLine(ref handler);
			Log.Message(stringBuilder.ToString());
		}
	}
}
