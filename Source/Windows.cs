using HarmonyLib;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("JPT.OpenTheWindows")]
internal class Windows
{
	public Windows(ModContentPack mod)
	{
		MpCompat.RegisterLambdaMethod(AccessTools.TypeByName("OpenTheWindows.Building_Window"), "GetGizmos", 1, 3, 5);
		MpCompat.RegisterLambdaMethod(AccessTools.TypeByName("OpenTheWindows.CompWindow"), "CompGetGizmosExtra", 1);
	}
}
