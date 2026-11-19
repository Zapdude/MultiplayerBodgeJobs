using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

/// <summary>
/// Keeps the Supply Shelf <c>freeze</c> snapshot for the current game so that a client which
/// joins later is told the host's value instead of running with its own mod settings file.
/// </summary>
public sealed class SupplyShelfSettingsComponent : GameComponent
{
	private readonly Game game;

	private IExposable state;

	private IExposable State => state ?? (state = SupplyShelf.SettingsState(game));

	public SupplyShelfSettingsComponent(Game game)
	{
		this.game = game;
	}

	public override void ExposeData()
	{
		if (SupplyShelf.Installed)
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

	internal void Ensure()
	{
		if (SupplyShelf.Installed && MP.IsInMultiplayer)
		{
			SessionServices.EnsureSettings(State);
		}
	}
}
