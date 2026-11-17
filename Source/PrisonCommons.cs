using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("me.lubar.PrisonCommons")]
[MpCompatFor("me.lubar.PrisonCommons.temp")]
[MpCompatFor("Mlie.PrisonCommons")]
internal class PrisonCommons
{
	public PrisonCommons(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
	}

	private static void LatePatch()
	{
		MpCompat.RegisterLambdaMethod("RimWorld.CompPrisonCommons", "CompGetGizmosExtra", 1);
	}
}
