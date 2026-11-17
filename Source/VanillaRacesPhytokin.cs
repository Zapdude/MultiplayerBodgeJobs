using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("vanillaracesexpanded.phytokin")]
public class VanillaRacesPhytokin
{
	public VanillaRacesPhytokin(ModContentPack mod)
	{
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod("VanillaRacesExpandedPhytokin.HediffComp_Saplingchild:TryCreateSaplingChild"));
		Type type = AccessTools.TypeByName("VanillaRacesExpandedPhytokin.CompVariablePollutionPump");
		MP.RegisterSyncMethod(type, "Pump").SetDebugOnly();
		MpCompat.RegisterLambdaDelegate(type, "CompGetGizmosExtra", 1).SetDebugOnly();
		type = AccessTools.TypeByName("VanillaRacesExpandedPhytokin.Building_SaplingChild");
		MP.RegisterSyncMethod(type, "Hatch").SetDebugOnly().SetVersion(17);
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod(type, "ProgressBirth", new Type[1] { typeof(int) })).SetDebugOnly().SetVersion(17);
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod(type, "ProgressBirth", new Type[1] { typeof(float) })).SetDebugOnly().SetVersion(17);
	}
}
