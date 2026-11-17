using System;
using HarmonyLib;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("fridgeBaron.Immortals")]
internal class Immortals
{
	private static Type immortalComponent;

	private static bool shouldCall;

	public Immortals(ModContentPack mod)
	{
		immortalComponent = AccessTools.TypeByName("Immortals.Immortal_Component");
		MpCompat.harmony.Patch(AccessTools.Method("Immortals.Immortal_Component:GameComponentUpdate"), new HarmonyMethod(typeof(Immortals), "GameComponentUpdatePrefix"));
		MpCompat.harmony.Patch(AccessTools.Method("Verse.GameComponent:GameComponentTick"), new HarmonyMethod(typeof(Immortals), "GameComponentTickPrefix"));
	}

	private static void GameComponentTickPrefix(GameComponent __instance)
	{
		if (__instance.GetType() == immortalComponent)
		{
			shouldCall = true;
			__instance.GameComponentUpdate();
			shouldCall = false;
		}
	}

	private static bool GameComponentUpdatePrefix()
	{
		return shouldCall;
	}
}
