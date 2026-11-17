using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat.Mods;

[MpCompatFor("Aelanna.EccentricTech.Flares2")]
internal class EccentricTechFlares
{
	public EccentricTechFlares(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
	}

	private void LatePatch()
	{
		MP.RegisterSyncMethod(AccessTools.TypeByName("EccentricFlares.CompIlluminatorPack"), "DoStartThrow");
		MpCompat.RegisterLambdaMethod(AccessTools.TypeByName("EccentricFlares.CompIlluminatorPack"), "CompGetWornGizmosExtra", 1);
	}
}
