using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("memegoddess.RunAndGun")]
internal class RunandGunContinued
{
	public RunandGunContinued(ModContentPack mod)
	{
		MpCompat.RegisterLambdaDelegate("RunAndGun.Harmony.Pawn_DraftController_GetGizmos_Patch", "Postfix", 1);
		PatchingUtilities.PatchUnityRand("RunAndGun.Harmony.MentalStateHandler_TryStartMentalState:shouldRunAndGun", patchPushPop: false);
	}
}
