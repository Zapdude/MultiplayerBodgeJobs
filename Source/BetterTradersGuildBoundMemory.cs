using Verse;

namespace Multiplayer.Compat;

public sealed class BetterTradersGuildBoundMemory : IExposable
{
	public Pawn Pawn;

	public Thing Launchable;

	public int Tick;

	public void ExposeData()
	{
		Scribe_References.Look(ref Pawn, "pawn");
		Scribe_References.Look(ref Launchable, "launchable");
		Scribe_Values.Look(ref Tick, "tick", 0);
	}
}
