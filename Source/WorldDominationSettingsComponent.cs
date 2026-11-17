using System;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

public sealed class WorldDominationSettingsComponent : GameComponent
{
	private string snapshot;

	public WorldDominationSettingsComponent(Game game)
	{
	}

	public override void ExposeData()
	{
		if (WorldDomination.HasSettings)
		{
			if (Scribe.mode == LoadSaveMode.Saving)
			{
				snapshot = Convert.ToBase64String(WorldDomination.CaptureSettings());
			}
			Scribe_Values.Look(ref snapshot, "mpWorldDominationSettings");
			if (Scribe.mode == LoadSaveMode.LoadingVars && MP.IsInMultiplayer && snapshot != null)
			{
				WorldDomination.ApplySettings(Convert.FromBase64String(snapshot));
			}
		}
	}
}
