using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Aoba.DeadManSwitch.AncientCorps")]
public sealed class DeadMansSwitchAncientCorps
{
	public DeadMansSwitchAncientCorps(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(() =>
		{
			MpCompat.harmony.Patch(AccessTools.DeclaredMethod("AncientCorps.CompUseEffect_DisableMechs:DoEffect"), new HarmonyMethod(typeof(DeadMansSwitchAncientCorps), "ValidUse"));
		});
	}

	private static bool ValidUse(Pawn usedBy)
	{
		if (MP.IsInMultiplayer)
		{
			return usedBy?.MapHeld != null;
		}
		return true;
	}
}
