using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

public sealed class RimMarketSettingsComponent : GameComponent
{
	private readonly Game game;

	private IExposable state;

	private IExposable State => state ?? (state = RimMarket.RoomSettings(game));

	public RimMarketSettingsComponent(Game game)
	{
		this.game = game;
	}

	public override void ExposeData()
	{
		if (RimMarket.Installed)
		{
			State.ExposeData();
		}
	}

	public override void StartedNewGame()
	{
		Ensure();
	}

	public override void LoadedGame()
	{
		Ensure();
	}

	private void Ensure()
	{
		if (RimMarket.Installed && MP.IsInMultiplayer)
		{
			SessionServices.EnsureSettings(State);
		}
	}
}
