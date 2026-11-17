using System;
using System.Collections.Generic;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Uveren.HemogenExtractor")]
public class HemogenExtractor
{
	[MpCompatRequireMod("Uveren.HemogenExtractor")]
	public record HemogenExtractorContext(CompRefuelable Obj) : ThingFilterContext
	{
		public CompRefuelable Obj { get; } = Obj;

		public override ThingFilter Filter => ((StorageSettings)getStoreSettingsMethod(Obj)).filter;

		public override ThingFilter ParentFilter => ((StorageSettings)getParentStoreSettingsMethod(Obj))?.filter;
	}

	private static FastInvokeHandler getStoreSettingsMethod;

	private static FastInvokeHandler getParentStoreSettingsMethod;

	private static Type nutritionRefuelableType;

	public HemogenExtractor(ModContentPack mod)
	{
		Extensions.SetDebugOnly((IEnumerable<ISyncMethod>)MpCompat.RegisterLambdaMethod("HemogenExtractor.CompSpawnerHemogen", "CompGetGizmosExtra", new int[2] { 0, 1 }));
		Type type = (nutritionRefuelableType = AccessTools.TypeByName("HemogenExtractor.CompNutritionRefuelable"));
		getStoreSettingsMethod = MethodInvoker.GetHandler(AccessTools.DeclaredMethod(type, "GetStoreSettings"));
		getParentStoreSettingsMethod = MethodInvoker.GetHandler(AccessTools.DeclaredMethod(type, "GetParentStoreSettings"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod("HemogenExtractor.ITab_CustomNutrition:FillTab"), new HarmonyMethod(typeof(HemogenExtractor), "PreFillTab"), null, null, new HarmonyMethod(typeof(HemogenExtractor), "PostFillTab"));
	}

	private static void PreFillTab()
	{
		if (MP.IsInMultiplayer && Find.Selector.SingleSelectedThing is ThingWithComps { comps: var comps } && comps.FirstOrDefault((ThingComp c) => c.GetType() == nutritionRefuelableType) is CompRefuelable obj)
		{
			MP.SetThingFilterContext(new HemogenExtractorContext(obj));
		}
	}

	private static void PostFillTab()
	{
		MP.SetThingFilterContext(null);
	}
}
