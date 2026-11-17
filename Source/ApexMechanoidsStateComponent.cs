using System.Collections.Generic;
using Verse;

namespace Multiplayer.Compat;

public sealed class ApexMechanoidsStateComponent : GameComponent
{
	private List<int> shieldKeys;

	private List<Pawn> shieldTargets;

	private List<int> shieldTicks;

	private int cleanupTick = -99999;

	public ApexMechanoidsStateComponent(Game game)
	{
		ApexMechanoidsState.ResetGameState();
	}

	public override void ExposeData()
	{
		base.ExposeData();
		if (ApexMechanoidsState.HasShieldMemory)
		{
			if (Scribe.mode == LoadSaveMode.Saving)
			{
				ApexMechanoidsState.SnapshotShieldMemory(out shieldKeys, out shieldTargets, out shieldTicks, out cleanupTick);
			}
			Scribe_Collections.Look(ref shieldKeys, "mpApexShieldKeys", LookMode.Value);
			Scribe_Collections.Look(ref shieldTargets, "mpApexShieldTargets", LookMode.Reference);
			Scribe_Collections.Look(ref shieldTicks, "mpApexShieldTicks", LookMode.Value);
			Scribe_Values.Look(ref cleanupTick, "mpApexShieldCleanupTick", -99999);
			if (Scribe.mode == LoadSaveMode.ResolvingCrossRefs)
			{
				ApexMechanoidsState.RestoreShieldMemory(shieldKeys, shieldTargets, shieldTicks, cleanupTick);
			}
		}
	}
}
