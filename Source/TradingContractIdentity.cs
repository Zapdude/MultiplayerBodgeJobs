using Verse;

namespace Multiplayer.Compat;

public sealed class TradingContractIdentity : IExposable
{
	public int id;

	public int list;

	public int index;

	public string key;

	public string owner;

	public void ExposeData()
	{
		Scribe_Values.Look(ref id, "id", 0);
		Scribe_Values.Look(ref list, "list", 0);
		Scribe_Values.Look(ref index, "index", -1);
		Scribe_Values.Look(ref key, "key");
		Scribe_Values.Look(ref owner, "owner");
	}
}
