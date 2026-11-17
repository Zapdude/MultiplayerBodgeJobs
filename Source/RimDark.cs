using System;
using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;
using Verse.AI;

namespace Multiplayer.Compat;

[MpCompatFor("Phonicmas.RimDark.FrameworkCore")]
public class RimDark
{
	private sealed class DraftPart
	{
		internal ThingComp Original;

		internal ThingComp Draft;

		internal int Kind;

		internal int Index;

		internal string Baseline;
	}

	private sealed class DraftSession
	{
		internal Window Window;

		internal Pawn Pawn;

		internal int JobId;

		internal int Kind;

		internal readonly List<DraftPart> Parts = new List<DraftPart>();

		internal bool Closing;
	}

	private sealed class DraftScope : IDisposable
	{
		private readonly List<(List<ThingComp> list, int index, ThingComp before, ThingComp after)> replaced = new List<(List<ThingComp>, int, ThingComp, ThingComp)>();

		private bool disposed;

		internal DraftScope(DraftSession session)
		{
			previewDepth++;
			foreach (DraftPart part in session.Parts)
			{
				List<ThingComp> allComps = part.Original.parent.AllComps;
				int num = allComps.IndexOf(part.Original);
				if (num >= 0)
				{
					replaced.Add((allComps, num, part.Original, part.Draft));
					allComps[num] = part.Draft;
				}
			}
		}

		public void Dispose()
		{
			if (disposed)
			{
				return;
			}
			disposed = true;
			try
			{
				for (int num = replaced.Count - 1; num >= 0; num--)
				{
					(List<ThingComp>, int, ThingComp, ThingComp) tuple = replaced[num];
					if (tuple.Item2 < tuple.Item1.Count && tuple.Item1[tuple.Item2] == tuple.Item4)
					{
						tuple.Item1[tuple.Item2] = tuple.Item3;
					}
				}
			}
			finally
			{
				previewDepth--;
			}
		}
	}

	private sealed class ChoiceHolder
	{
		public object Choice;
	}

	private sealed class SettingsEdit
	{
		internal bool Genes;

		internal string Before;
	}

	private struct ActiveRead
	{
		internal bool Restore;

		internal bool CachedActive;

		internal bool Evaluating;

		internal bool ExtensionResolved;

		internal Gene Override;

		internal object Extension;

		internal int CachedTick;

		internal PatchingUtilities.TimeSnapshot? Time;
	}

	private sealed class InjectorClockState
	{
		internal bool ValidNegative;
	}

	private const string Ns = "Core40k.";

	private static readonly ConcurrentDictionary<(Type, string), MemberInfo> Members = new ConcurrentDictionary<(Type, string), MemberInfo>();

	private static readonly ConcurrentDictionary<string, Type> Types = new ConcurrentDictionary<string, Type>();

	private static readonly string[][] AppearanceFields = new string[4][]
	{
		new string[4] { "drawColorOne", "drawColorTwo", "drawColorThree", "maskDef" },
		new string[1] { "currentAlternateBaseForm" },
		new string[2] { "decorations", "drawDatas" },
		new string[1] { "choices" }
	};

	private static readonly string[] AppearanceTypes = new string[4] { "CompMultiColor", "CompAlternateTexture", "CompDecorativeBase", "CompGeneCustomization" };

	private static readonly string[][] PendingFields = new string[3][]
	{
		new string[5] { "pendingColorOne", "pendingColorTwo", "pendingColorThree", "pendingMaskDef", "hasPendingChange" },
		new string[2] { "pendingAlternateBaseForm", "hasPendingChange" },
		new string[7] { "pendingDecorations", "pendingDrawDatas", "pendingAdded", "pendingRemoved", "pendingUnlock", "pendingAppearance", "hasPendingChange" }
	};

	private static readonly Dictionary<Window, DraftSession> CustomizationWindows = new Dictionary<Window, DraftSession>();

	private static readonly ConditionalWeakTable<ThingComp, DraftPart> DraftParts = new ConditionalWeakTable<ThingComp, DraftPart>();

	private static readonly MethodInfo ShallowCopy = AccessTools.DeclaredMethod(typeof(object), "MemberwiseClone");

	[ThreadStatic]
	private static int previewDepth;

	private static Game draftGame;

	private static readonly string[] CoreSettingsFields = new string[7] { "decorationWorkEnabled", "decorationCostEnabled", "appearanceChangeWorkAmount", "minimumWorkAmount", "repairCostFactor", "notifyOnRankEligibility", "notifyOnGeneVariantAvailability" };

	private static readonly string[] GeneSettingsFields = new string[22]
	{
		"psychicPhenomena", "psykerPariahBirth", "psykerPariahBirthChance", "perpetualBirth", "perpetualBirthChance", "livingSaintSystem", "livingSaintOnlyBaselinerAscension", "livingSaintMale", "livingSaintAddedChanceFromViolenceSkills", "livingSaintLimit",
		"livingSaintBaseChance", "livingSaintBigThreat", "livingSaintSmallThreat", "implantationSuccessOffset", "implantationCapOffset", "matrixGestationTimeFactor", "chapterStabilityRange", "chapterBaseTraitPicks", "primarchComplexityRange", "primarchBaseTraitPicks",
		"psychicCrafting", "allowFemalePrimarchBirths"
	};

	private static AccessTools.FieldRef<IList> artifactTicking;

	private static AccessTools.FieldRef<IList> shieldPending;

	private static AccessTools.FieldRef<bool> shieldApplying;

	private static Func<ThingComp, Pawn> artifactWielder;

	private static Action<ThingComp, Pawn, int> artifactTickWorkers;

	private static Func<object, Pawn> shieldCaster;

	private static Func<object, Pawn> shieldTarget;

	private static Action<object> applyShieldEntry;

	private static AccessTools.FieldRef<object, Gene> activeOverride;

	private static AccessTools.FieldRef<object, int> activeCachedTick;

	private static AccessTools.FieldRef<object, bool> activeCached;

	private static AccessTools.FieldRef<object, bool> activeEvaluating;

	private static AccessTools.FieldRef<object, bool> activeExtensionResolved;

	private static AccessTools.FieldRef<object, object> activeExtension;

	private static readonly ConditionalWeakTable<object, InjectorClockState> InjectorClocks = new ConditionalWeakTable<object, InjectorClockState>();

	private static AccessTools.FieldRef<object, int> injectorRearm;

	private static AccessTools.FieldRef<object, int> shieldAttack;

	private static bool Intercept
	{
		get
		{
			if (MP.IsInMultiplayer && MP.InInterface)
			{
				return !MP.IsExecutingSyncCommand;
			}
			return false;
		}
	}

	public RimDark(ModContentPack mod)
	{
		PatchingUtilities.SetupAsyncTime();
		InstallSettings();
		InstallCommands();
		InstallCustomization();
		InstallSimulation();
	}

	private static Type TypeOf(string name)
	{
		return Types.GetOrAdd(name, (string key) => AccessTools.TypeByName("Core40k." + key) ?? throw new TypeLoadException("Core40k." + key));
	}

	private static MemberInfo Member(Type type, string name)
	{
		return Members.GetOrAdd((type, name), ((Type, string) key) => (MemberInfo)(((object)AccessTools.Field(key.Item1, key.Item2)) ?? ((object)(AccessTools.Property(key.Item1, key.Item2) ?? throw new MissingMemberException(key.Item1.FullName, key.Item2)))));
	}

