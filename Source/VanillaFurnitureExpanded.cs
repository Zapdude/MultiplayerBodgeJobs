using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("VanillaExpanded.VFECore")]
internal class VanillaFurnitureExpanded
{
	[MpCompatSyncField("VanillaFurnitureEC.CompBinClean", "cleanupTarget")]
	private static ISyncField cleanupTargetField;

	public VanillaFurnitureExpanded(ModContentPack mod)
	{
		MpCompatPatchLoader.LoadPatch(this);
		PatchingUtilities.PatchPushPopRand("VanillaFurnitureEC.JobDriver_PlayDarts:ThrowDart");
	}

	[MpCompatPrefix("VanillaFurnitureEC.Command_CleanupTarget", "GizmoOnGUI", MethodType.Normal)]
	private static void PreGizmoGui(ThingComp ___comp, out bool __state)
	{
		if (!MP.IsInMultiplayer || ___comp == null)
		{
			__state = false;
			return;
		}
		__state = true;
		MP.WatchBegin();
		cleanupTargetField.Watch(___comp);
	}

	[MpCompatPostfix("VanillaFurnitureEC.Command_CleanupTarget", "GizmoOnGUI", MethodType.Normal)]
	private static void PostGizmoGui(bool __state)
	{
		if (__state)
		{
			MP.WatchEnd();
		}
	}
}
