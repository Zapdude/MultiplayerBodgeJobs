using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("OskarPotocki.VanillaVehiclesExpanded")]
public class VanillaVehiclesExpanded
{
	private static FieldInfo handbrakeDealsDamageField;

	private static bool savedHandbrakeValue;

	public VanillaVehiclesExpanded(ModContentPack mod)
	{
		Type type = AccessTools.TypeByName("VanillaVehiclesExpanded.GarageDoor");
		MpCompat.RegisterLambdaDelegate(type, "GetDoorGizmos", 0, 1, 2, 3);
		MP.RegisterSyncMethod(type, "StartOpening");
		MP.RegisterSyncMethod(type, "StartClosing");
		type = AccessTools.TypeByName("VanillaVehiclesExpanded.CompRefrigerator");
		MpCompat.RegisterLambdaMethod(type, "CompGetGizmosExtra", 1);
		type = AccessTools.TypeByName("VanillaVehiclesExpanded.CompReloadableWithVerbs");
		MP.RegisterSyncMethod(type, "TryReload");
		MpCompat.RegisterLambdaMethod(type, "CompGetGizmosExtra", default(int)).SetDebugOnly();
		type = AccessTools.TypeByName("VanillaVehiclesExpanded.CompVehicleWreck");
		MpCompat.RegisterLambdaDelegate(type, "CompGetGizmosExtra", 0, 1);
		handbrakeDealsDamageField = AccessTools.Field(AccessTools.TypeByName("VanillaVehiclesExpanded.VanillaVehiclesExpandedSettings"), "handbrakeDealsDamage");
		MethodInfo methodInfo = AccessTools.DeclaredMethod(AccessTools.TypeByName("VanillaVehiclesExpanded.CompVehicleMovementController"), "Slowdown");
		if (methodInfo != null && handbrakeDealsDamageField != null)
		{
			MpCompat.harmony.Patch(methodInfo, new HarmonyMethod(typeof(VanillaVehiclesExpanded), "PreSlowdown"), new HarmonyMethod(typeof(VanillaVehiclesExpanded), "PostSlowdown"));
		}
	}

	private static void PreSlowdown()
	{
		if (MP.IsInMultiplayer)
		{
			savedHandbrakeValue = (bool)handbrakeDealsDamageField.GetValue(null);
			handbrakeDealsDamageField.SetValue(null, true);
		}
	}

	private static void PostSlowdown()
	{
		if (MP.IsInMultiplayer)
		{
			handbrakeDealsDamageField.SetValue(null, savedHandbrakeValue);
		}
	}
}
