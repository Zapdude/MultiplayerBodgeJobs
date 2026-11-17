using System.Collections.Generic;
using Verse;

namespace Multiplayer.Compat;

public sealed class FffCompanyMapOperations(Game game) : GameComponent
{
	internal List<FffPendingCompanyRaid> Raids = new List<FffPendingCompanyRaid>();

	internal List<FffPendingReadinessIncident> Incidents = new List<FffPendingReadinessIncident>();

	public override void ExposeData()
	{
		if (Scribe.mode != LoadSaveMode.Saving || Raids.Count != 0 || Incidents.Count != 0)
		{
			Scribe_Collections.Look(ref Raids, "mpFFF_companyRaids", LookMode.Deep);
			Scribe_Collections.Look(ref Incidents, "mpFFF_readinessIncidents", LookMode.Deep);
			if (Raids == null)
			{
				Raids = new List<FffPendingCompanyRaid>();
			}
			if (Incidents == null)
			{
				Incidents = new List<FffPendingReadinessIncident>();
			}
		}
	}
}
