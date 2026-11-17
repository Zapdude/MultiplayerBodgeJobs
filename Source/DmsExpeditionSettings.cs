using System.Collections;
using System.Collections.Generic;
using Verse;

namespace Multiplayer.Compat;

public sealed class DmsExpeditionSettings : GameComponent
{
	internal List<IExposable> Records;

	internal IList EditingRecords;

	public DmsExpeditionSettings(Game game)
	{
		Records = DeadMansSwitchExpedition.LocalRecords();
	}

	public override void ExposeData()
	{
		if (!DeadMansSwitchExpedition.Installed)
		{
			return;
		}
		Scribe_Collections.Look(ref Records, "mpDmseCraterRecords", LookMode.Deep);
		if (Scribe.mode == LoadSaveMode.PostLoadInit)
		{
			if (Records == null)
			{
				Records = new List<IExposable>();
			}
			EditingRecords = null;
		}
	}
}
