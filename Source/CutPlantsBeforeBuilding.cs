using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("tammybee.cutplantsbeforebuilding")]
[MpCompatFor("Mlie.CutPlantsBeforeBuilding")]
internal class CutPlantsBeforeBuilding
{
	private static ISyncField syncAutoCutPlants;

	public CutPlantsBeforeBuilding(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
	}

	private static void LatePatch()
	{
		syncAutoCutPlants = MP.RegisterSyncField(AccessTools.Field(AccessTools.TypeByName("CutPlantsBeforeBuilding.Main"), "AutoDesignatePlantsCutMode"));
		MpCompat.harmony.Patch(AccessTools.Method("CutPlantsBeforeBuilding.HarmonyPatches.PlaySettings_DoPlaySettingsGlobalControls:Prefix"), new HarmonyMethod(new Action(WatchAutoCutPlantsPrefix)));
	}

	private static void WatchAutoCutPlantsPrefix()
	{
		syncAutoCutPlants.Watch();
	}
}
