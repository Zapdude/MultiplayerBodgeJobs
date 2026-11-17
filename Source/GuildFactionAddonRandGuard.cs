using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Poupun.GuildFactionAddon")]
internal class GuildFactionAddonRandGuard
{
	public GuildFactionAddonRandGuard(ModContentPack mod)
	{
		RandGuard.Install(RandGuard.GuildFactionAddonTargets);
	}
}
