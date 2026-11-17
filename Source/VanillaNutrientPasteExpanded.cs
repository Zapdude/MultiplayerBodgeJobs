using System;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("VanillaExpanded.VNutrientE")]
public class VanillaNutrientPasteExpanded
{
	private static Action<ThingWithComps> dripperBaseRareTick;

	private static void InstallDripperLifecycle()
	{
		MethodInfo original = AccessTools.DeclaredMethod(AccessTools.TypeByName("VNPE.Building_Dripper"), "TickRare", Type.EmptyTypes);
		MethodInfo meth = AccessTools.DeclaredMethod(typeof(ThingWithComps), "TickRare", Type.EmptyTypes);
		DynamicMethod dynamicMethod = new DynamicMethod("MPCompat_DripperBaseRareTick", typeof(void), new Type[1] { typeof(ThingWithComps) }, typeof(VanillaNutrientPasteExpanded), skipVisibility: true);
		ILGenerator iLGenerator = dynamicMethod.GetILGenerator();
		iLGenerator.Emit(OpCodes.Ldarg_0);
		iLGenerator.Emit(OpCodes.Call, meth);
		iLGenerator.Emit(OpCodes.Ret);
		dripperBaseRareTick = (Action<ThingWithComps>)dynamicMethod.CreateDelegate(typeof(Action<ThingWithComps>));
		MpCompat.harmony.Patch(original, new HarmonyMethod(typeof(VanillaNutrientPasteExpanded), "DripperRareTickPrefix"));
	}

	private static bool DripperRareTickPrefix(ThingWithComps __instance)
	{
		if (!MP.IsInMultiplayer || __instance.Spawned)
		{
			return true;
		}
		dripperBaseRareTick(__instance);
		return false;
	}

	public VanillaNutrientPasteExpanded(ModContentPack mod)
	{
		InstallDripperLifecycle();
		LongEventHandler.ExecuteWhenFinished(LatePatch);
		MpCompat.RegisterLambdaDelegate("VNPE.Building_NutrientPasteDispenser_GetGizmos", "Postfix", 0, 1, 2);
	}

	private static void LatePatch()
	{
		MpCompat.RegisterLambdaMethod("VNPE.Building_NutrientPasteTap", "GetGizmos", 0, 1, 2);
		MpCompat.RegisterLambdaMethod("VNPE.CompRegisterIngredients", "CompGetGizmosExtra", default(int));
	}
}
