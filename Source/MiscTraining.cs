using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Haplo.Miscellaneous.Training")]
internal class MiscTraining
{
	public MiscTraining(ModContentPack mod)
	{
		PatchingUtilities.PatchPushPopRand("TrainingFacility.JobDriver_Archery:ShootArrow");
	}
}
