using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace Multiplayer.Compat;

internal static class VanillaRacesLycanthropeRuntime
{
	private sealed class LightState
	{
		internal bool ValidNegative;
	}

	private static bool installed;

	private static AccessTools.FieldRef<object, int> lightSeen;

	private static FieldInfo lightSeenField;

	private static readonly ConditionalWeakTable<MentalState, LightState> lightStates = new ConditionalWeakTable<MentalState, LightState>();

	internal static void Install()
	{
		if (!installed)
		{
			Type type = AccessTools.TypeByName("VanillaRacesExpandedLycanthrope.MentalState_Photophobia");
			lightSeenField = AccessTools.DeclaredField(type, "lastLightSeenTick");
			lightSeen = AccessTools.FieldRefAccess<int>(type, "lastLightSeenTick");
			PatchingUtilities.RegisterTimestampFields(type, FixLight);
			MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, "MentalStateTick"), null, null, new HarmonyMethod(typeof(VanillaRacesLycanthropeRuntime), "LightMarkers"));
			MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, "ExposeData"), null, new HarmonyMethod(typeof(VanillaRacesLycanthropeRuntime), "ExposeLight"));
			installed = true;
		}
	}

	private static void FixLight(object obj, int offset)
	{
		MentalState mentalState = (MentalState)obj;
		LightState value = lightStates.GetValue(mentalState, (MentalState _) => new LightState());
		ref int reference = ref lightSeen(mentalState);
		if (reference >= 0 || value.ValidNegative)
		{
			reference += offset;
			value.ValidNegative = reference < 0;
		}
	}

	private static int LightMarker(MentalState owner)
	{
		int num = lightSeen(owner);
		if (num >= 0 || !lightStates.TryGetValue(owner, out var value) || !value.ValidNegative)
		{
			return num;
		}
		return 0;
	}

	private static void RecordLight(MentalState owner, int tick)
	{
		lightSeen(owner) = tick;
		if (lightStates.TryGetValue(owner, out var value))
		{
			value.ValidNegative = false;
		}
	}

	private static void ExposeLight(MentalState __instance)
	{
		LightState value = lightStates.GetValue(__instance, (MentalState _) => new LightState());
		if (Scribe.mode == LoadSaveMode.Saving && lightSeen(__instance) >= 0)
		{
			value.ValidNegative = false;
		}
		Scribe_Values.Look(ref value.ValidNegative, "mpVreLightSeenTickIsValidNegative", defaultValue: false);
	}

	private static IEnumerable<CodeInstruction> LightMarkers(IEnumerable<CodeInstruction> instructions)
	{
		List<CodeInstruction> list = instructions.ToList();
		int num = 0;
		int num2 = 0;
		for (int i = 0; i < list.Count; i++)
		{
			if (i + 1 < list.Count && list[i].LoadsField(lightSeenField) && list[i + 1].LoadsConstant(0L))
			{
				list[i].opcode = OpCodes.Call;
				list[i].operand = AccessTools.Method(typeof(VanillaRacesLycanthropeRuntime), "LightMarker");
				num++;
			}
			if (list[i].StoresField(lightSeenField))
			{
				list[i].opcode = OpCodes.Call;
				list[i].operand = AccessTools.Method(typeof(VanillaRacesLycanthropeRuntime), "RecordLight");
				num2++;
			}
		}
		if (num != 2 || num2 != 1)
		{
			throw new InvalidOperationException($"VRE photophobia timestamp IL changed: {num}/{num2}");
		}
		return list;
	}
}
