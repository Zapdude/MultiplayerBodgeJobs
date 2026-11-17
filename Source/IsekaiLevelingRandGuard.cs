using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("JellyCreative.IsekaiLeveling")]
internal class IsekaiLevelingRandGuard
{
	public IsekaiLevelingRandGuard(ModContentPack mod)
	{
		RandGuard.Install(RandGuard.IsekaiLevelingTargets);
	}
}
