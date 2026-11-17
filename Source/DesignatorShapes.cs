using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Merthsoft.DesignatorShapes")]
public class DesignatorShapes
{
	private static FastInvokeHandler settingsGetter = (object _, object[] _) => (object)null;

	private static AccessTools.FieldRef<ModSettings, int> floodFillCellLimitField = null;

	public DesignatorShapes(ModContentPack mod)
	{
		MpCompatPatchLoader.LoadPatch(this);
		LongEventHandler.ExecuteWhenFinished(LatePatch);
		Type type = AccessTools.TypeByName("Merthsoft.DesignatorShapes.HistoryManager");
		MP.RegisterSyncMethod(type, "Redo");
		MP.RegisterSyncMethod(type, "Undo");
	}

	private static void LatePatch()
	{
		Type type = AccessTools.TypeByName("Merthsoft.DesignatorShapes.Defs.DesignatorShapeDef");
		FieldInfo fieldInfo = AccessTools.DeclaredField(type, "pauseOnSelection");
		if (AccessTools.DeclaredPropertyGetter(typeof(DefDatabase<>).MakeGenericType(type), "AllDefs").Invoke(null, Array.Empty<object>()) is IEnumerable enumerable)
		{
			foreach (object item in enumerable)
			{
				fieldInfo.SetValue(item, false);
			}
		}
		settingsGetter = HarmonyLib.MethodInvoker.GetHandler(AccessTools.DeclaredPropertyGetter("Merthsoft.DesignatorShapes.DesignatorShapes:Settings"));
		floodFillCellLimitField = AccessTools.FieldRefAccess<int>("Merthsoft.DesignatorShapes.DesignatorSettings:FloodFillCellLimit");
	}

	[MpCompatPrefix("Merthsoft.DesignatorShapes.Shapes.FloodFill", "Fill", MethodType.Normal)]
	private static void PreFloodFill(ref int? __state)
	{
		if (MP.IsInMultiplayer && floodFillCellLimitField != null && settingsGetter(null) is ModSettings instance)
		{
			ref int reference = ref floodFillCellLimitField(instance);
			if (reference > 10000)
			{
				__state = reference;
				reference = 10000;
			}
		}
	}

	[MpCompatFinalizer("Merthsoft.DesignatorShapes.Shapes.FloodFill", "Fill", MethodType.Normal)]
	private static void PostFloodFill(int? __state)
	{
		if (__state.HasValue)
		{
			floodFillCellLimitField(settingsGetter(null) as ModSettings) = __state.Value;
		}
	}
}
