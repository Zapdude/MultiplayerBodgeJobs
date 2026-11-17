using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

public sealed class HeartsOfRimWorldSettingsComponent(Game game) : GameComponent
{
	private IExposable state;

	private IExposable State => state ?? (state = HeartsOfRimWorld.SettingsState(game));

	public override void ExposeData()
	{
		if (HeartsOfRimWorld.Installed)
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
		if (HeartsOfRimWorld.Installed && MP.IsInMultiplayer)
		{
			SessionServices.EnsureSettings(State);
		}
	}
}
