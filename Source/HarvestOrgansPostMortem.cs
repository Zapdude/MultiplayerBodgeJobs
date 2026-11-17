using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Smuffle.HarvestOrgansPostMortem")]
public class HarvestOrgansPostMortem
{
	public HarvestOrgansPostMortem(ModContentPack mod)
	{
		PatchingUtilities.PatchSystemRand("Autopsy.NewMedicalRecipesUtility:TraverseBody");
	}
}
