using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Aoba.AMP")]
[MpCompatFor("Aoba.WalkerGears")]
public class MechFramework
{
	private static bool patchOnce;

	public MechFramework(ModContentPack mod)
	{
		if (!patchOnce)
		{
			patchOnce = true;
			LongEventHandler.ExecuteWhenFinished(LatePatch);
		}
	}

	private static void LatePatch()
	{
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod("WalkerGear.WalkerGear_Core:GetOut"));
	}
}