	private static object Get(object target, string name)
	{
		if (target == null)
		{
			return null;
		}
		Type type = (target as Type) ?? target.GetType();
		object obj = ((target is Type) ? null : target);
		MemberInfo memberInfo = Member(type, name);
		if (!(memberInfo is FieldInfo fieldInfo))
		{
			if (memberInfo is PropertyInfo propertyInfo)
			{
				return propertyInfo.GetValue(obj);
			}
			throw new MissingMemberException(type.FullName, name);
		}
		return fieldInfo.GetValue(obj);
	}

	private static void Set(object target, string name, object value)
	{
		Type type = (target as Type) ?? target.GetType();
		object obj = ((target is Type) ? null : target);
		MemberInfo memberInfo = Member(type, name);
		if (!(memberInfo is FieldInfo fieldInfo))
		{
			if (memberInfo is PropertyInfo propertyInfo)
			{
				propertyInfo.SetValue(obj, value);
			}
		}
		else
		{
			fieldInfo.SetValue(obj, value);
		}
	}

	private static object Call(object target, string name, params object[] args)
	{
		Type type = (target as Type) ?? target.GetType();
		List<MethodInfo> list = new List<MethodInfo>();
		Type type2 = type;
		while (type2 != null)
		{
			list.AddRange(AccessTools.GetDeclaredMethods(type2));
			type2 = type2.BaseType;
		}
		MethodInfo[] array = (from method in list
			group method by method.Name + "(" + string.Join(",", from p in method.GetParameters()
				select p.ParameterType.FullName) + ")" into @group
			select @group.First() into method
			where method.Name == name && method.GetParameters().Length == args.Length && method.GetParameters().Select((ParameterInfo parameter, int index) =>
			{
				if (args[index] != null)
				{
					return parameter.ParameterType.IsInstanceOfType(args[index]);
				}
				return !parameter.ParameterType.IsValueType || Nullable.GetUnderlyingType(parameter.ParameterType) != null;
			}).All((bool match) => match)
			select method).ToArray();
		if (array.Length != 1)
		{
			throw new AmbiguousMatchException($"Expected one {type.FullName}.{name} for {args.Length} arguments, found {array.Length}");
		}
		return array[0].Invoke((target is Type) ? null : target, args);
	}

	private static void Patch(string type, string method, string prefix = null, string postfix = null, string transpiler = null, string finalizer = null)
	{
		MethodInfo original = AccessTools.DeclaredMethod(TypeOf(type), method) ?? throw new MissingMethodException("Core40k." + type, method);
		MpCompat.harmony.Patch(original, Hook(prefix), Hook(postfix), Hook(transpiler), Hook(finalizer));
		static HarmonyMethod Hook(string name)
		{
			if (name != null)
			{
				return new HarmonyMethod(typeof(RimDark), name);
			}
			return null;
		}
	}

	private static void InstallCommands()
	{
		Type type = TypeOf("GizmoTextureTogglePatch");
		MethodInfo methodInfo = AccessTools.DeclaredMethod(type, "WithToggles");
		MethodInfo methodInfo2 = AccessTools.EnumeratorMoveNext(methodInfo);
		MethodInfo lambda = MpMethodUtil.GetLambda(type, methodInfo.Name);
		MethodInfo lambda2 = MpMethodUtil.GetLambda(type, methodInfo.Name, MethodType.Normal, null, 1);
		bool flag = TextureReadNeedsPatch(methodInfo2, lambda);
		string[] array = new string[3] { "SyncRank", "SyncAmmo", "SyncTextureToggle" };
		foreach (string methodOrPropertyName in array)
		{
			MP.RegisterSyncMethod(typeof(RimDark), methodOrPropertyName);
		}
		Patch("ITab_RankSystem", "UnlockRank", "RankPrefix");
		MP.RegisterSyncMethod(TypeOf("CompRankInfo"), "OpenedRankCategory");
		MP.RegisterSyncMethod(TypeOf("CompRankInfo"), "ResetRanks").SetDebugOnly();
		MpCompat.harmony.Patch(MpMethodUtil.GetLambda(TypeOf("Gizmo_AmmoChanger"), "GizmoOnGUI"), new HarmonyMethod(typeof(RimDark), "AmmoPrefix"));
		MpCompat.harmony.Patch(lambda2, new HarmonyMethod(typeof(RimDark), "TextureTogglePrefix"));
		if (flag)
		{
			MpCompat.harmony.Patch(methodInfo2, null, null, new HarmonyMethod(typeof(RimDark), "TextureReadTranspiler"));
		}
	}

	private static bool RankPrefix(object __instance, Def rank)
	{
		if (!Intercept)
		{
			return true;
		}
		SyncRank(Get(__instance, "pawn") as Pawn, rank, Get(__instance, "currentlySelectedRankCategory") as Def);
		Set(__instance, "redoRankInfo", true);
		return false;
	}

	private static void SyncRank(Pawn pawn, Def rank, Def category)
	{
		if (pawn == null || !pawn.Spawned || rank == null || category == null || rank.GetType() != TypeOf("RankDef") || category.GetType() != TypeOf("RankCategoryDef") || ((IEnumerable)Get(category, "ranks")).Cast<object>().All((object item) => Get(item, "rankDef") != rank))
		{
			return;
		}
		object obj = pawn.AllComps.FirstOrDefault(TypeOf("CompRankInfo").IsInstanceOfType);
		if (obj == null || (bool)Call(obj, "HasRank", rank))
		{
			return;
		}
		using (new RimDarkMapScope(pawn.Map))
		{
			if ((bool)Call(rank, "RequirementMet", pawn, obj, category) && (!(bool)Call(TypeOf("RankUtils"), "IsLimited", rank) || (bool)Call(Get(obj, "GameComponentRankInfo"), "CanHaveMoreOfRank", rank)))
			{
				Call(obj, "UnlockRank", rank);
			}
		}
	}

	private static bool AmmoPrefix(object __instance)
	{
		if (!Intercept)
		{
			return true;
		}
		SyncAmmo((Get(Get(__instance, "<>4__this"), "compAmmoChanger") as ThingComp)?.parent, Get(__instance, "availableProjectile") as ThingDef);
		return false;
	}

