using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

public sealed class VanillaFurniturePropsSettings : GameComponent
{
	private readonly Game game;

	private IExposable state;

	private IExposable State => state ?? (state = VanillaFurnitureProps.State(game));

	public VanillaFurniturePropsSettings(Game game)
	{
		this.game = game;
	}

	public override void ExposeData()
	{
		if (VanillaFurnitureProps.Installed)
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
		if (VanillaFurnitureProps.Installed && MP.IsInMultiplayer)
		{
			SessionServices.EnsureSettings(State);
		}
	}
}
