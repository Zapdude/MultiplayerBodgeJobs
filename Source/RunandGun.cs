using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("roolo.RunAndGun")]
internal class RunandGun
{
	public RunandGun(ModContentPack mod)
	{
		MpCompat.RegisterLambdaDelegate("RunAndGun.Harmony.Pawn_DraftController_GetGizmos_Patch", "Postfix", 2);
		PatchingUtilities.PatchUnityRand("RunAndGun.Harmony.MentalStateHandler_TryStartMentalState:shouldRunAndGun", patchPushPop: false);
	}
}
