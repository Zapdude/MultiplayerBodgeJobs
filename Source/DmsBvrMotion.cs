using System.Collections.Generic;
using Verse;

namespace Multiplayer.Compat;

public sealed class DmsBvrMotion : IExposable
{
	public int Last = -1;

	public List<DmsBvrSample> Samples = new List<DmsBvrSample>();

	public void ExposeData()
	{
		Scribe_Values.Look(ref Last, "mpDmseMotionTick", -1);
		Scribe_Collections.Look(ref Samples, "mpDmseMotion", LookMode.Deep);
	}
}
