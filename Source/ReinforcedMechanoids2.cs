using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("hlx.ReinforcedMechanoids2")]
public class ReinforcedMechanoids2
{
	public ReinforcedMechanoids2(ModContentPack mod)
	{
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod("ReinforcedMechanoids.CompCauseGameCondition_ForceWeather:ChangeWeather"));
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod("ReinforcedMechanoids.CompCauseGameCondition_TemperatureOffset:SetTemperatureOffset"));
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod("ReinforcedMechanoids.CompPawnJumper:DoJump", new Type[1] { typeof(LocalTargetInfo) }));
		MpCompat.RegisterLambdaDelegate("ReinforcedMechanoids.CompAllianceOverlayToggle", "CompGetGizmosExtra", 1);
		MpCompat.RegisterLambdaMethod("ReinforcedMechanoids.CompInvisibility", "CompGetGizmosExtra", 1);
	}
}
