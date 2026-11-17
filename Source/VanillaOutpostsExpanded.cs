using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("vanillaexpanded.outposts")]
internal class VanillaOutpostsExpanded
{
	public VanillaOutpostsExpanded(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
	}

	public void LatePatch()
	{
		MP.RegisterSyncMethod(AccessTools.TypeByName("VOE.Outpost_Artillery"), "Fire");
		MpCompat.RegisterLambdaDelegate("VOE.Outpost_Defensive", "GetGizmos", 3);
	}
}
