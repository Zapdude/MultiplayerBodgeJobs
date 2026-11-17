using HarmonyLib;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Mlie.SimplyMoreBridges")]
internal class SimplyMoreBridgesCompat
{
	public SimplyMoreBridgesCompat(ModContentPack mod)
	{
		MpCompat.harmony.Patch(AccessTools.Method("SimplyMoreBridges.GenerateBridges:generateBridgeDef"), null, new HarmonyMethod(typeof(SimplyMoreBridgesCompat), "GenerateBridgeDefPostfix"));
	}

	private static void GenerateBridgeDefPostfix(TerrainDef __result)
	{
		DefDatabase<BuildableDef>.Add(__result);
	}
}
