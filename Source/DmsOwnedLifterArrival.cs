using System.Collections.Generic;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Compat;

public sealed class DmsOwnedLifterArrival : TransportersArrivalAction
{
	public TransportersArrivalAction native;

	public string owner;

	public override bool GeneratesMap => native?.GeneratesMap ?? true;

	public override FloatMenuAcceptanceReport StillValid(IEnumerable<IThingHolder> pods, PlanetTile destinationTile)
	{
		return native?.StillValid(pods, destinationTile) ?? ((FloatMenuAcceptanceReport)false);
	}

	public override bool ShouldUseLongEvent(List<ActiveTransporterInfo> pods, PlanetTile tile)
	{
		return native?.ShouldUseLongEvent(pods, tile) ?? true;
	}

	public override void Arrived(List<ActiveTransporterInfo> transporters, PlanetTile tile)
	{
		if (!MP.IsInMultiplayer)
		{
			native.Arrived(transporters, tile);
		}
		else
		{
			DeadMansSwitchII.LifterState.Remember(transporters, tile, owner);
		}
	}

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_Deep.Look(ref native, "native");
		Scribe_Values.Look(ref owner, "owner");
	}
}
