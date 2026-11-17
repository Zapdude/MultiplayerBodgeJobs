using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Compat;

public sealed class DmsPendingLifterLanding : IExposable
{
	public int id;

	public int revision;

	public string owner;

	public PlanetTile tile;

	public Map map;

	public TravellingTransporters carrier;

	public IntVec3 fallback;

	public List<ActiveTransporterInfo> pods = new List<ActiveTransporterInfo>();

	public IntVec2 Size => pods.FirstOrDefault()?.sentTransporterDef?.Size ?? IntVec2.One;

	public void ExposeData()
	{
		Scribe_Values.Look(ref id, "id", 0);
		Scribe_Values.Look(ref revision, "revision", 0);
		Scribe_Values.Look(ref owner, "owner");
		Scribe_Values.Look(ref tile, "tile");
		Scribe_Values.Look(ref fallback, "fallback");
		Scribe_References.Look(ref map, "map");
		Scribe_References.Look(ref carrier, "carrier");
	}
}
