using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("brrainz.achtung")]
internal sealed class AchtungDynamicWorkAdapter
{
	public AchtungDynamicWorkAdapter(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(AchtungDynamicWork.Install);
	}
}
