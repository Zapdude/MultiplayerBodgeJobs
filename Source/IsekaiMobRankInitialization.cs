using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

internal static class IsekaiMobRankInitialization
{
	private const int RankPurpose = 1297239627;

	private const int StatsPurpose = 1297306689;

	private static AccessTools.FieldRef<object, bool> statsInitialized;

	private static AccessTools.FieldRef<object, int> rankSystemVersion;

	private static int currentVersion;

	internal static void Setup()
	{
		Type type = AccessTools.TypeByName("IsekaiLeveling.MobRanking.MobRankComponent");
		if (!(type == null))
		{
			MethodInfo methodInfo = AccessTools.DeclaredMethod(type, "RecalculateRank", Type.EmptyTypes);
			MethodInfo methodInfo2 = AccessTools.DeclaredMethod(type, "EnsureStatsInitialized", new Type[1] { typeof(string) });
			FieldInfo fieldInfo = AccessTools.DeclaredField(type, "CurrentRankSystemVersion");
			if (methodInfo == null || methodInfo2 == null || (object)fieldInfo == null || !fieldInfo.IsLiteral || fieldInfo.FieldType != typeof(int))
			{
				throw new InvalidOperationException("Unexpected Isekai creature rank initialization contract");
			}
			statsInitialized = AccessTools.FieldRefAccess<object, bool>(AccessTools.Field(type, "statsInitialized"));
			rankSystemVersion = AccessTools.FieldRefAccess<object, int>(AccessTools.Field(type, "rankSystemVersion"));
			currentVersion = (int)fieldInfo.GetRawConstantValue();
			MpCompat.harmony.Patch(methodInfo, new HarmonyMethod(typeof(IsekaiMobRankInitialization), "BeforeRank"), null, null, new HarmonyMethod(typeof(IsekaiMobRankInitialization), "AfterScope"));
			MpCompat.harmony.Patch(methodInfo2, new HarmonyMethod(typeof(IsekaiMobRankInitialization), "BeforeStats"), new HarmonyMethod(typeof(IsekaiMobRankInitialization), "CommitVersion"), null, new HarmonyMethod(typeof(IsekaiMobRankInitialization), "AfterScope"));
		}
	}

	private static void Begin(object instance, int purpose, out bool pushed)
	{
		pushed = false;
		if (MP.IsInMultiplayer && !statsInitialized(instance) && Find.World != null && instance is ThingComp { parent: Pawn parent })
		{
			Rand.PushState(Gen.HashCombineInt(Gen.HashCombineInt(Find.World.ConstantRandSeed, parent.thingIDNumber), purpose));
			pushed = true;
		}
	}

	private static void BeforeRank(object __instance, out bool __state)
	{
		Begin(__instance, 1297239627, out __state);
	}

	private static void BeforeStats(object __instance, out bool __state)
	{
		Begin(__instance, 1297306689, out __state);
	}

	private static void CommitVersion(object __instance, bool __state)
	{
		if (__state && statsInitialized(__instance))
		{
			rankSystemVersion(__instance) = currentVersion;
		}
	}

	private static void AfterScope(bool __state)
	{
		if (__state)
		{
			Rand.PopState();
		}
	}
}
