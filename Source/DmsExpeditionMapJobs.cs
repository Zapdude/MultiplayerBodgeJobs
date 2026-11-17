using System.Collections.Generic;
using Verse;

namespace Multiplayer.Compat;

public sealed class DmsExpeditionMapJobs(Game game) : GameComponent
{
	internal List<DmsExpeditionMapJob> Arrivals = new List<DmsExpeditionMapJob>();

	public override void GameComponentUpdate()
	{
		if (DeadMansSwitchExpedition.Installed)
		{
			DeadMansSwitchExpedition.TerminalUiTick();
		}
	}

	public override void ExposeData()
	{
		if (DeadMansSwitchExpedition.Installed)
		{
			Scribe_Collections.Look(ref Arrivals, "mpDmseMapArrivals", LookMode.Deep);
			if (Scribe.mode == LoadSaveMode.PostLoadInit && Arrivals == null)
			{
				Arrivals = new List<DmsExpeditionMapJob>();
			}
		}
	}
}
