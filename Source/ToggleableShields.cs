using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Owlchemist.ToggleableShields")]
internal class ToggleableShields
{
	public ToggleableShields(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
	}

	private static void LatePatch()
	{
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod("ToggleableShields.Patch_Gizmo_EnergyShieldStatus_GizmoOnGUI:ToggleShields"));
	}
}
