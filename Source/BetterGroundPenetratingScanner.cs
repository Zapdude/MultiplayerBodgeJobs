using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("kikohi.BetterGroundPenetratingScanner")]
public class BetterGroundPenetratingScanner
{
	public BetterGroundPenetratingScanner(ModContentPack mod)
	{
		MpCompat.RegisterLambdaDelegate("BGPScanner.CompBetterDeepScanner", "PostSpawnSetup", 2, 3).SetContext(SyncContext.MapSelected);
	}
}
