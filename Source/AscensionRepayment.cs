using Verse;

namespace Multiplayer.Compat;

public sealed class AscensionRepayment : IExposable
{
	public string contractId;

	public int cycle;

	public Map map;

	public void ExposeData()
	{
		Scribe_Values.Look(ref contractId, "contract");
		Scribe_Values.Look(ref cycle, "cycle", 0);
		Scribe_References.Look(ref map, "map");
	}
}
