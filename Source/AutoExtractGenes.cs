using System;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Nibato.AutoExtractGenes")]
public class AutoExtractGenes
{
	private static Type autoExtractGenesCompType;

	private static ISyncField autoExtractGenesField;

	public AutoExtractGenes(ModContentPack mod)
	{
		autoExtractGenesCompType = AccessTools.TypeByName("AutoExtractGenes.AutoExtractGenesComp");
		autoExtractGenesField = MP.RegisterSyncField(autoExtractGenesCompType, "isEnabled");
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(HealthCardUtility), "DrawOverviewTab"), new HarmonyMethod(typeof(AutoExtractGenes), "PreFillTab"), null, null, new HarmonyMethod(typeof(AutoExtractGenes), "PostFillTab"));
	}

	private static void PreFillTab(Pawn pawn)
	{
		if (MP.IsInMultiplayer)
		{
			MP.WatchBegin();
			ThingComp thingComp = ((ThingWithComps)pawn).comps.SingleOrDefault((ThingComp comp) => comp.GetType() == autoExtractGenesCompType);
			if (thingComp != null)
			{
				autoExtractGenesField.Watch(thingComp);
			}
		}
	}

	private static void PostFillTab()
	{
		if (MP.IsInMultiplayer)
		{
			MP.WatchEnd();
		}
	}
}
