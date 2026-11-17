using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

public sealed class BetterTradersGuildSettingsComponent : GameComponent
{
	private BetterTradersGuild.GuildSettingsSnapshot snapshot;

	private bool refreshPowerAfterLoad;

	public BetterTradersGuildSettingsComponent(Game game)
	{
	}

	public override void ExposeData()
	{
		if (BetterTradersGuild.HasGuildSettings)
		{
			if (Scribe.mode == LoadSaveMode.Saving)
			{
				snapshot = BetterTradersGuild.CaptureGuildSettings();
			}
			Scribe_Deep.Look(ref snapshot, "mpBetterTradersGuildSettings");
			if (Scribe.mode == LoadSaveMode.LoadingVars && MP.IsInMultiplayer)
			{
				BetterTradersGuild.RestoreGuildSettings(snapshot);
				refreshPowerAfterLoad = snapshot != null;
			}
		}
	}

	public override void FinalizeInit()
	{
		if (refreshPowerAfterLoad && MP.IsInMultiplayer && BetterTradersGuild.HasGuildSettings)
		{
			BetterTradersGuild.RefreshGuildPowerAfterLoad();
			refreshPowerAfterLoad = false;
		}
	}
}
