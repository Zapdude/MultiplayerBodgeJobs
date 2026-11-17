using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

internal static class VanillaGravshipPreview
{
	private static readonly HashSet<Type> supportedTypes = new HashSet<Type>();

	private static readonly HashSet<MethodInfo> patchedCaptures = new HashSet<MethodInfo>();

	private static bool drawPatched;

	internal static void Install(params string[] typeNames)
	{
		foreach (string text in typeNames)
		{
			Type type = AccessTools.TypeByName(text) ?? throw new InvalidOperationException("VGE landing preview type was not loaded: " + text);
			MethodInfo methodInfo = AccessTools.Method(type, "CaptureGravshipCoroutine") ?? throw new InvalidOperationException("VGE landing preview coroutine was not found: " + text);
			methodInfo = AccessTools.DeclaredMethod(methodInfo.DeclaringType, methodInfo.Name);
			if (supportedTypes.Add(type) && patchedCaptures.Add(methodInfo))
			{
				MpCompat.harmony.Patch(methodInfo, new HarmonyMethod(typeof(VanillaGravshipPreview), "BeforePreviewCapture"));
			}
		}
		if (!drawPatched)
		{
			MpCompat.harmony.Patch(AccessTools.Method("VanillaGravshipExpanded.LandingStructureBase:DrawAt"), new HarmonyMethod(typeof(VanillaGravshipPreview), "BeforeLandingDraw"));
			drawPatched = true;
		}
	}

	private static bool BeforePreviewCapture(Thing __instance, ref IEnumerator __result)
	{
		if (!MP.IsInMultiplayer || !supportedTypes.Contains(__instance.GetType()))
		{
			return true;
		}
		__result = Array.Empty<object>().GetEnumerator();
		return false;
	}

	private static bool BeforeLandingDraw(Thing __instance)
	{
		if (!MP.IsInMultiplayer || !supportedTypes.Contains(__instance.GetType()))
		{
			return true;
		}
		GenDraw.DrawRadiusRing(__instance.Position, 12f);
		return false;
	}
}
