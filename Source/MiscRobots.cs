using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Haplo.Miscellaneous.Robots")]
internal class MiscRobots
{
	public MiscRobots(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
	}

	private static void LatePatch()
	{
		Type type = AccessTools.TypeByName("AIRobot.X2_Building_AIRobotRechargeStation");
		Type type2 = AccessTools.TypeByName("AIRobot.X2_AIRobot");
		Type type3 = AccessTools.TypeByName("AIRobot.AIRobot_Helper");
		MP.RegisterSyncMethod(type, "Button_CallAllBotsForShutdown");
		MP.RegisterSyncMethod(type, "Button_CallBotForShutdown");
		MP.RegisterSyncMethod(type, "Button_RequestRepair4Robot");
		MP.RegisterSyncMethod(type, "Button_RepairDamagedRobot").SetDebugOnly();
		MP.RegisterSyncMethod(type, "Button_ResetDestroyedRobot").SetDebugOnly();
		MP.RegisterSyncMethod(type, "Button_SpawnAllAvailableBots");
		MP.RegisterSyncMethod(type, "Button_SpawnBot");
		MP.RegisterSyncMethod(type2, "Debug_ForceGotoDistance");
		MP.RegisterSyncMethod(type2, "Debug_Info");
		MP.RegisterSyncMethod(type, "AddRobotToContainer").SetContext(SyncContext.CurrentMap);
		PatchingUtilities.PatchPushPopRand("AIRobot.MoteThrowHelper:ThrowBatteryXYZ");
		PatchingUtilities.PatchPushPopRand("AIRobot.MoteThrowHelper:ThrowNoRobotSign");
		PatchingUtilities.PatchPushPopRand("AIRobot.X2_Building_AIRobotRechargeStation:TryHealDamagedBodyPartOfRobot");
		MP.RegisterSyncMethod(type3, "StartStationRepairJob");
	}
}
