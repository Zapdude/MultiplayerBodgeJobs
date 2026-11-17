using RimWorld.Planet;
using Verse;

namespace Multiplayer.Compat;

public sealed class DmsBvrSample : IExposable
{
	public int Id;

	public int Tick;

	public int Seen;

	public PlanetTile Tile;

	public float Speed;

	public void ExposeData()
	{
		Scribe_Values.Look(ref Id, "id", 0);
		Scribe_Values.Look(ref Tile, "tile", PlanetTile.Invalid);
		Scribe_Values.Look(ref Tick, "tick", 0);
		Scribe_Values.Look(ref Seen, "seen", 0);
		Scribe_Values.Look(ref Speed, "speed", 0f);
	}
}
