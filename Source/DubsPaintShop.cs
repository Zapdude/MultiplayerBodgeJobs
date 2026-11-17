using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("dubwise.dubspaintshop")]
internal class DubsPaintShop
{
	public DubsPaintShop(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(() =>
		{
			DubsMintMenu.Patch("DubRoss.MP_Util");
		});
	}
}
