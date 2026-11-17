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

namespace Multiplayer.Compat;

[MpCompatFor("VanillaExpanded.VPsycastsE")]
public class VanillaPsycastsExpanded
{
	private sealed class FocusClock
	{
		public Map map;
	}

	private class PsysetRenameHolder(IRenameable psyset, Hediff hediff) : IRenameable
	{
		public readonly IRenameable psyset = psyset;

		public readonly Hediff hediff = hediff;

		public string RenamableLabel
		{
			get
			{
				return psyset?.RenamableLabel ?? string.Empty;
			}
			set
			{
				if (psyset != null)
				{
					psyset.RenamableLabel = value;
				}
			}
		}

		public string BaseLabel => psyset?.BaseLabel ?? string.Empty;

		public string InspectLabel => psyset?.InspectLabel ?? string.Empty;
	}

	private class Dialog_RenamePsysetMp : Dialog_Rename<PsysetDraftRename>
	{
		public Dialog_RenamePsysetMp(IRenameable psyset, Hediff hediff)
			: base(new PsysetDraftRename(psyset, hediff))
		{
		}
	}

	private sealed class PsysetSnapshot
	{
		internal string[] Names;

		internal Def[][] Abilities;

		internal static PsysetSnapshot Capture(IList list)
		{
			return new PsysetSnapshot
			{
				Names = (from object set in list
					select psysetNameField(set)).ToArray(),
				Abilities = list.Cast<object>().Select(ReadPsysetAbilities).ToArray()
			};
		}
	}

	private sealed class PsysetDraft
	{
		internal Hediff Hediff;

		internal object Value;

		internal int Index;

		internal PsysetSnapshot Expected;
	}

	private sealed class PsysetDraftRename : IRenameable
	{
		private readonly IRenameable source;

		private readonly Hediff hediff;

		private readonly PsysetSnapshot expected;

		private readonly int index;

		public string RenamableLabel
		{
			get
			{
				return source?.RenamableLabel ?? string.Empty;
			}
			set
			{
				if (!MP.IsInMultiplayer)
				{
					if (source != null)
					{
						source.RenamableLabel = value;
					}
				}
				else if (expected != null && index >= 0)
				{
					ApplyPsysetChange(hediff, 3, index, expected.Names, expected.Abilities, value, null);
				}
			}
		}

		public string BaseLabel => source?.BaseLabel ?? string.Empty;

		public string InspectLabel => source?.InspectLabel ?? string.Empty;

		internal PsysetDraftRename(IRenameable source, Hediff hediff)
		{
			this.source = source;
			this.hediff = hediff;
			if (MP.IsInMultiplayer && hediff != null)
			{
				IList list = hediffPsysetsList(hediff);
				index = list.IndexOf(source);
				expected = PsysetSnapshot.Capture(list);
			}
		}
	}

	private static Type overlayHediffType;

	private static Type blizzardHediffType;

	private static FastInvokeHandler getPsycastsManager;

	private static AccessTools.FieldRef<object, IList> overlayList;

	private static AccessTools.FieldRef<object, IList> blizzardList;

	private static FastInvokeHandler generateFocusInfo;

	private static FastInvokeHandler modifiedFocus;

	private static FastInvokeHandler focusInfoPawn;

	private static FastInvokeHandler focusInfoExpiry;

	private static Func<Pawn, Map> focusClockMap;

	private static readonly ConditionalWeakTable<object, FocusClock> focusClocks = new ConditionalWeakTable<object, FocusClock>();

	private static ISyncField syncPsychicEntropyLimit;

	private static ISyncField syncPsychicEntropyTargetFocus;

	private static AccessTools.FieldRef<object, Pawn_PsychicEntropyTracker> psychicEntropyGetter;

	private static FastInvokeHandler removePsysetMethod;

	private static AccessTools.FieldRef<object, IList> hediffPsysetsList;

	private static Type psysetType;

	private static AccessTools.FieldRef<object, IEnumerable> psysetAbilitiesField;

	private static AccessTools.FieldRef<object, string> psysetNameField;

	private static AccessTools.FieldRef<object, object> dialogPsysetPsysetField;

	private static AccessTools.FieldRef<object, Hediff> dialogPsysetHediffField;

	private static AccessTools.FieldRef<object, Window> innerClassDialogPsysetField;

	private static Type abilityDefHashSetType;

	private static FastInvokeHandler abilityDefHashSetAddMethod;

	private static Type createPsyringDialogType;

	private static AccessTools.FieldRef<object, Pawn> createPsyringPawnField;

	private static AccessTools.FieldRef<object, Thing> createPsyringFuelField;

	private static AccessTools.FieldRef<object, object> commandAbilityFieldRef;

	private static Type iAbilityToggleType;

	private static PropertyInfo toggleToggleProperty;

	private static AccessTools.FieldRef<object, Pawn> abilityPawnFieldRef;

	private static AccessTools.FieldRef<object, object> abilityDefFieldRef;

	private static Type compAbilitiesLocalType;

	private static AccessTools.FieldRef<object, IEnumerable> learnedAbilitiesLocalField;

	private static AccessTools.FieldRef<bool> killskipMeleeMultiplier;

	private const int CreatePsyset = 0;

	private const int EditPsyset = 1;

	private const int RemovePsyset = 2;

	private const int RenamePsyset = 3;

	private static readonly ConditionalWeakTable<Window, PsysetDraft> PsysetDrafts = new ConditionalWeakTable<Window, PsysetDraft>();

