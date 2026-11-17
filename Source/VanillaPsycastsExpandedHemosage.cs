using System;
using System.Collections;
using HarmonyLib;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("VanillaExpanded.VPE.Hemosage")]
public class VanillaPsycastsExpandedHemosage
{
	private static AccessTools.FieldRef<IDictionary> unroofedCellsField;

	public VanillaPsycastsExpandedHemosage(ModContentPack mod)
	{
		PatchingUtilities.PatchPushPopRand("VPEHemosage.Hediff_Bloodmist:ThrowFleck");
		MpCompat.RegisterLambdaDelegate("VPEHemosage.Ability_Bloodfocus", "GetGizmo", default(int));
		unroofedCellsField = AccessTools.StaticFieldRefAccess<IDictionary>(AccessTools.DeclaredField("VPEHemosage.WeatherOverlay_Bloodstorm:unroofedCells"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(GameComponentUtility), "FinalizeInit"), null, new HarmonyMethod(typeof(VanillaPsycastsExpandedHemosage), "ClearCache"));
		Type type = AccessTools.TypeByName("VPEHemosage.Hediff_Bloodmist");
		AccessTools.FieldRef<object, int> vomit = AccessTools.FieldRefAccess<int>(type, "nextVomitTick");
		AccessTools.FieldRef<object, int> fleck = AccessTools.FieldRefAccess<int>(type, "nextFleckSpawnTick");
		PatchingUtilities.RegisterTimestampFields(type, (object hediff, int offset) =>
		{
			vomit(hediff) = vomit(hediff) + offset;
			fleck(hediff) = fleck(hediff) + offset;
		});
	}

	private static void ClearCache()
	{
		unroofedCellsField().Clear();
	}
}
