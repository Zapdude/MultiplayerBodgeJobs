using HarmonyLib;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("fluffy.stuffedfloors")]
internal class StuffedFloorsCompat
{
	public StuffedFloorsCompat(ModContentPack mod)
	{
		MpCompat.harmony.Patch(AccessTools.Method("StuffedFloors.FloorTypeDef:GetStuffedTerrainDef"), null, new HarmonyMethod(typeof(StuffedFloorsCompat), "GetStuffedTerrainDefPosfix"));
	}

	private static void GetStuffedTerrainDefPosfix(TerrainDef __result)
	{
		DefDatabase<BuildableDef>.Add(__result);
	}
}
