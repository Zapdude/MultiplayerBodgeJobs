using HarmonyLib;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("avilmask.GrazingLands")]
public class GrazingLands
{
	public GrazingLands(ModContentPack mod)
	{
		PatchingUtilities.PatchSystemRand(AccessTools.Method(AccessTools.Inner(AccessTools.TypeByName("GrazingLands.PlantPropertiesPatch"), "Plant_IngestedCalculateAmounts_GrazingLandsPatch"), "Prefix"), patchPushPop: false);
	}
}
