using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("smartkar.enhancedvatlearning")]
public class EnhancedVatLearning
{
	public EnhancedVatLearning(ModContentPack mod)
	{
		PatchingUtilities.PatchSystemRandCtor("EnhancedVatLearning.HediffComp_EnhancedLearning", patchPushPop: false);
	}
}
