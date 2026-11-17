using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("FluffyKittens.Autoharvester")]
internal class AutoharvesterAutocycle
{
	public AutoharvesterAutocycle(ModContentPack mod)
	{
		MpCompat.RegisterLambdaMethod("AutoHarvesterCycle.CycleComp", "CompGetGizmosExtra", 0, 1, 2, 3);
	}
}
