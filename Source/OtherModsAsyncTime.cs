using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

internal static class OtherModsAsyncTime
{
	private static bool pipeProcessorInstalled;

	private static bool ideologyClockInstalled;

	private static Func<int> worldTicks;

	internal static void InstallIdeologyClock()
	{
		if (!ideologyClockInstalled)
		{
			PatchingUtilities.SetupAsyncTime();
			worldTicks = AccessTools.MethodDelegate<Func<int>>(AccessTools.Method("Multiplayer.Client.AsyncTimeComp:GetWorldTicks") ?? throw new MissingMethodException("AsyncTimeComp", "GetWorldTicks"));
			Type type = AccessTools.TypeByName("IdeoTickOpt.IdeoTickCache") ?? throw new TypeLoadException("IdeoTickOpt.IdeoTickCache");
			string[] array = new string[2] { "TryRecountIdeosImmediately", "TryRecountIdeos" };
			foreach (string name in array)
			{
				MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, name), null, null, new HarmonyMethod(typeof(OtherModsAsyncTime), "GlobalCacheClock"));
			}
			ideologyClockInstalled = true;
		}
	}

	private static int GlobalTicks(int nativeTicks)
	{
		if (!MP.IsInMultiplayer || !PatchingUtilities.IsAsyncTime)
		{
			return nativeTicks;
		}
		return worldTicks();
	}

	private static IEnumerable<CodeInstruction> GlobalCacheClock(IEnumerable<CodeInstruction> instructions)
	{
		int count = 0;
		MethodInfo getter = AccessTools.PropertyGetter(typeof(TickManager), "TicksGame");
		foreach (CodeInstruction instruction in instructions)
		{
			yield return instruction;
			if (instruction.Calls(getter))
			{
				yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(OtherModsAsyncTime), "GlobalTicks"));
				count++;
			}
		}
		if (count != 2)
		{
			throw new InvalidOperationException("Ideology global cache clock pattern changed");
		}
	}

	internal static void InstallPipeProcessor()
	{
		if (!pipeProcessorInstalled)
		{
			Type type = AccessTools.TypeByName("PipeSystem.CompResourceProcessor") ?? throw new TypeLoadException("PipeSystem.CompResourceProcessor");
			AccessTools.FieldRef<object, int> deadline = AccessTools.FieldRefAccess<int>(type, "nextProcessTick");
			PatchingUtilities.RegisterTimestampFields(type, (object component, int offset) =>
			{
				deadline(component) = deadline(component) + offset;
			});
			pipeProcessorInstalled = true;
		}
	}
}
