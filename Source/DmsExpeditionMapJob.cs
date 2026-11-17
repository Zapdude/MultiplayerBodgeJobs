using RimWorld.Planet;
using Verse;

namespace Multiplayer.Compat;

public sealed class DmsExpeditionMapJob : IExposable
{
	public WorldObject Track;

	public Map Map;

	public int Action;

	public int Target = -1;

	public IntVec3 Cell;

	public bool HasCell;

	public void ExposeData()
	{
		Scribe_References.Look(ref Track, "track");
		Scribe_References.Look(ref Map, "map");
		Scribe_Values.Look(ref Action, "action", 0);
		Scribe_Values.Look(ref Target, "target", -1);
		Scribe_Values.Look(ref Cell, "cell");
		Scribe_Values.Look(ref HasCell, "hasCell", defaultValue: false);
	}
}
