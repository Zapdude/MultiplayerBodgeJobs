using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("VanillaExpanded.HelixienGas")]
public class VanillaHelixienGasExpanded
{
	public VanillaHelixienGasExpanded(ModContentPack mod)
	{
		MpCompat.RegisterLambdaMethod("VHelixienGasE.Building_GasGeyser", "GetGizmos", default(int));
		PatchingUtilities.PatchPushPopRand("VHelixienGasE.IntermittentGasSprayer:ThrowGasPuffUp");
		InstallInspect();
	}

	private static void InstallInspect()
	{
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(CompTempControl), "CompInspectStringExtra"), null, new HarmonyMethod(typeof(VanillaHelixienGasExpanded), "GasHeaterInspect"));
	}

	private static void GasHeaterInspect(CompTempControl __instance, ref string __result)
	{
		if (MP.IsInMultiplayer && __instance.parent?.def?.defName == "VHGE_GasHeater")
		{
			__result = __result?.TrimEnd(new char[2] { '\r', '\n' });
		}
	}
}
