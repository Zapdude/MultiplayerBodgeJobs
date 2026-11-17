using Verse;

namespace Multiplayer.Compat;

public sealed class DmsTaktikRecall : IExposable
{
	public Thing Thing;

	public Map Target;

	public IntVec3 Cell = IntVec3.Invalid;

	public bool Selected;

	public void ExposeData()
	{
		Scribe_References.Look(ref Thing, "thing");
		Scribe_References.Look(ref Target, "target");
		Scribe_Values.Look(ref Cell, "cell");
		Scribe_Values.Look(ref Selected, "selected", defaultValue: false);
	}
}
