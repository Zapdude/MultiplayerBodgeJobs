using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

public sealed class AlphaGenesSettingsComponent : GameComponent
{
	private readonly Game game;

	private IExposable state;

	private IExposable State => state ?? (state = AlphaGenesSettings.SettingsState(game));

	public AlphaGenesSettingsComponent(Game game)
	{
		this.game = game;
	}

	public override void ExposeData()
	{
		if (AlphaGenesSettings.Installed)
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
		if (AlphaGenesSettings.Installed && MP.IsInMultiplayer)
		{
			SessionServices.EnsureSettings(State);
		}
	}
}