	public VanillaPsycastsExpanded(ModContentPack mod)
	{
		(Action, string, bool)[] array = new (Action, string, bool)[13]
		{
			(InitializeCommonData, "Shared patch data", false),
			(PatchPsysets, "Psysets", false),
			(PatchPsyringDialog, "Psyring", false),
			(RegisterSyncGizmos, "General gizmos", true),
			(PatchAbilityToggle, "Ability toggle gizmo", true),
			(PatchPsychicStatusGizmo, "Psychic status gizmo", true),
			(PatchPsycasterHediff, "Skill point usage", true),
			(PatchMotesAndFlecks, "Motes and flecks", true),
			(PatchITab, "Psychic abilities tab transpiler (psysets)", true),
			(PatchCorpseTargetValidation, "Corpse ability execution validation", true),
			(PatchAsyncEffectClocks, "Map effects and focus cache ownership", true),
			(PatchKillskipMeleeScope, "Killskip melee multiplier ownership", true),
			(PatchOverlayMapOwnership, "Moving overlay map ownership", true)
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
					Log.Error("Encountered an error patching " + componentName + " part of Vanilla Expanded Framework - this part of the mod may not work properly!");
					Log.Error(ex.ToString());
				}
			}
		}
	}

	private static void PatchOverlayMapOwnership()
	{
		overlayHediffType = AccessTools.TypeByName("VanillaPsycastsExpanded.Hediff_Overlay");
		blizzardHediffType = AccessTools.TypeByName("VanillaPsycastsExpanded.Hediff_BlizzardSource");
		Type type = AccessTools.TypeByName("VanillaPsycastsExpanded.MapComponent_PsycastsManager");
		getPsycastsManager = HarmonyLib.MethodInvoker.GetHandler(AccessTools.FirstMethod(typeof(Map), (MethodInfo method) => method.Name == "GetComponent" && method.IsGenericMethodDefinition && method.GetParameters().Length == 0).MakeGenericMethod(type));
		overlayList = AccessTools.FieldRefAccess<IList>(type, "hediffsToDraw");
		blizzardList = AccessTools.FieldRefAccess<IList>(type, "blizzardSources");
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(Pawn), "DeSpawn"), new HarmonyMethod(typeof(VanillaPsycastsExpanded), "BeforeOverlayDespawn"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(Pawn), "SpawnSetup"), null, new HarmonyMethod(typeof(VanillaPsycastsExpanded), "AfterOverlaySpawn"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(blizzardHediffType, "PostRemoved"), null, null, new HarmonyMethod(typeof(VanillaPsycastsExpanded), "UseSafeBlizzardRemoval"));
	}

	private static void BeforeOverlayDespawn(Pawn __instance)
	{
		if (!MP.IsInMultiplayer || __instance.Map == null || __instance.health?.hediffSet?.hediffs == null)
		{
			return;
		}
		object instance = getPsycastsManager(__instance.Map);
		foreach (Hediff hediff in __instance.health.hediffSet.hediffs)
		{
			if (overlayHediffType.IsInstanceOfType(hediff))
			{
				overlayList(instance).Remove(hediff);
				if (blizzardHediffType.IsInstanceOfType(hediff))
				{
					blizzardList(instance).Remove(hediff);
				}
			}
		}
	}

	private static void AfterOverlaySpawn(Pawn __instance)
	{
		if (!MP.IsInMultiplayer || __instance.Map == null || __instance.health?.hediffSet?.hediffs == null)
		{
			return;
		}
		object instance = getPsycastsManager(__instance.Map);
		foreach (Hediff hediff in __instance.health.hediffSet.hediffs)
		{
			if (overlayHediffType.IsInstanceOfType(hediff))
			{
				if (!overlayList(instance).Contains(hediff))
				{
					overlayList(instance).Add(hediff);
				}
				if (blizzardHediffType.IsInstanceOfType(hediff) && !blizzardList(instance).Contains(hediff))
				{
					blizzardList(instance).Add(hediff);
				}
			}
		}
	}

	private static IEnumerable<CodeInstruction> UseSafeBlizzardRemoval(IEnumerable<CodeInstruction> instructions)
	{
		List<CodeInstruction> list = instructions.ToList();
		FieldInfo list2 = AccessTools.DeclaredField("VanillaPsycastsExpanded.MapComponent_PsycastsManager:blizzardSources");
		int num = list.FindIndex((CodeInstruction instruction) => instruction.LoadsField(list2));
		if (num < 4 || num + 3 >= list.Count || GenCollection.Count(list, (CodeInstruction instruction) => instruction.LoadsField(list2)) != 1 || list[num - 4].opcode != OpCodes.Ldarg_0 || !list[num - 3].LoadsField(AccessTools.Field(typeof(Hediff), "pawn")) || !list[num - 2].Calls(AccessTools.PropertyGetter(typeof(Thing), "Map")) || !(list[num - 1].operand is MethodInfo methodInfo) || methodInfo.Name != "GetComponent" || list[num + 1].opcode != OpCodes.Ldarg_0 || !(list[num + 2].operand is MethodInfo methodInfo2) || methodInfo2.Name != "Remove" || list[num + 3].opcode != OpCodes.Pop)
		{
			throw new InvalidOperationException("VPE blizzard removal contract changed");
		}
		int num2 = num - 4;
		if (list.Skip(num2 + 1).Take(7).Any((CodeInstruction instruction) => instruction.labels.Count != 0) || list.Skip(num2).Take(8).Any((CodeInstruction instruction) => instruction.blocks.Count != 0))
		{
			throw new InvalidOperationException("VPE blizzard removal block boundaries changed");
		}
		list[num2].opcode = OpCodes.Ldarg_0;
		list[num2].operand = null;
		list.RemoveRange(num2 + 1, 7);
		list.Insert(num2 + 1, new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(VanillaPsycastsExpanded), "RemoveBlizzardRegistration")));
		return list;
	}

	private static void RemoveBlizzardRegistration(Hediff hediff)
	{
		if (!MP.IsInMultiplayer)
		{
			blizzardList(getPsycastsManager(hediff.pawn.Map)).Remove(hediff);
			return;
		}
		foreach (Map map in Find.Maps)
		{
			blizzardList(getPsycastsManager(map)).Remove(hediff);
		}
	}

	private static void PatchAsyncEffectClocks()
	{
		PatchingUtilities.SetupAsyncTime();
		VanillaExpandedFramework.PatchWorldClock(AccessTools.DeclaredMethod("VanillaPsycastsExpanded.TradeDeal_TryExecute_Patch:Postfix"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod("VanillaPsycastsExpanded.GameComponent_PsycastsManager:GameComponentTick"), null, null, new HarmonyMethod(typeof(VanillaPsycastsExpanded), "UseBrambleClock"));
		Type type = AccessTools.TypeByName("VanillaPsycastsExpanded.NearbyFocusBoostCachingUtility");
		Type type2 = AccessTools.Inner(type, "CachedFocusInfo");
		generateFocusInfo = HarmonyLib.MethodInvoker.GetHandler(AccessTools.DeclaredMethod(type2, "GenerateCacheInfoFor"));
		modifiedFocus = HarmonyLib.MethodInvoker.GetHandler(AccessTools.PropertyGetter(type2, "ModifiedFocus"));
		focusInfoPawn = HarmonyLib.MethodInvoker.GetHandler(AccessTools.PropertyGetter(type2, "Pawn"));
		focusInfoExpiry = HarmonyLib.MethodInvoker.GetHandler(AccessTools.PropertyGetter(type2, "ExpirationTick"));
		focusClockMap = (Func<Pawn, Map>)AccessTools.DeclaredMethod("Multiplayer.Client.Patches.TimestampFixer:GetPawnClockMap").CreateDelegate(typeof(Func<Pawn, Map>));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, "GetOrCacheModifiedFociBoost"), new HarmonyMethod(typeof(VanillaPsycastsExpanded), "ReadFreshFocus"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type2, "GenerateCacheInfoFor"), new HarmonyMethod(typeof(VanillaPsycastsExpanded), "BeforeGenerateFocus"), new HarmonyMethod(typeof(VanillaPsycastsExpanded), "RecordFocusClock"), null, new HarmonyMethod(typeof(VanillaPsycastsExpanded), "RestoreFocusTimeSnapshot"));
		MpCompat.harmony.Patch(AccessTools.PropertyGetter(type2, "IsExpired"), new HarmonyMethod(typeof(VanillaPsycastsExpanded), "CheckFocusExpiry"));
		Type type3 = AccessTools.TypeByName("VanillaPsycastsExpanded.Ability_Killskip");
		AccessTools.FieldRef<object, int> attack = AccessTools.FieldRefAccess<int>(type3, "attackInTicks");
		PatchingUtilities.RegisterTimestampFields(type3, (object ability, int offset) =>
		{
			if (attack(ability) != -1)
			{
				int num = attack(ability) + offset;
				attack(ability) = ((num != -1) ? num : 0);
			}
		});
	}

	private static IEnumerable<CodeInstruction> UseBrambleClock(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
	{
		MethodInfo ticks = AccessTools.PropertyGetter(typeof(TickManager), "TicksGame");
		LocalVariableInfo localVariableInfo = __originalMethod.GetMethodBody().LocalVariables.Single((LocalVariableInfo local) => local.LocalType == typeof(Thing));
		List<CodeInstruction> list = instructions.ToList();
		(CodeInstruction, int)[] array = (from entry in list.Select((CodeInstruction instruction, int index) => (instruction: instruction, index: index))
			where entry.instruction.Calls(ticks)
			select entry).ToArray();
		if (array.Length != 2)
		{
			throw new InvalidOperationException("VPE bramble expiry clock contract changed");
		}
		CodeInstruction codeInstruction = list[array[1].Item2];
		codeInstruction.opcode = OpCodes.Ldloc;
		codeInstruction.operand = localVariableInfo.LocalIndex;
		CodeInstruction codeInstruction2 = new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(VanillaPsycastsExpanded), "ReadBrambleTicks"));
		for (int num = codeInstruction.blocks.Count - 1; num >= 0; num--)
		{
			if (codeInstruction.blocks[num].blockType == ExceptionBlockType.EndExceptionBlock)
			{
				codeInstruction2.blocks.Insert(0, codeInstruction.blocks[num]);
				codeInstruction.blocks.RemoveAt(num);
			}
		}
		list.Insert(array[1].Item2 + 1, codeInstruction2);
		return list;
	}

	private static int ReadBrambleTicks(TickManager manager, Thing thing)
	{
		if (!MP.IsInMultiplayer || !PatchingUtilities.IsAsyncTime || thing.MapHeld == null)
		{
			return manager.TicksGame;
		}
		PatchingUtilities.TimeSnapshot? andSetFromMap = PatchingUtilities.TimeSnapshot.GetAndSetFromMap(thing.MapHeld);
		try
		{
			return manager.TicksGame;
		}
		finally
		{
			andSetFromMap?.Set();
		}
	}

	private static bool ReadFreshFocus(Pawn pawn, Thing focus, float basePsyfocus, ref float __result)
	{
		if (!MP.InInterface)
		{
			return true;
		}
		__result = (float)modifiedFocus(generateFocusInfo(null, pawn, focus, basePsyfocus));
		return false;
	}

	private static void BeforeGenerateFocus(Pawn pawn, out PatchingUtilities.TimeSnapshot? __state)
	{
		__state = ((MP.IsInMultiplayer && PatchingUtilities.IsAsyncTime) ? PatchingUtilities.TimeSnapshot.GetAndSetFromPawn(pawn) : ((PatchingUtilities.TimeSnapshot?)null));
	}

	private static void RestoreFocusTimeSnapshot(PatchingUtilities.TimeSnapshot? __state)
	{
		__state?.Set();
	}

	private static void RecordFocusClock(object __result)
	{
		if (MP.IsInMultiplayer && !MP.InInterface)
		{
			focusClocks.Add(__result, new FocusClock
			{
				map = focusClockMap((Pawn)focusInfoPawn(__result))
			});
		}
	}

	private static bool CheckFocusExpiry(object __instance, ref bool __result)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		Pawn pawn = (Pawn)focusInfoPawn(__instance);
		if (!focusClocks.TryGetValue(__instance, out var value) || value.map != focusClockMap(pawn))
		{
			__result = true;
			return false;
		}
		PatchingUtilities.TimeSnapshot? timeSnapshot = (PatchingUtilities.IsAsyncTime ? PatchingUtilities.TimeSnapshot.GetAndSetFromPawn(pawn) : ((PatchingUtilities.TimeSnapshot?)null));
		try
		{
			__result = (int)focusInfoExpiry(__instance) < Find.TickManager.TicksGame;
		}
		finally
		{
			timeSnapshot?.Set();
		}
		return false;
	}

	private static void PatchCorpseTargetValidation()
	{
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod("VanillaPsycastsExpanded.Ability_SpawnSkeleton:Cast"), new HarmonyMethod(typeof(VanillaPsycastsExpanded), "BeforeSpawnSkeleton"));
	}

	private static bool BeforeSpawnSkeleton(object __instance, GlobalTargetInfo[] __0)
	{
		Pawn pawn = (Pawn)AccessTools.Field(__instance.GetType(), "pawn").GetValue(__instance);
		if (pawn?.Map == null || __0 == null || __0.Length == 0)
		{
			return false;
		}
		HashSet<Corpse> hashSet = new HashSet<Corpse>();
		foreach (GlobalTargetInfo globalTargetInfo in __0)
		{
			if (!(globalTargetInfo.Thing is Corpse { Destroyed: false, Spawned: not false } corpse) || corpse.Map != pawn.Map || !hashSet.Add(corpse))
			{
				return false;
			}
		}
		return true;
	}

	private static void InitializeCommonData()
	{
		syncPsychicEntropyLimit = (ISyncField)AccessTools.Field("Multiplayer.Client.SyncFields:SyncPsychicEntropyLimit").GetValue(null);
		syncPsychicEntropyTargetFocus = (ISyncField)AccessTools.Field("Multiplayer.Client.SyncFields:SyncPsychicEntropyTargetFocus").GetValue(null);
	}

	private static void PatchPsychicStatusGizmo()
	{
		Type type = AccessTools.TypeByName("VanillaPsycastsExpanded.UI.PsychicStatusGizmo");
		psychicEntropyGetter = AccessTools.FieldRefAccess<Pawn_PsychicEntropyTracker>(type, "tracker");
		MpCompat.harmony.Patch(AccessTools.Method(type, "GizmoOnGUI"), new HarmonyMethod(typeof(VanillaPsycastsExpanded), "PrePsyfocusTarget"), new HarmonyMethod(typeof(VanillaPsycastsExpanded), "PostPsyfocusTarget"));
	}

	private static void PrePsyfocusTarget(object __instance)
	{
		if (MP.IsInMultiplayer)
		{
			MP.WatchBegin();
			Pawn_PsychicEntropyTracker pawn_PsychicEntropyTracker = psychicEntropyGetter(__instance);
			if (pawn_PsychicEntropyTracker?.Pawn != null)
			{
				syncPsychicEntropyLimit.Watch(pawn_PsychicEntropyTracker.Pawn);
				syncPsychicEntropyTargetFocus.Watch(pawn_PsychicEntropyTracker.Pawn);
			}
		}
	}

	private static void PostPsyfocusTarget()
	{
		if (MP.IsInMultiplayer)
		{
			MP.WatchEnd();
		}
	}

	private static void PatchPsysets()
	{
		MP.RegisterSyncMethod(typeof(VanillaPsycastsExpanded), "SyncPsyset");
		MP.RegisterSyncMethod(typeof(VanillaPsycastsExpanded), "SyncRemovePsyset");
		MP.RegisterSyncMethod(typeof(VanillaPsycastsExpanded), "SyncEnsurePsysetExists");
		MP.RegisterSyncWorker<PsysetRenameHolder>(SyncPsysetRenameHolder);
		Type type = AccessTools.TypeByName("VEF.Abilities.AbilityDef");
		abilityDefHashSetType = typeof(HashSet<>).MakeGenericType(type);
		abilityDefHashSetAddMethod = HarmonyLib.MethodInvoker.GetHandler(AccessTools.Method(abilityDefHashSetType, "Add"));
		psysetType = AccessTools.TypeByName("VanillaPsycastsExpanded.PsySet");
		psysetAbilitiesField = AccessTools.FieldRefAccess<IEnumerable>(psysetType, "Abilities");
		psysetNameField = AccessTools.FieldRefAccess<string>(psysetType, "Name");
		RegisterPsysetChanges();
		Type type2 = AccessTools.TypeByName("VanillaPsycastsExpanded.UI.Dialog_Psyset");
		dialogPsysetPsysetField = AccessTools.FieldRefAccess<object>(type2, "psyset");
		dialogPsysetHediffField = AccessTools.FieldRefAccess<Hediff>(type2, "hediff");
		MpCompat.harmony.Patch(AccessTools.GetDeclaredConstructors(type2).FirstOrDefault(), null, new HarmonyMethod(typeof(VanillaPsycastsExpanded), "PostPsysetDialogCtor"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(Window), "PostClose"), null, new HarmonyMethod(typeof(VanillaPsycastsExpanded), "CommitPsysetDraft"));
	}

	private static void ReplacedRemovePsyset(Hediff hediff, object psyset)
	{
		if (!MP.IsInMultiplayer)
		{
			removePsysetMethod(hediff, psyset);
		}
		else
		{
			RequestPsysetChange(hediff, psyset, 2, null, null);
		}
	}

	private static void SyncRemovePsyset(Hediff hediff, int index)
	{
		if (hediff != null && index >= 0 && index < hediffPsysetsList(hediff).Count)
		{
			object obj = hediffPsysetsList(hediff)[index];
			removePsysetMethod(hediff, obj);
		}
	}

	private static void PostPsysetDialogCtor(Window __instance, object psyset, Hediff ___hediff)
	{
		BeginPsysetDraft(__instance, psyset, ___hediff);
	}

	private static void SyncEnsurePsysetExists(Hediff hediff, int count)
	{
		if (hediff != null && count > 0)
		{
			IList list = hediffPsysetsList(hediff);
			if (list.Count == count - 1)
			{
				object obj = Activator.CreateInstance(psysetType);
				psysetNameField(obj) = "VPE.Untitled".Translate();
				list.Add(obj);
			}
		}
	}

	private static void PrePsysetDoWindowContents(object ___psyset, ref int __state)
	{
		if (MP.IsInMultiplayer && ___psyset != null)
		{
			__state = psysetAbilitiesField(___psyset).EnumerableCount();
		}
	}

	private static void PostPsysetDoWindowContents(object ___psyset, Hediff ___hediff, ref int __state)
	{
		if (MP.IsInMultiplayer && ___psyset != null && ___hediff != null)
		{
			CheckMatch(in ___psyset, in ___hediff, in __state);
		}
	}

	private static void PrePsysetInnerClassMethod(object __instance, ref int __state)
	{
		if (MP.IsInMultiplayer)
		{
			Window instance = innerClassDialogPsysetField(__instance);
			object instance2 = dialogPsysetPsysetField(instance);
			__state = psysetAbilitiesField(instance2).EnumerableCount();
		}
	}

	private static void PostPsysetInnerClassMethod(object __instance, ref int __state)
	{
		if (MP.IsInMultiplayer)
		{
			Window instance = innerClassDialogPsysetField(__instance);
			object psyset = dialogPsysetPsysetField(instance);
			Hediff hediff = dialogPsysetHediffField(instance);
			CheckMatch(in psyset, in hediff, in __state);
		}
	}

	private static void CheckMatch(in object psyset, in Hediff hediff, in int count)
	{
		Def[] array = psysetAbilitiesField(psyset).Cast<Def>().ToArray();
		if (count != array.Length)
		{
			int num = hediffPsysetsList(hediff).IndexOf(psyset);
			if (num >= 0)
			{
				SyncPsyset(hediff, num, array);
			}
		}
	}

	private static void SyncPsyset(Hediff hediff, int psysetIndex, Def[] defs)
	{
		if (hediff != null && psysetIndex >= 0 && psysetIndex < hediffPsysetsList(hediff).Count && ValidPsysetAbilities(defs))
		{
			object instance = hediffPsysetsList(hediff)[psysetIndex];
			object obj = Activator.CreateInstance(abilityDefHashSetType);
			foreach (Def def in defs)
			{
				abilityDefHashSetAddMethod(obj, def);
			}
			psysetAbilitiesField(instance) = (IEnumerable)obj;
		}
	}

	private static IEnumerable<CodeInstruction> PatchPsycastsITab(IEnumerable<CodeInstruction> instr)
	{
		MethodInfo target = AccessTools.Method("VanillaPsycastsExpanded.Hediff_PsycastAbilities:RemovePsySet");
		MethodInfo replacement = AccessTools.Method(typeof(VanillaPsycastsExpanded), "ReplacedRemovePsyset");
		Type dialogRenameType = AccessTools.TypeByName("VanillaPsycastsExpanded.UI.Dialog_RenamePsyset");
		ConstructorInfo replacedDialogRenameType = AccessTools.DeclaredConstructor(typeof(Dialog_RenamePsysetMp), new Type[2]
		{
			typeof(IRenameable),
			typeof(Hediff)
		});
		FieldInfo originalHediffField = AccessTools.Field("VanillaPsycastsExpanded.UI.ITab_Pawn_Psycasts:hediff");
		MethodInfo addPsyset = AccessTools.DeclaredMethod(typeof(List<>).MakeGenericType(psysetType), "Add");
		int removes = 0;
		int renames = 0;
		int additions = 0;
		foreach (CodeInstruction ci in instr)
		{
			if (ci.opcode == OpCodes.Callvirt && ci.operand is MethodInfo methodInfo && methodInfo == target)
			{
				ci.opcode = OpCodes.Call;
				ci.operand = replacement;
				removes++;
			}
			else if (ci.opcode == OpCodes.Newobj && ci.operand is ConstructorInfo constructorInfo && constructorInfo.DeclaringType == dialogRenameType)
			{
				yield return new CodeInstruction(OpCodes.Ldarg_0);
				yield return new CodeInstruction(OpCodes.Ldfld, originalHediffField);
				ci.operand = replacedDialogRenameType;
				renames++;
			}
			else if (ci.Calls(addPsyset))
			{
				ci.opcode = OpCodes.Call;
				ci.operand = AccessTools.DeclaredMethod(typeof(VanillaPsycastsExpanded), "AddPsysetFromUi");
				additions++;
			}
			yield return ci;
		}
		if (removes != 1 || renames != 1 || additions != 1)
		{
			throw new InvalidOperationException($"VPE psyset UI contract changed: remove={removes}, rename={renames}, add={additions}");
		}
	}

	private static void SyncPsysetRenameHolder(SyncWorker sync, ref PsysetRenameHolder holder)
	{
		if (sync.isWriting)
		{
			sync.Write(holder.hediff);
			if (holder.hediff != null)
			{
				sync.Write(hediffPsysetsList(holder.hediff).IndexOf(holder.psyset));
			}
			return;
		}
		Hediff hediff = sync.Read<Hediff>();
		IRenameable psyset = null;
		if (hediff != null)
		{
			int num = sync.Read<int>();
			IList list = hediffPsysetsList(hediff);
			if (num >= 0 && num < list.Count)
			{
				psyset = list[num] as IRenameable;
			}
		}
		holder = new PsysetRenameHolder(psyset, hediff);
	}

	private static void PatchPsyringDialog()
	{
		createPsyringDialogType = AccessTools.TypeByName("VanillaPsycastsExpanded.Technomancer.Dialog_CreatePsyring");
		createPsyringPawnField = AccessTools.FieldRefAccess<Pawn>(createPsyringDialogType, "pawn");
		createPsyringFuelField = AccessTools.FieldRefAccess<Thing>(createPsyringDialogType, "fuel");
		MP.RegisterSyncWorker<object>(SyncDialogCreatePsyring, createPsyringDialogType);
		MP.RegisterSyncMethod(createPsyringDialogType, "Create");
		DialogUtilities.RegisterDialogCloseSync(createPsyringDialogType, addPauseLock: true);
	}

	private static void SyncDialogCreatePsyring(SyncWorker sync, ref object dialog)
	{
		if (sync.isWriting)
		{
			sync.Write(createPsyringPawnField(dialog));
			sync.Write(createPsyringFuelField(dialog));
			return;
		}
		Pawn pawn = sync.Read<Pawn>();
		Thing thing = sync.Read<Thing>();
		dialog = Activator.CreateInstance(createPsyringDialogType, pawn, thing, null);
	}

	private static void PatchPsycasterHediff()
	{
		Type type = AccessTools.TypeByName("VanillaPsycastsExpanded.Hediff_PsycastAbilities");
		removePsysetMethod = HarmonyLib.MethodInvoker.GetHandler(AccessTools.Method(type, "RemovePsySet"));
		hediffPsysetsList = AccessTools.FieldRefAccess<IList>(type, "psysets");
		MP.RegisterSyncMethod(type, "SpentPoints");
		MP.RegisterSyncMethod(type, "ImproveStats");
		MP.RegisterSyncMethod(type, "UnlockPath");
		MP.RegisterSyncMethod(type, "UnlockMeditationFocus");
		MP.RegisterSyncMethod(type, "GainExperience");
	}

	private static void PatchAbilityToggle()
	{
		Type type = AccessTools.TypeByName("VanillaPsycastsExpanded.Staticlord.Command_AbilityToggle");
		commandAbilityFieldRef = AccessTools.FieldRefAccess<object>(AccessTools.TypeByName("VEF.Abilities.Command_Ability"), "ability");
		iAbilityToggleType = AccessTools.TypeByName("VanillaPsycastsExpanded.Staticlord.IAbilityToggle");
		toggleToggleProperty = AccessTools.Property(iAbilityToggleType, "Toggle");
		Type type2 = AccessTools.TypeByName("VEF.Abilities.Ability");
		abilityPawnFieldRef = AccessTools.FieldRefAccess<Pawn>(type2, "pawn");
		abilityDefFieldRef = AccessTools.FieldRefAccess<object>(type2, "def");
		compAbilitiesLocalType = AccessTools.TypeByName("VEF.Abilities.CompAbilities");
		learnedAbilitiesLocalField = AccessTools.FieldRefAccess<IEnumerable>(compAbilitiesLocalType, "learnedAbilities");
		MP.RegisterSyncMethod(typeof(VanillaPsycastsExpanded), "SyncedAbilityToggle");
		MpCompat.harmony.Patch(AccessTools.Method(type, "ProcessInput"), new HarmonyMethod(typeof(VanillaPsycastsExpanded), "PreAbilityToggleProcessInput"));
	}

	private static bool PreAbilityToggleProcessInput(object __instance)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		object instance = commandAbilityFieldRef(__instance);
		Pawn pawn = abilityPawnFieldRef(instance);
		Def abilityDef = (Def)abilityDefFieldRef(instance);
		SyncedAbilityToggle(pawn, abilityDef);
		return false;
	}

	private static void SyncedAbilityToggle(Pawn pawn, Def abilityDef)
	{
		ThingComp thingComp = pawn.AllComps.FirstOrDefault((ThingComp c) => compAbilitiesLocalType.IsInstanceOfType(c));
		if (thingComp == null)
		{
			return;
		}
		foreach (object item in learnedAbilitiesLocalField(thingComp))
		{
			if ((Def)abilityDefFieldRef(item) == abilityDef && iAbilityToggleType.IsInstanceOfType(item))
			{
				bool flag = (bool)toggleToggleProperty.GetValue(item);
				toggleToggleProperty.SetValue(item, !flag);
				break;
			}
		}
	}

	private static void RegisterSyncGizmos()
	{
		MpCompat.RegisterLambdaMethod("VanillaPsycastsExpanded.CompBreakLink", "GetGizmos", default(int));
		MpCompat.RegisterLambdaDelegate("VanillaPsycastsExpanded.Ability_GuardianSkipBarrier", "GetGizmo", default(int));
		MpCompat.RegisterLambdaMethod("VanillaPsycastsExpanded.Skipmaster.Skipdoor", "GetDoorTeleporterGismoz", default(int)).SetContext(SyncContext.None);
	}

	private static void PatchMotesAndFlecks()
	{
		PatchingUtilities.PatchPushPopRand(new string[2] { "VanillaPsycastsExpanded.FixedTemperatureZone:ThrowFleck", "VanillaPsycastsExpanded.Conflagrator.FireTornado:ThrowPuff" });
		HarmonyMethod transpiler = new HarmonyMethod(typeof(VanillaPsycastsExpanded), "TranspileThrowSmokeRandFix");
		MpCompat.harmony.Patch(AccessTools.Method("VanillaPsycastsExpanded.Staticlord.Vortex:Tick"), null, null, transpiler);
		MpCompat.harmony.Patch(AccessTools.Method("VanillaPsycastsExpanded.Staticlord.GameCondition_PsychicFlashstorm:GameConditionTick"), null, null, transpiler);
	}

	private static IEnumerable<CodeInstruction> TranspileThrowSmokeRandFix(IEnumerable<CodeInstruction> instr)
	{
		MethodInfo target = AccessTools.Method(typeof(FleckMaker), "ThrowSmoke", new Type[3]
		{
			typeof(Vector3),
			typeof(Map),
			typeof(float)
		});
		MethodInfo replacement = AccessTools.Method(typeof(VanillaPsycastsExpanded), "ThrowSmokeRandSafe");
		foreach (CodeInstruction item in instr)
		{
			if (item.operand is MethodInfo methodInfo && methodInfo == target)
			{
				item.operand = replacement;
			}
			yield return item;
		}
	}

	private static void ThrowSmokeRandSafe(Vector3 loc, Map map, float size)
	{
		Rand.PushState();
		FleckMaker.ThrowSmoke(loc, map, size);
		Rand.PopState();
	}

	private static void PatchITab()
	{
		MpCompat.harmony.Patch(AccessTools.Method("VanillaPsycastsExpanded.UI.ITab_Pawn_Psycasts:DoPsysets"), null, null, new HarmonyMethod(typeof(VanillaPsycastsExpanded), "PatchPsycastsITab"));
	}

	private static void PatchKillskipMeleeScope()
	{
		Type type = AccessTools.TypeByName("VanillaPsycastsExpanded.Ability_Killskip") ?? throw new TypeLoadException("VanillaPsycastsExpanded.Ability_Killskip");
		Type type2 = AccessTools.TypeByName("VanillaPsycastsExpanded.VerbProperties_AdjustedMeleeDamageAmount_Patch") ?? throw new TypeLoadException("VanillaPsycastsExpanded.VerbProperties_AdjustedMeleeDamageAmount_Patch");
		killskipMeleeMultiplier = AccessTools.StaticFieldRefAccess<bool>(AccessTools.DeclaredField(type2, "multiplyByPawnMeleeSkill") ?? throw new MissingFieldException(type2.FullName, "multiplyByPawnMeleeSkill"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, "AttackTarget", new Type[1] { typeof(LocalTargetInfo) }) ?? throw new MissingMethodException(type.FullName, "AttackTarget"), new HarmonyMethod(typeof(VanillaPsycastsExpanded), "BeforeKillskipMelee"), null, null, new HarmonyMethod(typeof(VanillaPsycastsExpanded), "AfterKillskipMelee"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type2, "Postfix", new Type[3]
		{
			typeof(float).MakeByRefType(),
			typeof(Verb),
			typeof(Pawn)
		}) ?? throw new MissingMethodException(type2.FullName, "Postfix"), new HarmonyMethod(typeof(VanillaPsycastsExpanded), "AllowKillskipMeleeMultiplier"));
	}

	private static void BeforeKillskipMelee(out bool? __state)
	{
		__state = (MP.IsInMultiplayer ? new bool?(killskipMeleeMultiplier()) : ((bool?)null));
	}

	private static Exception AfterKillskipMelee(Exception __exception, bool? __state)
	{
		if (__state.HasValue)
		{
			killskipMeleeMultiplier() = __state.Value;
		}
		return __exception;
	}

	private static bool AllowKillskipMeleeMultiplier(Pawn attacker)
	{
		if (MP.IsInMultiplayer && killskipMeleeMultiplier() && attacker != null)
		{
			return attacker.skills != null;
		}
		return true;
	}

	private static void RegisterPsysetChanges()
	{
		MP.RegisterSyncMethod(typeof(VanillaPsycastsExpanded), "ApplyPsysetChange").SetVersion(7);
	}

	private static Def[] ReadPsysetAbilities(object set)
	{
		return psysetAbilitiesField(set).Cast<Def>().OrderBy((Def def) => def.defName, StringComparer.Ordinal).ToArray();
	}

	private static bool ValidPsysetAbilities(Def[] defs)
	{
		if (defs != null && defs.All((Def def) => def != null && abilityDefHashSetType.GetGenericArguments()[0].IsInstanceOfType(def)))
		{
			return defs.Distinct(ReferenceEqualityComparer.Instance).Count() == defs.Length;
		}
		return false;
	}

	private static bool SamePsysetAbilities(object set, Def[] expected)
	{
		if (!ValidPsysetAbilities(expected))
		{
			return false;
		}
		Def[] array = psysetAbilitiesField(set).Cast<Def>().ToArray();
		if (array.Length == expected.Length)
		{
			return array.All((Def def) => expected.Any((Def value) => value == def));
		}
		return false;
	}

	private static void WritePsysetAbilities(object set, Def[] defs)
	{
		object obj = Activator.CreateInstance(abilityDefHashSetType);
		foreach (Def def in defs)
		{
			abilityDefHashSetAddMethod(obj, def);
		}
		psysetAbilitiesField(set) = (IEnumerable)obj;
	}

	private static void AddPsysetFromUi(IList list, object set)
	{
		if (!MP.IsInMultiplayer)
		{
			list.Add(set);
		}
	}

	private static void BeginPsysetDraft(Window dialog, object source, Hediff hediff)
	{
		if (MP.IsInMultiplayer && source != null && hediff != null)
		{
			IList list = hediffPsysetsList(hediff);
			object obj = Activator.CreateInstance(psysetType);
			psysetNameField(obj) = psysetNameField(source);
			WritePsysetAbilities(obj, ReadPsysetAbilities(source));
			PsysetDraft value = new PsysetDraft
			{
				Hediff = hediff,
				Value = obj,
				Index = list.IndexOf(source),
				Expected = PsysetSnapshot.Capture(list)
			};
			dialogPsysetPsysetField(dialog) = obj;
			PsysetDrafts.Remove(dialog);
			PsysetDrafts.Add(dialog, value);
		}
	}

	private static void CommitPsysetDraft(Window __instance)
	{
		if (!PsysetDrafts.TryGetValue(__instance, out var value))
		{
			return;
		}
		PsysetDrafts.Remove(__instance);
		if (MP.IsInMultiplayer && MP.InInterface && !MP.IsExecutingSyncCommand)
		{
			Def[] values = ReadPsysetAbilities(value.Value);
			if (value.Index < 0 || !SamePsysetAbilities(value.Value, value.Expected.Abilities[value.Index]))
			{
				ApplyPsysetChange(value.Hediff, (value.Index >= 0) ? 1 : 0, value.Index, value.Expected.Names, value.Expected.Abilities, psysetNameField(value.Value), values);
			}
		}
	}

	private static void RequestPsysetChange(Hediff hediff, object set, int operation, string name, Def[] values)
	{
		if (hediff != null && set != null)
		{
			IList list = hediffPsysetsList(hediff);
			int num = list.IndexOf(set);
			if (num >= 0)
			{
				PsysetSnapshot psysetSnapshot = PsysetSnapshot.Capture(list);
				ApplyPsysetChange(hediff, operation, num, psysetSnapshot.Names, psysetSnapshot.Abilities, name, values);
			}
		}
	}

	private static void ApplyPsysetChange(Hediff hediff, int operation, int index, string[] names, Def[][] abilities, string name, Def[] values)
	{
		if (hediff == null || names == null || abilities == null || names.Length != abilities.Length || operation < 0 || operation > 3)
		{
			return;
		}
		IList list = hediffPsysetsList(hediff);
		if (list.Count != names.Length)
		{
			return;
		}
		for (int i = 0; i < list.Count; i++)
		{
			if (!string.Equals(psysetNameField(list[i]), names[i], StringComparison.Ordinal) || !SamePsysetAbilities(list[i], abilities[i]))
			{
				return;
			}
		}
		if (operation == 0)
		{
			if (index == -1 && ValidPsysetAbilities(values))
			{
				object obj = Activator.CreateInstance(psysetType);
				psysetNameField(obj) = name;
				WritePsysetAbilities(obj, values);
				list.Add(obj);
			}
		}
		else
		{
			if (index < 0 || index >= list.Count)
			{
				return;
			}
			object obj2 = list[index];
			switch (operation)
			{
			case 2:
				removePsysetMethod(hediff, obj2);
				return;
			case 3:
				psysetNameField(obj2) = name;
				return;
			}
			if (ValidPsysetAbilities(values))
			{
				WritePsysetAbilities(obj2, values);
			}
		}
	}
}
