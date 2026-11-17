using System;
using System.Buffers;
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
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Phonicmas.RimDark.MankindsFinest")]
public class RimDarkGenes
{
	private sealed class ColorDraft
	{
		internal object Original;

		internal bool Flag;
	}

	private sealed class DesignHistory
	{
		internal List<IExposable> Designs = new List<IExposable>();
	}

	private sealed record DesignPayload(int Operation, int EditingId, Def Template, string Name, Def Icon, GeneDef Primarch, Def[] Traits, Def[] Disciplines, int[] Levels, Thing Gestator) : ISyncSimple;

	private sealed class PresenceLoad
	{
		internal bool Restored;
	}

	private const string Ns = "Genes40k.";

	private static readonly string[] ColorSettingsFields = new string[5] { "chapterColorOne", "chapterColorTwo", "chapterColorThree", "chapterShoulderIcon", "chapterShoulderIconColor" };

	private static readonly string[] ColorPresetFields = new string[6] { "primaryColour", "secondaryColour", "tertiaryColour", "relatedChapterIcon", "chapterIconColour", "colorAmount" };

	private static readonly string[] FlagFields = new string[4] { "drawColorOne", "drawColorTwo", "flagInsigniaFilePath", "currentlySelectedPreset" };

	private static readonly ConditionalWeakTable<Window, ColorDraft> ColorDrafts = new ConditionalWeakTable<Window, ColorDraft>();

	private static readonly Dictionary<MethodBase, int> BuildingActions = new Dictionary<MethodBase, int>();

	private static readonly ConditionalWeakTable<object, DesignHistory> DesignHistories = new ConditionalWeakTable<object, DesignHistory>();

	private static readonly MethodInfo ReadOwnerTicks = AccessTools.Method("Multiplayer.Client.SimulationTimeContext:ReadTicks");

	private static Type unyieldingType;

	private static FieldInfo unyieldingLastMoved;

	private static Type perpetualGeneType;

	private static AccessTools.FieldRef<object, Dictionary<Pawn, int>> perpetualPending;

	private static AccessTools.FieldRef<object, int> perpetualCurrentTick;

	private static Action<Pawn> keepPerpetualPawn;

	private static Type presenceType;

	private static FieldInfo presenceCounts;

	private static readonly ConditionalWeakTable<object, PresenceLoad> PresenceLoads = new ConditionalWeakTable<object, PresenceLoad>();

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

	private static object Designs => Get(TypeOf("GameComponent_CustomChapterGenes"), "Instance");

	private static object ColorSettings
	{
		get
		{
			Mod mod = LoadedModManager.ModHandles.FirstOrDefault((Mod m) => m.GetType() == TypeOf("Genes40kMod"));
			if (mod != null)
			{
				return Get(mod, "Settings");
			}
			return null;
		}
	}

	public RimDarkGenes(ModContentPack mod)
	{
		PatchingUtilities.SetupAsyncTime();
		InstallDesigns();
		InstallCommands();
		InstallColors();
		InstallSimulation();
	}

	private static Type TypeOf(string name)
	{
		return AccessTools.TypeByName("Genes40k." + name) ?? throw new TypeLoadException("Genes40k." + name);
	}

	private static object Get(object obj, string name)
	{
		if (obj == null)
		{
			return null;
		}
		Type type = (obj as Type) ?? obj.GetType();
		object obj2 = ((obj is Type) ? null : obj);
		FieldInfo fieldInfo = AccessTools.Field(type, name);
		if ((object)fieldInfo == null)
		{
			return (AccessTools.Property(type, name) ?? throw new MissingMemberException(type.FullName, name)).GetValue(obj2);
		}
		return fieldInfo.GetValue(obj2);
	}

	private static void Set(object obj, string name, object value)
	{
		Type type = (obj as Type) ?? obj.GetType();
		object obj2 = ((obj is Type) ? null : obj);
		FieldInfo fieldInfo = AccessTools.Field(type, name);
		if ((object)fieldInfo != null)
		{
			fieldInfo.SetValue(obj2, value);
		}
		else
		{
			(AccessTools.Property(type, name) ?? throw new MissingMemberException(type.FullName, name)).SetValue(obj2, value);
		}
	}

	private static object Call(object obj, string name, params object[] args)
	{
		Type obj2 = (obj as Type) ?? obj.GetType();
		List<MethodInfo> list = new List<MethodInfo>();
		Type type = obj2;
		while (type != null)
		{
			list.AddRange(AccessTools.GetDeclaredMethods(type));
			type = type.BaseType;
		}
		return (from m in list
			group m by m.Name + "(" + string.Join(",", from p in m.GetParameters()
				select p.ParameterType.FullName) + ")" into g
			select g.First()).Single((MethodInfo m) => m.Name == name && m.GetParameters().Length == args.Length && m.GetParameters().Select((ParameterInfo p, int i) =>
		{
			if (args[i] != null)
			{
				return p.ParameterType.IsInstanceOfType(args[i]);
			}
			return !p.ParameterType.IsValueType || Nullable.GetUnderlyingType(p.ParameterType) != null;
		}).All((bool x) => x)).Invoke((obj is Type) ? null : obj, args);
	}

