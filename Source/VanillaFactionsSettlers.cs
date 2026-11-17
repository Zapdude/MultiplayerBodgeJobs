using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("OskarPotocki.VanillaFactionsExpanded.SettlersModule")]
internal class VanillaFactionsSettlers
{
	public VanillaFactionsSettlers(ModContentPack mod)
	{
		Type type = AccessTools.TypeByName("VFE_Settlers.Utilities.UtilityEvent");
		MP.RegisterSyncDialogNodeTree(type, "ProtectionFee");
		MpCompat.RegisterLambdaDelegate(type, "CommandTurnInWanted", default(int));
		PatchingUtilities.PatchUnityRand("VFE_Settlers.JobGivers.JobDriver_PlayFiveFingerFillet:WatchTickAction", patchPushPop: false);
	}
}
