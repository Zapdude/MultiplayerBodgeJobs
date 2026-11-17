using RimWorld;
using Verse;

namespace Multiplayer.Compat;

public sealed class FffPendingCompanyRaid : IExposable
{
	internal Map Map;

	internal string Company;

	internal Faction Faction;

	public void ExposeData()
	{
		Map refee = ((Map != null && !Map.Disposed && Find.Maps.Contains(Map)) ? Map : null);
		Scribe_References.Look(ref refee, "map");
		if (Scribe.mode != LoadSaveMode.Saving)
		{
			Map = refee;
		}
		Scribe_Values.Look(ref Company, "company");
		Scribe_References.Look(ref Faction, "faction");
	}
}
