using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Multiplayer.Compat;

[MpCompatFor("OskarPotocki.VanillaFactionsExpanded.Core")]
internal class VanillaExpandedFramework
{
	private sealed class ShieldClockState
	{
		public bool[] valid = new bool[3];
	}

	private sealed class ShieldCommandOwner
	{
		public object comp;
	}

	private delegate object GetManager(Pawn p, bool createIfMissing);

	private static AccessTools.FieldRef<object, int> projectileFrame;

	private static AccessTools.FieldRef<object, int> projectileFadeFrame;

	private static AccessTools.FieldRef<object, int> projectilePreviousTick;

	private static FastInvokeHandler projectileMaterial;

	[ThreadStatic]
	private static bool advancingProjectileAnimation;

	private static Type advancedResourceProcessorType;

	private static AccessTools.FieldRef<ThingComp, object> advancedResourceProcessorProcessStackField;

	private static AccessTools.FieldRef<object, ThingWithComps> processParentField;

	private static AccessTools.FieldRef<object, string> processIdField;

	private static AccessTools.FieldRef<object, Def> processDefField;

	private static AccessTools.FieldRef<object, int> processTargetCountField;

	private static AccessTools.FieldRef<object, float> processProgressField;

	private static AccessTools.FieldRef<object, bool> processSuspendedField;

	private static FastInvokeHandler processStackAddProcessMethod;

	private static FastInvokeHandler processStackNotifyProcessChangeMethod;

	private static AccessTools.FieldRef<object, IList> processStackProcessesField;

	private static AccessTools.FieldRef<IDictionary> processUtilityClipboardField;

	private static AccessTools.FieldRef<object, ThingComp> processorField;

	private static ISyncField overclockMultiplier;

	private static Type compAbilitiesType;

	private static AccessTools.FieldRef<object, IEnumerable> learnedAbilitiesField;

	private static Type compAbilitiesApparelType;

	private static AccessTools.FieldRef<object, IEnumerable> givenAbilitiesField;

	private static FastInvokeHandler abilityApparelPawnGetter;

	private static FastInvokeHandler abilityInitMethod;

	private static AccessTools.FieldRef<object, Thing> abilityHolderField;

	private static AccessTools.FieldRef<object, Pawn> abilityPawnField;

	private static AccessTools.FieldRef<IExposable, int> abilityCooldownField;

	private static ISyncField abilityAutoCastField;

	private static readonly ConditionalWeakTable<object, ShieldClockState> shieldClocks = new ConditionalWeakTable<object, ShieldClockState>();

	private static AccessTools.FieldRef<object, int>[] shieldTimes;

	private static FieldInfo[] shieldTimeFields;

	private static readonly Dictionary<MethodBase, (int guards, int writes)> shieldClockContracts = new Dictionary<MethodBase, (int, int)>();

	private static readonly ConditionalWeakTable<object, ShieldCommandOwner> shieldCommandOwners = new ConditionalWeakTable<object, ShieldCommandOwner>();

	private static readonly Dictionary<Type, AccessTools.FieldRef<object, object>> shieldIteratorOwners = new Dictionary<Type, AccessTools.FieldRef<object, object>>();

	private static ConstructorInfo shieldCommandConstructor;

	private static AccessTools.FieldRef<object, int> shieldCommandLastUsed;

	private static FastInvokeHandler shieldHostThing;

	private static Func<int> worldTicksAbs;

	private static AccessTools.FieldRef<object, IDictionary> hireDeadCount;

	private static AccessTools.FieldRef<object, object> hirePenaltyCount;

	private static AccessTools.FieldRef<object, object> hirePenaltyExpiry;

	private static Type hireDialogType;

	private static AccessTools.FieldRef<object, Dictionary<PawnKindDef, Pair<int, string>>> hireDataField;

	private static ISyncField daysAmountField;

	private static ISyncField currentFactionDefField;

	private static Type contractInfoDialogType;

	private static AccessTools.FieldRef<IList> hireablesList;

	private static Type randomBuildingGraphicCompType;

	private static FastInvokeHandler randomBuildingGraphicCompChangeGraphicMethod;

	private static Type dummyGlowerType;

	private static AccessTools.FieldRef<ThingComp, CompGlower> compGlowerExtendedGlowerField;

	private static AccessTools.FieldRef<ThingWithComps, ThingComp> dummyGlowerParentCompField;

	private static IEnumerable<string> mvcfEnabledFeaturesSet;

	private static FastInvokeHandler mvcfVerbManagerPawnGetter;

	private static FastInvokeHandler mvcfVerbManagerTickMethod;

	private static AccessTools.FieldRef<object, IList> mvcfVerbManagerVerbsField;

	private static GetManager mvcfPawnVerbUtilityGetManager;

	private static FastInvokeHandler mvcfManagedVerbManagerGetter;

	private static AccessTools.FieldRef<object, IList> mvcfVerbWithCompsField;

	private static AccessTools.FieldRef<object, object> mvcfVerbCompParentField;

	private static AccessTools.FieldRef<WorldComponent> mvcfWorldCompInstanceField;

	private static AccessTools.FieldRef<WorldComponent, IList> mvcfWorldCompTickManagersField;

	private static FastInvokeHandler mvcfWeakReferenceTryGetVerbManagerMethod;

	private static Type deconstructPipeDesignatorType;

	private static AccessTools.FieldRef<Designator_Deconstruct, Def> deconstructPipeDesignatorNetDefField;

	private static Type pipeNetManagerType;

	private static AccessTools.FieldRef<MapComponent, IList> pipeNetManagerPipeNetsListField;

	private static AccessTools.FieldRef<object, Map> pipeNetMapField;

	private static AccessTools.FieldRef<object, Def> pipeNetDefField;

	private static FastInvokeHandler pipeResourceInspectNet;

	private static Type newFactionSpawningDialogType;

	private static AccessTools.FieldRef<object, FactionDef> factionDefField;

	private static Type renameDoorTeleporterDialogType;

	private static ConstructorInfo renameDoorTeleporterDialogConstructor;

	private static AccessTools.FieldRef<object, ThingWithComps> renameDoorTeleporterDialogThingField;

	private static Type terrainCompType;

	private static AccessTools.FieldRef<object, object> terrainCompParentField;

	private static AccessTools.FieldRef<object, IntVec3> terrainInstancePositionField;

	private static FastInvokeHandler getSpecialTerrain;

	private static FastInvokeHandler updateSpecialTerrain;

	private static Type weatherOverlayEffectsType;

	private static AccessTools.FieldRef<SkyOverlay, int> weatherOverlayEffectsNextDamageTickField;

	private static AccessTools.FieldRef<SkyOverlay, Dictionary<Map, int>> weatherOverlayEffectsNextDamageTickForMapField;

	private static Type thoughtHediffType;

	private static Type thingDefExtensionType;

	private static AccessTools.FieldRef<DefModExtension, object> thingDefExtensionConstructionSkillRequirementField;

	private static AccessTools.FieldRef<object, WorkTypeDef> constructionSkillRequirementWorkTypeField;

	private static IConstructible lastThing;

	private static Type graphicCustomizationDialogType;

	internal static AccessTools.FieldRef<Window, ThingComp> graphicCustomizationCompField;

	private static AccessTools.FieldRef<Window, ThingComp> graphicCustomizationGeneratedNamesCompField;

	private static AccessTools.FieldRef<Window, Pawn> graphicCustomizationPawnField;

	private static AccessTools.FieldRef<Window, IList> graphicCustomizationCurrentVariantsField;

	private static AccessTools.FieldRef<Window, string> graphicCustomizationCurrentNameField;

	private static SyncType variantsListType;

	private static AccessTools.FieldRef<object, string> textureVariantTexNameField;

	private static AccessTools.FieldRef<object, string> textureVariantTextureField;

	private static AccessTools.FieldRef<object, string> textureVariantOutlineField;

	private static AccessTools.FieldRef<object, object> textureVariantTextureVariantOverrideField;

	private static AccessTools.FieldRef<object, float> textureVariantChanceOverrideField;

	private static SyncType textureVariantOverrideType;

	private static AccessTools.FieldRef<object, float> textureVariantOverrideChanceField;

	private static AccessTools.FieldRef<object, string> textureVariantOverrideGroupNameField;

	private static AccessTools.FieldRef<object, string> textureVariantOverrideTexNameField;

	private static FastInvokeHandler getDraftedActionDataMethod;

	private static FastInvokeHandler draftedActionDataGetPawnMethod;

	private static AccessTools.FieldRef<GameComponent> questChainsGameCompInstanceField;

	private static AccessTools.FieldRef<GameComponent, IList> questChainsGameCompQuestsField;

	private static AccessTools.FieldRef<GameComponent, IList> questChainsGameCompFutureQuestsField;

	private static FastInvokeHandler questInfoQuestGetter;

	private static AccessTools.FieldRef<object, QuestScriptDef> futureQuestInfoQuestDefField;

	public VanillaExpandedFramework(ModContentPack mod)
	{
		(Action, string, bool)[] array = new (Action, string, bool)[29]
		{
			(PatchAdvancedResourceProcessor, "Advanced Resource Processor", true),
			(PatchOtherRng, "Other RNG", false),
			(PatchWeapons, "Weapons", false),
			(PatchDebug, "Debug Gizmos", false),
			(PatchAbilities, "Abilities", true),
			(PatchHireableFactions, "Hireable Factions", false),
			(PatchVanillaFurnitureExpanded, "Vanilla Furniture Expanded", true),
			(PatchAnimalBehaviour, "Animal Behaviour", false),
			(PatchMVCF, "Multi-Verb Combat Framework", false),
			(PatchVanillaApparelExpanded, "Vanilla Apparel Expanded", false),
			(PatchPipeSystem, "Pipe System", true),
			(PatchKCSG, "KCSG (custom structure generation)", false),
			(PatchFactionDiscovery, "Faction Discovery", false),
			(PatchGenes, "Genes", false),
			(PatchCooking, "Cooking", true),
			(PatchDoorTeleporter, "Teleporter Doors", true),
			(PatchSpecialTerrain, "Special Terrain", true),
			(PatchWeatherOverlayEffects, "Weather Overlay Effects", false),
			(PatchExtraPregnancyApproaches, "Extra Pregnancy Approaches", false),
			(PatchWorkGiverDeliverResources, "Building stuff requiring non-construction skill", false),
			(PatchGraphicCustomizationDialog, "Graphic Customization Dialog", true),
			(PatchDraftedAi, "Drafted AI", true),
			(PatchMapObjectGeneration, "Thing spawning on map generation (ObjectSpawnsDef)", false),
			(PatchQuestChainsDevMode, "Quest chain dev mode window", false),
			(PatchMovingBases, "Moving bases", true),
			(PatchAsyncWorldTimers, "World timer ownership", true),
			(PatchPawnEffectTimestamps, "Pawn effect timestamp ownership", true),
			(VanillaExpandedFrameworkCustomFire.Install, "Custom Fire map caches", true),
			(VanillaExpandedFrameworkExtinguish.Install, "Extinguish attachment handling", true)
		};
		for (int i = 0; i < array.Length; i++)
		{
			(Action, string, bool) tuple = array[i];
			var (patchMethod, componentName, _) = tuple;
			if (tuple.Item3)
			{
				LongEventHandler.ExecuteWhenFinished(ApplyPatch);
			}
			else
			{
				ApplyPatch();
			}
			void ApplyPatch()
			{
				try
				{
					patchMethod();
				}
				catch (Exception ex)
				{
					Log.Error("MPCompat :: Encountered an error patching " + componentName + " part of Vanilla Expanded Framework - this part of the mod may not work properly!");
					Log.Error(ex.ToString());
				}
			}
		}
	}

	private static void SyncCommandWithBuilding(SyncWorker sync, ref Command command)
	{
		Traverse traverse = Traverse.Create(command).Field("building");
		if (sync.isWriting)
		{
			sync.Write(traverse.GetValue() as Thing);
		}
		else
		{
			traverse.SetValue(sync.Read<Thing>());
		}
	}

	private static void SyncCommandWithCompBuilding(SyncWorker sync, ref Command command)
	{
		Traverse traverse = Traverse.Create(command).Field("building");
		if (sync.isWriting)
		{
			sync.Write(traverse.GetValue() as ThingComp);
		}
		else
		{
			traverse.SetValue(sync.Read<ThingComp>());
		}
	}

	private static void PatchOtherRng()
	{
		PatchingUtilities.PatchPushPopRand(new _003C_003Ez__ReadOnlyArray<string>(new string[2] { "VEF.Hediffs.HediffComp_Spreadable:ThrowFleck", "VEF.Maps.TerrainComp_MoteSpawner:ThrowMote" }));
	}

	private static void PatchDebug()
	{
		MpCompat.RegisterLambdaMethod("VEF.Pawns.CompPawnDependsOn", "CompGetGizmosExtra", default(int)).SetDebugOnly();
	}

	private static void PatchVanillaApparelExpanded()
	{
		MpCompat.RegisterLambdaMethod("VEF.Apparels.CompSwitchApparel", "CompGetWornGizmosExtra", default(int));
	}

