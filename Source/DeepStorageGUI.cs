using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("netrve.dsgui")]
[MpCompatFor("Mlie.NetrvesDeepStorageGUI")]
internal class DeepStorageGUI
{
	public DeepStorageGUI(ModContentPack mod)
	{
		MP.RegisterSyncMethod(AccessTools.TypeByName("DSGUI.DSGUI_TabItem"), "EjectTarget").CancelIfAnyArgNull();
	}
}
