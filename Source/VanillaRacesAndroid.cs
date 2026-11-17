using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("vanillaracesexpanded.android")]
public class VanillaRacesAndroid
{
	private static Type androidCreationWindowType;

	private static AccessTools.FieldRef<GeneCreationDialogBase, Building> androidCreationWindowStationField;

	private static AccessTools.FieldRef<GeneCreationDialogBase, Pawn> androidCreationWindowCreatorField;

	private static Type androidModificationWindowType;

	private static AccessTools.FieldRef<GeneCreationDialogBase, Building_Enterable> androidModificationWindowStationField;

	private static AccessTools.FieldRef<GeneCreationDialogBase, Pawn> androidModificationWindowAndroidField;

	private static FastInvokeHandler cancelModificationMethod;

	private static AccessTools.FieldRef<Building_Enterable, bool> initModificationField;

	private static AccessTools.FieldRef<object, Building_Enterable> innerClassGizmoBuildingField;

	private static AccessTools.FieldRef<object, Building_Enterable> innerClassFloatMenuBuildingField;

	private static FastInvokeHandler acceptInnerMethod;

	private static AccessTools.FieldRef<GeneCreationDialogBase, List<ThingDefCount>> androidWindowRequiredItemsField;

	private static AccessTools.FieldRef<IDictionary> canInitiateRandomInteractionCacheField;

	private static AccessTools.FieldRef<IDictionary> canDoRandomMentalBreakCacheField;

	private static FastInvokeHandler trySetChoicesMethod;

	private static FastInvokeHandler makeChoicesMethod;

	private static AccessTools.FieldRef<ChoiceLetter, int> passionChoiceCountField;

	private static AccessTools.FieldRef<ChoiceLetter, int> passionGainsCountField;

	private static AccessTools.FieldRef<ChoiceLetter, int> traitChoiceCountField;

	private static AccessTools.FieldRef<ChoiceLetter, List<SkillDef>> passionChoicesField;

	private static AccessTools.FieldRef<ChoiceLetter, List<Trait>> traitChoicesField;

	private static FastInvokeHandler recipeForAndroidMethod;

	private static ISyncField syncSelfTend;

	private static ISyncField syncAutoRepair;

	private static bool isDrawingAwakeningDialog;

