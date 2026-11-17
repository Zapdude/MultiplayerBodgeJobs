using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("VanillaExpanded.VAEEndAndExt")]
internal class VanillaAnimalsEndangered
{
	public VanillaAnimalsEndangered(ModContentPack mod)
	{
		MP.RegisterSyncDelegateLambda(AccessTools.TypeByName("VanillaAnimalsExpandedEndangered.Pawn_GetGizmos_Patch"), "Postfix", 1);
	}
}
