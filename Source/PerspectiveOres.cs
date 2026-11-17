using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Owlchemist.PerspectiveOres")]
public class PerspectiveOres
{
	public PerspectiveOres(ModContentPack mod)
	{
		PatchingUtilities.PatchLongEventMarkers();
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod("PerspectiveOres.PerspectiveOresSetup:ProcessMap"), new HarmonyMethod(typeof(PerspectiveOres), "StopSecondHostCall"));
	}

	private static bool StopSecondHostCall(bool reset)
	{
		if (!PatchingUtilities.AllowedToRunLongEvents)
		{
			return false;
		}
		if (reset && MP.IsInMultiplayer)
		{
			return false;
		}
		return true;
	}
}
