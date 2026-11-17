using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("dubwise.dubsmintminimap")]
internal class DubsMintMinimap
{
	public DubsMintMinimap(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(() =>
		{
			DubsMintMenu.Patch("DubsMintMinimap.MP_Util");
		});
	}
}
