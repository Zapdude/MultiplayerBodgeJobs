using System;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

public sealed class WorldDominationConquestDecision : IExposable
{
	public int tile;

	public int tier;

	public int ruinsId;

	public int caravanId;

	public string name;

	public Faction conqueredFaction;

	public bool bought;

	public void ExposeData()
	{
		Scribe_Values.Look(ref tile, "tile", 0);
		Scribe_Values.Look(ref tier, "tier", 0);
		Scribe_Values.Look(ref ruinsId, "ruinsId", -1);
		Scribe_Values.Look(ref caravanId, "caravanId", -1);
		Scribe_Values.Look(ref name, "name");
		Scribe_Values.Look(ref bought, "bought", defaultValue: false);
		Scribe_References.Look(ref conqueredFaction, "conqueredFaction");
	}

	internal object ToNative()
	{
		return Activator.CreateInstance((Type)WorldDomination.NativeType("ConquestOpportunityContext"), tile, name, ruinsId, Enum.ToObject((Type)WorldDomination.NativeType("SettlementTier"), tier), conqueredFaction, caravanId, bought);
	}
}
