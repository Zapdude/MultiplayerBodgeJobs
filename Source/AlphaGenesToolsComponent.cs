using System.Collections.Generic;
using Verse;

namespace Multiplayer.Compat;

public sealed class AlphaGenesToolsComponent(Game game) : GameComponent
{
	internal List<AlphaGenesToolSelection> Pending = new List<AlphaGenesToolSelection>();

	public override void ExposeData()
	{
		if (AlphaGenesTools.Installed)
		{
			Scribe_Collections.Look(ref Pending, "mpAlphaGenesToolSelections", LookMode.Deep);
			if (Pending == null)
			{
				Pending = new List<AlphaGenesToolSelection>();
			}
		}
	}

	public override void GameComponentOnGUI()
	{
		AlphaGenesTools.Pump();
	}
}
