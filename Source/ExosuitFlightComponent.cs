using System.Collections.Generic;
using Verse;

namespace Multiplayer.Compat;

public sealed class ExosuitFlightComponent(Game game) : GameComponent
{
	internal int NextId = 1;

	internal List<ExosuitFlightRequest> Requests = new List<ExosuitFlightRequest>();

	public override void ExposeData()
	{
		if (ExosuitFramework.ModernFlightInstalled)
		{
			Scribe_Values.Look(ref NextId, "mpExosuitNextFlight", 1);
			Scribe_Collections.Look(ref Requests, "mpExosuitFlights", LookMode.Deep);
			if (Scribe.mode == LoadSaveMode.PostLoadInit && Requests == null)
			{
				Requests = new List<ExosuitFlightRequest>();
			}
		}
	}

	public override void GameComponentUpdate()
	{
		ExosuitFramework.UpdateModernFlightUi();
	}

	public override void GameComponentTick()
	{
		ExosuitFramework.TickModernFlight();
	}
}
