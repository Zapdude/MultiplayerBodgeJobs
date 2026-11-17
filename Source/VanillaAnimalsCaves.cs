using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("VanillaExpanded.VAECaves")]
internal class VanillaAnimalsCaves
{
	public VanillaAnimalsCaves(ModContentPack mod)
	{
		PatchingUtilities.PatchSystemRand(new string[4] { "VAECaves.Building_SpiderEggs:Tick", "VAECaves.DamageWorker_TwoArmSlam:Apply", "VAECaves.IncidentWorker_Hulk:CanFireNowSub", "VAECaves.JobDriver_CreateEggs:<MakeNewToils>b__1_0" }, patchPushPop: false);
		PatchingUtilities.PatchSystemRandCtor("VAECaves.Hediff_WallBreaker", patchPushPop: false);
		MpCompat.RegisterLambdaMethod("VAECaves.Building_Cocoon", "GetGizmos", default(int));
		MpCompat.RegisterLambdaMethod("VAECaves.CompConditionalSpawner", "CompGetGizmosExtra", default(int)).SetDebugOnly();
	}
}
