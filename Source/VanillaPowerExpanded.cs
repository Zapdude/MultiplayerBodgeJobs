using HarmonyLib;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("VanillaExpanded.VFEPower")]
internal class VanillaPowerExpanded
{
	public VanillaPowerExpanded(ModContentPack mod)
	{
		MpCompat.RegisterLambdaMethod(AccessTools.TypeByName("VanillaPowerExpanded.CompSoulsPowerPlant"), "CompGetGizmosExtra", 1);
	}
}
