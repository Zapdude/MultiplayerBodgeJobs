using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("VanillaExpanded.VFEFactory")]
internal class VanillaFurnitureExpandedFactory
{
	public VanillaFurnitureExpandedFactory(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
	}

	private static void LatePatch()
	{
		MpCompat.RegisterLambdaDelegate("VanillaFurnitureExpandedFactory.Building_Conveyor", "GetGizmos", 1, 2, 3, 4);
		MpCompat.RegisterLambdaMethod("VanillaFurnitureExpandedFactory.Building_FactoryHopper", "GetGizmos", 1, 3);
		MpCompat.RegisterLambdaDelegate("VanillaFurnitureExpandedFactory.Building_UndergroundConveyorBase", "GetGizmos", 2, 4);
		MpCompat.RegisterLambdaMethod("VanillaFurnitureExpandedFactory.Building_Autofarmer", "GetGizmos", 0, 1, 2, 3, 5, 7, 9);
		MP.RegisterSyncMethod(AccessTools.TypeByName("VanillaFurnitureExpandedFactory.Building_Autofarmer"), "SetPlantDefToGrow").SetVersion(17);
	}
}