	private static void SyncAmmo(ThingWithComps weapon, ThingDef projectile)
	{
		ThingComp thingComp = weapon?.AllComps.FirstOrDefault(TypeOf("Comp_AmmoChanger").IsInstanceOfType);
		Pawn pawn = ((thingComp == null) ? null : (Get(thingComp, "pawn") as Pawn));
		if (pawn == null || !pawn.Spawned || pawn.equipment?.Primary != weapon || projectile == null || !(Get(thingComp, "AvailableProjectiles") is IList list) || !list.Contains(projectile))
		{
			return;
		}
		object[] parameters = new object[2] { projectile, null };
		if (!(bool)AccessTools.DeclaredMethod(TypeOf("Comp_AmmoChanger"), "HasResearchForAmmo").Invoke(thingComp, parameters))
		{
			return;
		}
		using (new RimDarkMapScope(pawn.Map))
		{
			if (GenDefDatabase.GetDef(typeof(JobDef), "BEWH_ChangeAmmo", errorOnFail: false) is JobDef def && pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(def, weapon), JobTag.Misc))
			{
				Call(thingComp, "SetNextProjectile", projectile);
			}
		}
	}

	private static bool TextureTogglePrefix(object __instance)
	{
		if (!Intercept)
		{
			return true;
		}
		(Pawn, Thing) tuple = ((Pawn, Thing))Get(__instance, "pair");
		SyncTextureToggle(tuple.Item1, tuple.Item2);
		return false;
	}

	private static void SyncTextureToggle(Pawn pawn, Thing thing)
	{
		if (pawn == null || thing == null || thing.Destroyed || (thing.ParentHolder != pawn.apparel && thing.ParentHolder != pawn.equipment))
		{
			return;
		}
		GameComponent gameComponent = Current.Game?.components.FirstOrDefault(TypeOf("GameComponent_CoreUtils").IsInstanceOfType);
		if (gameComponent != null)
		{
			Dictionary<(Pawn, Thing), bool> dictionary = (Dictionary<(Pawn, Thing), bool>)Get(gameComponent, "cachedGizmoToggles");
			dictionary.TryGetValue((pawn, thing), out var value);
			if (value)
			{
				dictionary.Remove((pawn, thing));
			}
			else
			{
				dictionary[(pawn, thing)] = true;
			}
			pawn.Drawer?.renderer?.SetAllGraphicsDirty();
		}
	}

	private static bool TextureReadNeedsPatch(MethodBase iterator, MethodBase isActive)
	{
		string[] array = ToggleDictionaryCalls(iterator);
		string[] array2 = ToggleDictionaryCalls(isActive);
		if (array2 != null && array2.Length == 1 && array2[0] == "TryGetValue")
		{
			if (array != null && array.Length == 1 && array[0] == "TryGetValue")
			{
				return false;
			}
			if (array != null && array.Length == 3 && array[0] == "ContainsKey" && array[1] == "Add" && array[2] == "TryGetValue")
			{
				return true;
			}
		}
		throw new InvalidOperationException("Unsupported RimDark texture toggle drawing methods");
		static string[] ToggleDictionaryCalls(MethodBase method)
		{
			return (from methodInfo in (from instruction in PatchProcessor.GetOriginalInstructions(method)
					select instruction.operand).OfType<MethodInfo>()
				where methodInfo.DeclaringType == typeof(Dictionary<(Pawn, Thing), bool>)
				select methodInfo.Name).ToArray();
		}
	}

	private static IEnumerable<CodeInstruction> TextureReadTranspiler(IEnumerable<CodeInstruction> input)
	{
		MethodInfo add = AccessTools.DeclaredMethod(typeof(Dictionary<(Pawn, Thing), bool>), "Add");
		int changed = 0;
		foreach (CodeInstruction item in input)
		{
			if (item.Calls(add))
			{
				changed++;
				item.opcode = OpCodes.Call;
				item.operand = AccessTools.Method(typeof(RimDark), "DoNotInsertDrawDefault");
			}
			yield return item;
		}
		if (changed != 1)
		{
			throw new InvalidOperationException("RimDark toggle read site changed: " + changed);
		}
	}

	private static void DoNotInsertDrawDefault(Dictionary<(Pawn, Thing), bool> values, (Pawn, Thing) key, bool value)
	{
		if (!MP.IsInMultiplayer)
		{
			values.Add(key, value);
		}
	}

	private static int AppearanceKind(ThingComp comp)
	{
		for (int i = 0; i < AppearanceTypes.Length; i++)
		{
			if (TypeOf(AppearanceTypes[i]).IsInstanceOfType(comp))
			{
				return i;
			}
		}
		return -1;
	}

	private static ThingComp CopyComp(ThingComp original)
	{
		ThingComp thingComp = (ThingComp)ShallowCopy.Invoke(original, null);
		Type type = original.GetType();
		while (type != null && type != typeof(ThingComp))
		{
			FieldInfo[] fields = type.GetFields(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			foreach (FieldInfo fieldInfo in fields)
			{
				fieldInfo.SetValue(thingComp, RimDarkState.Copy(fieldInfo.GetValue(original)));
			}
			type = type.BaseType;
		}
		DraftParts.Add(thingComp, new DraftPart
		{
			Original = original,
			Draft = thingComp
		});
		return thingComp;
	}

	private static void InstallCustomization()
	{
		string[] array = new string[3] { "SyncAppearance", "SyncCancelCustomization", "SyncGeneAppearance" };
		foreach (string methodOrPropertyName in array)
		{
			MP.RegisterSyncMethod(typeof(RimDark), methodOrPropertyName);
		}
		array = new string[3] { "Dialog_CustomizeApparel", "Dialog_CustomizeWeapon", "Dialog_GeneCustomization" };
		for (int i = 0; i < array.Length; i++)
		{
			Type type = TypeOf(array[i]);
			MpCompat.harmony.Patch(AccessTools.DeclaredConstructor(type, new Type[1] { typeof(Pawn) }), new HarmonyMethod(typeof(RimDark), "CustomizationConstructorPrefix"), null, null, new HarmonyMethod(typeof(RimDark), "DraftScopeFinalizer"));
			MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, "Close"), new HarmonyMethod(typeof(RimDark), "CustomizationClosePrefix"), new HarmonyMethod(typeof(RimDark), "CustomizationClosePostfix"));
		}
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(Window), "InnerWindowOnGUI"), new HarmonyMethod(typeof(RimDark), "CustomizationGuiPrefix"), null, null, new HarmonyMethod(typeof(RimDark), "DraftScopeFinalizer"));
		Patch("DecorationWorkUtility", "TryAccept", "AppearanceAcceptPrefix");
		Patch("Dialog_GeneCustomization", "Accept", "GeneAppearanceAcceptPrefix");
		array = new string[3] { "AddAbilitiesOf", "RemoveAbilitiesOf", "OnDecorationsChanged" };
		foreach (string method in array)
		{
			Patch("CompDecorativeBase", method, "DraftMutationPrefix");
		}
		Patch("CompGeneCustomization", "ReconcileGrants", "DraftMutationPrefix");
		Patch("CompAlternateTexture", "SwapCurrentAlternateBaseForm", "DraftAlternatePrefix");
		Patch("ApparelLayerConflictUtility", "ResolveWornConflicts", "DraftConflictPrefix");
	}

	private static void CustomizationConstructorPrefix(Window __instance, Pawn pawn, out DraftScope __state)
	{
		__state = null;
		if (!MP.IsInMultiplayer || pawn == null)
		{
			return;
		}
		if (draftGame != Current.Game)
		{
			CustomizationWindows.Clear();
			draftGame = Current.Game;
		}
		int num = ((__instance.GetType() == TypeOf("Dialog_GeneCustomization")) ? 2 : ((__instance.GetType() == TypeOf("Dialog_CustomizeWeapon")) ? 1 : 0));
		DraftSession draftSession = new DraftSession
		{
			Window = __instance,
			Pawn = pawn,
			Kind = num,
			JobId = ((num == 2) ? (-1) : (pawn.CurJob?.loadID ?? (-1)))
		};
		ThingWithComps[] first = new ThingWithComps[1] { pawn };
		Pawn_ApparelTracker apparel = pawn.apparel;
		IEnumerable<ThingWithComps> first2 = first.Concat(((apparel != null) ? apparel.WornApparel.Cast<ThingWithComps>() : null) ?? Enumerable.Empty<ThingWithComps>());
		IEnumerable<ThingWithComps> enumerable = pawn.equipment?.AllEquipmentListForReading;
		foreach (ThingWithComps item in first2.Concat(enumerable ?? Enumerable.Empty<ThingWithComps>()))
		{
			for (int i = 0; i < item.AllComps.Count; i++)
			{
				ThingComp thingComp = item.AllComps[i];
				int num2 = AppearanceKind(thingComp);
				if (num2 >= 0 && !((num == 2) ? (num2 != 3) : (num2 == 3)))
				{
					draftSession.Parts.Add(new DraftPart
					{
						Original = thingComp,
						Draft = CopyComp(thingComp),
						Kind = num2,
						Index = i,
						Baseline = RimDarkState.Capture(thingComp, AppearanceFields[num2])
					});
				}
			}
		}
		CustomizationWindows[__instance] = draftSession;
		__state = new DraftScope(draftSession);
	}

	private static void CustomizationGuiPrefix(Window __instance, out DraftScope __state)
	{
		__state = null;
		if (MP.IsInMultiplayer && CustomizationWindows.Count != 0)
		{
			DraftSession draftSession = (CustomizationWindows.TryGetValue(__instance, out var value) ? value : CustomizationWindows.Values.LastOrDefault((DraftSession s) => Find.WindowStack.Windows.Contains(s.Window)));
			if (draftSession != null)
			{
				__state = new DraftScope(draftSession);
			}
		}
	}

	private static Exception DraftScopeFinalizer(Exception __exception, DraftScope __state)
	{
		__state?.Dispose();
		return __exception;
	}

	private static bool DraftMutationPrefix(ThingComp __instance)
	{
		DraftPart value;
		if (MP.IsInMultiplayer)
		{
			return !DraftParts.TryGetValue(__instance, out value);
		}
		return true;
	}

	private static bool DraftAlternatePrefix(ThingComp __instance, object alternateBaseFormDef)
	{
		if (!MP.IsInMultiplayer || !DraftParts.TryGetValue(__instance, out var _))
		{
			return true;
		}
		Set(__instance, "currentAlternateBaseForm", alternateBaseFormDef);
		return false;
	}

	private static bool DraftConflictPrefix()
	{
		if (MP.IsInMultiplayer)
		{
			return previewDepth == 0;
		}
		return true;
	}

	private static bool CustomizationClosePrefix(Window __instance)
	{
		if (!MP.IsInMultiplayer || !CustomizationWindows.TryGetValue(__instance, out var value) || value.Closing || value.Kind == 2 || !Intercept)
		{
			return true;
		}
		SyncCancelCustomization(value.Pawn, value.JobId, value.Kind);
		return false;
	}

	private static void CustomizationClosePostfix(Window __instance)
	{
		if (CustomizationWindows.TryGetValue(__instance, out var value) && (value.Closing || value.Kind == 2 || !Find.WindowStack.Windows.Contains(__instance)))
		{
			CustomizationWindows.Remove(__instance);
		}
	}

	private static void CloseCustomization(Pawn pawn, int jobId, int kind)
	{
		DraftSession[] array = CustomizationWindows.Values.Where((DraftSession s) => s.Pawn == pawn && s.JobId == jobId && s.Kind == kind).ToArray();
		foreach (DraftSession draftSession in array)
		{
			draftSession.Closing = true;
			using (new DraftScope(draftSession))
			{
				draftSession.Window.Close();
			}
			CustomizationWindows.Remove(draftSession.Window);
		}
	}

	private static void SyncCancelCustomization(Pawn pawn, int jobId, int kind)
	{
		if (pawn != null)
		{
			CloseCustomization(pawn, jobId, kind);
		}
	}

	private static IList GraphicList(IEnumerable<object> values)
	{
		IList list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(TypeOf("CompGraphicParent")));
		foreach (object value in values)
		{
			list.Add(value);
		}
		return list;
	}

	private static bool CanAfford(Pawn pawn, List<ThingDefCountClass> cost)
	{
		Call(TypeOf("UpgradeCostUtility"), "InvalidateAvailability");
		object[] parameters = new object[3] { pawn, cost, null };
		return (bool)AccessTools.DeclaredMethod(TypeOf("UpgradeCostUtility"), "CanAfford", new Type[3]
		{
			typeof(Pawn),
			typeof(List<ThingDefCountClass>),
			typeof(ThingDefCountClass).MakeByRefType()
		}).Invoke(null, parameters);
	}

	private static bool AppearanceAcceptPrefix(Pawn pawn, object dialogComps)
	{
		if (!Intercept)
		{
			return true;
		}
		ThingComp[] drafts = ((IEnumerable)dialogComps).Cast<ThingComp>().ToArray();
		if (!drafts.Any((ThingComp part) => DraftParts.TryGetValue(part, out var _)))
		{
			return true;
		}
		DraftSession session = CustomizationWindows.Values.LastOrDefault((DraftSession value) => value.Pawn == pawn && value.Kind != 2);
		if (session == null)
		{
			return false;
		}
		DraftPart[] parts = session.Parts.Where((DraftPart part) => drafts.Contains(part.Draft, null)).ToArray();
		IList list = GraphicList(((IEnumerable<DraftPart>)parts).Select((Func<DraftPart, object>)((DraftPart part) => part.Draft)));
		Type target = TypeOf("DecorationWorkUtility");
		List<ThingDefCountClass> list2 = (List<ThingDefCountClass>)Call(target, "PreviewCost", list);
		float num = (((bool)Get(target, "WorkEnabled")) ? ((float)Call(target, "PreviewWork", list)) : 0f);
		if (!CanAfford(pawn, list2))
		{
			Messages.Message("BEWH.Framework.Customization.NotEnoughResources".Translate(), MessageTypeDefOf.RejectInput);
			return false;
		}
		if (num <= 0f && list2.Count == 0)
		{
			Submit();
		}
		else
		{
			string text = ((list2.Count == 0) ? "BEWH.Framework.Customization.CostNothing".Translate().ToString() : ((string)Call(TypeOf("UpgradeCostUtility"), "CostToString", list2)));
			TaggedString text2 = ((num > 0f) ? "BEWH.Framework.Customization.ConfirmWork".Translate(text, num.ToString("F0")) : "BEWH.Framework.Customization.ConfirmCostOnly".Translate(text));
			Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(text2, Submit));
		}
		return false;
		void Submit()
		{
			SyncAppearance(pawn, session.JobId, session.Kind, parts.Select((DraftPart p) => p.Original.parent).ToArray(), parts.Select((DraftPart p) => p.Index).ToArray(), parts.Select((DraftPart p) => p.Baseline).ToArray(), parts.Select((DraftPart p) => RimDarkState.Capture(p.Draft, AppearanceFields[p.Kind])).ToArray());
		}
	}

	private static void SyncAppearance(Pawn pawn, int jobId, int windowKind, ThingWithComps[] owners, int[] indices, string[] before, string[] selected)
	{
		int num;
		if (pawn != null && pawn.Spawned)
		{
			Job curJob = pawn.CurJob;
			num = ((curJob == null || curJob.loadID != jobId) ? 1 : 0);
		}
		else
		{
			num = 1;
		}
		bool flag = (byte)num != 0;
		if (!flag)
		{
			bool flag2 = ((windowKind < 0 || windowKind > 1) ? true : false);
			flag = flag2;
		}
		if (flag || owners == null || owners.Length > 128 || indices?.Length != owners.Length || before?.Length != owners.Length || selected?.Length != owners.Length)
		{
			return;
		}
		List<ThingComp> list = new List<ThingComp>();
		List<ThingComp> list2 = new List<ThingComp>();
		for (int i = 0; i < owners.Length; i++)
		{
			ThingWithComps thingWithComps = owners[i];
			if (thingWithComps == null || thingWithComps.Destroyed || (thingWithComps.ParentHolder != pawn.apparel && thingWithComps.ParentHolder != pawn.equipment) || indices[i] < 0 || indices[i] >= thingWithComps.AllComps.Count)
			{
				return;
			}
			ThingComp thingComp = thingWithComps.AllComps[indices[i]];
			int num2 = AppearanceKind(thingComp);
			flag = ((num2 < 0 || num2 > 2) ? true : false);
			if (flag || list.Contains(thingComp) || RimDarkState.Capture(thingComp, AppearanceFields[num2]) != before[i] || (bool)Get(thingComp, "HasPendingChange"))
			{
				return;
			}
			ThingComp thingComp2 = CopyComp(thingComp);
			Call(thingComp2, "SetOriginals");
			RimDarkState.Apply(thingComp2, AppearanceFields[num2], selected[i]);
			list.Add(thingComp);
			list2.Add(thingComp2);
		}
		using (new RimDarkMapScope(pawn.Map))
		{
			for (int j = 0; j < list2.Count; j++)
			{
				ThingComp draft = list2[j];
				int num3 = AppearanceKind(draft);
				if (num3 == 0)
				{
					object[] parameters = new object[1];
					if ((bool)AccessTools.DeclaredMethod(TypeOf("CompMultiColor"), "ColoursLocked").Invoke(draft, parameters) && selected[j] != before[j])
					{
						return;
					}
				}
				if (num3 == 1 && Get(draft, "currentAlternateBaseForm") is Def def && (!((IList)Call(TypeOf("DecorationIndex"), "AlternatesFor", draft.parent.def)).Contains(def) || !(bool)Call(def, "MeetsRequirements", pawn)))
				{
					return;
				}
				if (num3 != 2)
				{
					continue;
				}
				ThingComp thingComp3 = list[j];
				IList list3 = (IList)Call(TypeOf("DecorationIndex"), "DecorationsFor", draft.parent.def);
				IDictionary dictionary = (IDictionary)Get(thingComp3, "decorations");
				IDictionary dictionary2 = (IDictionary)Get(draft, "decorations");
				foreach (DictionaryEntry item in dictionary)
				{
					object[] parameters2 = new object[2] { item.Key, null };
					if (((bool)AccessTools.DeclaredMethod(TypeOf("CompDecorativeBase"), "IsDecorationLocked").Invoke(thingComp3, parameters2) && !dictionary2.Contains(item.Key)) || (dictionary2.Contains(item.Key) && (bool)Call(thingComp3, "AreDecorationColoursLocked", item.Key) && RimDarkState.Capture(item.Value, new string[4] { "Color", "ColorTwo", "ColorThree", "maskDef" }) != RimDarkState.Capture(dictionary2[item.Key], new string[4] { "Color", "ColorTwo", "ColorThree", "maskDef" })))
					{
						return;
					}
				}
				if ((int)Get(draft, "UsedInternalSlots") > (int)Get(draft, "TotalInternalSlots"))
				{
					return;
				}
				ThingComp thingComp4 = list2.FirstOrDefault((ThingComp item) => item.parent == draft.parent && AppearanceKind(item) == 1) ?? draft.parent.AllComps.FirstOrDefault(TypeOf("CompAlternateTexture").IsInstanceOfType);
				object obj = ((thingComp4 == null) ? null : Get(thingComp4, "currentAlternateBaseForm"));
				foreach (DictionaryEntry item2 in dictionary2)
				{
					if (!list3.Contains(item2.Key) || (!dictionary.Contains(item2.Key) && !(bool)Call(item2.Key, "MeetsRequirements", pawn)))
					{
						return;
					}
					if (dictionary.Contains(item2.Key))
					{
						continue;
					}
					if ((obj == null) ? ((bool)Get(item2.Key, "isIncompatibleWithBaseTexture")) : ((IList)Get(obj, "incompatibleDecorations")).Contains(item2.Key))
					{
						return;
					}
					foreach (object key in dictionary2.Keys)
					{
						if (key != item2.Key && ((Get(item2.Key, "incompatibleDecorations") is IList list4 && list4.Contains(key)) || (Get(key, "incompatibleDecorations") is IList list5 && list5.Contains(item2.Key))))
						{
							return;
						}
					}
					if (draft.parent is Apparel apparel && Call(TypeOf("ApparelLayerConflictUtility"), "WornDisplacedBy", pawn, apparel, item2.Key) is IEnumerable source && source.Cast<Apparel>().Any(pawn.apparel.IsLocked))
					{
						return;
					}
				}
			}
			IList list6 = GraphicList(list2);
			Type target = TypeOf("DecorationWorkUtility");
			List<ThingDefCountClass> list7 = (List<ThingDefCountClass>)Call(target, "PreviewCost", list6);
			float num4 = (((bool)Get(target, "WorkEnabled")) ? ((float)Call(target, "PreviewWork", list6)) : 0f);
			if (!CanAfford(pawn, list7))
			{
				Messages.Message("BEWH.Framework.Customization.NotEnoughResources".Translate(), pawn, MessageTypeDefOf.RejectInput);
				return;
			}
			Call(target, "CaptureAll", list6);
			for (int num5 = 0; num5 < list.Count; num5++)
			{
				Call(list[num5], "SetOriginals");
				string[] array = PendingFields[AppearanceKind(list[num5])];
				foreach (string name in array)
				{
					Set(list[num5], name, RimDarkState.Copy(Get(list2[num5], name)));
				}
			}
			if (num4 <= 0f && list7.Count == 0)
			{
				foreach (ThingComp item3 in list)
				{
					Call(item3, "CommitPending");
				}
				Call(target, "CommitImmediately", pawn, GraphicList(list));
			}
			else if (num4 <= 0f)
			{
				Call(target, "SettleFromMap", pawn);
			}
			CloseCustomization(pawn, jobId, windowKind);
		}
	}

	private static bool GeneAppearanceAcceptPrefix(Window __instance)
	{
		if (!Intercept || !CustomizationWindows.TryGetValue(__instance, out var value))
		{
			return true;
		}
		DraftPart draftPart = value.Parts.SingleOrDefault((DraftPart p) => p.Kind == 3);
		if (draftPart == null)
		{
			return false;
		}
		SyncGeneAppearance(value.Pawn, draftPart.Baseline, RimDarkState.Capture(draftPart.Draft, AppearanceFields[3]));
		Set(__instance, "accepted", true);
		__instance.Close();
		return false;
	}

	private static void SyncGeneAppearance(Pawn pawn, string before, string selected)
	{
		if (pawn == null || !pawn.Spawned)
		{
			return;
		}
		ThingComp thingComp = pawn.AllComps.FirstOrDefault(TypeOf("CompGeneCustomization").IsInstanceOfType);
		if (thingComp == null || RimDarkState.Capture(thingComp, AppearanceFields[3]) != before)
		{
			return;
		}
		ThingComp thingComp2 = CopyComp(thingComp);
		RimDarkState.Apply(thingComp2, AppearanceFields[3], selected);
		object[] array = ((IList)Get(thingComp2, "choices")).Cast<object>().ToArray();
		if (array.Select((object target3) => Get(target3, "gene")).Distinct().Count() != array.Length)
		{
			return;
		}
		IList list = (IList)Call(thingComp, "CustomizableGenes");
		using (new RimDarkMapScope(pawn.Map))
		{
			object[] array2 = array;
			foreach (object target in array2)
			{
				object obj = Get(target, "gene");
				object obj2 = Get(target, "variant");
				if (obj == null || obj2 == null || !list.Contains(obj) || !(bool)Call(obj2, "AppliesToGene", obj) || !(bool)Call(obj2, "MeetsRequirements", pawn))
				{
					return;
				}
			}
			object[] old = ((IList)Get(thingComp, "choices")).Cast<object>().ToArray();
			foreach (object gene in old.Select((object target3) => Get(target3, "gene")).Concat(array.Select((object target3) => Get(target3, "gene"))).Distinct())
			{
				if (!(gene is GeneDef { modExtensions: var modExtensions }))
				{
					return;
				}
				DefModExtension defModExtension = modExtensions?.FirstOrDefault(TypeOf("DefModExtension_CustomizableGene").IsInstanceOfType);
				object choice = old.FirstOrDefault((object target3) => Get(target3, "gene") == gene);
				object obj3 = array.FirstOrDefault((object target3) => Get(target3, "gene") == gene);
				if (defModExtension == null || (!(bool)Call(defModExtension, "MeetsRequirements", pawn) && RimDarkState.Capture(new ChoiceHolder
				{
					Choice = choice
				}, new string[1] { "Choice" }) != RimDarkState.Capture(new ChoiceHolder
				{
					Choice = obj3
				}, new string[1] { "Choice" })) || (obj3 == null && !(bool)Get(defModExtension, "allowNone")))
				{
					return;
				}
			}
			object[] array3 = array.Where((object target3) => !old.Any((object item) => Get(item, "gene") == Get(target3, "gene") && Get(item, "variant") == Get(target3, "variant"))).ToArray();
			List<ThingDefCountClass> list2 = new List<ThingDefCountClass>();
			array2 = array3;
			for (int num = 0; num < array2.Length; num++)
			{
				foreach (object item in (IEnumerable)Get(Get(array2[num], "variant"), "costs"))
				{
					if (item != null && TypeOf("GeneVariantCost_Resources").IsInstanceOfType(item))
					{
						Call(TypeOf("UpgradeCostUtility"), "AddCost", list2, Get(item, "cost"));
					}
				}
			}
			if (!CanAfford(pawn, list2))
			{
				return;
			}
			array2 = array3;
			foreach (object target2 in array2)
			{
				object[] parameters = new object[2] { pawn, null };
				object obj4 = Get(target2, "variant");
				if (!(bool)AccessTools.Method(obj4.GetType(), "CanPayCost").Invoke(obj4, parameters))
				{
					return;
				}
			}
			array2 = array3;
			for (int num = 0; num < array2.Length; num++)
			{
				Call(Get(array2[num], "variant"), "PayCost", pawn);
			}
			Call(thingComp, "Restore", Get(thingComp2, "choices"));
			Call(thingComp, "Reconcile");
		}
	}

	private static object SettingsFor(bool genes)
	{
		Type type = AccessTools.TypeByName(genes ? "Genes40k.Genes40kMod" : "Core40k.Core40kMod");
		if (type == null)
		{
			return null;
		}
		Mod mod = LoadedModManager.ModHandles.FirstOrDefault((Mod item) => item.GetType() == type);
		if (mod != null)
		{
			return Get(mod, "Settings");
		}
		return null;
	}

	internal static string SettingsSnapshot(bool genes)
	{
		object obj = SettingsFor(genes);
		if (obj != null)
		{
			return RimDarkState.Capture(obj, genes ? GeneSettingsFields : CoreSettingsFields);
		}
		return null;
	}

	internal static void ApplySettings(bool genes, string text)
	{
		object obj = SettingsFor(genes);
		if (obj != null && text != null)
		{
			RimDarkState.Apply(obj, genes ? GeneSettingsFields : CoreSettingsFields, text);
		}
	}

	private static void InstallSettings()
	{
		MP.RegisterSyncMethod(typeof(RimDark), "SyncSettings");
		Patch("CoreMod", "DoSettingsWindowContents", "SettingsPrefix", null, null, "SettingsFinalizer");
	}

	private static void SettingsPrefix(object __instance, out SettingsEdit __state)
	{
		bool genes = __instance.GetType().FullName == "Genes40k.Genes40kMod";
		__state = (Intercept ? new SettingsEdit
		{
			Genes = genes,
			Before = SettingsSnapshot(genes)
		} : null);
	}

	private static Exception SettingsFinalizer(Exception __exception, SettingsEdit __state)
	{
		if (__state == null)
		{
			return __exception;
		}
		string text = SettingsSnapshot(__state.Genes);
		ApplySettings(__state.Genes, __state.Before);
		if (__exception == null && text != __state.Before)
		{
			SyncSettings(__state.Genes, text);
		}
		return __exception;
	}

	private static void SyncSettings(bool genes, string text)
	{
		object obj = SettingsFor(genes);
		if (obj != null && text != null)
		{
			object obj2 = ShallowCopy.Invoke(obj, null);
			RimDarkState.Apply(obj2, genes ? GeneSettingsFields : CoreSettingsFields, text);
			if ((genes || (!((float)Get(obj2, "appearanceChangeWorkAmount") < 0f) && !((float)Get(obj2, "minimumWorkAmount") < 0f) && !((float)Get(obj2, "repairCostFactor") < 0f))) && (!genes || ((int)Get(obj2, "matrixGestationTimeFactor") >= 1 && (int)Get(obj2, "chapterBaseTraitPicks") >= 0 && (int)Get(obj2, "primarchBaseTraitPicks") >= 0)))
			{
				ApplySettings(genes, text);
			}
		}
	}

	private static void InstallSimulation()
	{
		InstallTimestamps();
		InstallCompiledSimulationAccess();
		Patch("Comp_ConversionField", "DrawField", "VisualRandPrefix", null, null, "VisualRandFinalizer");
		Patch("ArtifactWeaponUtility", "Tick", "ArtifactWorldPrefix");
		Patch("ShieldBashUtility", "Tick", "ShieldWorldPrefix");
		Patch("GameComponent_Devotion", "TryFireIncident", "DevotionMapPrefix", null, null, "MapFinalizer");
		Patch("GeneVariantAvailabilityNotifier", "CheckPawn", "PawnMapPrefix", null, null, "MapFinalizer");
		Patch("RankEligibilityNotifier", "CheckPawn", "PawnMapPrefix", null, null, "MapFinalizer");
		MpCompat.harmony.Patch(AccessTools.PropertyGetter(TypeOf("Gene_DisabledBy"), "Active"), new HarmonyMethod(typeof(RimDark), "GeneActiveReadPrefix"), null, null, new HarmonyMethod(typeof(RimDark), "GeneActiveReadFinalizer"));
		MethodInfo original = AccessTools.DeclaredMethod(typeof(MapComponentUtility), "MapComponentTick");
		MpCompat.harmony.Patch(original, null, new HarmonyMethod(typeof(RimDark), "MapTickPostfix"));
	}

	private static void VisualRandPrefix(out bool __state)
	{
		__state = MP.IsInMultiplayer;
		if (__state)
		{
			Rand.PushState();
		}
	}

	private static Exception VisualRandFinalizer(Exception __exception, bool __state)
	{
		if (__state)
		{
			Rand.PopState();
		}
		return __exception;
	}

	private static void DevotionMapPrefix(out RimDarkMapScope __state)
	{
		__state = (MP.IsInMultiplayer ? new RimDarkMapScope(Find.AnyPlayerHomeMap) : null);
	}

	private static void PawnMapPrefix(Pawn pawn, out RimDarkMapScope __state)
	{
		__state = (MP.IsInMultiplayer ? new RimDarkMapScope(pawn?.MapHeld) : null);
	}

	private static Exception MapFinalizer(Exception __exception, RimDarkMapScope __state)
	{
		__state?.Dispose();
		return __exception;
	}

	private static void GeneActiveReadPrefix(Gene __instance, out ActiveRead __state)
	{
		__state = (Intercept ? new ActiveRead
		{
			Restore = true,
			Override = activeOverride(__instance),
			CachedTick = activeCachedTick(__instance),
			CachedActive = activeCached(__instance),
			Evaluating = activeEvaluating(__instance),
			Extension = activeExtension(__instance),
			ExtensionResolved = activeExtensionResolved(__instance),
			Time = PatchingUtilities.TimeSnapshot.GetAndSetFromPawn(__instance.pawn)
		} : default(ActiveRead));
	}

	private static Exception GeneActiveReadFinalizer(Exception __exception, Gene __instance, ActiveRead __state)
	{
		if (!__state.Restore)
		{
			return __exception;
		}
		try
		{
			activeOverride(__instance) = __state.Override;
			activeCachedTick(__instance) = __state.CachedTick;
			activeCached(__instance) = __state.CachedActive;
			activeEvaluating(__instance) = __state.Evaluating;
			activeExtension(__instance) = __state.Extension;
			activeExtensionResolved(__instance) = __state.ExtensionResolved;
			return __exception;
		}
		finally
		{
			__state.Time?.Set();
		}
	}

	private static bool ArtifactWorldPrefix()
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		TickArtifacts(null);
		return false;
	}

	private static bool ShieldWorldPrefix()
	{
		return !MP.IsInMultiplayer;
	}

	private static void MapTickPostfix(Map map)
	{
		if (MP.IsInMultiplayer)
		{
			TickArtifacts(map);
			TickShieldQueue(map);
		}
	}

	private static void TickArtifacts(Map map)
	{
		IList list = artifactTicking();
		int count = list.Count;
		if (count == 0)
		{
			return;
		}
		ThingComp[] array = ArrayPool<ThingComp>.Shared.Rent(count);
		try
		{
			list.CopyTo(array, 0);
			for (int i = 0; i < count; i++)
			{
				ThingComp thingComp = array[i];
				ThingWithComps thingWithComps = thingComp?.parent;
				Pawn pawn;
				try
				{
					pawn = ((thingComp == null) ? null : artifactWielder(thingComp));
				}
				catch (Exception inner)
				{
					throw new TargetInvocationException(inner);
				}
				if (thingWithComps == null || thingWithComps.Destroyed || pawn == null || pawn.Dead)
				{
					list.Remove(thingComp);
				}
				else if (pawn.MapHeld == map)
				{
					try
					{
						artifactTickWorkers(thingComp, pawn, Find.TickManager.TicksGame);
					}
					catch (Exception inner2)
					{
						throw new TargetInvocationException(inner2);
					}
				}
			}
		}
		finally
		{
			ArrayPool<ThingComp>.Shared.Return(array, clearArray: true);
		}
	}

	private static void TickShieldQueue(Map map)
	{
		IList list = shieldPending();
		if (list.Count == 0)
		{
			return;
		}
		bool flag = shieldApplying();
		shieldApplying() = true;
		try
		{
			int num = 0;
			while (num < list.Count)
			{
				object obj = list[num];
				Pawn pawn = shieldCaster(obj);
				Pawn pawn2 = shieldTarget(obj);
				if (pawn == null || pawn2 == null || pawn.Dead || pawn2.Dead || !pawn.Spawned || !pawn2.Spawned || pawn.Map != pawn2.Map)
				{
					list.RemoveAt(num);
					continue;
				}
				if (pawn.Map != map)
				{
					num++;
					continue;
				}
				list.RemoveAt(num);
				try
				{
					applyShieldEntry(obj);
				}
				catch (Exception inner)
				{
					throw new TargetInvocationException(inner);
				}
			}
		}
		finally
		{
			shieldApplying() = flag;
		}
	}

	private static void InstallCompiledSimulationAccess()
	{
		Type type = TypeOf("CompArtifactWeapon");
		artifactTicking = AccessTools.StaticFieldRefAccess<IList>(AccessTools.DeclaredField(TypeOf("ArtifactWeaponUtility"), "ticking"));
		MethodInfo meth = AccessTools.PropertyGetter(type, "Wielder") ?? throw new MissingMethodException(type.FullName, "get_Wielder");
		DynamicMethod dynamicMethod = new DynamicMethod("RimDark_ArtifactWielder", typeof(Pawn), new Type[1] { typeof(ThingComp) }, typeof(RimDark), skipVisibility: true);
		ILGenerator iLGenerator = dynamicMethod.GetILGenerator();
		iLGenerator.Emit(OpCodes.Ldarg_0);
		iLGenerator.Emit(OpCodes.Castclass, type);
		iLGenerator.Emit(OpCodes.Callvirt, meth);
		iLGenerator.Emit(OpCodes.Ret);
		artifactWielder = dynamicMethod.CreateDelegate<Func<ThingComp, Pawn>>();
		MethodInfo meth2 = AccessTools.DeclaredMethod(type, "TickWorkers", new Type[2]
		{
			typeof(Pawn),
			typeof(int)
		}) ?? throw new MissingMethodException(type.FullName, "TickWorkers(Pawn,int)");
		DynamicMethod dynamicMethod2 = new DynamicMethod("RimDark_ArtifactTick", typeof(void), new Type[3]
		{
			typeof(ThingComp),
			typeof(Pawn),
			typeof(int)
		}, typeof(RimDark), skipVisibility: true);
		iLGenerator = dynamicMethod2.GetILGenerator();
		iLGenerator.Emit(OpCodes.Ldarg_0);
		iLGenerator.Emit(OpCodes.Castclass, type);
		iLGenerator.Emit(OpCodes.Ldarg_1);
		iLGenerator.Emit(OpCodes.Ldarg_2);
		iLGenerator.Emit(OpCodes.Callvirt, meth2);
		iLGenerator.Emit(OpCodes.Ret);
		artifactTickWorkers = dynamicMethod2.CreateDelegate<Action<ThingComp, Pawn, int>>();
		Type type2 = TypeOf("Gene_DisabledBy");
		activeOverride = AccessTools.FieldRefAccess<Gene>(type2, "overriddenByGene");
		activeCachedTick = AccessTools.FieldRefAccess<int>(type2, "cachedTick");
		activeCached = AccessTools.FieldRefAccess<bool>(type2, "cachedActive");
		activeEvaluating = AccessTools.FieldRefAccess<bool>(type2, "evaluating");
		activeExtension = AccessTools.FieldRefAccess<object>(type2, "cachedExtension");
		activeExtensionResolved = AccessTools.FieldRefAccess<bool>(type2, "extensionResolved");
		Type type3 = TypeOf("ShieldBashUtility");
		FieldInfo fieldInfo = AccessTools.DeclaredField(type3, "pending") ?? throw new MissingFieldException(type3.FullName, "pending");
		shieldPending = AccessTools.StaticFieldRefAccess<IList>(fieldInfo);
		shieldApplying = AccessTools.StaticFieldRefAccess<bool>(AccessTools.DeclaredField(type3, "<ApplyingQueued>k__BackingField"));
		Type entry = fieldInfo.FieldType.GetGenericArguments()[0];
		FieldInfo[] array = new string[3] { "Item1", "Item2", "Item3" }.Select((string name) => AccessTools.DeclaredField(entry, name) ?? throw new MissingFieldException(entry.FullName, name)).ToArray();
		shieldCaster = Reader(array[0]);
		shieldTarget = Reader(array[1]);
		MethodInfo meth3 = AccessTools.DeclaredMethod(type3, "Apply", new Type[3]
		{
			typeof(Pawn),
			typeof(Pawn),
			array[2].FieldType
		}) ?? throw new MissingMethodException(type3.FullName, "Apply(Pawn,Pawn,ShieldBashParams)");
		DynamicMethod dynamicMethod3 = new DynamicMethod("RimDark_ApplyQueuedShield", typeof(void), new Type[1] { typeof(object) }, typeof(RimDark), skipVisibility: true);
		iLGenerator = dynamicMethod3.GetILGenerator();
		FieldInfo[] array2 = array;
		foreach (FieldInfo field in array2)
		{
			iLGenerator.Emit(OpCodes.Ldarg_0);
			iLGenerator.Emit(OpCodes.Unbox, entry);
			iLGenerator.Emit(OpCodes.Ldfld, field);
		}
		iLGenerator.Emit(OpCodes.Call, meth3);
		iLGenerator.Emit(OpCodes.Ret);
		applyShieldEntry = dynamicMethod3.CreateDelegate<Action<object>>();
		Func<object, Pawn> Reader(FieldInfo fieldInfo2)
		{
			DynamicMethod dynamicMethod4 = new DynamicMethod("RimDark_Shield" + fieldInfo2.Name, typeof(Pawn), new Type[1] { typeof(object) }, typeof(RimDark), skipVisibility: true);
			ILGenerator iLGenerator2 = dynamicMethod4.GetILGenerator();
			iLGenerator2.Emit(OpCodes.Ldarg_0);
			iLGenerator2.Emit(OpCodes.Unbox, entry);
			iLGenerator2.Emit(OpCodes.Ldfld, fieldInfo2);
			iLGenerator2.Emit(OpCodes.Ret);
			return dynamicMethod4.CreateDelegate<Func<object, Pawn>>();
		}
	}

	private static void InstallTimestamps()
	{
		PatchingUtilities.RegisterTimestampFields(TypeOf("CompRankInfo"), (object value, int offset) =>
		{
			IDictionary dictionary = (IDictionary)Get(value, "daysAsRank");
			object[] array = dictionary.Keys.Cast<object>().ToArray();
			foreach (object key in array)
			{
				dictionary[key] = (int)dictionary[key] + offset;
			}
		});
		PatchingUtilities.RegisterTimestampFields(TypeOf("CompArtifactWeapon"), (object value, int offset) =>
		{
			List<int> list = (List<int>)Get(value, "stageTicks");
			for (int i = 0; i < list.Count; i++)
			{
				list[i] += offset;
			}
		});
		PatchingUtilities.RegisterTimestampFields(TypeOf("ArtifactBearerRecord"), (object value, int offset) =>
		{
			Set(value, "firstTick", (int)Get(value, "firstTick") + offset);
			Set(value, "lastTick", (int)Get(value, "lastTick") + offset);
		});
		PatchingUtilities.RegisterTimestampFields(TypeOf("Comp_PhysicalShield"), (object value, int offset) =>
		{
			Set(value, "lastBlockTick", (int)Get(value, "lastBlockTick") + offset);
			shieldAttack(value) += offset;
		});
		shieldAttack = AccessTools.FieldRefAccess<int>(TypeOf("Comp_PhysicalShield"), "lastAttackedTick");
		Patch("Comp_PhysicalShield", "PostExposeData", null, "ShieldAttackSavePostfix");
		Patch("CompRankInfo", "GetDaysAsRank", "RankReadPrefix", null, null, "RankReadFinalizer");
		injectorRearm = AccessTools.FieldRefAccess<int>(TypeOf("HediffComp_AutoInjector"), "rearmedOnTick");
		PatchingUtilities.RegisterTimestampFields(TypeOf("HediffComp_AutoInjector"), (object value, int offset) =>
		{
			InjectorClockState orCreateValue = InjectorClocks.GetOrCreateValue(value);
			if (injectorRearm(value) >= 0 || orCreateValue.ValidNegative)
			{
				injectorRearm(value) += offset;
				orCreateValue.ValidNegative = injectorRearm(value) < 0;
			}
		});
		MpCompat.harmony.Patch(AccessTools.PropertyGetter(TypeOf("HediffComp_AutoInjector"), "Armed"), null, null, new HarmonyMethod(typeof(RimDark), "InjectorMarkerTranspiler"));
		Patch("HediffComp_AutoInjector", "CompExposeData", null, "InjectorSavePostfix");
	}

	private static void ShieldAttackSavePostfix(object __instance)
	{
		if (MP.IsInMultiplayer)
		{
			Scribe_Values.Look(ref shieldAttack(__instance), "mpLastAttackedTick", -99999);
		}
	}

	private static void RankReadPrefix(ThingComp __instance, out PatchingUtilities.TimeSnapshot? __state)
	{
		__state = (MP.IsInMultiplayer ? PatchingUtilities.TimeSnapshot.GetAndSetFromPawn(__instance.parent as Pawn) : ((PatchingUtilities.TimeSnapshot?)null));
	}

	private static Exception RankReadFinalizer(Exception __exception, PatchingUtilities.TimeSnapshot? __state)
	{
		__state?.Set();
		return __exception;
	}

	private static void InjectorSavePostfix(object __instance)
	{
		if (MP.IsInMultiplayer)
		{
			InjectorClockState orCreateValue = InjectorClocks.GetOrCreateValue(__instance);
			if (Scribe.mode == LoadSaveMode.Saving && injectorRearm(__instance) >= 0)
			{
				orCreateValue.ValidNegative = false;
			}
			Scribe_Values.Look(ref orCreateValue.ValidNegative, "mpRearmIsValidNegative", defaultValue: false);
		}
	}

	private static int ReadInjectorMarker(object comp)
	{
		if (!MP.IsInMultiplayer || injectorRearm(comp) >= 0 || !InjectorClocks.TryGetValue(comp, out var value) || !value.ValidNegative)
		{
			return injectorRearm(comp);
		}
		return 0;
	}

	private static IEnumerable<CodeInstruction> InjectorMarkerTranspiler(IEnumerable<CodeInstruction> input)
	{
		List<CodeInstruction> list = input.ToList();
		FieldInfo field = AccessTools.DeclaredField(TypeOf("HediffComp_AutoInjector"), "rearmedOnTick");
		int num = 0;
		for (int i = 0; i + 1 < list.Count; i++)
		{
			if (list[i].LoadsField(field) && list[i + 1].LoadsConstant(0L))
			{
				list[i].opcode = OpCodes.Call;
				list[i].operand = AccessTools.DeclaredMethod(typeof(RimDark), "ReadInjectorMarker");
				num++;
			}
		}
		if (num != 1)
		{
			throw new InvalidOperationException("RimDark injector marker site changed: " + num);
		}
		return list;
	}
}
