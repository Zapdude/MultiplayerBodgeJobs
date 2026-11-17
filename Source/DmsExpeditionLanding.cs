using RimWorld.Planet;
using Verse;

namespace Multiplayer.Compat;

public sealed class DmsExpeditionLanding : IExposable
{
	public WorldObject Ship;

	public Map Destination;

	public IntVec3 Offset;

	public bool Moved;

	public void ExposeData()
	{
		Scribe_References.Look(ref Ship, "ship");
		Scribe_References.Look(ref Destination, "destination");
		Scribe_Values.Look(ref Offset, "offset");
		Scribe_Values.Look(ref Moved, "moved", defaultValue: false);
	}
}
