using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("dhultgren.smarterconstruction")]
internal class SmarterConstruction
{
	private static AccessTools.FieldRef<IDictionary> workGiverScannerCacheField;

	private static AccessTools.FieldRef<object> closedRegionDetectorCacheField;

	private static AccessTools.FieldRef<object, IDictionary> encloseThingsCacheField;

	private static FieldInfo positionsCacheField;

	private static FieldInfo lastPositionsCacheCleanupField;

	private static AccessTools.FieldRef<int> constructFinishFrameCurrentTickField;

	public SmarterConstruction(ModContentPack mod)
	{
		Type type = AccessTools.TypeByName("SmarterConstruction.Patches.Patch_WorkGiver_Scanner_GetPriority");
		AccessTools.Field(type, "random").SetValue(null, PatchingUtilities.RandRedirector.Instance);
		workGiverScannerCacheField = AccessTools.StaticFieldRefAccess<IDictionary>(AccessTools.DeclaredField(type, "cache"));
		closedRegionDetectorCacheField = AccessTools.StaticFieldRefAccess<object>(AccessTools.DeclaredField("SmarterConstruction.Core.ClosedRegionDetector:cache"));
		encloseThingsCacheField = AccessTools.FieldRefAccess<IDictionary>("SmarterConstruction.Core.EncloseThingsCache:cache");
		Type type2 = AccessTools.TypeByName("SmarterConstruction.Core.PawnPositionCache");
		positionsCacheField = AccessTools.DeclaredField(type2, "positionCache");
		lastPositionsCacheCleanupField = AccessTools.DeclaredField(type2, "lastCacheCleanup");
		constructFinishFrameCurrentTickField = AccessTools.StaticFieldRefAccess<int>(AccessTools.DeclaredField("SmarterConstruction.Patches.WorkGiver_ConstructFinishFrames_JobOnThing:currentTick"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(GameComponentUtility), "FinalizeInit"), null, new HarmonyMethod(typeof(SmarterConstruction), "ClearCache"));
	}

	private static void ClearCache()
	{
		workGiverScannerCacheField().Clear();
		encloseThingsCacheField(closedRegionDetectorCacheField()).Clear();
		((IDictionary)positionsCacheField.GetValue(null)).Clear();
		lastPositionsCacheCleanupField.SetValue(null, 0);
		constructFinishFrameCurrentTickField() = -1;
	}
}
