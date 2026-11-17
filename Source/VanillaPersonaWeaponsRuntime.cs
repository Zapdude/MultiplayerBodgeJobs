using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

internal static class VanillaPersonaWeaponsRuntime
{
	private delegate bool WorldReader(out int ticks, out TimeSpeed speed, out TimeSlower slower, out int start);

	private static bool installed;

	private static Type letterType;

	private static FieldInfo openedTickField;

	private static AccessTools.FieldRef<object, int> openedTick;

	private static WorldReader readWorld;

	internal static void Install()
	{
		if (!installed)
		{
			SessionServices.Install();
			letterType = AccessTools.TypeByName("VanillaPersonaWeaponsExpanded.ChoiceLetter_ChoosePersonaWeapon");
			openedTickField = AccessTools.DeclaredField(letterType, "tickWhenOpened");
			openedTick = AccessTools.FieldRefAccess<int>(letterType, "tickWhenOpened");
			readWorld = (WorldReader)AccessTools.DeclaredMethod("Multiplayer.Client.Compatibility.OwnerClock:TryReadWorld").CreateDelegate(typeof(WorldReader));
			MP.RegisterSyncMethod(typeof(VanillaPersonaWeaponsRuntime), "SyncMarkOpened").SetVersion(17);
			MpCompat.harmony.Patch(AccessTools.DeclaredMethod(letterType, "OpenLetter"), null, null, new HarmonyMethod(typeof(VanillaPersonaWeaponsRuntime), "ReplaceLocalTickWrite"));
			MpCompat.harmony.Patch(AccessTools.DeclaredMethod(letterType, "OpenChooseDialog"), new HarmonyMethod(typeof(VanillaPersonaWeaponsRuntime), "BeforePreview"), null, new HarmonyMethod(typeof(VanillaPersonaWeaponsRuntime), "ReplaceLocalTickWrite"), new HarmonyMethod(typeof(VanillaPersonaWeaponsRuntime), "AfterPreview"));
			installed = true;
		}
	}

	private static void SyncMarkOpened(ChoiceLetter letter)
	{
		if (letter != null && letterType.IsInstanceOfType(letter) && !letter.ArchivedOnly && readWorld(out var ticks, out var _, out var _, out var _))
		{
			openedTick(letter) = ticks;
		}
	}

	private static void BeforePreview(out IDisposable __state)
	{
		__state = null;
		if (MP.IsInMultiplayer)
		{
			__state = SessionServices.EnterLocalOnly();
		}
	}

	private static Exception AfterPreview(Exception __exception, IDisposable __state)
	{
		__state?.Dispose();
		return __exception;
	}

	private static void StoreOpenTick(ChoiceLetter letter, int tick)
	{
		if (!MP.IsInMultiplayer)
		{
			openedTick(letter) = tick;
		}
		else
		{
			SyncMarkOpened(letter);
		}
	}

	private static IEnumerable<CodeInstruction> ReplaceLocalTickWrite(IEnumerable<CodeInstruction> instructions)
	{
		int count = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.StoresField(openedTickField))
			{
				instruction.opcode = OpCodes.Call;
				instruction.operand = AccessTools.Method(typeof(VanillaPersonaWeaponsRuntime), "StoreOpenTick");
				count++;
			}
			yield return instruction;
		}
		if (count != 1)
		{
			throw new InvalidOperationException("VPWE open-time field write changed: " + count);
		}
	}
}
