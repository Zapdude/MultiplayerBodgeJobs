using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Fluffy.FollowMe")]
internal class FollowMe
{
	public static Type cinematicCamera = AccessTools.TypeByName("FollowMe.CinematicCamera");

	public static MethodInfo followNewSubject = AccessTools.Method("FollowMe.CinematicCamera:FollowNewSubject");

	public static MethodInfo verseRandRange = AccessTools.Method("Verse.Rand:Range", new Type[2]
	{
		typeof(int),
		typeof(int)
	});

	private static uint state;

	public FollowMe(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
	}

	private static uint myCheapRand()
	{
		uint num = state;
		uint num2 = num ^ (num << 13);
		uint num3 = num2 ^ (num2 >> 17);
		return state = num3 ^ (num3 << 5);
	}

	private static float myCheapRandFloat()
	{
		return (float)myCheapRand() / 4.2949673E+09f;
	}

	private static int myCheapRange(int min, int max)
	{
		float num = myCheapRandFloat();
		return (int)((float)min + (float)(max - min) * num);
	}

	public static IEnumerable<CodeInstruction> transpileCheapRand(IEnumerable<CodeInstruction> instr)
	{
		foreach (CodeInstruction item in instr)
		{
			if (item.opcode == OpCodes.Call && item.operand is MethodInfo methodInfo && methodInfo == verseRandRange)
			{
				item.operand = AccessTools.Method(typeof(FollowMe), "myCheapRange");
			}
			yield return item;
		}
	}

	private static void LatePatch()
	{
		state = (uint)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds;
		HarmonyMethod transpiler = new HarmonyMethod(typeof(FollowMe), "transpileCheapRand");
		MpCompat.harmony.Patch(followNewSubject, null, null, transpiler);
	}
}
