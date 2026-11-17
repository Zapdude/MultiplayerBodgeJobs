using System.Collections.Generic;
using Verse;

namespace Multiplayer.Compat;

public sealed class DmsStrikeWaves : IExposable
{
	public List<IExposable> Waves = new List<IExposable>();

	public void ExposeData()
	{
		Scribe_Collections.Look(ref Waves, "waves", LookMode.Deep);
	}
}
