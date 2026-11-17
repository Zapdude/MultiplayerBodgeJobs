using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("VanillaExpanded.VanillaAspirationsExpanded")]
public class VanillaAspirationsExpanded
{
	private static bool isDrawingDialog;

	private static Type aspirationDefListType;

	private static FastInvokeHandler pickRandomTraitAndPassionsMethod;

	private static AccessTools.FieldRef<ChoiceLetter_GrowthMoment, IList> aspirationChoicesField;

	private static AccessTools.FieldRef<ChoiceLetter_GrowthMoment, int> aspirationGainsCountField;

	private static FastInvokeHandler trySetAspirationChoicesMethod;

	private static FastInvokeHandler makeAspirationChoicesMethod;

	public VanillaAspirationsExpanded(ModContentPack mod)
	{
		MpCompatPatchLoader.LoadPatch(this);
		Type type = AccessTools.TypeByName("VAspirE.AspirationDef");
		aspirationDefListType = typeof(List<>).MakeGenericType(type);
		type = AccessTools.TypeByName("Multiplayer.Client.SyncDelegates");
		MethodInfo methodInfo = AccessTools.DeclaredMethod(type, "PickRandomTraitAndPassions");
		pickRandomTraitAndPassionsMethod = HarmonyLib.MethodInvoker.GetHandler(methodInfo);
		type = AccessTools.TypeByName("VAspirE.ChoiceLetter_AspirationsFulfilled");
		MP.RegisterDefaultLetterChoice(methodInfo, type);
		type = AccessTools.TypeByName("VAspirE.ChoiceLetter_GrowthMoment_Aspirations");
		aspirationChoicesField = AccessTools.FieldRefAccess<IList>(type, "aspirationChoices");
		aspirationGainsCountField = AccessTools.FieldRefAccess<int>(type, "aspirationGainsCount");
		trySetAspirationChoicesMethod = HarmonyLib.MethodInvoker.GetHandler(AccessTools.DeclaredMethod(type, "TrySetAspirationChoices"));
		MethodInfo methodInfo2 = AccessTools.DeclaredMethod(type, "MakeAspirationChoices");
		makeAspirationChoicesMethod = HarmonyLib.MethodInvoker.GetHandler(methodInfo2);
		MP.RegisterSyncMethod(methodInfo2);
		MP.RegisterDefaultLetterChoice(MpMethodUtil.MethodOf(new Action<ChoiceLetter_GrowthMoment>(PickRandomAspirationsTraitPassions)), type);
	}

	private static void PickRandomAspirationsTraitPassions(ChoiceLetter_GrowthMoment letter)
	{
		trySetAspirationChoicesMethod(letter);
		IList list = aspirationChoicesField(letter);
		int num = aspirationGainsCountField(letter);
		if (num > 0 && list != null)
		{
			makeAspirationChoicesMethod(letter, Activator.CreateInstance(aspirationDefListType, list.Cast<Def>().InRandomOrder().Take(num)));
		}
		else
		{
			makeAspirationChoicesMethod(letter, null);
		}
		pickRandomTraitAndPassionsMethod(letter);
	}

	[MpCompatPrefix("VAspirE.ChoiceLetter_GrowthMoment_Aspirations", "TrySetAspirationChoices", MethodType.Normal)]
	private static void PreTrySetAspirationChoices(ChoiceLetter_GrowthMoment __instance)
	{
		Rand.PushState(Gen.HashCombineInt(__instance.pawn.thingIDNumber, __instance.arrivalTick));
	}

	[MpCompatPostfix("VAspirE.ChoiceLetter_GrowthMoment_Aspirations", "TrySetAspirationChoices", MethodType.Normal)]
	private static void PostTrySetAspirationChoices()
	{
		Rand.PopState();
	}

	[MpCompatPrefix("VAspirE.Dialog_GrowthMomentChoices_Aspirations", "DoWindowContents", MethodType.Normal)]
	private static void PreDoWindowContents()
	{
		isDrawingDialog = true;
	}

	[MpCompatFinalizer("VAspirE.Dialog_GrowthMomentChoices_Aspirations", "DoWindowContents", MethodType.Normal)]
	private static void PostDoWindowContents()
	{
		isDrawingDialog = false;
	}

	[MpCompatPrefix(typeof(LetterStack), "RemoveLetter", (Type[])null, MethodType.Normal)]
	private static bool DontRemoveChoiceLetter()
	{
		if (MP.IsInMultiplayer)
		{
			return !isDrawingDialog;
		}
		return true;
	}
}
