using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("vanillaexpanded.gravship")]
internal class GravshipRandGuard
{
	public GravshipRandGuard(ModContentPack mod)
	{
		RandGuard.Install(RandGuard.GravshipTargets);
	}
}
