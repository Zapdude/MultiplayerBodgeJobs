using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Archoran.Utils.VPEAutoPlow")]
internal class VanillaPlantsAutoPlowPatch
{
	public VanillaPlantsAutoPlowPatch(ModContentPack mod)
	{
		MP.RegisterSyncDelegateLambda(AccessTools.TypeByName("VPEAutoPlow.Patch_Zone_Growing_GetGizmos"), "Add_AllowAutoPlow_Gizmo", 1);
	}
}
