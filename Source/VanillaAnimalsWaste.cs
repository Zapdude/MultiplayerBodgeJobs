using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("VanillaExpanded.VAEWaste")]
public class VanillaAnimalsWaste
{
	public VanillaAnimalsWaste(ModContentPack mod)
	{
		PatchingUtilities.PatchSystemRand(new string[2] { "VanillaAnimalsExpandedWaste.HediffComp_Toxflu:CompPostPostRemoved", "VanillaAnimalsExpandedWaste.VanillaAnimalsExpandedWaste_InteractionWorker_Interacted_Patch:Toxflu" }, patchPushPop: false);
	}
}
