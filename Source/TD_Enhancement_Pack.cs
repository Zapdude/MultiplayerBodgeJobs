using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("memegoddess.tdpack")]
internal class TD_Enhancement_Pack
{
	public TD_Enhancement_Pack(ModContentPack mod)
	{
		MP.RegisterSyncMethod(AccessTools.TypeByName("TD_Enhancement_Pack.Zone_Growing_Extensions"), "ToggleHarvest");
		MP.RegisterSyncMethod(AccessTools.TypeByName("TD_Enhancement_Pack.Building_PlantGrower_Extensions"), "ToggleHarvest");
	}
}
