using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

public sealed class BloodAnimationsSettingsComponent(Game game) : GameComponent
{
	private IExposable state;

	private IExposable State => state ?? (state = BloodAnimations.SettingsState(game));

	public override void ExposeData()
	{
		if (BloodAnimations.Installed)
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
		if (BloodAnimations.Installed && MP.IsInMultiplayer)
		{
			SessionServices.EnsureSettings(State);
		}
	}
}