	private static void PatchAnimalBehaviour()
	{
		PatchingUtilities.PatchSystemRand("VEF.AnimalBehaviours.DamageWorker_ExtraInfecter:ApplySpecialEffectsToPart", patchPushPop: false);
		PatchingUtilities.PatchSystemRandCtor(new string[2] { "VEF.AnimalBehaviours.CompInitialHediff", "VEF.AnimalBehaviours.DeathActionWorker_DropOnDeath" }, patchPushPop: false);
		Type type = AccessTools.TypeByName("VEF.AnimalBehaviours.CompDestroyThisItem");
		MP.RegisterSyncMethod(type, "SetObjectForDestruction");
		MP.RegisterSyncMethod(type, "CancelObjectForDestruction");
		type = AccessTools.TypeByName("VEF.AnimalBehaviours.CompDieAndChangeIntoOtherDef");
		MP.RegisterSyncMethod(type, "ChangeDef");
		type = AccessTools.TypeByName("VEF.AnimalBehaviours.CompDiseasesAfterPeriod");
		MpCompat.RegisterLambdaMethod(type, "GetGizmos", default(int)).SetDebugOnly();
		type = AccessTools.TypeByName("VEF.AnimalBehaviours.Pawn_GetGizmos_Patch");
		string text = "Postfix";
		if (type == null || AccessTools.DeclaredMethod(type, text) == null)
		{
			type = AccessTools.TypeByName("VEF.AnimalBehaviours.AnimalBehavioursPawnGizmos");
			text = "GetGizmos";
		}
		MethodInfo lambda = MpMethodUtil.GetLambda(type, text, MethodType.Normal, null, 1);
		if (lambda.ReturnType != typeof(bool) || lambda.GetParameters().Length != 0 || lambda.DeclaringType.GetFields(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Length != 1 || AccessTools.DeclaredField(lambda.DeclaringType, "pawn")?.FieldType != typeof(Pawn))
		{
			throw new InvalidOperationException("Unknown VEF animal gizmo getter payload");
		}
		object value = Activator.CreateInstance(AccessTools.TypeByName("Multiplayer.Client.SyncDelegate"), lambda.DeclaringType, lambda, null);
		((IList)AccessTools.DeclaredField("Multiplayer.Client.Sync:handlers").GetValue(null)).Add(value);
		((ISyncMethod)MpCompat.RegisterLambdaDelegate(type, text, default(int))[0]).SetVersion(10);
	}

	private static void PatchKCSG()
	{
		PatchingUtilities.PatchSystemRand(AccessTools.Method(AccessTools.Inner(AccessTools.TypeByName("KCSG.SettlementGenUtils"), "Sampling"), "Sample"));
		PatchingUtilities.PatchPushPopRand(new _003C_003Ez__ReadOnlyArray<string>(new string[2] { "KCSG.KCSG_Skyfaller:SaveImpact", "KCSG.KCSG_Skyfaller:Tick" }));
	}

	private static void PatchGenes()
	{
		Type type = AccessTools.TypeByName("VEF.Genes.CompHumanHatcher");
		PatchingUtilities.PatchSystemRand(AccessTools.Method(type, "Hatch"));
		MpCompat.RegisterLambdaMethod(type, "CompGetGizmosExtra", default(int)).SetDebugOnly();
	}

	private static void PatchExtraPregnancyApproaches()
	{
		MpCompat.RegisterLambdaDelegate("VEF.Pawns.VanillaExpandedFramework_SocialCardUtility_DrawPregnancyApproach_Patch", "AddPregnancyApproachOptions", 0, 2);
	}

	private static void PatchWeapons()
	{
		PatchingUtilities.PatchPushPopRand(new _003C_003Ez__ReadOnlyArray<string>(new string[4] { "VEF.Weapons.Verb_ShootWithSmoke:TryCastShot", "VEF.Weapons.SmokeMaker:ThrowMoteDef", "VEF.Weapons.SmokeMaker:ThrowFleckDef", "VEF.Weapons.SmokeMaker:ThrowSmokeTrail" }));
		MpCompat.RegisterLambdaMethod("VEF.Weapons.CompLaserCapacitor", "CompGetGizmosExtra", 1);
		MpCompat.harmony.Patch(AccessTools.DeclaredPropertyGetter("VEF.Weapons.ExpandableProjectile:StartingPosition"), new HarmonyMethod(typeof(VanillaExpandedFramework), "PreStartingPositionGetter"), new HarmonyMethod(typeof(VanillaExpandedFramework), "PostStartingPositionGetter"));
		PatchProjectileAnimation();
	}

	private static void PatchProjectileAnimation()
	{
		Type type = AccessTools.TypeByName("VEF.Weapons.ExpandableProjectile");
		projectileFrame = AccessTools.FieldRefAccess<int>(type, "curProjectileIndex");
		projectileFadeFrame = AccessTools.FieldRefAccess<int>(type, "curProjectileFadeOutIndex");
		projectilePreviousTick = AccessTools.FieldRefAccess<int>(type, "prevTick");
		MethodInfo methodInfo = AccessTools.DeclaredPropertyGetter(type, "ProjectileMat");
		projectileMaterial = HarmonyLib.MethodInvoker.GetHandler(methodInfo);
		MpCompat.harmony.Patch(methodInfo, new HarmonyMethod(typeof(VanillaExpandedFramework), "BeforeProjectileMaterial"), null, null, new HarmonyMethod(typeof(VanillaExpandedFramework), "RestoreProjectileMaterial"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, "TickInterval"), null, new HarmonyMethod(typeof(VanillaExpandedFramework), "TickProjectileAnimation"));
	}

	private static void BeforeProjectileMaterial(object __instance, out (int frame, int fade, int previous)? __state)
	{
		__state = ((MP.IsInMultiplayer && !advancingProjectileAnimation) ? new (int, int, int)?((projectileFrame(__instance), projectileFadeFrame(__instance), projectilePreviousTick(__instance))) : (((int, int, int)?)null));
	}

	private static void RestoreProjectileMaterial(object __instance, (int frame, int fade, int previous)? __state)
	{
		if (__state.HasValue)
		{
			(int, int, int) valueOrDefault = __state.GetValueOrDefault();
			projectileFrame(__instance) = valueOrDefault.Item1;
			projectileFadeFrame(__instance) = valueOrDefault.Item2;
			projectilePreviousTick(__instance) = valueOrDefault.Item3;
		}
	}

	private static void TickProjectileAnimation(object __instance, bool __runOriginal)
	{
		if (!MP.IsInMultiplayer || !__runOriginal || ((Thing)__instance).Destroyed)
		{
			return;
		}
		bool flag = advancingProjectileAnimation;
		advancingProjectileAnimation = true;
		try
		{
			projectileMaterial(__instance);
		}
		finally
		{
			advancingProjectileAnimation = flag;
		}
	}

	private static void PreStartingPositionGetter(Vector3 ___startingPosition, bool ___pawnMoved, ref (Vector3, bool)? __state)
	{
		if (MP.InInterface)
		{
			__state = (___startingPosition, ___pawnMoved);
		}
	}

	private static void PostStartingPositionGetter(ref Vector3 ___startingPosition, ref bool ___pawnMoved, (Vector3, bool)? __state)
	{
		if (__state.HasValue)
		{
			(___startingPosition, ___pawnMoved) = __state.Value;
		}
	}

	private static void PatchAdvancedResourceProcessor()
	{
		Type type = (advancedResourceProcessorType = AccessTools.TypeByName("PipeSystem.CompAdvancedResourceProcessor"));
		advancedResourceProcessorProcessStackField = AccessTools.FieldRefAccess<object>(type, "processStack");
		MpCompat.RegisterLambdaDelegate(type, "ProcessesOptions", MethodType.Getter, 2);
		MpCompat.RegisterLambdaMethod(type, "Settings", MethodType.Getter, default(int));
		MpCompat.RegisterLambdaDelegate(type, "CompGetGizmosExtra", new string[1] { "<>4__this" }, default(int));
		MpCompat.RegisterLambdaDelegate(type, "CompGetGizmosExtra", new string[1] { "<>4__this" }, 3, 4, 5).SetDebugOnly();
		MpCompat.harmony.Patch(MpMethodUtil.GetLambda(type, "CompGetGizmosExtra", MethodType.Normal, null, 1), new HarmonyMethod(new Func<object, bool>(PrePasteProcessesGizmo)));
		type = AccessTools.TypeByName("PipeSystem.Process");
		processParentField = AccessTools.FieldRefAccess<ThingWithComps>(type, "parent");
		processIdField = AccessTools.FieldRefAccess<string>(type, "id");
		processDefField = AccessTools.FieldRefAccess<Def>(type, "def");
		processTargetCountField = AccessTools.FieldRefAccess<int>(type, "targetCount");
		processProgressField = AccessTools.FieldRefAccess<float>(type, "progress");
		processSuspendedField = AccessTools.FieldRefAccess<bool>(type, "suspended");
		MP.RegisterSyncWorker<object>(SyncProcess, type, isImplicit: true);
		MP.RegisterSyncMethod(type, "ResetProcess");
		MpCompat.RegisterLambdaMethod(type, "Options", MethodType.Getter, 0, 1);
		MP.RegisterSyncMethod(MpMethodUtil.MethodOf(new Action<ThingComp, string, int>(SyncedProcessModifyTargetCountButton)));
		MP.RegisterSyncMethod(MpMethodUtil.MethodOf(new Action<ThingComp, string>(SyncedProcessSuspendButton)));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, "DoInterface"), null, null, new HarmonyMethod(new Func<IEnumerable<CodeInstruction>, MethodBase, IEnumerable<CodeInstruction>>(ReplaceProcessImageButtons)));
		type = AccessTools.TypeByName("PipeSystem.ProcessStack");
		processStackAddProcessMethod = HarmonyLib.MethodInvoker.GetHandler(AccessTools.DeclaredMethod(type, "AddProcess"));
		processStackNotifyProcessChangeMethod = HarmonyLib.MethodInvoker.GetHandler(AccessTools.DeclaredMethod(type, "Notify_ProcessChange"));
		processStackProcessesField = AccessTools.FieldRefAccess<IList>(type, "processes");
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, "Reorder"), new HarmonyMethod(PreProcessStackReorder));
		MP.RegisterSyncMethod(type, "AddProcess").CancelIfAnyArgNull().TransformTarget(Serializer.New((object _, object _, object[] args) => (ThingWithComps)args[1], GetProcessStackFromThingWithComps), skipTypeCheck: true);
		MP.RegisterSyncMethod(type, "Delete").CancelIfAnyArgNull().TransformTarget(Serializer.New((object _, object _, object[] args) => processParentField(args[0]), GetProcessStackFromThingWithComps), skipTypeCheck: true);
		MP.RegisterSyncMethod(type, "Reorder").CancelIfAnyArgNull().TransformTarget(Serializer.New((object _, object _, object[] args) => processParentField(args[0]), GetProcessStackFromThingWithComps), skipTypeCheck: true);
		type = AccessTools.TypeByName("PipeSystem.ProcessUtility");
		processUtilityClipboardField = AccessTools.StaticFieldRefAccess<IDictionary>(AccessTools.DeclaredField(type, "Clipboard"));
		type = AccessTools.TypeByName("PipeSystem.ITab_Processor");
		MP.RegisterSyncMethod(MpMethodUtil.MethodOf(new Action<ThingComp, List<(Def, int)>>(SyncedPasteProcesses))).CancelIfAnyArgNull();
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, "FillTab"), null, null, new HarmonyMethod(new Func<IEnumerable<CodeInstruction>, MethodBase, IEnumerable<CodeInstruction>>(ReplaceProcessorITabButtons)));
		overclockMultiplier = MP.RegisterSyncField(AccessTools.Field(advancedResourceProcessorType, "overclockMultiplier"));
		type = AccessTools.TypeByName("PipeSystem.Window_Overclock");
		processorField = AccessTools.FieldRefAccess<ThingComp>(type, "building");
		MpCompat.harmony.Patch(AccessTools.Method(type, "DoWindowContents"), new HarmonyMethod(new Action<Window>(PreOverclockWindow)), new HarmonyMethod(new Action<Window>(PostOverclockWindow)));
	}

	private static object GetProcessStackFromComp(ThingComp comp)
	{
		return advancedResourceProcessorProcessStackField(comp);
	}

	private static object GetProcessStackFromThingWithComps(ThingWithComps thing)
	{
		return GetProcessStackFromComp(GetProcessorCompFromThingWithComps(thing));
	}

	private static ThingComp GetProcessorCompFromThingWithComps(ThingWithComps thing)
	{
		return thing.AllComps.Find((ThingComp x) => advancedResourceProcessorType.IsInstanceOfType(x));
	}

	private static object GetProcessById(IList processes, string id)
	{
		if (processes == null)
		{
			return null;
		}
		return processes.Cast<object>().FirstOrDefault((object process) => processIdField(process) == id);
	}

	private static void PreProcessStackReorder(object process, ref int offset, IList ___processes)
	{
		if (MP.IsExecutingSyncCommand)
		{
			int num = ___processes.IndexOf(process);
			int num2 = Mathf.Clamp(offset + num, 0, ___processes.Count - 1);
			offset = num2 - num;
		}
	}

	private static bool PrePasteProcessesGizmo(object __instance)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		ThingComp thingComp = (ThingComp)AccessTools.Field(__instance.GetType(), "<>4__this").GetValue(__instance);
		IDictionary dictionary = processUtilityClipboardField();
		if (!dictionary.Contains(thingComp.parent.def))
		{
			return false;
		}
		if (!(dictionary[thingComp.parent.def] is IList { Count: not 0 } list))
		{
			return false;
		}
		List<(Def, int)> clipboard = (from object process in list
			select (processDefField(process), processTargetCountField(process))).ToList();
		SyncedPasteProcesses(thingComp, clipboard);
		return false;
	}

	private static void PreOverclockWindow(Window __instance)
	{
		ThingComp target = processorField(__instance);
		MP.WatchBegin();
		overclockMultiplier.Watch(target);
	}

	private static void PostOverclockWindow(Window __instance)
	{
		MP.WatchEnd();
	}

	private static void SyncedPasteProcesses(ThingComp comp, List<(Def, int)> clipboard)
	{
		if (clipboard.Empty())
		{
			return;
		}
		object processStackFromComp = GetProcessStackFromComp(comp);
		IList list = processStackProcessesField(processStackFromComp);
		list.Clear();
		foreach (var (def, num) in clipboard)
		{
			processStackAddProcessMethod(processStackFromComp, def, comp.parent, num);
		}
		foreach (object item in list)
		{
			processProgressField(item) = 0f;
		}
	}

	private static bool ReplacedPasteProcesses(Rect butRect, Texture2D tex, bool doMouseoverSound, string tooltip)
	{
		bool flag = Widgets.ButtonImage(butRect, tex, doMouseoverSound, tooltip);
		if (!flag || !MP.IsInMultiplayer)
		{
			return flag;
		}
		if (!(Find.Selector.SingleSelectedThing is ThingWithComps thingWithComps))
		{
			return false;
		}
		ThingComp processorCompFromThingWithComps = GetProcessorCompFromThingWithComps(thingWithComps);
		if (processorCompFromThingWithComps == null)
		{
			return false;
		}
		IDictionary dictionary = processUtilityClipboardField();
		if (!dictionary.Contains(thingWithComps.def))
		{
			return false;
		}
		if (!(dictionary[thingWithComps.def] is IList { Count: not 0 } list))
		{
			return false;
		}
		List<(Def, int)> clipboard = (from object process in list
			select (processDefField(process), processTargetCountField(process))).ToList();
		SyncedPasteProcesses(processorCompFromThingWithComps, clipboard);
		return false;
	}

	private static void SyncedProcessModifyTargetCountButton(ThingComp comp, string processId, int offset)
	{
		object processStackFromComp = GetProcessStackFromComp(comp);
		object processById = GetProcessById(processStackProcessesField(processStackFromComp), processId);
		if (processById != null)
		{
			ref int reference = ref processTargetCountField(processById);
			if (reference == -1)
			{
				processTargetCountField(processById) = 0;
				reference = 1;
			}
			else if (reference > -1)
			{
				reference = Mathf.Max(0, reference + offset);
			}
			processStackNotifyProcessChangeMethod(processStackFromComp);
		}
	}

	private static bool ReplacedProcessModifyTargetCountButton(WidgetRow row, Texture2D texture, string tooltip, Color? mouseoverColor, Color? backgroundColor, Color? mouseoverBackgroundColor, bool doMouseoverSound, float overrideSize, object process)
	{
		bool flag = row.ButtonIcon(texture, tooltip, mouseoverColor, backgroundColor, mouseoverBackgroundColor, doMouseoverSound, overrideSize);
		if (!flag || !MP.IsInMultiplayer)
		{
			return flag;
		}
		ThingWithComps thingWithComps = processParentField(process);
		if (thingWithComps == null)
		{
			return false;
		}
		ThingComp processorCompFromThingWithComps = GetProcessorCompFromThingWithComps(thingWithComps);
		if (processorCompFromThingWithComps == null)
		{
			return false;
		}
		string processId = processIdField(process);
		int num = GenUI.CurrentAdjustmentMultiplier();
		if (texture == TexButton.Minus)
		{
			num = -num;
		}
		SyncedProcessModifyTargetCountButton(processorCompFromThingWithComps, processId, num);
		SoundDefOf.DragSlider.PlayOneShotOnCamera();
		return false;
	}

	private static void SyncedProcessSuspendButton(ThingComp comp, string processId)
	{
		object processStackFromComp = GetProcessStackFromComp(comp);
		object processById = GetProcessById(processStackProcessesField(processStackFromComp), processId);
		if (processById != null)
		{
			ref bool reference = ref processSuspendedField(processById);
			reference = !reference;
			processStackNotifyProcessChangeMethod(processStackFromComp);
		}
	}

	private static bool ReplacedProcessSuspendButton(Rect butRect, Texture2D tex, Color baseColor, bool doMouseoverSound, string tooltip, object process)
	{
		bool flag = Widgets.ButtonImage(butRect, tex, baseColor, doMouseoverSound, tooltip);
		if (!flag || !MP.IsInMultiplayer)
		{
			return flag;
		}
		ThingWithComps thingWithComps = processParentField(process);
		if (thingWithComps == null)
		{
			return false;
		}
		ThingComp processorCompFromThingWithComps = GetProcessorCompFromThingWithComps(thingWithComps);
		if (processorCompFromThingWithComps == null)
		{
			return false;
		}
		string processId = processIdField(process);
		SyncedProcessSuspendButton(processorCompFromThingWithComps, processId);
		SoundDefOf.DragSlider.PlayOneShotOnCamera();
		return false;
	}

	private static IEnumerable<CodeInstruction> ReplaceProcessorITabButtons(IEnumerable<CodeInstruction> instr, MethodBase baseMethod)
	{
		MethodInfo methodInfo = AccessTools.DeclaredMethod(typeof(Widgets), "ButtonImage", new Type[4]
		{
			typeof(Rect),
			typeof(Texture2D),
			typeof(bool),
			typeof(string)
		});
		MethodInfo to = MpMethodUtil.MethodOf(new Func<Rect, Texture2D, bool, string, bool>(ReplacedPasteProcesses));
		return instr.ReplaceMethod(methodInfo, to, baseMethod, null, null, 1, "PipeSystem_PasteProcesses");
	}

	private static IEnumerable<CodeInstruction> ReplaceProcessImageButtons(IEnumerable<CodeInstruction> instr, MethodBase baseMethod)
	{
		MethodInfo methodInfo = AccessTools.DeclaredMethod(typeof(WidgetRow), "ButtonIcon");
		MethodInfo to = MpMethodUtil.MethodOf(new Func<WidgetRow, Texture2D, string, Color?, Color?, Color?, bool, float, object, bool>(ReplacedProcessModifyTargetCountButton));
		FieldInfo firstTargetField = AccessTools.DeclaredField(typeof(TexButton), "Plus");
		FieldInfo secondTargetField = AccessTools.DeclaredField(typeof(TexButton), "Minus");
		MethodInfo methodInfo2 = AccessTools.DeclaredMethod(typeof(Widgets), "ButtonImage", new Type[5]
		{
			typeof(Rect),
			typeof(Texture2D),
			typeof(Color),
			typeof(bool),
			typeof(string)
		});
		MethodInfo to2 = MpMethodUtil.MethodOf(new Func<Rect, Texture2D, Color, bool, string, object, bool>(ReplacedProcessSuspendButton));
		FieldInfo widgetButtonField = AccessTools.DeclaredField(typeof(TexButton), "Suspend");
		return instr.ReplaceMethod(methodInfo, to, baseMethod, ExtraInstructions, null, 2, null, null, (CodeInstruction ci) => ci.LoadsField(firstTargetField) || ci.LoadsField(secondTargetField)).ReplaceMethod(methodInfo2, to2, baseMethod, ExtraInstructions, null, 1, null, null, (CodeInstruction ci) => ci.LoadsField(widgetButtonField));
		static IEnumerable<CodeInstruction> ExtraInstructions(CodeInstruction _)
		{
			yield return CodeInstruction.LoadArgument(0);
		}
	}

	private static void SyncProcess(SyncWorker sync, ref object process)
	{
		if (sync.isWriting)
		{
			if (process == null)
			{
				sync.Write<ThingComp>(null);
				Log.Error("MPCompat :: Trying to sync a null process.");
				return;
			}
			ThingComp processorCompFromThingWithComps = GetProcessorCompFromThingWithComps(processParentField(process));
			sync.Write(processorCompFromThingWithComps);
			if (processorCompFromThingWithComps == null)
			{
				Log.Error($"MPCompat :: Trying to sync a process with no parent. Process={process}");
			}
			else
			{
				sync.Write(processIdField(process));
			}
			return;
		}
		ThingComp thingComp = sync.Read<ThingComp>();
		if (thingComp == null)
		{
			return;
		}
		object obj = advancedResourceProcessorProcessStackField(thingComp);
		if (obj == null)
		{
			Log.Error($"MPCompat :: The process has no stack. Comp={thingComp}");
			return;
		}
		IList list = processStackProcessesField(obj);
		if (list == null)
		{
			Log.Error($"MPCompat :: The process has ProcessStack with no process list. Comp={thingComp}");
			return;
		}
		string text = sync.Read<string>();
		process = GetProcessById(list, text);
		if (process == null)
		{
			Log.Error($"MPCompat :: Could not find the correct process. Comp={thingComp}, id={text}");
		}
	}

	private static void PatchAbilities()
	{
		PatchingUtilities.SetupAsyncTime();
		compAbilitiesType = AccessTools.TypeByName("VEF.Abilities.CompAbilities");
		learnedAbilitiesField = AccessTools.FieldRefAccess<IEnumerable>(compAbilitiesType, "learnedAbilities");
		MP.RegisterSyncMethod(compAbilitiesType, "GiveAbility");
		compAbilitiesApparelType = AccessTools.TypeByName("VEF.Abilities.CompAbilitiesApparel");
		givenAbilitiesField = AccessTools.FieldRefAccess<IEnumerable>(compAbilitiesApparelType, "givenAbilities");
		abilityApparelPawnGetter = HarmonyLib.MethodInvoker.GetHandler(AccessTools.PropertyGetter(compAbilitiesApparelType, "Pawn"));
		Type type = AccessTools.TypeByName("VEF.Abilities.Ability");
		abilityInitMethod = HarmonyLib.MethodInvoker.GetHandler(AccessTools.Method(type, "Init"));
		abilityHolderField = AccessTools.FieldRefAccess<Thing>(type, "holder");
		abilityPawnField = AccessTools.FieldRefAccess<Pawn>(type, "pawn");
		abilityCooldownField = AccessTools.FieldRefAccess<int>(type, "cooldown");
		MP.RegisterSyncMethod(type, "StartAbilityJob", new SyncType[1] { typeof(GlobalTargetInfo[]) }).TransformArgument(0, Serializer.New((GlobalTargetInfo[] targets) => targets.Select((GlobalTargetInfo x) => (thingId: x.thingInt?.thingIDNumber ?? (-1), tileId: x.tileInt, worldObjectId: x.worldObjectInt?.ID ?? (-1), cell: x.cellInt, mapId: x.mapInt?.uniqueID ?? (-1))).ToList(), (List<(int thingId, PlanetTile tileId, int worldObjectId, IntVec3 cell, int mapId)> result) => result.Select(((int thingId, PlanetTile tileId, int worldObjectId, IntVec3 cell, int mapId) x) =>
		{
			var (num, planetTile, worldObjectId, cell, mapId) = x;
			if (num != -1)
			{
				if (MP.TryGetThingById(num, out var value))
				{
					return new GlobalTargetInfo(value);
				}
			}
			else
			{
				if (planetTile != -1)
				{
					return new GlobalTargetInfo(planetTile);
				}
				if (worldObjectId != -1)
				{
					WorldObject worldObject = Find.World.worldObjects.AllWorldObjects.Find((WorldObject w) => w.ID == worldObjectId) ?? Find.World.pocketMaps.Find((PocketMapParent p) => p.ID == worldObjectId);
					if (worldObject != null)
					{
						return new GlobalTargetInfo(worldObject);
					}
				}
				else if (cell.IsValid)
				{
					return new GlobalTargetInfo(cell, Find.Maps.FirstOrDefault((Map map) => map.uniqueID == mapId), allowNullMap: true);
				}
			}
			return GlobalTargetInfo.Invalid;
		}).ToArray()));
		MP.RegisterSyncWorker<ITargetingSource>(SyncVEFAbility, type, isImplicit: true);
		abilityAutoCastField = MP.RegisterSyncField(type, "autoCast");
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, "DoAction"), new HarmonyMethod(typeof(VanillaExpandedFramework), "PreAbilityDoAction"), new HarmonyMethod(typeof(VanillaExpandedFramework), "PostAbilityDoAction"));
		foreach (Type item in type.AllSubclasses().Concat(type))
		{
			if (!item.IsAbstract)
			{
				PatchingUtilities.RegisterTimestampFixer(item, MpMethodUtil.MethodOf(FixAbilityTimestamp));
			}
			MethodInfo methodInfo = AccessTools.DeclaredMethod(item, "GetGizmo");
			if (methodInfo != null)
			{
				MpCompat.harmony.Patch(methodInfo, new HarmonyMethod(MpMethodUtil.MethodOf(PreGetGizmo)), null, null, new HarmonyMethod(MpMethodUtil.MethodOf(new Action<PatchingUtilities.TimeSnapshot?>(RestoreProperTimeSnapshot))));
			}
		}
		type = AccessTools.TypeByName("VEF.Apparels.CompShieldField");
		MpCompat.RegisterLambdaMethod(type, "CompGetWornGizmosExtra", default(int));
		MpCompat.RegisterLambdaMethod(type, "GetGizmos", 0, 2);
		type = AccessTools.TypeByName("VEF.Abilities.Command_Ability");
		foreach (Type item2 in type.AllSubclasses().Concat(type))
		{
			MethodInfo methodInfo2 = AccessTools.DeclaredMethod(item2, "GizmoOnGUIInt");
			if (methodInfo2 != null)
			{
				MpCompat.harmony.Patch(methodInfo2, new HarmonyMethod(MpMethodUtil.MethodOf(PreAbilityGizmoGui)), null, null, new HarmonyMethod(MpMethodUtil.MethodOf(new Action<PatchingUtilities.TimeSnapshot?>(RestoreProperTimeSnapshot))));
			}
		}
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod("VEF.Abilities.Ability:Cast", new Type[1] { typeof(GlobalTargetInfo[]) }), null, null, new HarmonyMethod(typeof(VanillaExpandedFramework), "UseAbilityClockForCooldown"));
	}

	private static void SyncVEFAbility(SyncWorker sync, ref ITargetingSource source)
	{
		if (sync.isWriting)
		{
			sync.Write(abilityHolderField(source));
			sync.Write(source.GetVerb.GetUniqueLoadID());
			return;
		}
		Thing thing = sync.Read<Thing>();
		string text = sync.Read<string>();
		if (thing is ThingWithComps thingWithComps)
		{
			IEnumerable enumerable = null;
			ThingComp thingComp = thingWithComps.AllComps.FirstOrDefault((ThingComp c) => compAbilitiesType.IsInstanceOfType(c));
			ThingComp thingComp2 = null;
			if (thingComp != null)
			{
				enumerable = learnedAbilitiesField(thingComp);
			}
			if (enumerable == null)
			{
				thingComp2 = thingWithComps.AllComps.FirstOrDefault((ThingComp c) => compAbilitiesApparelType.IsInstanceOfType(c));
				if (thingComp2 != null)
				{
					enumerable = givenAbilitiesField(thingComp2);
				}
			}
			if (enumerable != null)
			{
				foreach (object item in enumerable)
				{
					ITargetingSource targetingSource = item as ITargetingSource;
					if (targetingSource?.GetVerb.GetUniqueLoadID() == text)
					{
						source = targetingSource;
						break;
					}
				}
				if (source != null && thingComp2 != null)
				{
					Pawn pawn = abilityApparelPawnGetter(thingComp2) as Pawn;
					abilityPawnField(source) = pawn;
					abilityInitMethod(source);
				}
			}
			else
			{
				Log.Error("MPCompat :: SyncVEFAbility : Holder is missing or of unsupported type");
			}
		}
		else
		{
			Log.Error("MPCompat :: SyncVEFAbility : Holder isn't a ThingWithComps");
		}
	}

	private static void PreAbilityDoAction(object __instance)
	{
		if (MP.IsInMultiplayer)
		{
			MP.WatchBegin();
			abilityAutoCastField.Watch(__instance);
		}
	}

	private static void PostAbilityDoAction()
	{
		if (MP.IsInMultiplayer)
		{
			MP.WatchEnd();
		}
	}

	private static void PreGetGizmo(object __instance, out PatchingUtilities.TimeSnapshot? __state)
	{
		__state = SetTemporaryTimeSnapshot(__instance);
	}

	public static void PreAbilityGizmoGui(object ___ability, out PatchingUtilities.TimeSnapshot? __state)
	{
		__state = SetTemporaryTimeSnapshot(___ability);
	}

	private static PatchingUtilities.TimeSnapshot? SetTemporaryTimeSnapshot(object ability)
	{
		if (!MP.IsInMultiplayer || !PatchingUtilities.IsAsyncTime || ability == null)
		{
			return null;
		}
		Pawn pawn = abilityPawnField(ability);
		if (pawn != null)
		{
			return PatchingUtilities.TimeSnapshot.GetAndSetFromPawn(pawn);
		}
		Thing thing = abilityHolderField(ability);
		if (thing == null)
		{
			return null;
		}
		Map mapHeld = thing.MapHeld;
		if (mapHeld == null)
		{
			return PatchingUtilities.TimeSnapshot.GetAndSetFromWorld();
		}
		return PatchingUtilities.TimeSnapshot.GetAndSetFromMap(mapHeld);
	}

	public static void RestoreProperTimeSnapshot(PatchingUtilities.TimeSnapshot? __state)
	{
		__state?.Set();
	}

	private static ref int FixAbilityTimestamp(IExposable ability)
	{
		return ref abilityCooldownField(ability);
	}

	private static IEnumerable<CodeInstruction> UseAbilityClockForCooldown(IEnumerable<CodeInstruction> instructions)
	{
		List<CodeInstruction> list = instructions.ToList();
		Type type = AccessTools.TypeByName("VEF.Abilities.Ability");
		MethodInfo method = AccessTools.PropertyGetter(typeof(Find), "TickManager");
		MethodInfo method2 = AccessTools.PropertyGetter(typeof(TickManager), "TicksGame");
		MethodInfo method3 = AccessTools.DeclaredMethod(type, "GetCooldownForPawn");
		FieldInfo objB = AccessTools.DeclaredField(type, "cooldown");
		int num = -1;
		for (int i = 2; i + 4 < list.Count; i++)
		{
			if (!(list[i - 2].opcode != OpCodes.Ldarg_0) && list[i - 1].Calls(method) && list[i].Calls(method2) && !(list[i + 1].opcode != OpCodes.Ldarg_0) && list[i + 2].Calls(method3) && !(list[i + 3].opcode != OpCodes.Add) && !(list[i + 4].opcode != OpCodes.Stfld) && object.Equals(list[i + 4].operand, objB))
			{
				if (num != -1)
				{
					throw new InvalidOperationException("VEF ability cooldown-write contract has multiple matches");
				}
				num = i;
			}
		}
		if (num == -1)
		{
			throw new InvalidOperationException("VEF ability cooldown-write contract changed");
		}
		list[num].opcode = OpCodes.Ldarg_0;
		list[num].operand = null;
		CodeInstruction codeInstruction = new CodeInstruction(OpCodes.Call, MpMethodUtil.MethodOf(new Func<TickManager, object, int>(ReadAbilityCooldownClock)));
		for (int num2 = list[num].blocks.Count - 1; num2 >= 0; num2--)
		{
			ExceptionBlock exceptionBlock = list[num].blocks[num2];
			if (exceptionBlock.blockType == ExceptionBlockType.EndExceptionBlock)
			{
				codeInstruction.blocks.Insert(0, exceptionBlock);
				list[num].blocks.RemoveAt(num2);
			}
		}
		list.Insert(num + 1, codeInstruction);
		return list;
	}

	private static int ReadAbilityCooldownClock(TickManager manager, object ability)
	{
		PatchingUtilities.TimeSnapshot? timeSnapshot = SetTemporaryTimeSnapshot(ability);
		try
		{
			return manager.TicksGame;
		}
		finally
		{
			timeSnapshot?.Set();
		}
	}

	private static void PatchPawnEffectTimestamps()
	{
		Type type = AccessTools.TypeByName("VEF.Hediffs.HediffComp_Spreadable");
		AccessTools.FieldRef<object, int> spreading = AccessTools.FieldRefAccess<int>(type, "nextSpreadingTick");
		AccessTools.FieldRef<object, int> fleck = AccessTools.FieldRefAccess<int>(type, "nextFleckSpawnTick");
		PatchingUtilities.RegisterTimestampFields(type, (object comp, int offset) =>
		{
			spreading(comp) = spreading(comp) + offset;
			fleck(comp) = fleck(comp) + offset;
		});
		PatchShieldTimestamps();
	}

	private static void PatchShieldTimestamps()
	{
		Type type = AccessTools.TypeByName("VEF.Apparels.CompShieldField");
		shieldTimeFields = new string[3] { "lastTimeActivated", "lastTimeDisabled", "lastTimeDisarmed" }.Select((string name2) => AccessTools.DeclaredField(type, name2)).ToArray();
		shieldTimes = shieldTimeFields.Select((FieldInfo field) => AccessTools.FieldRefAccess<object, int>(field)).ToArray();
		Patch(AccessTools.PropertyGetter(type, "ManuallyActivated"), 1, 0);
		Patch(AccessTools.DeclaredMethod(type, "CanActivateShield"), 2, 0);
		Patch(AccessTools.DeclaredMethod(type, "CompTick"), 1, 2);
		Patch(AccessTools.DeclaredMethod(type, "BreakShield"), 0, 1);
		Patch(MpMethodUtil.GetLambda(type, "CompGetWornGizmosExtra"), 0, 2);
		Patch(MpMethodUtil.GetLambda(type, "GetGizmos", MethodType.Normal, null, 2), 0, 2);
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, "PostExposeData"), null, new HarmonyMethod(typeof(VanillaExpandedFramework), "ExposeShieldClockState"));
		Type type2 = AccessTools.TypeByName("VEF.Apparels.Command_ActionWithCooldown");
		shieldCommandConstructor = AccessTools.Constructor(type2, new Type[2]
		{
			typeof(int),
			typeof(int)
		});
		shieldCommandLastUsed = AccessTools.FieldRefAccess<int>(type2, "lastUsedTick");
		shieldHostThing = HarmonyLib.MethodInvoker.GetHandler(AccessTools.PropertyGetter(type, "HostThing"));
		string[] array = new string[2] { "CompGetWornGizmosExtra", "GetGizmos" };
		foreach (string name in array)
		{
			MethodInfo methodInfo = AccessTools.EnumeratorMoveNext(AccessTools.DeclaredMethod(type, name));
			shieldIteratorOwners.Add(methodInfo.DeclaringType, AccessTools.FieldRefAccess<object>(methodInfo.DeclaringType, "<>4__this"));
			MpCompat.harmony.Patch(methodInfo, new HarmonyMethod(typeof(VanillaExpandedFramework), "BeforeShieldIterator"), null, new HarmonyMethod(typeof(VanillaExpandedFramework), "TagShieldCommands"), new HarmonyMethod(typeof(VanillaExpandedFramework), "RestoreProperTimeSnapshot"));
		}
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type2, "GizmoOnGUI"), new HarmonyMethod(typeof(VanillaExpandedFramework), "BeforeShieldCommand"), null, new HarmonyMethod(typeof(VanillaExpandedFramework), "UseShieldCommandMarker"), new HarmonyMethod(typeof(VanillaExpandedFramework), "RestoreProperTimeSnapshot"));
		PatchingUtilities.RegisterTimestampFields(type, (object comp, int offset) =>
		{
			ShieldClockState orCreateValue = shieldClocks.GetOrCreateValue(comp);
			for (int i = 0; i < shieldTimes.Length; i++)
			{
				if (orCreateValue.valid[i] || shieldTimes[i](comp) > 0)
				{
					orCreateValue.valid[i] = true;
					shieldTimes[i](comp) = shieldTimes[i](comp) + offset;
				}
			}
		});
		static void Patch(MethodInfo method, int guards, int writes)
		{
			if (method == null)
			{
				throw new MissingMethodException("VEF shield clock consumer");
			}
			shieldClockContracts.Add(method, (guards, writes));
			MpCompat.harmony.Patch(method, null, null, new HarmonyMethod(typeof(VanillaExpandedFramework), "KeepShieldTimestampValidity"));
		}
	}

	private static IEnumerable<CodeInstruction> KeepShieldTimestampValidity(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
	{
		List<CodeInstruction> list = instructions.ToList();
		MethodInfo method = AccessTools.PropertyGetter(typeof(TickManager), "TicksGame");
		int num = 0;
		int num2 = 0;
		for (int i = 0; i < list.Count; i++)
		{
			if (!(list[i].operand is FieldInfo value))
			{
				continue;
			}
			int num3 = Array.IndexOf(shieldTimeFields, value);
			if (num3 < 0)
			{
				continue;
			}
			if (list[i].opcode == OpCodes.Ldfld && i + 1 < list.Count && list[i + 1].LoadsConstant(0L))
			{
				list[i].opcode = OpCodes.Ldc_I4;
				list[i].operand = num3;
				CodeInstruction codeInstruction = new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(VanillaExpandedFramework), "ReadShieldTimeMarker"));
				MoveEndingBlocks(list[i], codeInstruction);
				list.Insert(++i, codeInstruction);
				num++;
			}
			else if (list[i].opcode == OpCodes.Stfld)
			{
				bool flag = i > 0 && list[i - 1].Calls(method);
				if (!flag && (i == 0 || !list[i - 1].LoadsConstant(0L)))
				{
					throw new InvalidOperationException("VEF shield timestamp write contract changed");
				}
				list[i].opcode = OpCodes.Ldc_I4;
				list[i].operand = num3;
				CodeInstruction codeInstruction2 = new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(VanillaExpandedFramework), "StoreShieldTime"));
				MoveEndingBlocks(list[i], codeInstruction2);
				list.Insert(++i, new CodeInstruction(flag ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0));
				list.Insert(++i, codeInstruction2);
				num2++;
			}
		}
		(int, int) tuple = shieldClockContracts[__originalMethod];
		int num4 = num;
		int num5 = num2;
		if (tuple.Item1 != num4 || tuple.Item2 != num5)
		{
			throw new InvalidOperationException($"VEF shield clock contract changed for {__originalMethod}: {num}/{num2}");
		}
		return list;
	}

	private static int ReadShieldTimeMarker(object comp, int kind)
	{
		int num = shieldTimes[kind](comp);
		if (!MP.IsInMultiplayer)
		{
			return num;
		}
		if (num <= 0 && (!shieldClocks.TryGetValue(comp, out var value) || !value.valid[kind]))
		{
			return 0;
		}
		return 1;
	}

	private static void StoreShieldTime(object comp, int tick, int kind, bool valid)
	{
		shieldTimes[kind](comp) = tick;
		if (MP.IsInMultiplayer)
		{
			shieldClocks.GetOrCreateValue(comp).valid[kind] = valid;
		}
	}

	private static void ExposeShieldClockState(object __instance)
	{
		if (MP.IsInMultiplayer)
		{
			ShieldClockState orCreateValue = shieldClocks.GetOrCreateValue(__instance);
			Scribe_Values.Look(ref orCreateValue.valid[0], "mpActivatedTimeValid", defaultValue: false);
			Scribe_Values.Look(ref orCreateValue.valid[1], "mpDisabledTimeValid", defaultValue: false);
			Scribe_Values.Look(ref orCreateValue.valid[2], "mpDisarmedTimeValid", defaultValue: false);
		}
	}

	private static PatchingUtilities.TimeSnapshot? ShieldTime(object comp)
	{
		if (!MP.IsInMultiplayer || !PatchingUtilities.IsAsyncTime)
		{
			return null;
		}
		Thing thing = (Thing)shieldHostThing(comp);
		if (thing is Pawn pawn)
		{
			return PatchingUtilities.TimeSnapshot.GetAndSetFromPawn(pawn);
		}
		Map mapHeld = thing.MapHeld;
		if (mapHeld == null)
		{
			return PatchingUtilities.TimeSnapshot.GetAndSetFromWorld();
		}
		return PatchingUtilities.TimeSnapshot.GetAndSetFromMap(mapHeld);
	}

	private static void BeforeShieldIterator(object __instance, out PatchingUtilities.TimeSnapshot? __state)
	{
		__state = ShieldTime(shieldIteratorOwners[__instance.GetType()](__instance));
	}

	private static void BeforeShieldCommand(object __instance, out PatchingUtilities.TimeSnapshot? __state)
	{
		__state = (shieldCommandOwners.TryGetValue(__instance, out var value) ? ShieldTime(value.comp) : ((PatchingUtilities.TimeSnapshot?)null));
	}

	private static IEnumerable<CodeInstruction> TagShieldCommands(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
	{
		List<CodeInstruction> list = instructions.ToList();
		int num = 0;
		for (int i = 0; i < list.Count; i++)
		{
			if (list[i].opcode == OpCodes.Newobj && object.Equals(list[i].operand, shieldCommandConstructor))
			{
				list[i].opcode = OpCodes.Ldarg_0;
				list[i].operand = null;
				CodeInstruction codeInstruction = new CodeInstruction(OpCodes.Castclass, shieldCommandConstructor.DeclaringType);
				MoveEndingBlocks(list[i], codeInstruction);
				list.InsertRange(i + 1, new _003C_003Ez__ReadOnlyArray<CodeInstruction>(new CodeInstruction[3]
				{
					new CodeInstruction(OpCodes.Ldfld, AccessTools.DeclaredField(__originalMethod.DeclaringType, "<>4__this")),
					new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(VanillaExpandedFramework), "CreateShieldCommand")),
					codeInstruction
				}));
				i += 3;
				num++;
			}
		}
		if (num != 1)
		{
			throw new InvalidOperationException("VEF shield command construction contract changed");
		}
		return list;
	}

	private static Gizmo CreateShieldCommand(int lastUsed, int cooldown, object comp)
	{
		Gizmo gizmo = (Gizmo)shieldCommandConstructor.Invoke(new object[2] { lastUsed, cooldown });
		if (MP.IsInMultiplayer)
		{
			shieldCommandOwners.Add(gizmo, new ShieldCommandOwner
			{
				comp = comp
			});
		}
		return gizmo;
	}

	private static IEnumerable<CodeInstruction> UseShieldCommandMarker(IEnumerable<CodeInstruction> instructions)
	{
		List<CodeInstruction> list = instructions.ToList();
		FieldInfo field = AccessTools.DeclaredField(shieldCommandConstructor.DeclaringType, "lastUsedTick");
		int num = 0;
		for (int i = 0; i + 1 < list.Count; i++)
		{
			if (list[i].LoadsField(field) && list[i + 1].LoadsConstant(0L))
			{
				list[i].opcode = OpCodes.Call;
				list[i].operand = AccessTools.DeclaredMethod(typeof(VanillaExpandedFramework), "ReadShieldCommandMarker");
				num++;
			}
		}
		if (num != 1)
		{
			throw new InvalidOperationException("VEF shield command marker contract changed");
		}
		return list;
	}

	private static int ReadShieldCommandMarker(object command)
	{
		if (!MP.IsInMultiplayer || !shieldCommandOwners.TryGetValue(command, out var value))
		{
			return shieldCommandLastUsed(command);
		}
		return ReadShieldTimeMarker(value.comp, 1);
	}

	private static void MoveEndingBlocks(CodeInstruction first, CodeInstruction last)
	{
		for (int num = first.blocks.Count - 1; num >= 0; num--)
		{
			if (first.blocks[num].blockType == ExceptionBlockType.EndExceptionBlock)
			{
				last.blocks.Insert(0, first.blocks[num]);
				first.blocks.RemoveAt(num);
			}
		}
	}

	private static void PatchAsyncWorldTimers()
	{
		PatchingUtilities.SetupAsyncTime();
		worldTicksAbs = (Func<int>)(AccessTools.DeclaredMethod("Multiplayer.Client.AsyncTimeComp:GetWorldTicksAbs") ?? throw new MissingMethodException("Multiplayer.Client.AsyncTimeComp", "GetWorldTicksAbs")).CreateDelegate(typeof(Func<int>));
		string[] array = new string[9] { "VEF.Factions.ContrabandDef:get_ImpactInTicks", "VEF.Planet.HiringContractTracker:SetNewContract", "VEF.Planet.HiringContractTracker:EndContract", "VEF.Planet.HiringContractTracker:BreakContract", "VEF.Planet.Dialog_ContractInfo:DoWindowContents", "VEF.Storyteller.GameComponent_QuestChains:ScheduleQuestInTicks", "VEF.Storyteller.GameComponent_QuestChains:QuestCompleted", "VEF.Storyteller.GameComponent_QuestChains:QuestExpired", "VEF.Storyteller.GameComponent_QuestChains:TryScheduleQuest" };
		for (int i = 0; i < array.Length; i++)
		{
			PatchWorldClock(AccessTools.DeclaredMethod(array[i]));
		}
		PatchWorldClock(MpMethodUtil.GetLambda(AccessTools.TypeByName("VEF.Planet.Dialog_ContractInfo"), "DoWindowContents", MethodType.Normal, null, 1));
		Type type = AccessTools.TypeByName("VEF.Planet.HiringContractTracker");
		hireDeadCount = AccessTools.FieldRefAccess<IDictionary>(type, "deadCount");
		Type type2 = AccessTools.TypeByName("VEF.Planet.ExposablePair");
		hirePenaltyCount = AccessTools.FieldRefAccess<object>(type2, "key");
		hirePenaltyExpiry = AccessTools.FieldRefAccess<object>(type2, "value");
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, "GetFactorForHireable"), new HarmonyMethod(typeof(VanillaExpandedFramework), "ReadHirePenalty"));
	}

	internal static void PatchWorldClock(MethodInfo method)
	{
		if (method == null)
		{
			throw new MissingMethodException("Missing world timer method");
		}
		MpCompat.harmony.Patch(method, null, null, new HarmonyMethod(typeof(VanillaExpandedFramework), "UseWorldClock"));
	}

	private static IEnumerable<CodeInstruction> UseWorldClock(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
	{
		MethodInfo game = AccessTools.PropertyGetter(typeof(TickManager), "TicksGame");
		MethodInfo abs = AccessTools.PropertyGetter(typeof(TickManager), "TicksAbs");
		MethodInfo days = AccessTools.PropertyGetter(typeof(GenDate), "DaysPassed");
		int replaced = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			string text;
			if (instruction.Calls(game))
			{
				text = "ReadWorldGameTicks";
			}
			else if (instruction.Calls(abs))
			{
				text = "ReadWorldAbsTicks";
			}
			else
			{
				text = (instruction.Calls(days) ? "ReadWorldDaysPassed" : null);
			}
			if (text != null)
			{
				instruction.opcode = OpCodes.Call;
				instruction.operand = AccessTools.DeclaredMethod(typeof(VanillaExpandedFramework), text);
				replaced++;
			}
			yield return instruction;
		}
		if (replaced != 1)
		{
			throw new InvalidOperationException($"World timer clock-read contract changed for {__originalMethod}: {replaced}");
		}
	}

	private static int ReadWorldGameTicks(TickManager manager)
	{
		if (!MP.IsInMultiplayer || !PatchingUtilities.IsAsyncTime)
		{
			return manager.TicksGame;
		}
		PatchingUtilities.TimeSnapshot? andSetFromWorld = PatchingUtilities.TimeSnapshot.GetAndSetFromWorld();
		try
		{
			return manager.TicksGame;
		}
		finally
		{
			andSetFromWorld?.Set();
		}
	}

	private static int ReadWorldAbsTicks(TickManager manager)
	{
		if (!MP.IsInMultiplayer || !PatchingUtilities.IsAsyncTime)
		{
			return manager.TicksAbs;
		}
		return worldTicksAbs();
	}

	private static int ReadWorldDaysPassed()
	{
		if (!MP.IsInMultiplayer || !PatchingUtilities.IsAsyncTime)
		{
			return GenDate.DaysPassed;
		}
		PatchingUtilities.TimeSnapshot? andSetFromWorld = PatchingUtilities.TimeSnapshot.GetAndSetFromWorld();
		try
		{
			return GenDate.DaysPassed;
		}
		finally
		{
			andSetFromWorld?.Set();
		}
	}

	private static bool ReadHirePenalty(object __instance, object __0, ref float __result)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		__result = 0f;
		if (!(hireDeadCount(__instance)[__0] is IList list))
		{
			return false;
		}
		int num = ReadWorldAbsTicks(Find.TickManager);
		foreach (object item in list)
		{
			if (num <= (int)hirePenaltyExpiry(item))
			{
				__result += 0.05f * (float)(int)hirePenaltyCount(item);
			}
		}
		return false;
	}

	private static void PatchHireableFactions()
	{
		hireDialogType = AccessTools.TypeByName("VEF.Planet.Dialog_Hire");
		DialogUtilities.RegisterDialogCloseSync(hireDialogType, addPauseLock: true);
		MP.RegisterSyncMethod(hireDialogType, "OnAcceptKeyPressed");
		MP.RegisterSyncWorker<Window>(SyncHireDialog, hireDialogType);
		MP.RegisterSyncMethod(typeof(VanillaExpandedFramework), "SyncedSetHireData");
		hireDataField = AccessTools.FieldRefAccess<Dictionary<PawnKindDef, Pair<int, string>>>(hireDialogType, "hireData");
		daysAmountField = MP.RegisterSyncField(hireDialogType, "daysAmount");
		currentFactionDefField = MP.RegisterSyncField(hireDialogType, "curFaction");
		MpCompat.harmony.Patch(AccessTools.Method(hireDialogType, "DoWindowContents"), new HarmonyMethod(typeof(VanillaExpandedFramework), "PreHireDialogDoWindowContents"), new HarmonyMethod(typeof(VanillaExpandedFramework), "PostHireDialogDoWindowContents"));
		Type type = AccessTools.TypeByName("VEF.Planet.Hireable");
		MP.RegisterSyncWorker<object>(SyncHireable, type);
		MpCompat.RegisterLambdaDelegate(type, "CommFloatMenuOption", default(int));
		hireablesList = AccessTools.StaticFieldRefAccess<IList>(AccessTools.Field(AccessTools.TypeByName("VEF.Planet.HireableSystemStaticInitialization"), "Hireables"));
		contractInfoDialogType = AccessTools.TypeByName("VEF.Planet.Dialog_ContractInfo");
		DialogUtilities.RegisterDialogCloseSync(contractInfoDialogType, addPauseLock: true);
		MP.RegisterSyncWorker<Window>(SyncContractInfoDialog, contractInfoDialogType);
		MpCompat.RegisterLambdaMethod(contractInfoDialogType, "DoWindowContents", 1);
		MpCompat.RegisterLambdaDelegate("VEF.Planet.HiringContractTracker", "CommFloatMenuOption", default(int));
	}

	private static void SyncHireDialog(SyncWorker sync, ref Window dialog)
	{
		if (!sync.isWriting)
		{
			dialog = Find.WindowStack.Windows.FirstOrDefault((Window x) => x.GetType() == hireDialogType);
		}
	}

	private static void PreHireDialogDoWindowContents(Window __instance, Dictionary<PawnKindDef, Pair<int, string>> ___hireData, ref Dictionary<PawnKindDef, Pair<int, string>> __state)
	{
		if (MP.IsInMultiplayer)
		{
			MP.WatchBegin();
			daysAmountField.Watch(__instance);
			currentFactionDefField.Watch(__instance);
			__state = ___hireData.ToDictionary((KeyValuePair<PawnKindDef, Pair<int, string>> x) => x.Key, (KeyValuePair<PawnKindDef, Pair<int, string>> x) => x.Value);
		}
	}

	private static void PostHireDialogDoWindowContents(Window __instance, Dictionary<PawnKindDef, Pair<int, string>> ___hireData, Dictionary<PawnKindDef, Pair<int, string>> __state)
	{
		if (!MP.IsInMultiplayer)
		{
			return;
		}
		MP.WatchEnd();
		foreach (KeyValuePair<PawnKindDef, Pair<int, string>> item in __state)
		{
			item.Deconstruct(out var key, out var value);
			PawnKindDef key2 = key;
			Pair<int, string> pair = value;
			int first = pair.First;
			value = ___hireData[key2];
			if (first != value.First)
			{
				hireDataField(__instance) = __state;
				SyncedSetHireData(___hireData);
				break;
			}
		}
	}

	private static void SyncedSetHireData(Dictionary<PawnKindDef, Pair<int, string>> hireData)
	{
		Window window = Find.WindowStack.Windows.FirstOrDefault((Window x) => x.GetType() == hireDialogType);
		if (window != null)
		{
			hireDataField(window) = hireData;
		}
	}

	private static void SyncContractInfoDialog(SyncWorker sync, ref Window window)
	{
		if (!sync.isWriting)
		{
			window = Find.WindowStack.windows.FirstOrDefault((Window x) => x.GetType() == contractInfoDialogType);
		}
	}

	private static void SyncHireable(SyncWorker sync, ref object obj)
	{
		if (sync.isWriting)
		{
			sync.Write(hireablesList().IndexOf(obj));
		}
		else
		{
			obj = hireablesList()[sync.Read<int>()];
		}
	}

	private static void PatchVanillaFurnitureExpanded()
	{
		MpCompat.RegisterLambdaMethod("VEF.Buildings.CompConfigurableSpawner", "CompGetGizmosExtra", default(int)).SetDebugOnly();
		Type type = AccessTools.TypeByName("VEF.Buildings.Command_SetItemsToSpawn");
		MpCompat.RegisterLambdaDelegate(type, "ProcessInput", 1);
		MP.RegisterSyncWorker<Command>(SyncCommandWithCompBuilding, type, isImplicit: false, shouldConstruct: true);
		MpCompat.RegisterLambdaMethod("VEF.Buildings.CompRockSpawner", "CompGetGizmosExtra", default(int));
		type = AccessTools.TypeByName("VEF.Buildings.Command_SetStoneType");
		MP.RegisterSyncWorker<Command>(SyncCommandWithCompBuilding, type, isImplicit: false, shouldConstruct: true);
		MpCompat.RegisterLambdaDelegate(type, "ProcessInput", 1);
		type = (randomBuildingGraphicCompType = AccessTools.TypeByName("VEF.Buildings.CompRandomBuildingGraphic"));
		randomBuildingGraphicCompChangeGraphicMethod = HarmonyLib.MethodInvoker.GetHandler(AccessTools.DeclaredMethod(type, "ChangeGraphic"));
		MpCompat.RegisterLambdaMethod(type, "CompGetGizmosExtra", default(int));
		type = AccessTools.TypeByName("VEF.Buildings.Dialog_ChooseGraphic");
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, "DoWindowContents"), null, null, new HarmonyMethod(typeof(VanillaExpandedFramework), "Dialog_ChooseGraphic_ReplaceSelectionButton"));
		MP.RegisterSyncMethod(typeof(VanillaExpandedFramework), "Dialog_ChooseGraphic_SyncChange");
		type = AccessTools.TypeByName("VEF.Buildings.CompGlowerExtended");
		MP.RegisterSyncMethod(type, "SwitchColor");
		compGlowerExtendedGlowerField = AccessTools.FieldRefAccess<CompGlower>(type, "compGlower");
		type = AccessTools.Inner(type, "CompGlower_SetGlowColorInternal_Patch");
		PatchingUtilities.PatchCancelInInterface(AccessTools.DeclaredMethod(type, "Postfix"));
		dummyGlowerParentCompField = AccessTools.FieldRefAccess<ThingComp>(dummyGlowerType = AccessTools.TypeByName("VEF.Buildings.DummyGlower"), "parentComp");
		MP.RegisterSyncWorker<CompGlower>(SyncCompGlower);
		type = AccessTools.TypeByName("VEF.Buildings.CompCustomizableGraphic");
		MP.RegisterSyncMethod(type, "SelectGraphic", new SyncType[2]
		{
			typeof(List<>).MakeGenericType(type),
			typeof(int)
		});
	}

	private static bool Dialog_ChooseGraphic_ReplacementButton(Rect butRect, bool doMouseoverSound, Thing thingToChange, int index, Window window)
	{
		bool flag = Widgets.ButtonInvisible(butRect, doMouseoverSound);
		if (!MP.IsInMultiplayer || !flag)
		{
			return flag;
		}
		window.Close();
		Dialog_ChooseGraphic_SyncChange(index, thingToChange, from thing in Find.Selector.SelectedObjects.OfType<ThingWithComps>()
			where thing.def == thingToChange.def
			select thing.AllComps.FirstOrDefault((ThingComp x) => x.GetType() == randomBuildingGraphicCompType) into comp
			where comp != null
			select comp);
		return false;
	}

	private static void Dialog_ChooseGraphic_SyncChange(int index, Thing thingToChange, IEnumerable<ThingComp> compsToChange)
	{
		LongEventHandler.ExecuteWhenFinished(() =>
		{
			foreach (ThingComp item in compsToChange)
			{
				randomBuildingGraphicCompChangeGraphicMethod(item, false, index, false);
			}
		});
		thingToChange.DirtyMapMesh(thingToChange.Map);
	}

	private static IEnumerable<CodeInstruction> Dialog_ChooseGraphic_ReplaceSelectionButton(IEnumerable<CodeInstruction> instr)
	{
		MethodInfo targetMethod = AccessTools.DeclaredMethod(typeof(Widgets), "ButtonInvisible", new Type[2]
		{
			typeof(Rect),
			typeof(bool)
		});
		MethodInfo replacementMethod = AccessTools.DeclaredMethod(typeof(VanillaExpandedFramework), "Dialog_ChooseGraphic_ReplacementButton");
		Type type = AccessTools.TypeByName("VEF.Buildings.Dialog_ChooseGraphic");
		FieldInfo thingToChangeField = AccessTools.DeclaredField(type, "thingToChange");
		FieldInfo indexField = null;
		foreach (CodeInstruction ci in instr)
		{
			if (indexField == null && (ci.opcode == OpCodes.Ldfld || ci.opcode == OpCodes.Stfld) && ci.operand is FieldInfo { Name: "i" } fieldInfo)
			{
				indexField = fieldInfo;
			}
			if (ci.opcode == OpCodes.Call && ci.operand is MethodInfo methodInfo && methodInfo == targetMethod && indexField != null)
			{
				ci.operand = replacementMethod;
				yield return new CodeInstruction(OpCodes.Ldarg_0);
				yield return new CodeInstruction(OpCodes.Ldfld, thingToChangeField);
				yield return new CodeInstruction(OpCodes.Ldloc_S, 5);
				yield return new CodeInstruction(OpCodes.Ldfld, indexField);
				yield return new CodeInstruction(OpCodes.Ldarg_0);
			}
			yield return ci;
		}
	}

	private static void SyncCompGlower(SyncWorker sync, ref CompGlower glower)
	{
		if (sync.isWriting)
		{
			if (dummyGlowerType.IsInstanceOfType(glower.parent))
			{
				sync.Write(obj: true);
				sync.Write(dummyGlowerParentCompField(glower.parent));
			}
			else
			{
				sync.Write(obj: false);
				sync.Write((ThingComp)glower);
			}
		}
		else if (sync.Read<bool>())
		{
			glower = compGlowerExtendedGlowerField(sync.Read<ThingComp>());
		}
		else
		{
			glower = sync.Read<ThingComp>() as CompGlower;
		}
	}

	private static void PatchMVCF()
	{
		PatchingUtilities.SetupAsyncTime();
		MpCompat.harmony.Patch(AccessTools.Method(typeof(Pawn), "SpawnSetup"), null, new HarmonyMethod(typeof(VanillaExpandedFramework), "EverybodyGetsVerbManager"));
		Type type = AccessTools.TypeByName("MVCF.MVCF");
		mvcfEnabledFeaturesSet = AccessTools.Field(type, "EnabledFeatures").GetValue(null) as IEnumerable<string>;
		if (mvcfEnabledFeaturesSet == null)
		{
			Log.Warning("MPCompat :: Cannot access the list of enabled MVCF features, this may cause issues");
		}
		type = AccessTools.TypeByName("MVCF.Utilities.PawnVerbUtility");
		mvcfPawnVerbUtilityGetManager = AccessTools.MethodDelegate<GetManager>(AccessTools.Method(type, "Manager"));
		type = AccessTools.TypeByName("MVCF.VerbManager");
		MP.RegisterSyncWorker<object>(SyncVerbManager, type, isImplicit: true);
		mvcfVerbManagerPawnGetter = HarmonyLib.MethodInvoker.GetHandler(AccessTools.PropertyGetter(type, "Pawn"));
		mvcfVerbManagerTickMethod = HarmonyLib.MethodInvoker.GetHandler(AccessTools.DeclaredMethod(type, "Tick"));
		mvcfVerbManagerVerbsField = AccessTools.FieldRefAccess<IList>(type, "verbs");
		type = typeof(System.WeakReference<>).MakeGenericType(type);
		mvcfWeakReferenceTryGetVerbManagerMethod = HarmonyLib.MethodInvoker.GetHandler(AccessTools.DeclaredMethod(type, "TryGetTarget"));
		type = AccessTools.TypeByName("MVCF.ManagedVerb");
		mvcfManagedVerbManagerGetter = HarmonyLib.MethodInvoker.GetHandler(AccessTools.PropertyGetter(type, "Manager"));
		MP.RegisterSyncWorker<object>(SyncManagedVerb, type, isImplicit: true);
		MP.RegisterSyncMethod(type, "Toggle");
		type = AccessTools.TypeByName("MVCF.VerbWithComps");
		mvcfVerbWithCompsField = AccessTools.FieldRefAccess<IList>(type, "comps");
		type = AccessTools.TypeByName("MVCF.VerbComps.VerbComp");
		mvcfVerbCompParentField = AccessTools.FieldRefAccess<object>(type, "parent");
		MP.RegisterSyncWorker<object>(SyncVerbComp, type, isImplicit: true);
		type = AccessTools.TypeByName("MVCF.VerbComps.VerbComp_Switch");
		MP.RegisterSyncMethod(type, "Enable");
		type = AccessTools.TypeByName("MVCF.Reloading.Comps.VerbComp_Reloadable_ChangeableAmmo");
		MethodInfo lambda = MpMethodUtil.GetLambda(type, "AmmoOptions", MethodType.Getter, null, 1);
		MP.RegisterSyncDelegate(type, lambda.DeclaringType.Name, lambda.Name);
		type = AccessTools.TypeByName("MVCF.PatchSets.PatchSet_HumanoidGizmos");
		MpCompat.RegisterLambdaDelegate(type, "GetGizmos_Postfix", 3);
		MpCompat.RegisterLambdaDelegate(type, "GetAttackGizmos_Postfix", 4);
		MpCompat.RegisterLambdaDelegate("MVCF.PatchSets.PatchSet_Animals", "Pawn_GetGizmos_Postfix", default(int));
		PatchingUtilities.PatchCancelInInterfaceSetResultToTrue("MVCF.PatchSets.PatchSet_MultiVerb:Prefix_OrderForceTarget");
		type = AccessTools.TypeByName("MVCF.WorldComponent_MVCF");
		mvcfWorldCompInstanceField = AccessTools.StaticFieldRefAccess<WorldComponent>(AccessTools.DeclaredField(type, "Instance"));
		mvcfWorldCompTickManagersField = AccessTools.FieldRefAccess<IList>(type, "TickManagers");
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, "WorldComponentTick"), null, null, new HarmonyMethod(typeof(VanillaExpandedFramework), "ReplaceTickWithConditionalTick"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(MapComponentUtility), "MapComponentTick"), null, new HarmonyMethod(typeof(VanillaExpandedFramework), "TickVerbManagersForMap"));
	}

	private static void EverybodyGetsVerbManager(Pawn __instance)
	{
		if (!MP.IsInMultiplayer)
		{
			return;
		}
		if (mvcfEnabledFeaturesSet == null)
		{
			try
			{
				mvcfPawnVerbUtilityGetManager(__instance, createIfMissing: true);
				return;
			}
			catch (NullReferenceException)
			{
				return;
			}
		}
		if (mvcfEnabledFeaturesSet.Any())
		{
			mvcfPawnVerbUtilityGetManager(__instance, createIfMissing: true);
		}
	}

	private static void SyncVerbManager(SyncWorker sync, ref object obj)
	{
		if (sync.isWriting)
		{
			sync.Write((Pawn)mvcfVerbManagerPawnGetter(obj));
			return;
		}
		Pawn pawn = sync.Read<Pawn>();
		obj = mvcfPawnVerbUtilityGetManager(pawn, createIfMissing: true);
		if (obj != null)
		{
			return;
		}
		throw new Exception($"MpCompat :: VerbManager of {pawn} isn't initialized! NO WAY!");
	}

	private static void SyncManagedVerb(SyncWorker sync, ref object obj)
	{
		if (sync.isWriting)
		{
			object obj2 = mvcfManagedVerbManagerGetter(obj);
			int num = mvcfVerbManagerVerbsField(obj2).IndexOf(obj);
			sync.Write(num);
			if (num >= 0)
			{
				SyncVerbManager(sync, ref obj2);
			}
			return;
		}
		int num2 = sync.Read<int>();
		if (num2 >= 0)
		{
			object obj3 = null;
			SyncVerbManager(sync, ref obj3);
			IList list = mvcfVerbManagerVerbsField(obj3);
			obj = list[num2];
		}
	}

	private static void SyncVerbComp(SyncWorker sync, ref object verbComp)
	{
		object obj = null;
		if (sync.isWriting)
		{
			obj = mvcfVerbCompParentField(verbComp);
			int obj2 = mvcfVerbWithCompsField(obj).IndexOf(verbComp);
			SyncManagedVerb(sync, ref obj);
			sync.Write(obj2);
			return;
		}
		SyncManagedVerb(sync, ref obj);
		int num = sync.Read<int>();
		if (num >= 0)
		{
			verbComp = mvcfVerbWithCompsField(obj)[num];
		}
	}

	private static void TickVerbManagersForMap(Map map)
	{
		if (!MP.IsInMultiplayer || !PatchingUtilities.IsAsyncTime)
		{
			return;
		}
		IList list = mvcfWorldCompTickManagersField(mvcfWorldCompInstanceField());
		if (list == null || list.Count <= 0)
		{
			return;
		}
		object[] array = new object[1];
		foreach (object item in list)
		{
			if ((bool)mvcfWeakReferenceTryGetVerbManagerMethod(item, array))
			{
				object target = array[0];
				if (mvcfVerbManagerPawnGetter(target) is Pawn pawn && pawn.MapHeld == map)
				{
					mvcfVerbManagerTickMethod(target);
				}
			}
		}
	}

	private static void TickOnlyNonMapManagers(object manager)
	{
		if (!MP.IsInMultiplayer || !PatchingUtilities.IsAsyncTime || !(mvcfVerbManagerPawnGetter(manager) is Pawn { MapHeld: not null }))
		{
			mvcfVerbManagerTickMethod(manager);
		}
	}

	private static IEnumerable<CodeInstruction> ReplaceTickWithConditionalTick(IEnumerable<CodeInstruction> instr, MethodBase baseMethod)
	{
		MethodInfo target = AccessTools.DeclaredMethod("MVCF.VerbManager:Tick", Type.EmptyTypes);
		MethodInfo replacement = AccessTools.DeclaredMethod(typeof(VanillaExpandedFramework), "TickOnlyNonMapManagers");
		int replacedCount = 0;
		foreach (CodeInstruction item in instr)
		{
			if (item.Calls(target))
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
			Log.Warning($"MPCompat :: Patched incorrect number of VerbManager.Tick calls (patched {replacedCount}, expected {1}) for method {value}");
		}
	}

	private static void PatchPipeSystem()
	{
		MpCompat.RegisterLambdaMethod("PipeSystem.CompConvertToThing", "PostSpawnSetup", 0, 1, 2, 3);
		MpCompat.RegisterLambdaMethod("PipeSystem.CompExplosiveContent", "CompGetGizmosExtra", default(int)).SetDebugOnly();
		Type type = AccessTools.TypeByName("PipeSystem.CompResourceProcessor");
		MethodInfo lambda = MpMethodUtil.GetLambda(type, "PostSpawnSetup", MethodType.Normal, null, 1);
		AccessTools.FieldRef<object, ThingComp> outputOwner = AccessTools.FieldRefAccess<ThingComp>(lambda.DeclaringType, "<>4__this");
		MP.RegisterSyncDelegate(type, lambda.DeclaringType.Name, lambda.Name, new string[2] { "<>4__this", "res" }).TransformField("res", Serializer.New((object result, object instance, object[] _) =>
		{
			ThingComp obj = outputOwner(instance);
			return (comp: obj, index: ResourceProcessorResults(obj).IndexOf(result));
		}, ((ThingComp comp, int index) data) =>
		{
			IList list = ((data.comp == null) ? null : ResourceProcessorResults(data.comp));
			return (list == null || data.index < 0 || data.index >= list.Count) ? null : list[data.index];
		}), skipTypeCheck: true).CancelIfFieldsNull("<>4__this", "res");
		MpCompat.RegisterLambdaMethod("PipeSystem.CompResourceStorage", "PostSpawnSetup", 0, 2, 3);
		MpCompat.RegisterLambdaMethod("PipeSystem.CompResourceStorage", "CompGetGizmosExtra", 0, 1, 2).SetDebugOnly();
		MpCompat.RegisterLambdaMethod("PipeSystem.CompSpawnerOrNet", "CompGetGizmosExtra", default(int)).SetDebugOnly();
		Type type2 = (deconstructPipeDesignatorType = AccessTools.TypeByName("PipeSystem.Designator_DeconstructPipe"));
		deconstructPipeDesignatorNetDefField = AccessTools.FieldRefAccess<Def>(type2, "pipeNetDef");
		MP.RegisterSyncWorker<Designator_Deconstruct>(SyncDeconstructPipeDesignator, type2);
		pipeNetManagerPipeNetsListField = AccessTools.FieldRefAccess<IList>(pipeNetManagerType = AccessTools.TypeByName("PipeSystem.PipeNetManager"), "pipeNets");
		type2 = AccessTools.TypeByName("PipeSystem.PipeNet");
		pipeNetMapField = AccessTools.FieldRefAccess<Map>(type2, "map");
		pipeNetDefField = AccessTools.FieldRefAccess<Def>(type2, "def");
		MP.RegisterSyncWorker<object>(SyncPipeNet, type2, isImplicit: true);
		OtherModsAsyncTime.InstallPipeProcessor();
		PatchPipeResourceInspect();
	}

	private static void PatchPipeResourceInspect()
	{
		Type type = AccessTools.TypeByName("PipeSystem.CompResource") ?? throw new TypeLoadException("PipeSystem.CompResource");
		Type type2 = AccessTools.TypeByName("PipeSystem.CompResourceTrader") ?? throw new TypeLoadException("PipeSystem.CompResourceTrader");
		pipeResourceInspectNet = HarmonyLib.MethodInvoker.GetHandler(AccessTools.PropertyGetter(type, "PipeNet") ?? throw new MissingMemberException(type.FullName, "PipeNet"));
		MpCompat.harmony.CreateReversePatcher(AccessTools.DeclaredMethod(type, "CompInspectStringExtra"), new HarmonyMethod(typeof(VanillaExpandedFramework), "UnconnectedPipeResourceInspect")).Patch();
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type2, "CompInspectStringExtra"), new HarmonyMethod(typeof(VanillaExpandedFramework), "PipeResourceInspectPrefix"));
	}

	private static bool PipeResourceInspectPrefix(ThingComp __instance, ref string __result)
	{
		if (!MP.IsInMultiplayer || pipeResourceInspectNet(__instance) != null)
		{
			return true;
		}
		__result = UnconnectedPipeResourceInspect(__instance);
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string UnconnectedPipeResourceInspect(ThingComp instance)
	{
		throw new NotSupportedException("PipeSystem base inspect reverse patch was not applied");
	}

	private static IList ResourceProcessorResults(ThingComp comp)
	{
		CompProperties props = comp.props;
		return (IList)AccessTools.Field(props.GetType(), "results").GetValue(props);
	}

	private static void SyncDeconstructPipeDesignator(SyncWorker sync, ref Designator_Deconstruct designator)
	{
		if (sync.isWriting)
		{
			sync.Write(deconstructPipeDesignatorNetDefField(designator));
			return;
		}
		designator = (Designator_Deconstruct)Activator.CreateInstance(deconstructPipeDesignatorType, sync.Read<Def>());
	}

	private static void SyncPipeNet(SyncWorker sync, ref object pipeNet)
	{
		if (sync.isWriting)
		{
			if (pipeNet == null)
			{
				sync.Write(-1);
				return;
			}
			Map map = pipeNetMapField(pipeNet);
			if (map == null)
			{
				Log.Error($"MPCompat :: Trying to sync a PipeNet with a null map. PipeNet={pipeNet}");
				sync.Write(-1);
				return;
			}
			MapComponent component = map.GetComponent(pipeNetManagerType);
			if (component == null)
			{
				Log.Error($"MPCompat :: Trying to sync a PipeNet with a map that doesn't have PipeNetManager. PipeNet={pipeNet}, Map={map}");
				sync.Write(-1);
				return;
			}
			Def def = pipeNetDefField(pipeNet);
			IList obj = pipeNetManagerPipeNetsListField(component);
			int num = -1;
			bool flag = false;
			foreach (object item in obj)
			{
				if (def == pipeNetDefField(item))
				{
					num++;
					if (pipeNet == item)
					{
						flag = true;
						break;
					}
				}
			}
			if (!flag)
			{
				Log.Error($"MPCompat :: Trying to sync a PipeNet, but it's not held by the manager. PipeNet={pipeNet}, map={map}, manager={component}");
				sync.Write(-1);
			}
			else
			{
				sync.Write(num);
				sync.Write(def);
				sync.Write(component);
			}
			return;
		}
		int num2 = sync.Read<int>();
		if (num2 < 0)
		{
			return;
		}
		Def def2 = sync.Read<Def>();
		MapComponent instance = sync.Read<MapComponent>();
		IList obj2 = pipeNetManagerPipeNetsListField(instance);
		int num3 = 0;
		foreach (object item2 in obj2)
		{
			if (def2 == pipeNetDefField(item2))
			{
				if (num3 == num2)
				{
					pipeNet = item2;
					break;
				}
				num3++;
			}
		}
	}

	private static void PatchFactionDiscovery()
	{
		newFactionSpawningDialogType = AccessTools.TypeByName("VEF.Factions.Dialog_NewFactionSpawning");
		factionDefField = AccessTools.FieldRefAccess<FactionDef>(newFactionSpawningDialogType, "factionDef");
		MP.RegisterSyncMethod(MpMethodUtil.GetLocalFunc(newFactionSpawningDialogType, "SpawnWithBases", MethodType.Normal, null, "SpawnCallback"));
		MP.RegisterSyncMethod(newFactionSpawningDialogType, "SpawnWithoutBases");
		MP.RegisterSyncMethod(newFactionSpawningDialogType, "Ignore");
		MP.RegisterSyncWorker<Window>(SyncFactionDiscoveryDialog, newFactionSpawningDialogType);
		Type type = AccessTools.TypeByName("VEF.Factions.VanillaExpandedFramework_GameComponentUtility_LoadedGame_Patch");
		type = AccessTools.Inner(type, "LoadedGame");
		MpCompat.harmony.Patch(AccessTools.Method(type, "OnGameLoaded"), new HarmonyMethod(typeof(VanillaExpandedFramework), "HostOnlyNewFactionDialog"));
	}

	private static void SyncFactionDiscoveryDialog(SyncWorker sync, ref Window window)
	{
		if (sync.isWriting)
		{
			sync.Write(factionDefField(window));
			return;
		}
		window = Find.WindowStack.Windows.FirstOrDefault((Window x) => x.GetType() == newFactionSpawningDialogType);
		FactionDef factionDef = sync.Read<FactionDef>();
		if (window == null)
		{
			if (window == null)
			{
				window = (Window)Activator.CreateInstance(newFactionSpawningDialogType, AccessTools.allDeclared, null, new object[1] { new List<FactionDef>().GetEnumerator() }, null);
			}
			factionDefField(window) = factionDef;
		}
	}

	private static bool HostOnlyNewFactionDialog()
	{
		if (MP.IsInMultiplayer)
		{
			return MP.IsHosting;
		}
		return true;
	}

	private static void PatchDoorTeleporter()
	{
		Type type = AccessTools.TypeByName("VEF.Buildings.DoorTeleporter");
		MpCompat.RegisterLambdaMethod(type, "GetDoorTeleporterGismoz", default(int)).SetContext(SyncContext.None);
		MpCompat.RegisterLambdaDelegate(type, "GetFloatMenuOptions", default(int))[0].TransformField("doorTeleporter", Serializer.New((Thing t) => t.thingIDNumber, (int id) =>
		{
			MP.TryGetThingById(id, out var value);
			return value;
		}), skipTypeCheck: true);
		renameDoorTeleporterDialogType = AccessTools.TypeByName("VEF.Buildings.Dialog_RenameDoorTeleporter");
		renameDoorTeleporterDialogConstructor = AccessTools.DeclaredConstructor(renameDoorTeleporterDialogType, new Type[1] { type });
		renameDoorTeleporterDialogThingField = AccessTools.FieldRefAccess<ThingWithComps>(renameDoorTeleporterDialogType, "DoorTeleporter");
		PatchingUtilities.PatchPushPopRand(renameDoorTeleporterDialogConstructor);
	}

	private static void PatchSpecialTerrain()
	{
		Type type = AccessTools.TypeByName("VEF.Maps.SpecialTerrainList");
		getSpecialTerrain = HarmonyLib.MethodInvoker.GetHandler(AccessTools.FirstMethod(typeof(Map), (MethodInfo method) => method.Name == "GetComponent" && method.IsGenericMethodDefinition && method.GetParameters().Length == 0).MakeGenericMethod(type));
		updateSpecialTerrain = HarmonyLib.MethodInvoker.GetHandler(AccessTools.DeclaredMethod(type, "TerrainUpdate"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod("VEF.Maps.SpecialTerrainList:TerrainUpdate"), new HarmonyMethod(typeof(VanillaExpandedFramework), "RemoveTerrainUpdateTimeBudget"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod("VEF.Maps.VanillaExpandedFramework_DoSingleTick_Patch:Postfix"), new HarmonyMethod(typeof(VanillaExpandedFramework), "AllowNativeTerrainLoop"));
		MpCompat.harmony.Patch(AccessTools.Method(typeof(Map), "MapPostTick"), null, new HarmonyMethod(typeof(VanillaExpandedFramework), "TickSpecialTerrain"));
		terrainCompType = AccessTools.TypeByName("VEF.Maps.TerrainComp");
		terrainCompParentField = AccessTools.FieldRefAccess<object>(terrainCompType, "parent");
		terrainInstancePositionField = AccessTools.FieldRefAccess<IntVec3>("VEF.Maps.TerrainInstance:positionInt");
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod("VEF.Maps.ActiveTerrainUtility:HashCodeToMod"), new HarmonyMethod(MpMethodUtil.MethodOf(SaferGetHashCode)));
	}

	private static bool AllowNativeTerrainLoop()
	{
		return !MP.IsInMultiplayer;
	}

	private static void TickSpecialTerrain(Map __instance, bool __runOriginal)
	{
		if (MP.IsInMultiplayer & __runOriginal)
		{
			updateSpecialTerrain(getSpecialTerrain(__instance), long.MaxValue);
		}
	}

	private static void RemoveTerrainUpdateTimeBudget(ref long timeBudget)
	{
		if (MP.IsInMultiplayer)
		{
			timeBudget = long.MaxValue;
		}
	}

	private static void SaferGetHashCode(ref object obj)
	{
		if (MP.IsInMultiplayer && !(obj is IntVec3))
		{
			if (terrainCompType.IsInstanceOfType(obj))
			{
				obj = terrainInstancePositionField(terrainCompParentField(obj));
				return;
			}
			Log.ErrorOnce($"MPCompat :: Potentially unsupported type for HashCodeToMod call in Multiplayer, desyncs likely to happen. Object type: {obj.GetType()}", obj.GetHashCode());
		}
	}

	private static void PatchWeatherOverlayEffects()
	{
		weatherOverlayEffectsType = AccessTools.TypeByName("VEF.Weathers.WeatherOverlay_Effects");
		FieldInfo fieldInfo = AccessTools.DeclaredField(weatherOverlayEffectsType, "nextDamageTick");
		FieldInfo fieldInfo2 = AccessTools.DeclaredField(weatherOverlayEffectsType, "nextDamageTickForMap");
		if (fieldInfo2 != null)
		{
			weatherOverlayEffectsNextDamageTickForMapField = AccessTools.FieldRefAccess<SkyOverlay, Dictionary<Map, int>>(fieldInfo2);
		}
		else
		{
			if (!(fieldInfo != null))
			{
				Log.Error("MPCompat :: VEF.Weathers.WeatherOverlay_Effects:nextDamageTick field, patch failed.");
				return;
			}
			weatherOverlayEffectsNextDamageTickField = AccessTools.FieldRefAccess<SkyOverlay, int>(fieldInfo);
		}
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(GameComponentUtility), "FinalizeInit"), null, new HarmonyMethod(typeof(VanillaExpandedFramework), "RefreshWeatherOverlayEffectCache"));
	}

	private static void RefreshWeatherOverlayEffectCache()
	{
		if (!MP.IsInMultiplayer)
		{
			return;
		}
		foreach (WeatherDef item in DefDatabase<WeatherDef>.AllDefsListForReading)
		{
			if (item.Worker.overlays == null)
			{
				continue;
			}
			foreach (SkyOverlay overlay in item.Worker.overlays)
			{
				if (weatherOverlayEffectsType.IsInstanceOfType(overlay))
				{
					if (weatherOverlayEffectsNextDamageTickForMapField != null)
					{
						weatherOverlayEffectsNextDamageTickForMapField(overlay).Clear();
					}
					else
					{
						weatherOverlayEffectsNextDamageTickField(overlay) = 0;
					}
				}
			}
		}
	}

	private static void PatchCooking()
	{
		thoughtHediffType = AccessTools.TypeByName("VEF.Cooking.Thought_Hediff");
		if (thoughtHediffType != null)
		{
			if (DefDatabase<ThoughtDef>.AllDefsListForReading.Any((ThoughtDef def) => thoughtHediffType.IsAssignableFrom(def.thoughtClass)))
			{
				PatchingUtilities.PatchTryGainMemory(TryGainThoughtHediff);
			}
		}
		else
		{
			Log.Error("MPCompat :: Trying to patch `VEF.Cooking.Thought_Hediff`, but the type is null. Did it get moved, renamed, or removed?");
		}
	}

	private static bool TryGainThoughtHediff(Thought_Memory thought)
	{
		if (!thoughtHediffType.IsInstanceOfType(thought))
		{
			return false;
		}
		thought.MoodOffset();
		return true;
	}

	private static void PatchWorkGiverDeliverResources()
	{
		MethodInfo methodInfo;
		MethodInfo methodInfo2;
		try
		{
			methodInfo = AccessTools.DeclaredMethod("RimWorld.WorkGiver_ConstructDeliverResourcesToBlueprints:NoCostFrameMakeJobFor");
			if (methodInfo == null)
			{
				throw new MissingMethodException("Could not access method: RimWorld.WorkGiver_ConstructDeliverResourcesToBlueprints:NoCostFrameMakeJobFor");
			}
			methodInfo2 = AccessTools.DeclaredMethod("Multiplayer.Client.OnlyConstructorsPlaceNoCostFrames:IsConstruction");
			if (methodInfo2 == null)
			{
				throw new MissingMethodException("Could not access method: Multiplayer.Client.OnlyConstructorsPlaceNoCostFrames:IsConstruction");
			}
			thingDefExtensionType = AccessTools.TypeByName("VEF.Things.ThingDefExtension");
			thingDefExtensionConstructionSkillRequirementField = AccessTools.FieldRefAccess<object>(thingDefExtensionType, "constructionSkillRequirement");
			constructionSkillRequirementWorkTypeField = AccessTools.FieldRefAccess<WorkTypeDef>("VEF.Pawns.ConstructionSkillRequirement:workType");
		}
		catch (Exception)
		{
			thingDefExtensionType = null;
			thingDefExtensionConstructionSkillRequirementField = null;
			constructionSkillRequirementWorkTypeField = null;
			throw;
		}
		MpCompat.harmony.Patch(methodInfo, new HarmonyMethod(typeof(VanillaExpandedFramework), "PreNoCostFrameMakeJobFor"));
		MpCompat.harmony.Patch(methodInfo2, null, new HarmonyMethod(typeof(VanillaExpandedFramework), "PostIsConstruction"));
	}

	private static void PreNoCostFrameMakeJobFor(IConstructible c)
	{
		lastThing = c;
	}

	private static void PostIsConstruction(WorkGiver w, ref bool __result)
	{
		if (__result || !(lastThing is Thing { def: var def } thing) || def?.entityDefToBuild?.modExtensions == null)
		{
			lastThing = null;
			return;
		}
		foreach (DefModExtension modExtension in thing.def.entityDefToBuild.modExtensions)
		{
			if (modExtension != null && thingDefExtensionType.IsInstanceOfType(modExtension))
			{
				object obj = thingDefExtensionConstructionSkillRequirementField(modExtension);
				if (obj != null)
				{
					WorkTypeDef workTypeDef = constructionSkillRequirementWorkTypeField(obj);
					__result = w.def.workType == workTypeDef;
				}
				break;
			}
		}
		lastThing = null;
	}

	private static void PatchGraphicCustomizationDialog()
	{
		Type type = (graphicCustomizationDialogType = AccessTools.TypeByName("VEF.Graphics.Dialog_GraphicCustomization"));
		graphicCustomizationCompField = AccessTools.FieldRefAccess<ThingComp>(type, "comp");
		graphicCustomizationGeneratedNamesCompField = AccessTools.FieldRefAccess<ThingComp>(type, "compGeneratedName");
		graphicCustomizationPawnField = AccessTools.FieldRefAccess<Pawn>(type, "pawn");
		graphicCustomizationCurrentVariantsField = AccessTools.FieldRefAccess<IList>(type, "currentVariants");
		graphicCustomizationCurrentNameField = AccessTools.FieldRefAccess<string>(type, "currentName");
		MpCompat.RegisterLambdaMethod(type, "DoWindowContents", default(int));
		MP.RegisterSyncWorker<Window>(SyncGraphicCustomizationDialog, type, isImplicit: true);
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, "DrawConfirmButton"), new HarmonyMethod(CloseGraphicCustomizationDialogOnAccept));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, "DoWindowContents"), new HarmonyMethod(new Action<Window, ThingComp, Pawn>(CloseGraphicCustomizationWhenNoLongerValid)));
		type = AccessTools.TypeByName("VEF.Graphics.TextureVariant");
		variantsListType = typeof(List<>).MakeGenericType(type);
		textureVariantTexNameField = AccessTools.FieldRefAccess<string>(type, "texName");
		textureVariantTextureField = AccessTools.FieldRefAccess<string>(type, "texture");
		textureVariantOutlineField = AccessTools.FieldRefAccess<string>(type, "outline");
		textureVariantChanceOverrideField = AccessTools.FieldRefAccess<float>(type, "chanceOverride");
		textureVariantTextureVariantOverrideField = AccessTools.FieldRefAccess<object>(type, "textureVariantOverride");
		MP.RegisterSyncWorker<object>(SyncTextureVariant, type, isImplicit: false, shouldConstruct: true);
		textureVariantOverrideType = (type = AccessTools.TypeByName("VEF.Graphics.TextureVariantOverride"));
		textureVariantOverrideChanceField = AccessTools.FieldRefAccess<float>(type, "chance");
		textureVariantOverrideGroupNameField = AccessTools.FieldRefAccess<string>(type, "groupName");
		textureVariantOverrideTexNameField = AccessTools.FieldRefAccess<string>(type, "texName");
		MP.RegisterSyncWorker<object>(SyncTextureVariantOverride, type, isImplicit: false, shouldConstruct: true);
	}

	private static void CloseGraphicCustomizationDialogOnAccept(Window __instance, ref Action action)
	{
		Action b = action;
		action = (Action)Delegate.Combine((Action)(() =>
		{
			__instance.Close();
		}), b);
	}

	private static void CloseGraphicCustomizationWhenNoLongerValid(Window __instance, ThingComp ___comp, Pawn ___pawn)
	{
		if (MP.IsInMultiplayer && !IsGraphicCustomizationDialogValid(___comp, ___pawn))
		{
			__instance.Close();
		}
	}

	private static bool CancelGraphicCustomizationExecutionIfNoLongerValid(ThingComp ___comp, Pawn ___pawn)
	{
		if (MP.IsInMultiplayer)
		{
			return IsGraphicCustomizationDialogValid(___comp, ___pawn);
		}
		return true;
	}

	private static bool IsGraphicCustomizationDialogValid(ThingComp comp, Pawn pawn)
	{
		if (comp.parent != null && pawn != null && comp.parent.Spawned && pawn.Spawned && !comp.parent.Destroyed && !pawn.Destroyed && !pawn.Dead)
		{
			return comp.parent.Map == pawn.Map;
		}
		return false;
	}

	private static void SyncGraphicCustomizationDialog(SyncWorker sync, ref Window dialog)
	{
		ThingComp thingComp = null;
		if (sync.isWriting)
		{
			sync.Write(graphicCustomizationCompField(dialog));
		}
		else
		{
			dialog = (Window)RuntimeHelpers.GetUninitializedObject(graphicCustomizationDialogType);
			thingComp = sync.Read<ThingComp>();
			graphicCustomizationCompField(dialog) = thingComp;
		}
		SyncGraphicCustomizationDialog(sync, ref dialog, thingComp);
	}

	internal static void SyncGraphicCustomizationDialog(SyncWorker sync, ref Window dialog, ThingComp graphicCustomizationComp)
	{
		if (sync.isWriting)
		{
			sync.Write(graphicCustomizationPawnField(dialog));
			sync.Write(graphicCustomizationCurrentVariantsField(dialog), variantsListType);
			sync.Write(graphicCustomizationCurrentNameField(dialog));
		}
		else
		{
			graphicCustomizationGeneratedNamesCompField(dialog) = graphicCustomizationComp?.parent.GetComp<CompGeneratedNames>();
			graphicCustomizationPawnField(dialog) = sync.Read<Pawn>();
			graphicCustomizationCurrentVariantsField(dialog) = sync.Read<IList>(variantsListType);
			graphicCustomizationCurrentNameField(dialog) = sync.Read<string>();
		}
	}

	private static void SyncTextureVariant(SyncWorker sync, ref object variant)
	{
		if (sync.isWriting)
		{
			if (variant == null)
			{
				sync.Write(obj: false);
				return;
			}
			sync.Write(obj: true);
			sync.Write(textureVariantTexNameField(variant));
			sync.Write(textureVariantTextureField(variant));
			sync.Write(textureVariantOutlineField(variant));
			sync.Write(textureVariantTextureVariantOverrideField(variant), textureVariantOverrideType);
			sync.Write(textureVariantChanceOverrideField(variant));
		}
		else if (sync.Read<bool>())
		{
			textureVariantTexNameField(variant) = sync.Read<string>();
			textureVariantTextureField(variant) = sync.Read<string>();
			textureVariantOutlineField(variant) = sync.Read<string>();
			textureVariantTextureVariantOverrideField(variant) = sync.Read<object>(textureVariantOverrideType);
			textureVariantChanceOverrideField(variant) = sync.Read<float>();
		}
	}

	private static void SyncTextureVariantOverride(SyncWorker sync, ref object variantOverride)
	{
		if (sync.isWriting)
		{
			if (variantOverride == null)
			{
				sync.Write(obj: false);
				return;
			}
			sync.Write(obj: true);
			sync.Write(textureVariantOverrideChanceField(variantOverride));
			sync.Write(textureVariantOverrideGroupNameField(variantOverride));
			sync.Write(textureVariantOverrideTexNameField(variantOverride));
		}
		else if (sync.Read<bool>())
		{
			textureVariantOverrideChanceField(variantOverride) = sync.Read<float>();
			textureVariantOverrideGroupNameField(variantOverride) = sync.Read<string>();
			textureVariantOverrideTexNameField(variantOverride) = sync.Read<string>();
		}
	}

	private static void PatchDraftedAi()
	{
		getDraftedActionDataMethod = HarmonyLib.MethodInvoker.GetHandler(AccessTools.DeclaredMethod("VEF.AI.DraftedActionHolder:GetData"));
		draftedActionDataGetPawnMethod = HarmonyLib.MethodInvoker.GetHandler(AccessTools.DeclaredPropertyGetter("VEF.AI.DraftedActionData:Pawn"));
		Type type = AccessTools.TypeByName("VEF.AI.DraftedActionData");
		MP.RegisterSyncMethod(type, "ToggleHuntMode");
		MP.RegisterSyncMethod(type, "ToggleAutoForAll");
		MP.RegisterSyncMethod(type, "ToggleAutoCastFor");
		MP.RegisterSyncWorker<object>(SyncDraftedActionData, type);
	}

	private static void SyncDraftedActionData(SyncWorker sync, ref object draftedActionData)
	{
		if (sync.isWriting)
		{
			if (draftedActionData == null)
			{
				sync.Write<Pawn>(null);
			}
			else
			{
				sync.Write((Pawn)draftedActionDataGetPawnMethod(draftedActionData));
			}
			return;
		}
		Pawn pawn = sync.Read<Pawn>();
		if (pawn != null)
		{
			getDraftedActionDataMethod(null, pawn);
		}
	}

	private static void PatchMapObjectGeneration()
	{
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod("VEF.Maps.VanillaExpandedFramework_MapGenerator_GenerateMap_Patch:DoMapSpawns"), new HarmonyMethod(new Action<Map>(PreDoSpawns)), null, null, new HarmonyMethod(new Action(PostDoSpawns)));
	}

	private static void PreDoSpawns(Map map)
	{
		if (MP.IsInMultiplayer)
		{
			Rand.PushState(Gen.HashCombineInt(Gen.HashCombineInt(map.uniqueID, map.Tile), Find.TickManager.TicksGame));
		}
	}

	private static void PostDoSpawns()
	{
		if (MP.IsInMultiplayer)
		{
			Rand.PopState();
		}
	}

	private static void PatchQuestChainsDevMode()
	{
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod("VEF.Storyteller.QuestChainsDevWindow:ViewQuestChains"), new HarmonyMethod(new Func<bool>(CancelDevModeWindowIfNotExecutingSelfCommand)));
		Type type = AccessTools.TypeByName("VEF.Storyteller.GameComponent_QuestChains");
		questChainsGameCompInstanceField = AccessTools.StaticFieldRefAccess<GameComponent>(AccessTools.DeclaredField(type, "Instance"));
		questChainsGameCompQuestsField = AccessTools.FieldRefAccess<IList>(type, "quests");
		questChainsGameCompFutureQuestsField = AccessTools.FieldRefAccess<IList>(type, "futureQuests");
		questInfoQuestGetter = HarmonyLib.MethodInvoker.GetHandler(AccessTools.DeclaredPropertyGetter("VEF.Storyteller.QuestInfo:Quest"));
		futureQuestInfoQuestDefField = AccessTools.FieldRefAccess<QuestScriptDef>("VEF.Storyteller.FutureQuestInfo:questDef");
		MP.RegisterSyncMethod(MpMethodUtil.MethodOf(new Action<Quest, QuestEndOutcome>(SyncedQuestChainDevForceEnd))).CancelIfAnyArgNull().SetDebugOnly();
		MP.RegisterSyncMethod(MpMethodUtil.MethodOf(new Action<QuestScriptDef, int>(SyncedQuestChainDevFireNow))).CancelIfAnyArgNull().SetDebugOnly();
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod("VEF.Storyteller.QuestChainsDevWindow:DrawQuestInfo"), null, null, new HarmonyMethod(new Func<IEnumerable<CodeInstruction>, MethodBase, IEnumerable<CodeInstruction>>(ReplaceQuestChainsWindowSuccessFailButtons)));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod("VEF.Storyteller.QuestChainsDevWindow:DrawFutureQuestInfo"), null, null, new HarmonyMethod(new Func<IEnumerable<CodeInstruction>, MethodBase, IEnumerable<CodeInstruction>>(ReplaceQuestChainsWindowFireNowButton)));
	}

	private static bool CancelDevModeWindowIfNotExecutingSelfCommand()
	{
		if (MP.IsInMultiplayer && MP.IsExecutingSyncCommand)
		{
			return MP.IsExecutingSyncCommandIssuedBySelf;
		}
		return true;
	}

	private static IEnumerable<CodeInstruction> ReplaceQuestChainsWindowSuccessFailButtons(IEnumerable<CodeInstruction> instr, MethodBase baseMethod)
	{
		MethodInfo methodInfo = AccessTools.DeclaredMethod(typeof(Widgets), "ButtonText", new Type[6]
		{
			typeof(Rect),
			typeof(string),
			typeof(bool),
			typeof(bool),
			typeof(bool),
			typeof(TextAnchor?)
		});
		MethodInfo to = MpMethodUtil.MethodOf(new Func<Rect, string, bool, bool, bool, TextAnchor?, object, bool>(ReplacedForceSuccessButton));
		MethodInfo to2 = MpMethodUtil.MethodOf(new Func<Rect, string, bool, bool, bool, TextAnchor?, object, bool>(ReplacedForceFailureButton));
		return instr.ReplaceMethod(methodInfo, to, baseMethod, ExtraInstructions, null, 1, "Force Success").ReplaceMethod(methodInfo, to2, baseMethod, ExtraInstructions, null, 1, "Force Fail");
		static IEnumerable<CodeInstruction> ExtraInstructions(CodeInstruction _)
		{
			return new _003C_003Ez__ReadOnlySingleElementList<CodeInstruction>(new CodeInstruction(OpCodes.Ldarg_2));
		}
	}

	private static IEnumerable<CodeInstruction> ReplaceQuestChainsWindowFireNowButton(IEnumerable<CodeInstruction> instr, MethodBase baseMethod)
	{
		MethodInfo methodInfo = AccessTools.DeclaredMethod(typeof(Widgets), "ButtonText", new Type[6]
		{
			typeof(Rect),
			typeof(string),
			typeof(bool),
			typeof(bool),
			typeof(bool),
			typeof(TextAnchor?)
		});
		MethodInfo to = MpMethodUtil.MethodOf(new Func<Rect, string, bool, bool, bool, TextAnchor?, object, bool>(ReplacedFireNowButton));
		return instr.ReplaceMethod(methodInfo, to, baseMethod, ExtraInstructions, null, 1, "Fire Now");
		static IEnumerable<CodeInstruction> ExtraInstructions(CodeInstruction _)
		{
			return new _003C_003Ez__ReadOnlySingleElementList<CodeInstruction>(new CodeInstruction(OpCodes.Ldarg_2));
		}
	}

	private static bool ReplacedForceSuccessButton(Rect rect, string label, bool drawBackground, bool doMouseoverSound, bool active, TextAnchor? overrideTextAnchor, object questInfo)
	{
		bool flag = Widgets.ButtonText(rect, label, drawBackground, doMouseoverSound, active, overrideTextAnchor);
		if (!MP.IsInMultiplayer || !flag)
		{
			return flag;
		}
		SyncedQuestChainDevForceEnd((Quest)questInfoQuestGetter(questInfo), QuestEndOutcome.Success);
		return false;
	}

	private static bool ReplacedForceFailureButton(Rect rect, string label, bool drawBackground, bool doMouseoverSound, bool active, TextAnchor? overrideTextAnchor, object questInfo)
	{
		bool flag = Widgets.ButtonText(rect, label, drawBackground, doMouseoverSound, active, overrideTextAnchor);
		if (!MP.IsInMultiplayer || !flag)
		{
			return flag;
		}
		SyncedQuestChainDevForceEnd((Quest)questInfoQuestGetter(questInfo), QuestEndOutcome.Fail);
		return false;
	}

	private static bool ReplacedFireNowButton(Rect rect, string label, bool drawBackground, bool doMouseoverSound, bool active, TextAnchor? overrideTextAnchor, object futureQuestInfo)
	{
		bool flag = Widgets.ButtonText(rect, label, drawBackground, doMouseoverSound, active, overrideTextAnchor);
		if (!MP.IsInMultiplayer || !flag)
		{
			return flag;
		}
		QuestScriptDef def = futureQuestInfoQuestDefField(futureQuestInfo);
		int index = questChainsGameCompFutureQuestsField(questChainsGameCompInstanceField()).IndexOf(futureQuestInfo);
		SyncedQuestChainDevFireNow(def, index);
		return false;
	}

	private static void SyncedQuestChainDevForceEnd(Quest quest, QuestEndOutcome outcome)
	{
		if (quest.State != QuestState.Ongoing)
		{
			return;
		}
		foreach (object item in questChainsGameCompQuestsField(questChainsGameCompInstanceField()))
		{
			if (questInfoQuestGetter(item) == quest)
			{
				quest.End(outcome, sendLetter: false);
				return;
			}
		}
		if (MP.IsExecutingSyncCommandIssuedBySelf)
		{
			Log.Error($"MPCompat :: Trying to find a quest chain's QuestInfo for quest with ID {quest.id} and name {quest.name}, but there was no matching QuestInfo. This should not happen.");
		}
	}

	private static void SyncedQuestChainDevFireNow(QuestScriptDef def, int index)
	{
		if (index < 0)
		{
			if (MP.IsExecutingSyncCommandIssuedBySelf)
			{
				Log.Error($"MPCompat :: Failed syncing FutureQuestInfo, index too small: index={index}, def={def}");
			}
			return;
		}
		IList list = questChainsGameCompFutureQuestsField(questChainsGameCompInstanceField());
		if (index >= list.Count)
		{
			if (MP.IsExecutingSyncCommandIssuedBySelf)
			{
				Log.Error($"MPCompat :: Failed syncing FutureQuestInfo, index too high: index={index}, count={list.Count}, def={def}");
			}
			return;
		}
		object obj = list[index];
		QuestScriptDef questScriptDef = futureQuestInfoQuestDefField(obj);
		if (questScriptDef != def)
		{
			if (MP.IsExecutingSyncCommandIssuedBySelf)
			{
				Log.Error($"MPCompat :: Failed syncing FutureQuestInfo, QuestScriptDef mismatch: index={index}, count={list.Count}, syncedDef={def}, localDef={questScriptDef}");
			}
		}
		else
		{
			Quest quest = QuestUtility.GenerateQuestAndMakeAvailable(def, StorytellerUtility.DefaultThreatPointsNow(Find.World));
			if (def.sendAvailableLetter)
			{
				QuestUtility.SendLetterQuestAvailable(quest);
			}
			list.Remove(obj);
		}
	}

	private static void PatchMovingBases()
	{
		Type type = AccessTools.TypeByName("VEF.Planet.MovingBase");
		MP.RegisterSyncMethod(type, "Attack");
		FieldInfo fieldInfo = AccessTools.DeclaredField(type, "AttackCommand");
		if (fieldInfo == null)
		{
			Log.Warning("MPCompat :: VEF.Planet.MovingBase:AttackCommand does not exist, temporary patch needs to be updated or removed");
		}
		else if (!fieldInfo.IsStatic)
		{
			Log.Warning("MPCompat :: VEF.Planet.MovingBase:AttackCommand is not static, temporary patch needs to be updated or removed");
		}
		else if (fieldInfo.FieldType != typeof(Texture2D))
		{
			Log.Warning("MPCompat :: VEF.Planet.MovingBase:AttackCommand is not Texture2D, temporary patch needs to be updated or removed");
		}
		else if (fieldInfo.GetValue(null) != null)
		{
			Log.Message("MPCompat :: VEF.Planet.MovingBase:AttackCommand is not null, temporary patch is no longer needed - if the graphic issue fix is included in MP itself");
		}
		else
		{
			fieldInfo.SetValue(null, ContentFinder<Texture2D>.Get("UI/Commands/AttackSettlement"));
		}
	}
}
