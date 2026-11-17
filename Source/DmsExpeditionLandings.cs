using System.Collections.Generic;
using Verse;

namespace Multiplayer.Compat;

public sealed class DmsExpeditionLandings(Game game) : GameComponent
{
	internal List<DmsExpeditionLanding> Pending = new List<DmsExpeditionLanding>();

	public override void ExposeData()
	{
		if (DeadMansSwitchExpedition.Installed)
		{
			Scribe_Collections.Look(ref Pending, "mpDmseLandings", LookMode.Deep);
			if (Scribe.mode == LoadSaveMode.PostLoadInit && Pending == null)
			{
				Pending = new List<DmsExpeditionLanding>();
			}
		}
	}
}
