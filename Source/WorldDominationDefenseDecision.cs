using RimWorld.Planet;
using Verse;

namespace Multiplayer.Compat;

public sealed class WorldDominationDefenseDecision : IExposable
{
	public int id;

	public WorldObject traveler;

	public WorldObject outpost;

	public bool skirmish;

	private bool ownsTraveler;

	public void ExposeData()
	{
		Scribe_Values.Look(ref id, "id", 0);
		Scribe_Values.Look(ref skirmish, "skirmish", defaultValue: false);
		Scribe_References.Look(ref outpost, "outpost");
		if (Scribe.mode == LoadSaveMode.Saving)
		{
			ownsTraveler = traveler != null && (traveler.Destroyed || !Find.WorldObjects.AllWorldObjects.Contains(traveler));
		}
		Scribe_Values.Look(ref ownsTraveler, "ownsTraveler", defaultValue: false);
		if (ownsTraveler)
		{
			Scribe_Deep.Look(ref traveler, "traveler");
		}
		else
		{
			Scribe_References.Look(ref traveler, "traveler");
		}
	}
}
