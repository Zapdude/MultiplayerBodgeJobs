using System;
using System.Collections;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("erdelf.MinifyEverything")]
public class MinifyEverything
{
	public MinifyEverything(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
	}

	public static void LatePatch()
	{
		Type type = AccessTools.TypeByName("MinifyEverything.MinifyEverything");
		MpCompat.harmony.Patch(AccessTools.Method(type, "DoStuff"), new HarmonyMethod(typeof(MinifyEverything), "PreDoStuff"));
	}

	private static bool PreDoStuff(Action action, ref IEnumerator __result)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		__result = FakeCoroutine();
		LongEventHandler.ExecuteWhenFinished(action);
		return false;
	}

	private static IEnumerator FakeCoroutine()
	{
		yield return null;
	}
}
