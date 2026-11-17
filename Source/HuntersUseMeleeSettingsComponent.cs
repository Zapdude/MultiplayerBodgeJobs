using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

public sealed class HuntersUseMeleeSettingsComponent : GameComponent
{
	private readonly Game game;

	private IExposable state;

	private IExposable State => state ?? (state = HuntersUseMeleeSettings.SettingsState(game));

	public HuntersUseMeleeSettingsComponent(Game game)
	{
		this.game = game;
	}

	public override void ExposeData()
	{
		if (HuntersUseMeleeSettings.Installed)
		{
			State.ExposeData();
		}
	}

	public override void StartedNewGame()
	{
		EnsureSession();
	}

	public override void LoadedGame()
	{
		EnsureSession();
	}

	internal void EnsureSession()
	{
		if (HuntersUseMeleeSettings.Installed && MP.IsInMultiplayer)
		{
			SessionServices.EnsureSettings(State);
		}
	}
}
