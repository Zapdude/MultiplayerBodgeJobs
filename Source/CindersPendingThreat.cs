using RimWorld;
using Verse;

namespace Multiplayer.Compat;

public sealed class CindersPendingThreat : IExposable
{
	public int Sequence;

	public int QueuedWorldTick;

	public Map Map;

	public Faction Recipient;

	public FiringIncident Incident;

	public IExposable Encounter;

	public bool Dispatched;

	public bool WasQueued;

	public bool Battle;

	public bool IgnoreExistingDanger;

	public bool WorldEncounterStarted;

	public bool TimeoutReleased;

	public int Phase;

	public int Batch;

	public int EncounterTimeoutTicks;

	public void ExposeData()
	{
		Scribe_Values.Look(ref Sequence, "sequence", 0);
		Scribe_Values.Look(ref QueuedWorldTick, "queuedWorldTick", 0);
		Scribe_References.Look(ref Map, "map");
		Scribe_References.Look(ref Recipient, "recipient");
		Scribe_Deep.Look(ref Incident, "incident");
		Scribe_Deep.Look(ref Encounter, "encounter");
		Scribe_Values.Look(ref Dispatched, "dispatched", defaultValue: false);
		Scribe_Values.Look(ref WasQueued, "wasQueued", defaultValue: false);
		Scribe_Values.Look(ref Battle, "battle", defaultValue: false);
		Scribe_Values.Look(ref IgnoreExistingDanger, "ignoreExistingDanger", defaultValue: false);
		Scribe_Values.Look(ref Phase, "phase", 0);
		Scribe_Values.Look(ref Batch, "batch", 0);
		Scribe_Values.Look(ref WorldEncounterStarted, "worldEncounterStarted", defaultValue: false);
		Scribe_Values.Look(ref EncounterTimeoutTicks, "encounterTimeoutTicks", 0);
		Scribe_Values.Look(ref TimeoutReleased, "timeoutReleased", defaultValue: false);
	}
}
