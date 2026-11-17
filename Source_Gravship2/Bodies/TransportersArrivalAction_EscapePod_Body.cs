using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using VanillaGravshipExpanded2;
using Verse;

namespace Multiplayer.Compat.Workshop17.Bodies;

public class TransportersArrivalAction_EscapePod_Body
{
	public static bool get_GeneratesMap(TransportersArrivalAction_EscapePod __instance)
	{
		return false;
	}

	public static void ExposeData(TransportersArrivalAction_EscapePod __instance)
	{
		((TransportersArrivalAction)__instance).ExposeData();
		Scribe_References.Look<Faction>(ref __instance.faction, "faction", false);
	}

	public static void Arrived(TransportersArrivalAction_EscapePod __instance, List<ActiveTransporterInfo> pods, PlanetTile tile)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		__instance.TryDropCaravanOrExistingMap(pods, tile, true);
	}

	public static FloatMenuAcceptanceReport StillValid(TransportersArrivalAction_EscapePod __instance, IEnumerable<IThingHolder> pods, PlanetTile tile)
	{
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		Rand.PushState();
		try
		{
			List<ActiveTransporterInfo> list = (pods as List<ActiveTransporterInfo>) ?? pods.OfType<ActiveTransporterInfo>().ToList();
			return ((FloatMenuAcceptanceReport)(__instance.TryDropCaravanOrExistingMap(list, tile, false)));
		}
		finally
		{
			Rand.PopState();
		}
	}

	public static bool TryDropCaravanOrExistingMap(TransportersArrivalAction_EscapePod __instance, List<ActiveTransporterInfo> pods, PlanetTile tile, bool arrive)
	{
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_005f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0088: Unknown result type (might be due to invalid IL or missing references)
		foreach (Map map in Find.Maps)
		{
			if (TransportersArrivalAction_EscapePod.TryDropInRandomCell(pods, tile, map, __instance.faction, arrive))
			{
				return true;
			}
		}
		foreach (Caravan caravan in Find.WorldObjects.Caravans)
		{
			if (TransportersArrivalAction_EscapePod.TryJoinCaravan(pods, tile, caravan, arrive))
			{
				return true;
			}
		}
		return TransportersArrivalAction_EscapePod.TryFormCaravan(pods, tile, arrive);
	}

	public static bool TryDropInRandomCell(List<ActiveTransporterInfo> transporters, PlanetTile targetTile, Map map, Faction faction, bool arrive)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0051: Expected Obj, but got Unknown
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		//IL_0054: Unknown result type (might be due to invalid IL or missing references)
		//IL_0068: Unknown result type (might be due to invalid IL or missing references)
		if (map.Tile != targetTile)
		{
			return false;
		}
		IntVec3 val = default;
		if (!DropCellFinder.FindSafeLandingSpot(out val, faction, map, 25, 5, 5, (IntVec2?)null, (IntVec3?)null))
		{
			val = DropCellFinder.RandomDropSpot(map, true);
		}
		if (!val.IsValid)
		{
			return false;
		}
		TransportersArrivalAction_LandInSpecificCell val2 = new TransportersArrivalAction_LandInSpecificCell(map.Parent, val);
		if (!((FloatMenuAcceptanceReport)(((TransportersArrivalAction)val2).StillValid((IEnumerable<IThingHolder>)transporters, targetTile))))
		{
			return false;
		}
		if (arrive)
		{
			((TransportersArrivalAction)val2).Arrived(transporters, targetTile);
		}
		return true;
	}

	public static bool TryJoinCaravan(List<ActiveTransporterInfo> transporters, PlanetTile targetTile, Caravan caravan, bool arrive)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Expected Obj, but got Unknown
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		if (((WorldObject)caravan).Tile != targetTile)
		{
			return false;
		}
		TransportersArrivalAction_GiveToCaravan val = new TransportersArrivalAction_GiveToCaravan(caravan);
		if (!((FloatMenuAcceptanceReport)(((TransportersArrivalAction)val).StillValid((IEnumerable<IThingHolder>)transporters, targetTile))))
		{
			return false;
		}
		if (arrive)
		{
			((TransportersArrivalAction)val).Arrived(transporters, targetTile);
		}
		return true;
	}

	public static bool TryFormCaravan(List<ActiveTransporterInfo> transporters, PlanetTile targetTile, bool arrive)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Expected Obj, but got Unknown
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		TransportersArrivalAction_FormCaravan val = new TransportersArrivalAction_FormCaravan();
		if (!((FloatMenuAcceptanceReport)(((TransportersArrivalAction)val).StillValid((IEnumerable<IThingHolder>)transporters, targetTile))))
		{
			return false;
		}
		if (arrive)
		{
			((TransportersArrivalAction)val).Arrived(transporters, targetTile);
		}
		return true;
	}
}