	private static void Patch(string type, string method, string prefix = null, string postfix = null, string transpiler = null, string finalizer = null)
	{
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(TypeOf(type), method) ?? throw new MissingMethodException("Genes40k." + type, method), Hook(prefix), Hook(postfix), Hook(transpiler), Hook(finalizer));
		static HarmonyMethod Hook(string n)
		{
			if (n != null)
			{
				return new HarmonyMethod(typeof(RimDarkGenes), n);
			}
			return null;
		}
	}

	private static object Design(int id)
	{
		if (id >= 0 && Designs != null)
		{
			return Call(Designs, "Get", id);
		}
		return null;
	}

	private static int Id(object design)
	{
		if (design != null)
		{
			return (int)Get(design, "id");
		}
		return -1;
	}

	private static IList TypedList(string type, IEnumerable<object> items)
	{
		IList list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(TypeOf(type)));
		foreach (object item in items)
		{
			list.Add(item);
		}
		return list;
	}

	private static object Clone(object value)
	{
		return AccessTools.Method(typeof(object), "MemberwiseClone").Invoke(value, null);
	}

	private static object Raw(string type)
	{
		return RuntimeHelpers.GetUninitializedObject(TypeOf(type));
	}

	private static void InstallColors()
	{
		MP.RegisterSyncMethod(typeof(RimDarkGenes), "SyncColors");
		MP.RegisterSyncMethod(typeof(RimDarkGenes), "SyncFlag");
		string[] array = new string[2] { "Dialog_ChangeDefaultChapterColour", "Dialog_ChangeFlagColour" };
		foreach (string text in array)
		{
			MpCompat.harmony.Patch(AccessTools.GetDeclaredConstructors(TypeOf(text)).Single(), new HarmonyMethod(typeof(RimDarkGenes), "ColorConstructorPrefix"), new HarmonyMethod(typeof(RimDarkGenes), "ColorConstructorPostfix"));
			Patch(text, "DoWindowContents", null, null, "ColorAcceptTranspiler");
		}
		Patch("Genes40kModSettings", "ExposeData", "SettingsSavePrefix", null, null, "SettingsSaveFinalizer");
	}

	private static object CloneColorSettings(object original)
	{
		object obj = Clone(original);
		object obj2 = Get(original, "CustomPreset");
		object value = Clone(obj2);
		Set(obj, "customPreset", value);
		if (Get(original, "CurrentlySelectedPreset") == obj2)
		{
			Set(obj, "CurrentlySelectedPreset", value);
		}
		return obj;
	}

	private static void ColorConstructorPrefix(object[] __args, out ColorDraft __state)
	{
		__state = null;
		if (Intercept)
		{
			object obj = __args[0];
			bool flag = obj is Thing;
			object obj2 = (flag ? Clone(obj) : CloneColorSettings(obj));
			if (flag)
			{
				Set(obj2, "flagInsigniaGraphics", new Graphic[4]);
			}
			__args[0] = obj2;
			__state = new ColorDraft
			{
				Original = obj,
				Flag = flag
			};
		}
	}

	private static void ColorConstructorPostfix(Window __instance, ColorDraft __state)
	{
		if (__state != null)
		{
			ColorDrafts.Add(__instance, __state);
		}
	}

	private static IEnumerable<CodeInstruction> ColorAcceptTranspiler(IEnumerable<CodeInstruction> input)
	{
		int close = 0;
		MethodInfo target = AccessTools.DeclaredMethod(typeof(Window), "Close");
		foreach (CodeInstruction item in input)
		{
			if (item.Calls(target))
			{
				int num = close + 1;
				close = num;
				if (num == 1)
				{
					item.opcode = OpCodes.Call;
					item.operand = AccessTools.Method(typeof(RimDarkGenes), "AcceptColorDraft");
				}
			}
			yield return item;
		}
		if (close != 2)
		{
			throw new InvalidOperationException("Genes colour Accept/Close changed: " + close);
		}
	}

	private static void AcceptColorDraft(Window window, bool sound)
	{
		if (ColorDrafts.TryGetValue(window, out var value))
		{
			ColorDrafts.Remove(window);
			if (value.Flag)
			{
				SyncFlag(value.Original as Thing, RimDarkState.Capture(Get(window, "decoFlag"), FlagFields));
			}
			else
			{
				object obj = Get(window, "settings");
				SyncColors(RimDarkState.Capture(obj, ColorSettingsFields), RimDarkState.Capture(Get(obj, "CustomPreset"), ColorPresetFields), SelectedPresetName(obj));
			}
		}
		window.Close(sound);
	}

	private static string SelectedPresetName(object settings)
	{
		object obj = Get(settings, "CurrentlySelectedPreset");
		string text;
		if (obj != Get(settings, "CustomPreset"))
		{
			text = ((Def)obj)?.defName;
			if (text == null)
			{
				return "";
			}
		}
		else
		{
			text = "";
		}
		return text;
	}

	internal static string[] CaptureColors()
	{
		object colorSettings = ColorSettings;
		if (colorSettings != null)
		{
			return new string[3]
			{
				RimDarkState.Capture(colorSettings, ColorSettingsFields),
				RimDarkState.Capture(Get(colorSettings, "CustomPreset"), ColorPresetFields),
				SelectedPresetName(colorSettings)
			};
		}
		return null;
	}

	internal static void ApplyColors(string[] values)
	{
		if (values == null || values.Length != 3)
		{
			return;
		}
		object colorSettings = ColorSettings;
		if (colorSettings != null)
		{
			object obj = CloneColorSettings(colorSettings);
			RimDarkState.Apply(obj, ColorSettingsFields, values[0]);
			RimDarkState.Apply(Get(obj, "CustomPreset"), ColorPresetFields, values[1]);
			object obj2 = ((values[2] == "") ? Get(obj, "CustomPreset") : GenDefDatabase.GetDef(TypeOf("ChapterColourDef"), values[2], errorOnFail: false));
			if (obj2 != null)
			{
				RimDarkState.Apply(colorSettings, ColorSettingsFields, values[0]);
				RimDarkState.Apply(Get(colorSettings, "CustomPreset"), ColorPresetFields, values[1]);
				Set(colorSettings, "CurrentlySelectedPreset", (values[2] == "") ? Get(colorSettings, "CustomPreset") : obj2);
			}
		}
	}

	private static void SyncColors(string settings, string custom, string selected)
	{
		ApplyColors(new string[3] { settings, custom, selected });
	}

	private static void SyncFlag(Thing flag, string text)
	{
		if (flag == null || !flag.Spawned || flag.GetType() != TypeOf("Building_DecorativeFlag") || text == null)
		{
			return;
		}
		object obj = Clone(flag);
		RimDarkState.Apply(obj, FlagFields, text);
		string path = Get(obj, "flagInsigniaFilePath") as string;
		if (path != null && !GenDefDatabase.GetAllDefsInDatabaseForDef(TypeOf("FlagIconDef")).Cast<Def>().Any((Def d) => (string)Get(d, "iconPath") == path))
		{
			return;
		}
		using (new RimDarkMapScope(flag.Map))
		{
			RimDarkState.Apply(flag, FlagFields, text);
			Call(flag, "SetFlagInsignia", path, path == null);
			Call(flag, "SetOriginals");
			flag.Notify_ColorChanged();
		}
	}

	private static void SettingsSavePrefix(object __instance, out object __state)
	{
		__state = ((MP.IsInMultiplayer && Scribe.mode == LoadSaveMode.Saving) ? Get(__instance, "currentlySelectedPreset") : null);
	}

	private static Exception SettingsSaveFinalizer(object __instance, object __state, Exception __exception)
	{
		if (__state != null)
		{
			Set(__instance, "currentlySelectedPreset", __state);
		}
		return __exception;
	}

	private static void InstallCommands()
	{
		MP.RegisterSyncMethod(typeof(RimDarkGenes), "SyncBuilding");
		MP.RegisterSyncMethod(typeof(RimDarkGenes), "SyncCraft");
		MP.RegisterSyncMethod(typeof(RimDarkGenes), "SyncSurgery");
		Patch("ChapterChoiceUtility", "CreateBill", "ChapterSurgeryPrefix");
		Patch("GeneseedVialKindUtility", "CreateBill", "VialSurgeryPrefix");
		BindBuilding("Building_GeneGestator", "GetGizmos", 1, 0);
		BindBuilding("Building_GeneGestator", "GetGizmos", 2, 1);
		BindBuilding("Building_GeneGestator", "GetGizmos", 3, 2);
		BindBuilding("Building_GeneGestator", "GetGizmos", 6, 3);
		BindBuilding("Building_GeneGestator", "GetGizmos", 7, 4);
		BindBuilding("Building_GeneGestator", "GetGizmos", 9, 5);
		BindBuilding("Building_GeneGestator", "GetGizmos", 10, 6);
		BindBuilding("Building_GeneGestator", "GetGizmos", 11, 7);
		MpCompat.harmony.Patch(MpMethodUtil.GetLambda(TypeOf("Building_GeneGestator"), "GetGizmos", MethodType.Normal, null, 5), null, null, new HarmonyMethod(typeof(RimDarkGenes), "StartStoreTranspiler"));
		BindBuilding("Building_PrimarchGrowthVat", "GetGizmos", 2, 10);
		BindBuilding("Building_PrimarchGrowthVat", "GetGizmos", 5, 11);
		BindBuilding("Building_PrimarchGrowthVat", "GetGizmos", 6, 12);
		MethodInfo methodInfo = AccessTools.GetDeclaredMethods(TypeOf("Building_PrimarchGrowthVat")).Single((MethodInfo m) => m.Name.StartsWith("<GetGizmos>g__Action|", StringComparison.Ordinal));
		BuildingActions.Add(methodInfo, 13);
		MpCompat.harmony.Patch(methodInfo, new HarmonyMethod(typeof(RimDarkGenes), "BuildingActionPrefix"));
		for (int num = 1; num <= 4; num++)
		{
			MpCompat.RegisterLambdaMethod(TypeOf("Building_PrimarchGrowthVat"), "DebugGizmo", num)[0].SetDebugOnly();
		}
		MpCompat.RegisterLambdaMethod(TypeOf("Gene_WarpShield"), "GetGizmos", 1);
		MpCompat.RegisterLambdaMethod(TypeOf("Gene_Perpetual"), "GetGizmos", 1);
		ISyncMethod[] array = MpCompat.RegisterLambdaMethod(TypeOf("Gene_DivineGrace"), "GetGizmos", 0, 1);
		for (int num2 = 0; num2 < array.Length; num2++)
		{
			array[num2].SetDebugOnly();
		}
		MP.RegisterSyncMethod(TypeOf("Gene_DivineGrace"), "SetTargetValuePct");
		MP.RegisterSyncMethod(TypeOf("ScenPart_StartingChapter"), "ApplyChapter");
		Patch("Dialog_CraftPrimarchEmbryo", "StartCraft", "CraftPrefix");
	}

	private static void BindBuilding(string type, string parent, int ordinal, int operation)
	{
		MethodInfo lambda = MpMethodUtil.GetLambda(TypeOf(type), parent, MethodType.Normal, null, ordinal);
		BuildingActions.Add(lambda, operation);
		MpCompat.harmony.Patch(lambda, new HarmonyMethod(typeof(RimDarkGenes), "BuildingActionPrefix"));
	}

	private static object Captured(object closure, string field)
	{
		if (closure == null)
		{
			return null;
		}
		FieldInfo fieldInfo = AccessTools.Field(closure.GetType(), field);
		if ((object)fieldInfo != null)
		{
			return fieldInfo.GetValue(closure);
		}
		foreach (FieldInfo item in from f in AccessTools.GetDeclaredFields(closure.GetType())
			where f.Name.StartsWith("CS$<>8__locals", StringComparison.Ordinal)
			select f)
		{
			object obj = Captured(item.GetValue(closure), field);
			if (obj != null)
			{
				return obj;
			}
		}
		return null;
	}

	private static bool BuildingActionPrefix(object __instance, MethodBase __originalMethod)
	{
		if (!Intercept)
		{
			return true;
		}
		Thing building = (__instance as Thing) ?? (Captured(__instance, "<>4__this") as Thing);
		int num = BuildingActions[__originalMethod];
		SyncBuilding(building, num, Captured(__instance, (num == 1) ? "material" : "matrix") as Def, (num == 2) ? Id(Captured(__instance, "customChapter")) : (-1), (num == 11) ? (Captured(__instance, "embryo") as Thing) : null);
		return false;
	}

	private static IEnumerable<CodeInstruction> StartStoreTranspiler(IEnumerable<CodeInstruction> input)
	{
		int changed = 0;
		FieldInfo field = AccessTools.DeclaredField(TypeOf("Building_GeneGestator"), "doWork");
		foreach (CodeInstruction item in input)
		{
			if (item.StoresField(field))
			{
				changed++;
				item.opcode = OpCodes.Call;
				item.operand = AccessTools.Method(typeof(RimDarkGenes), "StartStore");
			}
			yield return item;
		}
		if (changed != 2)
		{
			throw new InvalidOperationException("GeneGestator start sites changed: " + changed);
		}
	}

	private static void StartStore(object building, bool value)
	{
		if (Intercept)
		{
			SyncBuilding(building as Thing, 3, null, -1, null);
		}
		else
		{
			Set(building, "doWork", value);
		}
	}

	private static void SyncBuilding(Thing building, int operation, Def def, int designId, Thing embryo)
	{
		if (building == null || !building.Spawned || building.Destroyed)
		{
			return;
		}
		using (new RimDarkMapScope(building.Map))
		{
			if (operation < 10)
			{
				if (building.GetType() != TypeOf("Building_GeneGestator") || (operation != 7 && (bool)Get(building, "doWork")))
				{
					return;
				}
				object obj = Get(building, "containedMatrix");
				if (operation <= 4 && obj == null)
				{
					return;
				}
				switch (operation)
				{
				case 0:
					Set(building, "selectedMaterial", null);
					Set(building, "selectedCustomChapterId", -1);
					break;
				case 1:
					if (def is ThingDef && (bool)Call(building, "CanUseAnyMaterial"))
					{
						bool flag = (bool)Get(Get(building, "ContainedMatrixMod"), "canUsePrimarchMaterial");
						if (((IList)Get(Get(building, "GameComp"), flag ? "UnlockedPrimarchMaterial" : "UnlockedChapterMaterial")).Contains(def))
						{
							Set(building, "selectedMaterial", def);
							Set(building, "selectedCustomChapterId", -1);
						}
					}
					break;
				case 2:
				{
					object obj2 = Design(designId);
					if (obj2 != null && !(bool)Get(obj2, "adHoc") && (bool)Call(building, "CanUseAnyMaterial"))
					{
						string text = (((bool)Get(Get(building, "ContainedMatrixMod"), "canUsePrimarchMaterial")) ? "Primarch" : "Chapter");
						if (!(Get(obj2, "Kind").ToString() != text))
						{
							Set(building, "selectedMaterial", null);
							Set(building, "selectedCustomChapterId", designId);
						}
					}
					break;
				}
				case 3:
					if ((bool)Get(building, "PowerOn"))
					{
						Set(building, "doWork", true);
					}
					break;
				case 4:
					Call(building, "TryDropContainedMatrix");
					Call(building, "Reset");
					break;
				case 5:
					if (def is ThingDef && ((IList)Call(building, "AvailableGestatables")).Contains(def))
					{
						Set(building, "selectedMatrix", def);
						if (obj != null)
						{
							Call(building, "TryDropContainedMatrix");
						}
					}
					break;
				case 6:
					Set(building, "selectedMatrix", null);
					break;
				case 7:
					if (!MP.IsInMultiplayer || DebugSettings.ShowDevGizmos)
					{
						Set(building, "progressInt", (float)Get(building, "TotalTimeAdjusted") - 250f);
					}
					break;
				}
			}
			else
			{
				if (building.GetType() != TypeOf("Building_PrimarchGrowthVat"))
				{
					return;
				}
				bool flag2 = (bool)Get(building, "Working");
				switch (operation)
				{
				case 10:
					if (!flag2 && Get(building, "containedEmbryo") is Thing thing && GenPlace.TryPlaceThing(thing, building.InteractionCell, building.Map, ThingPlaceMode.Near))
					{
						Set(building, "containedEmbryo", null);
						Call(building, "OnStop");
					}
					break;
				case 11:
					if (!flag2 && Get(building, "containedEmbryo") == null && embryo != null && embryo.Spawned && embryo.Map == building.Map && ((IList)Call(building, "AvailableEmbryo")).Contains(embryo))
					{
						Set(building, "selectedEmbryo", embryo);
					}
					break;
				case 12:
					if (!flag2)
					{
						Set(building, "selectedEmbryo", null);
					}
					break;
				case 13:
					if (!flag2 && Get(building, "containedEmbryo") != null && (bool)Get(building, "PowerOn"))
					{
						((MethodInfo)BuildingActions.Single((KeyValuePair<MethodBase, int> p) => p.Value == 13).Key).Invoke(building, null);
					}
					break;
				}
			}
		}
	}

	private static bool CraftPrefix(object __instance)
	{
		if (!Intercept)
		{
			return true;
		}
		SyncCraft(Get(__instance, "geneTable") as Thing, Get(__instance, "chosenGeneseedVial") as Thing, Get(__instance, "chosenEmbryo") as Thing, Get(__instance, "chosenPawn") as Pawn);
		((Window)__instance).Close();
		return false;
	}

	private static void SyncCraft(Thing table, Thing vial, Thing embryo, Pawn pawn)
	{
		if (table == null || !table.Spawned || table.GetType() != TypeOf("Building_GeneTable") || vial == null || !vial.Spawned || vial.GetType() != TypeOf("GeneseedVial") || !(embryo is HumanEmbryo) || !embryo.Spawned || pawn == null || !pawn.Spawned || table.Map != vial.Map || table.Map != embryo.Map || table.Map != pawn.Map)
		{
			return;
		}
		using (new RimDarkMapScope(table.Map))
		{
			object obj = Raw("Dialog_CraftPrimarchEmbryo");
			Set(obj, "map", table.Map);
			Set(obj, "geneTable", table);
			Set(obj, "recipe", Get(TypeOf("Genes40kDefOf"), "BEWH_MakePrimarchEmbryo"));
			Set(obj, "chosenGeneseedVial", vial);
			Set(obj, "chosenEmbryo", embryo);
			Set(obj, "chosenPawn", pawn);
			if (Call(obj, "MissingSelectionReason") == null && Call(obj, "PawnBlockReason", pawn) == null && Call(obj, "IngredientBlockReason", vial, new List<Pawn> { pawn }) == null && Call(obj, "IngredientBlockReason", embryo, new List<Pawn> { pawn }) == null)
			{
				Call(obj, "StartCraft");
			}
		}
	}

	private static bool ChapterSurgeryPrefix(Pawn medPawn, RecipeDef recipe, BodyPartRecord part, object choice)
	{
		if (!Intercept)
		{
			return true;
		}
		SyncSurgery(medPawn, recipe, part, Get(choice, "chapterGene") as GeneDef, null, implant: false);
		return false;
	}

	private static bool VialSurgeryPrefix(Pawn medPawn, RecipeDef recipe, BodyPartRecord part, object kind)
	{
		if (!Intercept)
		{
			return true;
		}
		SyncSurgery(medPawn, recipe, part, Get(kind, "chapterGene") as GeneDef, Get(kind, "vialDef") as ThingDef, implant: true);
		return false;
	}

	private static void SyncSurgery(Pawn pawn, RecipeDef recipe, BodyPartRecord part, GeneDef chapter, ThingDef vial, bool implant)
	{
		if (pawn == null || !pawn.Spawned || pawn.Dead || recipe == null || !recipe.AvailableNow || (part != null && !pawn.RaceProps.body.AllParts.Contains(part)))
		{
			return;
		}
		using (new RimDarkMapScope(pawn.Map))
		{
			if (recipe.Worker.AvailableOnNow(pawn, part))
			{
				string name = (implant ? "GeneseedVialKindUtility" : "ChapterChoiceUtility");
				object obj = ((IEnumerable)(implant ? Call(TypeOf(name), "KindsFor", pawn.Map) : Call(TypeOf(name), "Choices"))).Cast<object>().FirstOrDefault((object choice) => (GeneDef)Get(choice, "chapterGene") == chapter && (!implant || (ThingDef)Get(choice, "vialDef") == vial) && (bool)Get(choice, "Available"));
				if (obj != null)
				{
					Call(TypeOf(name), "CreateBill", pawn, recipe, part, obj);
				}
			}
		}
	}

	private static void InstallDesigns()
	{
		MP.RegisterSyncMethod(typeof(RimDarkGenes), "SyncDesign");
		Patch("Dialog_CreateChapterGene", "Accept", "ChapterAcceptPrefix");
		Patch("Dialog_CustodesCustomization", "SavePreset", "CustodesSavePrefix");
		Patch("Dialog_CustodesCustomization", "SaveAsPreset", "CustodesAsPrefix");
		Patch("Dialog_CustodesCustomization", "AcceptForGestator", "CustodesAssignPrefix");
		Patch("GameComponent_CustomChapterGenes", "Remove", "RemoveDesignPrefix");
		Patch("CustomChapterGeneDefBuilder", "NewDefName", "DefNamePrefix");
		Patch("GameComponent_CustomChapterGenes", "Add", null, "AddedDesign");
		Patch("GameComponent_CustomChapterGenes", "ExposeData", "DesignHistoryScribe");
	}

	private static void AddedDesign(object __instance, IExposable __result)
	{
		if (MP.IsInMultiplayer && __result != null)
		{
			List<IExposable> designs = DesignHistories.GetOrCreateValue(__instance).Designs;
			if (!designs.Any((IExposable item) => Id(item) == Id(__result)))
			{
				designs.Add(__result);
			}
		}
	}

	private static void DesignHistoryScribe(object __instance)
	{
		DesignHistory orCreateValue = DesignHistories.GetOrCreateValue(__instance);
		if (Scribe.mode == LoadSaveMode.Saving)
		{
			IEnumerable<IExposable> second = ((IEnumerable)Get(__instance, "chapters")).Cast<IExposable>();
			orCreateValue.Designs = (from @group in orCreateValue.Designs.Concat(second).GroupBy(Id)
				select @group.Last()).OrderBy(Id).ToList();
		}
		Scribe_Collections.Look(ref orCreateValue.Designs, "mpGeneratedDesignHistory", LookMode.Deep);
		if (orCreateValue.Designs == null)
		{
			orCreateValue.Designs = new List<IExposable>();
		}
	}

	private static bool DefNamePrefix(object template, ref string __result)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		string text = ((template == null) ? "Chapter" : Get(template, "kind").ToString());
		string str = Find.World?.info?.seedString ?? "";
		int num = ((Designs != null) ? ((int)Get(Designs, "nextId")) : 0);
		__result = "BEWH_MP_" + text + "_" + GenText.StableStringHash(str).ToString("X8") + "_" + num;
		return false;
	}

	private static bool ChapterAcceptPrefix(object __instance)
	{
		if (!Intercept)
		{
			return true;
		}
		if (!(bool)Call(__instance, "CanAccept"))
		{
			return false;
		}
		SendDesign(__instance, 0);
		((Window)__instance).Close();
		return false;
	}

	private static bool CustodesSavePrefix(object __instance)
	{
		return CustodesPrefix(__instance, 1);
	}

	private static bool CustodesAsPrefix(object __instance)
	{
		return CustodesPrefix(__instance, 2);
	}

	private static bool CustodesAssignPrefix(object __instance)
	{
		return CustodesPrefix(__instance, 3);
	}

	private static bool CustodesPrefix(object window, int operation)
	{
		if (!Intercept)
		{
			return true;
		}
		if ((bool)Get(window, "readOnly"))
		{
			return false;
		}
		if ((operation != 1 || Get(window, "editing") == null || !(bool)Get(Get(window, "editing"), "locked")) && !(bool)Call(window, "ValidateLevels"))
		{
			return false;
		}
		if (operation == 1 && !(bool)Call(window, "ValidateName", Get(window, "editing")))
		{
			return false;
		}
		if (operation == 2 && string.IsNullOrWhiteSpace(Get(window, "presetName") as string))
		{
			return false;
		}
		SendDesign(window, operation);
		if (operation != 2)
		{
			((Window)window).Close();
		}
		return false;
	}

	private static void SendDesign(object window, int operation)
	{
		object[] source = ((operation == 0) ? Array.Empty<object>() : ((IEnumerable)Get(window, "CurrentLevels")).Cast<object>().ToArray());
		SyncDesign(new DesignPayload(operation, Id(Get(window, "editing")), Get(window, "template") as Def, Get(window, (operation == 0) ? "chapterName" : "presetName") as string, Get(window, "flagIconDef") as Def, (operation == 0) ? (Get(window, "relatedPrimarchGene") as GeneDef) : null, (operation == 0) ? ((IEnumerable)Get(window, "selected")).Cast<Def>().ToArray() : Array.Empty<Def>(), source.Select((object x) => (Def)Get(x, "def")).ToArray(), source.Select((object x) => (int)Get(x, "level")).ToArray(), (operation == 3) ? (Get(window, "gestator") as Thing) : null));
	}

	private static bool RemoveDesignPrefix(object chapter, ref bool __result)
	{
		if (!Intercept)
		{
			return true;
		}
		SyncDesign(new DesignPayload(4, Id(chapter), null, null, null, null, null, null, null, null));
		__result = false;
		return false;
	}

	private static void SyncDesign(DesignPayload payload)
	{
		if (payload == null)
		{
			return;
		}
		var (num3, num4, def3, text2, def4, geneDef2, array4, array5, array6, thing2) = payload;
		object designs = Designs;
		if (designs == null || num3 < 0 || num3 > 4)
		{
			return;
		}
		object obj = Design(num4);
		if (num4 >= 0 && obj == null)
		{
			return;
		}
		if (num3 == 4)
		{
			if (obj != null)
			{
				Call(designs, "Remove", obj);
			}
		}
		else
		{
			if (def3?.GetType() != TypeOf("CustomChapterGeneTemplateDef") || def4?.GetType() != TypeOf("FlagIconDef") || (obj != null && Get(obj, "Template") != def3) || (num3 != 3 && (string.IsNullOrWhiteSpace(text2) || text2.Length > 200)))
			{
				return;
			}
			using (new RimDarkGenesWorldScope())
			{
				if (num3 == 0)
				{
					if ((obj != null && (bool)Get(obj, "locked")) || array4 == null || array4.Length > 512 || array4.Any((Def t) => t?.GetType() != TypeOf("ChapterTraitDef")) || array4.Distinct().Count() != array4.Length)
					{
						return;
					}
					object obj2 = Raw("Dialog_CreateChapterGene");
					Set(obj2, "editing", obj);
					Set(obj2, "template", def3);
					Set(obj2, "chapterName", text2);
					Set(obj2, "flagIconDef", def4);
					Set(obj2, "relatedPrimarchGene", geneDef2);
					Set(obj2, "selected", TypedList("ChapterTraitDef", array4));
					if ((bool)Call(obj2, "CanAccept"))
					{
						if (obj == null)
						{
							Call(designs, "Add", def3, text2.Trim(), def4, Get(obj2, "selected"), null, (Get(def3, "kind").ToString() == "Chapter") ? geneDef2 : null, false);
						}
						else
						{
							Set(obj, "name", text2.Trim());
							Set(obj, "flagIconDef", def4);
							Set(obj, "traits", Get(obj2, "selected"));
							Set(obj, "RelatedPrimarchGene", (Get(def3, "kind").ToString() == "Chapter") ? geneDef2 : null);
							Call(obj, "RefreshGeneDef");
						}
					}
				}
				else
				{
					if (Get(def3, "kind").ToString() != "Custodes" || array5 == null || array6 == null || array5.Length != array6.Length || array6.Length > 128 || array5.Distinct().Count() != array5.Length || array5.Any((Def d) => d?.GetType() != TypeOf("CustodesDisciplineDef")))
					{
						return;
					}
					object dialog = Raw("Dialog_CustodesCustomization");
					Set(dialog, "template", def3);
					Set(dialog, "editing", obj);
					Set(dialog, "presetName", text2);
					Set(dialog, "flagIconDef", def4);
					IDictionary dictionary = (IDictionary)Activator.CreateInstance(AccessTools.Field(dialog.GetType(), "levels").FieldType);
					for (int num5 = 0; num5 < array6.Length; num5++)
					{
						if (array6[num5] <= 0 || array6[num5] > (int)Get(array5[num5], "maxLevel"))
						{
							return;
						}
						dictionary.Add(array5[num5], array6[num5]);
					}
					Set(dialog, "levels", dictionary);
					if ((obj == null || !(bool)Get(obj, "locked") || num3 != 1) && !(bool)Call(dialog, "ValidateLevels"))
					{
						return;
					}
					if (num3 == 3)
					{
						if (thing2 == null || !thing2.Spawned || thing2.GetType() != TypeOf("Building_GeneGestator") || (bool)Get(thing2, "doWork") || Get(thing2, "containedMatrix") == null)
						{
							return;
						}
						using (new RimDarkMapScope(thing2.Map))
						{
							Set(thing2, "selectedDisciplines", Get(dialog, "CurrentLevels"));
							Set(thing2, "selectedMaterial", null);
							Set(thing2, "selectedCustomChapterId", -1);
							return;
						}
					}
					object obj3 = obj;
					if (num3 == 2)
					{
						obj3 = ((IEnumerable)Get(designs, "Chapters")).Cast<object>().FirstOrDefault((object c) => Get(c, "Kind").ToString() == "Custodes" && (bool)Call(c, "SameDisciplinesAs", Get(dialog, "CurrentLevels")));
					}
					if ((bool)Call(designs, "NameTaken", text2.Trim(), obj3) || (obj == null && obj3 == null && ((IList)Call(designs, "DesignsOf", Get(def3, "kind"), true)).Count >= (int)Get(def3, "maxCustomChapters")))
					{
						return;
					}
					if (num3 == 2)
					{
						obj = Call(designs, "GetOrCreateCustodesDesign", Get(dialog, "CurrentLevels"));
					}
					if (obj == null)
					{
						Call(designs, "Add", def3, text2.Trim(), def4, null, Get(dialog, "CurrentLevels"), null, false);
					}
					else
					{
						Set(obj, "name", text2.Trim());
						Set(obj, "flagIconDef", def4);
						if (!(bool)Get(obj, "locked"))
						{
							Set(obj, "disciplines", Get(dialog, "CurrentLevels"));
						}
						Set(obj, "adHoc", false);
						Call(obj, "RefreshGeneDef");
					}
				}
			}
		}
	}

	private static void InstallSimulation()
	{
		Type type = TypeOf("GameComponent_Perpetual");
		perpetualPending = AccessTools.FieldRefAccess<Dictionary<Pawn, int>>(type, "perpetuals");
		perpetualCurrentTick = AccessTools.FieldRefAccess<int>(type, "currentTick");
		perpetualGeneType = TypeOf("Gene_Perpetual");
		keepPerpetualPawn = (AccessTools.DeclaredMethod(type, "KeepPawnForResurrection", new Type[1] { typeof(Pawn) }) ?? throw new MissingMethodException(type.FullName, "KeepPawnForResurrection(Pawn)")).CreateDelegate<Action<Pawn>>();
		Patch("GameComponent_Perpetual", "AddPerpetual", "PerpetualDeadlinePrefix");
		Patch("GameComponent_Perpetual", "GameComponentTick", "PerpetualTickPrefix");
		Patch("GameComponent_MankindFinestUtils", "GetRandomChapterForRaid", "WorldPrefix", null, null, "WorldFinalizer");
		Patch("CustodesRankUtility", "ColonyGoodwillGainFactor", "GoodwillPrefix", null, null, "GoodwillFinalizer");
		RegisterTimestamp("Gene_ProgenoidGlands", "timeWhenHarvestable", sentinel: false);
		RegisterTimestamp("HediffComp_Unyielding", "lastMovedTick", sentinel: false);
		RegisterTimestamp("Building_PrimarchGrowthVat", "startTick", sentinel: true);
		unyieldingType = TypeOf("HediffComp_Unyielding");
		unyieldingLastMoved = AccessTools.DeclaredField(unyieldingType, "lastMovedTick");
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(HediffComp), "CompExposeData"), null, new HarmonyMethod(typeof(RimDarkGenes), "UnyieldingScribe"));
		InstallTransport();
	}

	private static void RegisterTimestamp(string type, string name, bool sentinel)
	{
		FieldInfo field = AccessTools.Field(TypeOf(type), name) ?? throw new MissingFieldException("Genes40k." + type, name);
		Action<object, int> action = (object obj, int offset) =>
		{
			int num = (int)field.GetValue(obj);
			if (!sentinel || num != -1)
			{
				field.SetValue(obj, num + offset);
			}
		};
		AccessTools.Method("Multiplayer.Client.Patches.TimestampFixer:RegisterTimestampFields").Invoke(null, new object[2]
		{
			TypeOf(type),
			action
		});
	}

	private static void PerpetualDeadlinePrefix(ref int resurrectIn)
	{
		if (MP.IsInMultiplayer)
		{
			resurrectIn += (int)ReadOwnerTicks.Invoke(null, new object[1] { RimDarkGenesWorldScope.Owner }) - Find.TickManager.TicksGame;
		}
	}

	private static void WorldPrefix(out RimDarkGenesWorldScope __state)
	{
		__state = (MP.IsInMultiplayer ? new RimDarkGenesWorldScope() : null);
	}

	private static Exception WorldFinalizer(Exception __exception, RimDarkGenesWorldScope __state)
	{
		__state?.Dispose();
		return __exception;
	}

	private static bool PerpetualTickPrefix(object __instance)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		Dictionary<Pawn, int> dictionary = perpetualPending(__instance);
		int count = dictionary.Count;
		if (count > 0)
		{
			Pawn[] array = ArrayPool<Pawn>.Shared.Rent(count);
			try
			{
				dictionary.Keys.CopyTo(array, 0);
				for (int i = 0; i < count; i++)
				{
					Pawn pawn = array[i];
					if (pawn.Discarded)
					{
						dictionary.Remove(pawn);
						continue;
					}
					try
					{
						keepPerpetualPawn(pawn);
					}
					catch (Exception inner)
					{
						throw new TargetInvocationException(inner);
					}
				}
			}
			finally
			{
				ArrayPool<Pawn>.Shared.Return(array, clearArray: true);
			}
		}
		int num = perpetualCurrentTick(__instance);
		perpetualCurrentTick(__instance) = ((num != 4000) ? (num + 1) : 0);
		if (num != 4000)
		{
			return false;
		}
		KeyValuePair<Pawn, int>[] array2 = dictionary.Where((KeyValuePair<Pawn, int> p) => Find.TickManager.TicksGame >= p.Value).ToArray();
		foreach (KeyValuePair<Pawn, int> keyValuePair in array2)
		{
			Pawn key = keyValuePair.Key;
			Pawn_GeneTracker genes = key.genes;
			if (genes == null || !genes.GenesListForReading.Any(perpetualGeneType.IsInstanceOfType))
			{
				dictionary.Remove(key);
				continue;
			}
			Map map = key.Map ?? key.Corpse?.Map ?? (from m in Find.Maps
				where m.IsPlayerHome
				orderby m.uniqueID
				select m).FirstOrDefault() ?? Find.Maps.OrderBy((Map m) => m.uniqueID).FirstOrDefault();
			if (map == null)
			{
				continue;
			}
			using (new RimDarkMapScope(map))
			{
				if (key.Dead)
				{
					ResurrectionUtility.TryResurrect(key);
				}
				if (key.Spawned)
				{
					goto IL_02c4;
				}
				Corpse corpse = key.Corpse;
				if (corpse != null && corpse.Spawned)
				{
					goto IL_02c4;
				}
				if (!CellFinder.TryFindRandomCell(map, (IntVec3 cell) => cell.Walkable(map), out var result))
				{
					continue;
				}
				Thing thing = GenSpawn.Spawn(key, result, map);
				Find.LetterStack.ReceiveLetter("BEWH.MankindsFinest.Perpetual.PerpetualReturn".Translate(), "BEWH.MankindsFinest.Perpetual.PerpetualReturnMessage".Translate(thing), Get(TypeOf("Genes40kDefOf"), "BEWH_GoldenPositive") as LetterDef, thing);
				goto IL_02c4;
				IL_02c4:
				dictionary.Remove(key);
			}
		}
		return false;
	}

	private static void UnyieldingScribe(HediffComp __instance)
	{
		if (!(__instance.GetType() != unyieldingType))
		{
			int value = (int)unyieldingLastMoved.GetValue(__instance);
			Scribe_Values.Look(ref value, "mpUnyieldingLastMovedTick", -99999);
			if (Scribe.mode == LoadSaveMode.LoadingVars)
			{
				unyieldingLastMoved.SetValue(__instance, value);
			}
		}
	}

	private static bool GoodwillPrefix(ref float __result, out RimDarkGenesWorldScope __state)
	{
		__state = null;
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (!Intercept)
		{
			__state = new RimDarkGenesWorldScope();
			return true;
		}
		__result = 1f;
		Type type = AccessTools.TypeByName("Core40k.CompRankInfo");
		Type type2 = TypeOf("DefModExtension_GoodwillGainFactor");
		foreach (Pawn item in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists_NoCryptosleep)
		{
			ThingComp thingComp = item.AllComps.FirstOrDefault(type.IsInstanceOfType);
			if (thingComp == null)
			{
				continue;
			}
			foreach (Def item2 in (IEnumerable)Get(thingComp, "UnlockedRanks"))
			{
				IEnumerable<DefModExtension> modExtensions = item2.modExtensions;
				foreach (DefModExtension item3 in modExtensions ?? Enumerable.Empty<DefModExtension>())
				{
					if (type2.IsInstanceOfType(item3))
					{
						__result = Math.Max(__result, (float)Get(item3, "factor"));
					}
				}
			}
		}
		return false;
	}

	private static Exception GoodwillFinalizer(Exception __exception, RimDarkGenesWorldScope __state)
	{
		__state?.Dispose();
		return __exception;
	}

	private static void InstallTransport()
	{
		Patch("Ability_ForeswalkLong", "Cast", null, null, "ForeswalkDestinationTranspiler");
		Patch("Ability_ForeswalkShort", "Cast", null, null, "ForeswalkDestinationTranspiler");
		Patch("GameComponent_LivingSaint", "SpawnSaint", "SaintMapPrefix", null, null, "GenesMapFinalizer");
		presenceType = TypeOf("MapComponent_PrimarchPresence");
		presenceCounts = AccessTools.DeclaredField(presenceType, "primarchGeneCounts");
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(MapComponent), "ExposeData"), null, new HarmonyMethod(typeof(RimDarkGenes), "PresenceScribe"));
		Patch("MapComponent_PrimarchPresence", "FinalizeInit", "PresenceFinalizePrefix");
	}

	private static void PresenceScribe(MapComponent __instance)
	{
		if (__instance.GetType() != presenceType)
		{
			return;
		}
		Dictionary<GeneDef, int> dict = (Dictionary<GeneDef, int>)presenceCounts.GetValue(__instance);
		Scribe_Collections.Look(ref dict, "mpPrimarchPresenceCounts", LookMode.Def, LookMode.Value);
		if (Scribe.mode != LoadSaveMode.Saving && dict != null)
		{
			presenceCounts.SetValue(__instance, dict);
			if (Scribe.mode == LoadSaveMode.PostLoadInit)
			{
				PresenceLoads.GetOrCreateValue(__instance).Restored = true;
			}
		}
	}

	private static bool PresenceFinalizePrefix(object __instance)
	{
		if (!MP.IsInMultiplayer || !PresenceLoads.TryGetValue(__instance, out var value) || !value.Restored)
		{
			return true;
		}
		PresenceLoads.Remove(__instance);
		return false;
	}

	private static void SaintMapPrefix(Map map, out RimDarkMapScope __state)
	{
		__state = (MP.IsInMultiplayer ? new RimDarkMapScope(map) : null);
	}

	private static Exception GenesMapFinalizer(Exception __exception, RimDarkMapScope __state)
	{
		__state?.Dispose();
		return __exception;
	}

	private static IEnumerable<CodeInstruction> ForeswalkDestinationTranspiler(IEnumerable<CodeInstruction> input, MethodBase __originalMethod)
	{
		int cells = 0;
		int spawns = 0;
		int teleports = 0;
		int waits = 0;
		int caravans = 0;
		foreach (CodeInstruction item in input)
		{
			string text = null;
			if (item.operand is MethodInfo methodInfo)
			{
				if (methodInfo.DeclaringType == typeof(CellFinder) && methodInfo.Name == "TryFindRandomSpawnCellForPawnNear")
				{
					cells++;
					text = "DestinationCell";
				}
				if (methodInfo.DeclaringType == typeof(GenSpawn) && methodInfo.Name == "Spawn")
				{
					spawns++;
					text = "DestinationSpawn";
				}
				if (methodInfo.DeclaringType == typeof(Pawn) && methodInfo.Name == "Notify_Teleported")
				{
					teleports++;
					text = "DestinationTeleport";
				}
				if (methodInfo.DeclaringType == typeof(Pawn_GuestTracker) && methodInfo.Name == "WaitInsteadOfEscapingForDefaultTicks")
				{
					waits++;
					text = "DestinationGuestWait";
				}
				if (methodInfo.DeclaringType == typeof(CaravanMaker) && methodInfo.Name == "MakeCaravan")
				{
					caravans++;
					text = "DestinationCaravan";
				}
			}
			if (text != null)
			{
				item.opcode = OpCodes.Call;
				item.operand = AccessTools.DeclaredMethod(typeof(RimDarkGenes), text);
			}
			yield return item;
		}
		int num = ((__originalMethod.DeclaringType == TypeOf("Ability_ForeswalkLong")) ? 1 : 0);
		if (cells != 1 || spawns != 1 || teleports != 1 || waits != 1 || caravans != num)
		{
			throw new InvalidOperationException($"Genes foreswalk destination contract changed: {cells}/{spawns}/{teleports}/{waits}/{caravans}");
		}
	}

	private static bool DestinationCell(IntVec3 root, Map map, out IntVec3 result, int radius, Predicate<IntVec3> validator)
	{
		using (new RimDarkMapScope(map))
		{
			return CellFinder.TryFindRandomSpawnCellForPawnNear(root, map, out result, radius, validator);
		}
	}

	private static Thing DestinationSpawn(Thing thing, IntVec3 cell, Map map, WipeMode wipe)
	{
		using (new RimDarkMapScope(map))
		{
			return GenSpawn.Spawn(thing, cell, map, wipe);
		}
	}

	private static void DestinationTeleport(Pawn pawn, bool endJob, bool reset)
	{
		using (new RimDarkMapScope(pawn?.MapHeld))
		{
			pawn.Notify_Teleported(endJob, reset);
		}
	}

	private static void DestinationGuestWait(Pawn_GuestTracker guest)
	{
		using (new RimDarkMapScope(guest.pawn?.MapHeld))
		{
			guest.WaitInsteadOfEscapingForDefaultTicks();
		}
	}

	private static Caravan DestinationCaravan(IEnumerable<Pawn> pawns, Faction faction, PlanetTile tile, bool add)
	{
		using (new RimDarkGenesWorldScope())
		{
			return CaravanMaker.MakeCaravan(pawns, faction, tile, add);
		}
	}
}
