using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

public sealed class RimatomicsSettingsComponent : GameComponent
{
	private Rimatomics.SessionSettingsSnapshot snapshot;

	public RimatomicsSettingsComponent(Game game)
	{
	}

	public override void ExposeData()
	{
		if (Rimatomics.HasSessionSettings)
		{
			if (Scribe.mode == LoadSaveMode.Saving)
			{
				snapshot = Rimatomics.CaptureSessionSettings();
			}
			Scribe_Deep.Look(ref snapshot, "mpRimatomicsSettings");
			if (Scribe.mode == LoadSaveMode.LoadingVars && MP.IsInMultiplayer)
			{
				Rimatomics.ApplySessionSettings(snapshot);
			}
		}
	}
}