	public VanillaRacesAndroid(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
		VanillaRacesAndroidRuntime.Install();
		Type type = AccessTools.TypeByName("VREAndroids.Building_AndroidCreationStation");
		MpCompat.RegisterLambdaMethod(type, "GetGizmos", default(int));
		MP.RegisterSyncMethod(type, "FinishAndroidProject").SetDebugOnly();
		type = (androidCreationWindowType = AccessTools.TypeByName("VREAndroids.Window_AndroidCreation"));
		androidCreationWindowStationField = AccessTools.FieldRefAccess<Building>(type, "station");
		androidCreationWindowCreatorField = AccessTools.FieldRefAccess<Pawn>(type, "creator");
		MP.RegisterSyncWorker<GeneCreationDialogBase>(SyncAndroidCreationWindow, type);
		MP.RegisterSyncMethod(type, "AcceptInner").SetPreInvoke(PreAcceptInnerCreation);
		type = (androidModificationWindowType = AccessTools.TypeByName("VREAndroids.Window_AndroidModification"));
		androidModificationWindowStationField = AccessTools.FieldRefAccess<Building_Enterable>(type, "station");
		androidModificationWindowAndroidField = AccessTools.FieldRefAccess<Pawn>(type, "android");
		MP.RegisterSyncWorker<GeneCreationDialogBase>(SyncAndroidModificationWindow, type);
		MP.RegisterSyncMethod(type, "AcceptInner").SetPreInvoke(PreAcceptInnerModification).SetPostInvoke(PostAcceptInnerModification);
		type = AccessTools.TypeByName("VREAndroids.Window_CreateAndroidBase");
		androidWindowRequiredItemsField = AccessTools.FieldRefAccess<List<ThingDefCount>>(type, "requiredItems");
		acceptInnerMethod = HarmonyLib.MethodInvoker.GetHandler(AccessTools.DeclaredMethod(type, "AcceptInner"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, "Accept"), new HarmonyMethod(typeof(VanillaRacesAndroid), "PreAccept"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod("VREAndroids.Window_CreateAndroidBase:DoWindowContents"), new HarmonyMethod(typeof(VanillaRacesAndroid), "CloseDialogIfStationDestroyed"));
		MpCompat.RegisterLambdaMethod("VREAndroids.Building_AndroidSleepMode", "GetGizmos", default(int));
		MpCompat.RegisterLambdaMethod("VREAndroids.Gene_SleepMode", "GetGizmos", default(int));
		Type parentType = AccessTools.TypeByName("VREAndroids.Gene_SelfDestructProtocols");
		MpCompat.RegisterLambdaMethod(parentType, "GetGizmos", default(int));
		MpCompat.RegisterLambdaDelegate(parentType, "GetGizmos", 1);
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod("VREAndroids.UnfinishedAndroid:CancelProject"));
		MpCompat.RegisterLambdaDelegate("VREAndroids.Building_Bed_GetFloatMenuOptions_Patch", "GetFloatMenuOptions", default(int));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod("VREAndroids.HealthCardUtility_CreateSurgeryBill_Patch:Postfix"), new HarmonyMethod(typeof(VanillaRacesAndroid), "CancelOperationModificationIfResultNull"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod("VREAndroids.Building_NeutroCasket:GetInspectString"), new HarmonyMethod(typeof(VanillaRacesAndroid), "NeutrocasketGetInspectStringPrefix"), null, new HarmonyMethod(typeof(VanillaRacesAndroid), "ReplaceSetMedicalCall"), new HarmonyMethod(typeof(VanillaRacesAndroid), "NeutrocasketGetInspectStringFinalizer"));
		syncSelfTend = MP.RegisterSyncField(typeof(Pawn_PlayerSettings), "selfTend");
		syncAutoRepair = MP.RegisterSyncField(AccessTools.DeclaredField("VREAndroids.Gene_SyntheticBody:autoRepair"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod("VREAndroids.HealthCardUtility_DrawOverviewTab_Patch:DrawOverviewTabAndroid"), new HarmonyMethod(typeof(VanillaRacesAndroid), "PreDrawOverviewTabAndroid"), new HarmonyMethod(typeof(VanillaRacesAndroid), "WatchEndPostfix"));
	}

	private static void LatePatch()
	{
		Type type = AccessTools.TypeByName("VREAndroids.Building_AndroidBehavioristStation");
		cancelModificationMethod = HarmonyLib.MethodInvoker.GetHandler(AccessTools.DeclaredMethod(type, "CancelModification"));
		initModificationField = AccessTools.FieldRefAccess<bool>(type, "initModification");
		MpCompat.RegisterLambdaMethod(type, "GetGizmos", 2);
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, "TryAcceptPawn"), new HarmonyMethod(typeof(VanillaRacesAndroid), "PreTryAcceptPawn"), null, new HarmonyMethod(typeof(VanillaRacesAndroid), "ReplaceAddWindow"));
		MethodInfo lambda = MpMethodUtil.GetLambda(type, "GetGizmos", MethodType.Normal, null, 1);
		innerClassGizmoBuildingField = AccessTools.FieldRefAccess<Building_Enterable>(lambda.DeclaringType, "<>4__this");
		MpCompat.harmony.Patch(lambda, new HarmonyMethod(typeof(VanillaRacesAndroid), "PreSelectPawnGizmo"));
		lambda = MpMethodUtil.GetLambda(type, "GetFloatMenuOptions");
		innerClassFloatMenuBuildingField = AccessTools.FieldRefAccess<Building_Enterable>(lambda.DeclaringType, "<>4__this");
		MpCompat.harmony.Patch(lambda, new HarmonyMethod(typeof(VanillaRacesAndroid), "PreSelectPawnFloatMenu"));
		Type parentType = AccessTools.TypeByName("VREAndroids.Building_SubcorePolyanalyzer");
		MpCompat.RegisterLambdaMethod(parentType, "GetGizmos", 2, 5, 6, 0).TakeLast(2).SetDebugOnly();
		MpCompat.RegisterLambdaDelegate(parentType, "GetGizmos", 4);
		MpCompat.RegisterLambdaDelegate(parentType, "GetFloatMenuOptions", default(int));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(GameComponentUtility), "FinalizeInit"), null, new HarmonyMethod(typeof(VanillaRacesAndroid), "ClearCache"));
		canInitiateRandomInteractionCacheField = AccessTools.StaticFieldRefAccess<IDictionary>(AccessTools.DeclaredField("VREAndroids.SocialInteractionUtility_CanInitiateRandomInteraction_Patch:cachedResults"));
		canDoRandomMentalBreakCacheField = AccessTools.StaticFieldRefAccess<IDictionary>(AccessTools.DeclaredField("VREAndroids.MentalBreaker_CanDoRandomMentalBreaks_Patch:cachedResults"));
		Type type2 = AccessTools.TypeByName("VREAndroids.ChoiceLetter_AndroidAwakened");
		MethodInfo methodInfo = AccessTools.DeclaredMethod(type2, "MakeChoices");
		MP.RegisterSyncMethod(methodInfo).ExposeParameter(1);
		makeChoicesMethod = HarmonyLib.MethodInvoker.GetHandler(methodInfo);
		methodInfo = AccessTools.DeclaredMethod(type2, "TrySetChoices");
		trySetChoicesMethod = HarmonyLib.MethodInvoker.GetHandler(methodInfo);
		MpCompat.harmony.Patch(methodInfo, new HarmonyMethod(typeof(VanillaRacesAndroid), "PreSetChoices"), new HarmonyMethod(typeof(VanillaRacesAndroid), "PostSetChoices"));
		passionChoiceCountField = AccessTools.FieldRefAccess<int>(type2, "passionChoiceCount");
		passionGainsCountField = AccessTools.FieldRefAccess<int>(type2, "passionGainsCount");
		traitChoiceCountField = AccessTools.FieldRefAccess<int>(type2, "traitChoiceCount");
		passionChoicesField = AccessTools.FieldRefAccess<List<SkillDef>>(type2, "passionChoices");
		traitChoicesField = AccessTools.FieldRefAccess<List<Trait>>(type2, "traitChoices");
		MP.RegisterDefaultLetterChoice(AccessTools.DeclaredMethod(typeof(VanillaRacesAndroid), "DefaultDialogSelection"), type2);
		type2 = AccessTools.TypeByName("VREAndroids.Dialog_AndroidAwakenedChoices");
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type2, "DoWindowContents"), new HarmonyMethod(typeof(VanillaRacesAndroid), "PreAwakeningDraw"), new HarmonyMethod(typeof(VanillaRacesAndroid), "PostAwakeningDraw"));
		type2 = typeof(LetterStack);
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type2, "RemoveLetter"), new HarmonyMethod(typeof(VanillaRacesAndroid), "PreRemoveLetter"));
		MethodInfo methodInfo2 = AccessTools.DeclaredMethod("DubsMintMenus.Patch_HealthCardUtility:GenerateListing");
		if (methodInfo2 != null)
		{
			recipeForAndroidMethod = HarmonyLib.MethodInvoker.GetHandler(AccessTools.DeclaredMethod("VREAndroids.Utils:RecipeForAndroid"));
			MP.RegisterSyncMethod(typeof(VanillaRacesAndroid), "SyncedAddBill");
			MpCompat.harmony.Patch(methodInfo2, null, null, new HarmonyMethod(typeof(VanillaRacesAndroid), "ReplaceAddBillCall"));
		}
	}

	private static bool CloseDialogIfStationDestroyed(GeneCreationDialogBase __instance)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (androidCreationWindowType.IsInstanceOfType(__instance))
		{
			Building building = androidCreationWindowStationField(__instance);
			if (building == null || building.Destroyed || !building.Spawned)
			{
				__instance.Close();
				return false;
			}
		}
		else if (androidModificationWindowType.IsInstanceOfType(__instance))
		{
			Building_Enterable building_Enterable = androidModificationWindowStationField(__instance);
			if (building_Enterable == null || building_Enterable.Destroyed || !building_Enterable.Spawned)
			{
				__instance.Close();
				return false;
			}
			Pawn pawn = androidModificationWindowAndroidField(__instance);
			if (pawn == null || pawn.Dead || !pawn.Spawned || building_Enterable.Map != pawn.Map || !building_Enterable.CanAcceptPawn(pawn))
			{
				__instance.Close();
				return false;
			}
		}
		return true;
	}

	private static bool PreAccept(GeneCreationDialogBase __instance)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (!androidModificationWindowType.IsInstanceOfType(__instance))
		{
			return true;
		}
		acceptInnerMethod(__instance);
		Find.WindowStack.TryRemove(__instance);
		return false;
	}

	private static void PreAcceptInnerCreation(object instance, object[] args)
	{
		ref Pawn reference = ref androidCreationWindowCreatorField((GeneCreationDialogBase)instance);
		if (reference != null && (reference.Dead || !reference.Spawned))
		{
			reference = null;
		}
	}

	private static void PreAcceptInnerModification(object instance, object[] args)
	{
		GeneCreationDialogBase instance2 = (GeneCreationDialogBase)instance;
		Building_Enterable building_Enterable = androidModificationWindowStationField(instance2);
		if (building_Enterable != null && !building_Enterable.Destroyed && building_Enterable.Spawned)
		{
			cancelModificationMethod(building_Enterable);
			Pawn pawn = androidModificationWindowAndroidField(instance2);
			if (pawn != null && !pawn.Dead && pawn.Spawned && pawn.Map == building_Enterable.Map && (bool)building_Enterable.CanAcceptPawn(pawn))
			{
				building_Enterable.SelectPawn(pawn);
			}
		}
	}

	private static void PostAcceptInnerModification(object instance, object[] args)
	{
		Building_Enterable building_Enterable = androidModificationWindowStationField((GeneCreationDialogBase)instance);
		if (building_Enterable != null && building_Enterable.selectedPawn == null)
		{
			cancelModificationMethod(building_Enterable);
		}
	}

	private static bool PreTryAcceptPawn(bool ___initModification, ThingOwner ___innerContainer)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (___innerContainer.Any)
		{
			return false;
		}
		if (!___initModification)
		{
			return false;
		}
		return true;
	}

	private static void AddWindowReplacement(WindowStack windowStack, Window window)
	{
		if (!MP.IsInMultiplayer)
		{
			windowStack.Add(window);
		}
	}

	private static IEnumerable<CodeInstruction> ReplaceAddWindow(IEnumerable<CodeInstruction> instr)
	{
		return instr.MethodReplacer(AccessTools.DeclaredMethod(typeof(WindowStack), "Add"), AccessTools.DeclaredMethod(typeof(VanillaRacesAndroid), "AddWindowReplacement"));
	}

	private static bool PreSelectPawnGizmo(Pawn ___pawn, object __instance)
	{
		return DisplayAndroidModificationDialogIfNeeded(___pawn, innerClassGizmoBuildingField(__instance));
	}

	private static bool PreSelectPawnFloatMenu(Pawn ___selPawn, object __instance)
	{
		return DisplayAndroidModificationDialogIfNeeded(___selPawn, innerClassFloatMenuBuildingField(__instance));
	}

	private static bool DisplayAndroidModificationDialogIfNeeded(Pawn pawn, Building_Enterable building)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (initModificationField(building))
		{
			return building.SelectedPawn == pawn;
		}
		Find.WindowStack.Add((Window)Activator.CreateInstance(androidModificationWindowType, building, pawn, null));
		return false;
	}

	private static void PreSetChoices(Thing ___pawn, Letter __instance)
	{
		Rand.PushState(Gen.HashCombineInt(___pawn.thingIDNumber, __instance.arrivalTick));
	}

	private static void PostSetChoices()
	{
		Rand.PopState();
	}

	private static void DefaultDialogSelection(ChoiceLetter letter)
	{
		trySetChoicesMethod(letter);
		List<SkillDef> list = null;
		Trait trait = null;
		List<SkillDef> list2 = passionChoicesField(letter);
		if (list2 != null && passionChoiceCountField(letter) > 0)
		{
			list = list2.InRandomOrder().Take(passionGainsCountField(letter)).ToList();
		}
		List<Trait> list3 = traitChoicesField(letter);
		if (list3 != null && traitChoiceCountField(letter) > 0)
		{
			trait = list3.RandomElement();
		}
		makeChoicesMethod(letter, list, trait);
		Find.LetterStack.RemoveLetter(letter);
	}

	private static bool PreRemoveLetter()
	{
		if (MP.IsInMultiplayer)
		{
			return !isDrawingAwakeningDialog;
		}
		return true;
	}

	private static void PreAwakeningDraw()
	{
		isDrawingAwakeningDialog = true;
	}

	private static void PostAwakeningDraw()
	{
		isDrawingAwakeningDialog = false;
	}

	private static bool CancelOperationModificationIfResultNull(Bill_Medical __0)
	{
		return __0 != null;
	}

	private static void ClearCache()
	{
		canInitiateRandomInteractionCacheField().Clear();
		canDoRandomMentalBreakCacheField().Clear();
	}

	private static void ReplacedSetMedicalCall(Building_Bed instance, bool value)
	{
		instance.medicalInt = value;
	}

	private static void NeutrocasketGetInspectStringPrefix(Building_Bed __instance, out bool __state)
	{
		__state = __instance.medicalInt;
	}

	private static void NeutrocasketGetInspectStringFinalizer(Building_Bed __instance, bool __state)
	{
		__instance.medicalInt = __state;
	}

	private static IEnumerable<CodeInstruction> ReplaceSetMedicalCall(IEnumerable<CodeInstruction> instr, MethodBase baseMethod)
	{
		MethodInfo target = AccessTools.DeclaredPropertySetter(typeof(Building_Bed), "Medical");
		MethodInfo replacement = AccessTools.DeclaredMethod(typeof(VanillaRacesAndroid), "ReplacedSetMedicalCall");
		int replacedCount = 0;
		foreach (CodeInstruction item in instr)
		{
			if ((item.opcode == OpCodes.Call || item.opcode == OpCodes.Callvirt) && item.operand is MethodInfo methodInfo && methodInfo == target)
			{
				item.opcode = OpCodes.Call;
				item.operand = replacement;
				replacedCount++;
			}
			yield return item;
		}
		if (replacedCount != 2)
		{
			string value = ((baseMethod.DeclaringType?.Namespace).NullOrEmpty() ? baseMethod.Name : (baseMethod.DeclaringType.Name + ":" + baseMethod.Name));
			Log.Warning($"Patched incorrect number of Building_Bed.Medical calls (patched {replacedCount}, expected {2}) for method {value}");
		}
	}

	private static void PreDrawOverviewTabAndroid(Pawn pawn, Gene gene)
	{
		if (MP.IsInMultiplayer)
		{
			MP.WatchBegin();
			if (pawn.playerSettings != null)
			{
				syncSelfTend.Watch(pawn.playerSettings);
			}
			syncAutoRepair.Watch(gene);
		}
	}

	private static void WatchEndPostfix()
	{
		if (MP.IsInMultiplayer)
		{
			MP.WatchEnd();
		}
	}

	private static void SyncAndroidCreationWindow(SyncWorker sync, ref GeneCreationDialogBase window)
	{
		if (sync.isWriting)
		{
			sync.Write(androidCreationWindowStationField(window));
			sync.Write(androidCreationWindowCreatorField(window));
			SyncAndroidCreationBase(sync, ref window);
			return;
		}
		Building building = sync.Read<Building>();
		Pawn pawn = sync.Read<Pawn>();
		window = (GeneCreationDialogBase)Activator.CreateInstance(androidCreationWindowType, building, pawn, null);
		SyncAndroidCreationBase(sync, ref window);
	}

	private static void SyncAndroidModificationWindow(SyncWorker sync, ref GeneCreationDialogBase window)
	{
		if (sync.isWriting)
		{
			sync.Write(androidModificationWindowStationField(window));
			sync.Write(androidModificationWindowAndroidField(window));
			SyncAndroidCreationBase(sync, ref window);
			return;
		}
		Building building = sync.Read<Building>();
		Pawn pawn = sync.Read<Pawn>();
		window = (GeneCreationDialogBase)Activator.CreateInstance(androidModificationWindowType, building, pawn, null);
		SyncAndroidCreationBase(sync, ref window);
	}

	private static void SyncAndroidCreationBase(SyncWorker sync, ref GeneCreationDialogBase window)
	{
		if (sync.isWriting)
		{
			sync.Write(window.xenotypeName);
			sync.Write(window.iconDef);
			sync.Write(window.SelectedGenes);
			sync.Write(androidWindowRequiredItemsField(window));
		}
		else
		{
			window.xenotypeName = sync.Read<string>();
			window.iconDef = sync.Read<XenotypeIconDef>();
			window.SelectedGenes.Clear();
			window.SelectedGenes.AddRange(sync.Read<List<GeneDef>>());
			androidWindowRequiredItemsField(window) = sync.Read<List<ThingDefCount>>();
		}
	}

	private static void SyncedAddBill(BillStack billStack, RecipeDef recipe, BodyPartRecord part, bool isAndroid)
	{
		if (isAndroid && recipe.workSkill != SkillDefOf.Crafting)
		{
			recipe = (RecipeDef)recipeForAndroidMethod(null, recipe);
		}
		Bill_Medical bill = new Bill_Medical(recipe, new List<Thing>())
		{
			Part = part
		};
		billStack.AddBill(bill);
	}

	private static void ReplacedAddBillCall(BillStack billStack, Bill_Medical bill)
	{
		if (MP.IsInMultiplayer)
		{
			SyncedAddBill(billStack, bill.recipe, bill.part, bill.recipe.workSkill == SkillDefOf.Crafting);
		}
		else
		{
			billStack.AddBill(bill);
		}
	}

	private static IEnumerable<CodeInstruction> ReplaceAddBillCall(IEnumerable<CodeInstruction> instr, MethodBase baseMethod)
	{
		MethodInfo target = AccessTools.DeclaredMethod(typeof(BillStack), "AddBill");
		MethodInfo replacement = AccessTools.DeclaredMethod(typeof(VanillaRacesAndroid), "ReplacedAddBillCall");
		int replacedCount = 0;
		foreach (CodeInstruction item in instr)
		{
			if ((item.opcode == OpCodes.Call || item.opcode == OpCodes.Callvirt) && item.operand is MethodInfo methodInfo && methodInfo == target)
			{
				item.opcode = OpCodes.Call;
				item.operand = replacement;
				replacedCount++;
			}
			yield return item;
		}
		if (replacedCount != 1)
		{
			string value = ((baseMethod.DeclaringType?.Namespace).NullOrEmpty() ? baseMethod.Name : (baseMethod.DeclaringType.Name + ":" + baseMethod.Name));
			Log.Warning($"Patched incorrect number of AddBill calls (patched {replacedCount}, expected {1}) for method {value}");
		}
	}
}
