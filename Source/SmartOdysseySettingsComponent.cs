using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

public sealed class SmartOdysseySettingsComponent(Game game) : GameComponent
{
	private IExposable state;

	private IExposable State => state ?? (state = SmartOdyssey.State(game));

	public override void ExposeData()
	{
		if (SmartOdyssey.Installed)
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
		if (SmartOdyssey.Installed && MP.IsInMultiplayer)
		{
			SessionServices.EnsureSettings(State);
		}
	}
}
