using RimWorld;
using Verse;

namespace Multiplayer.Compat;

public sealed class FffPendingReadinessIncident : IExposable
{
	internal Map Map;

	internal IncidentDef Incident;

	internal Faction Faction;

	internal float PointsFactor;

	public void ExposeData()
	{
		Map refee = ((Map != null && !Map.Disposed && Find.Maps.Contains(Map)) ? Map : null);
		Scribe_References.Look(ref refee, "map");
		if (Scribe.mode != LoadSaveMode.Saving)
		{
			Map = refee;
		}
		Scribe_Defs.Look(ref Incident, "incident");
		Scribe_References.Look(ref Faction, "faction");
		Scribe_Values.Look(ref PointsFactor, "pointsFactor", 1f);
	}
}
