using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

internal static class VanillaGravshipMaintenanceEffects
{
	internal static void Install(Assembly author)
	{
		Type type = author.GetType("VanillaGravshipExpanded.CompGravMaintainable", throwOnError: true);
		MethodInfo original = AccessTools.DeclaredMethod(type, "EmissionTick", new Type[1] { typeof(IFleckCreator) }) ?? throw new MissingMethodException(type.FullName, "EmissionTick(IFleckCreator)");
		MpCompat.harmony.Patch(original, new HarmonyMethod(typeof(VanillaGravshipMaintenanceEffects), "BeforeEmission")
		{
			priority = 800
		}, null, null, new HarmonyMethod(typeof(VanillaGravshipMaintenanceEffects), "AfterEmission")
		{
			priority = 0
		});
	}

	private static void BeforeEmission(out bool __state)
	{
		__state = false;
		if (MP.IsInMultiplayer)
		{
			Rand.PushState();
			__state = true;
		}
	}

	private static Exception AfterEmission(Exception __exception, bool __state)
	{
		if (__state)
		{
			Rand.PopState();
		}
		return __exception;
	}
}
