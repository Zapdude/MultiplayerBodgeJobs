using Verse;

namespace Multiplayer.Compat;

public sealed class VanillaChristmasDistributionComponent(Game game) : GameComponent
{
	internal readonly VanillaChristmasDistribution.DistributionState State = new VanillaChristmasDistribution.DistributionState();

	internal int LastPumpTick = int.MinValue;

	public override void ExposeData()
	{
		if (VanillaChristmasDistribution.Installed)
		{
			State.ExposeData();
		}
	}
}
