using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Poupun.IsekaiCreaturesAddon")]
internal class IsekaiCreaturesRandGuard
{
	public IsekaiCreaturesRandGuard(ModContentPack mod)
	{
		RandGuard.Install(RandGuard.IsekaiCreaturesTargets);
	}
}
