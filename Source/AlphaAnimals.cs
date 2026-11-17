using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("sarg.alphaanimals")]
internal class AlphaAnimals
{
	private static MethodBase unsafeMethod;

	public AlphaAnimals(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
		if (!ModLister.AnyModActiveNoSuffix(new string[1] { "OskarPotocki.VFE.Insectoid2" }))
		{
			unsafeMethod = AccessTools.DeclaredMethod("AlphaBehavioursAndEvents.BlackCocoon:Tick");
			if (unsafeMethod != null)
			{
				MpCompat.harmony.Patch(AccessTools.DeclaredMethod("Multiplayer.Client.Extensions:PatchMeasure"), new HarmonyMethod(new Func<MethodBase, bool>(DontPatchUnsafeMethods)));
			}
		}
	}

	private static void LatePatch()
	{
		MethodInfo methodInfo = AccessTools.DeclaredMethod("AlphaBehavioursAndEvents.Pawn_Detonator:GetGizmos");
		if (methodInfo != null)
		{
			MP.RegisterSyncMethodLambda(methodInfo.DeclaringType, methodInfo.Name, 0);
		}
	}

	private static bool DontPatchUnsafeMethods(MethodBase original)
	{
		return original != unsafeMethod;
	}
}
