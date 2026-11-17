using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

public sealed class AlphaArmourySettingsComponent(Game game) : GameComponent
{
	private IExposable state;

	private IExposable State => state ?? (state = AlphaArmoury.State(game));

	public override void ExposeData()
	{
		if (AlphaArmoury.Installed)
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
		if (AlphaArmoury.Installed && MP.IsInMultiplayer)
		{
			SessionServices.EnsureSettings(State);
		}
	}
}
