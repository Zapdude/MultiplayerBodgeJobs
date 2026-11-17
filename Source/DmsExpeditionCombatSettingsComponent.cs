using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

public sealed class DmsExpeditionCombatSettingsComponent(Game game) : GameComponent
{
	private IExposable state;

	private IExposable State => state ?? (state = DmsExpeditionCombatSettings.State(game));

	public override void ExposeData()
	{
		if (DmsExpeditionCombatSettings.Installed)
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
		if (DmsExpeditionCombatSettings.Installed && MP.IsInMultiplayer)
		{
			SessionServices.EnsureSettings(State);
		}
	}
}
