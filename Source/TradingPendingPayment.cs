using Verse;

namespace Multiplayer.Compat;

public sealed class TradingPendingPayment : IExposable
{
	public int id;

	public int kind;

	public int gain;

	public int cost;

	public int amount;

	public int contractId;

	public string title;

	public string contractKey;

	public string owner;

	public ThingDef item;

	public ThingDef stuff;

	public Map map;

	public void ExposeData()
	{
		Scribe_Values.Look(ref id, "id", 0);
		Scribe_Values.Look(ref kind, "kind", 0);
		Scribe_Values.Look(ref gain, "gain", 0);
		Scribe_Values.Look(ref cost, "cost", 0);
		Scribe_Values.Look(ref amount, "amount", 0);
		Scribe_Values.Look(ref contractId, "contractId", -1);
		Scribe_Values.Look(ref contractKey, "contractKey");
		Scribe_Values.Look(ref owner, "owner");
		Scribe_Values.Look(ref title, "title");
		Scribe_Defs.Look(ref item, "item");
		Scribe_Defs.Look(ref stuff, "stuff");
		Scribe_References.Look(ref map, "map");
	}
}
