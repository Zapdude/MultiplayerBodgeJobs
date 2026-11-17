using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Inoshishi3.SmallVehicleAddons")]
public class SmallVehicleAddons
{
	public SmallVehicleAddons(ModContentPack mod)
	{
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod("SmallVehicleAddons.CompFastCargo:AddItemsToTheVehicleTransferables"));
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod("SmallVehicleAddons.CompHonk:DoHonkHonk"));
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod("SmallVehicleAddons.CompLights:ToggleDeployment"));
	}
}
