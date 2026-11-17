using HarmonyLib;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("vanillaracesexpanded.lycanthrope")]
public class VanillaRacesExpandedLycanthrope
{
	public VanillaRacesExpandedLycanthrope(ModContentPack mod)
	{
		AccessTools.DeclaredField("VanillaRacesExpandedLycanthrope.VanillaRacesExpandedLycanthrope_InteractionWorker_Interacted_Patch:random").SetValue(null, PatchingUtilities.RandRedirector.Instance);
		VanillaRacesLycanthropeRuntime.Install();
	}
}
