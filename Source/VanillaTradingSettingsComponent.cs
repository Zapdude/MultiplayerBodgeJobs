using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

public sealed class VanillaTradingSettingsComponent : GameComponent
{
	private VanillaTradingExpanded.TradingSettingsSnapshot snapshot;

	public VanillaTradingSettingsComponent(Game game)
	{
	}

	public override void ExposeData()
	{
		if (VanillaTradingExpanded.HasTradingSettings)
		{
			if (Scribe.mode == LoadSaveMode.Saving)
			{
				snapshot = VanillaTradingExpanded.CaptureTradingSettings();
			}
			Scribe_Deep.Look(ref snapshot, "mpTradingSettings");
			if (Scribe.mode == LoadSaveMode.LoadingVars && MP.IsInMultiplayer)
			{
				VanillaTradingExpanded.ApplyTradingSettings(snapshot, rebuild: true);
			}
		}
	}
}
