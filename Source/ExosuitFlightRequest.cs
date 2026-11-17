using RimWorld.Planet;
using Verse;

namespace Multiplayer.Compat;

public sealed class ExosuitFlightRequest : IExposable
{
	public int Id;

	public int Phase;

	public int Range;

	public Pawn Pawn;

	public Thing Flyer;

	public Map Source;

	public Map TargetMap;

	public MapParent Destination;

	public PlanetTile Origin;

	public IntVec3 Cell = IntVec3.Invalid;

	public bool Generated;

	public IExposable Ownership = SessionServices.CreateOwnership();

	public void ExposeData()
	{
		Scribe_Values.Look(ref Id, "id", 0);
		Scribe_Values.Look(ref Phase, "phase", 0);
		Scribe_Values.Look(ref Range, "range", 0);
		Scribe_References.Look(ref Pawn, "pawn");
		Scribe_References.Look(ref Flyer, "flyer");
		Scribe_References.Look(ref Source, "source");
		Scribe_References.Look(ref TargetMap, "targetMap");
		Scribe_References.Look(ref Destination, "destination");
		Scribe_Values.Look(ref Origin, "origin");
		Scribe_Values.Look(ref Cell, "cell", IntVec3.Invalid);
		Scribe_Values.Look(ref Generated, "generated", defaultValue: false);
		Ownership.ExposeData();
	}
}
