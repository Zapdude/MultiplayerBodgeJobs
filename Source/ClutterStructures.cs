using HarmonyLib;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("neronix17.clutterstructures")]
internal class ClutterStructures
{
	public ClutterStructures(ModContentPack mod)
	{
		MpCompat.harmony.Patch(AccessTools.Method("Clutter_StructureWall.StructureDefGenerator:StuffGeneratrs"), null, new HarmonyMethod(typeof(ClutterStructures), "GetClutterStructureDefPostfix"));
	}

	private static void GetClutterStructureDefPostfix(ThingDef Wallzie)
	{
		DefDatabase<BuildableDef>.Add(Wallzie);
	}
}
