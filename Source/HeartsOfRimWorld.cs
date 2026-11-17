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

[MpCompatFor("shengchipangxieyaotuke.heartsofrimworld")]
public sealed class HeartsOfRimWorld
{
	private enum Action : byte
	{
		MoveDivision,
		DisbandDivision,
		MoveFleet,
		ReturnFleet,
		DisbandFleet,
		Build,
		Rail,
		Justify,
		Declare,
		Offer,
		OfferPeace,
		Buy,
		Sell
	}

	private sealed record Target(Action Action, MethodInfo Method, string Prefix);

	private enum ProjectAction : byte
	{
		Assign,
		Swap,
		Cancel
	}

	private sealed record ProjectView(object Project, int Cap, int Assigned);

	private sealed record ConstructionView(object Nation, ProjectView[] Projects);

	private enum ProductionAction : byte
	{
		Create,
		ChangeProduct,
		Assign,
		MoveUp,
		Delete,
		FromTemplate,
		Train
	}

	private sealed class LineKey
	{
		internal int Id;
	}

	private delegate bool ProductionButton(Rect rect, string label, bool active);

	private sealed record LineEdit(object Nation, object Line, IList Lines, int Index, int Count, int Factories, bool Daily, bool Naval);

	private enum GroupAction : byte
	{
		Add,
		Rename,
		Ungroup,
		MoveLine,
		MoveLineAndCreate
	}

	private sealed record GroupView(object Nation, List<string> Groups, List<string> Folds, string[] Names, (object Line, string Group)[] Lines);

	internal sealed class SettingsSnapshot : IExposable
	{
		internal bool[] Booleans = new bool[BooleanNames.Length];

		internal int[] Integers = new int[IntegerNames.Length];

		internal float[] Floats = new float[FloatNames.Length];

		internal List<StatRow> Stats = new List<StatRow>();

		internal bool Valid
		{
			get
			{
				if (Booleans?.Length == BooleanNames.Length && Integers?.Length == IntegerNames.Length)
				{
					int num = Integers[0];
					if (num >= 20 && num <= 200)
					{
						num = Integers[1];
						if (num >= 2 && num <= 40 && Floats?.Length == FloatNames.Length)
						{
							float num2 = Floats[0];
							if (num2 >= 0.25f && num2 <= 4f)
							{
								num2 = Floats[1];
								if (num2 >= 0f && num2 <= 0.6f)
								{
									num2 = Floats[2];
									if (num2 >= 1f && num2 <= 8f && Stats != null && Stats.All((StatRow row) => row?.Valid ?? false))
									{
										return Stats.Select((StatRow row) => (Table: row.Table, Key: row.Key)).Distinct().Count() == Stats.Count;
									}
								}
							}
						}
					}
				}
				return false;
			}
		}

		internal bool SameOptions(SettingsSnapshot other)
		{
			if (Booleans.SequenceEqual(other.Booleans) && Integers.SequenceEqual(other.Integers))
			{
				return Floats.SequenceEqual(other.Floats);
			}
			return false;
		}

		public void ExposeData()
		{
			for (int i = 0; i < Booleans.Length; i++)
			{
				Scribe_Values.Look(ref Booleans[i], BooleanNames[i], defaultValue: false, forceSave: true);
			}
			for (int j = 0; j < Integers.Length; j++)
			{
				Scribe_Values.Look(ref Integers[j], IntegerNames[j], 0, forceSave: true);
			}
			for (int k = 0; k < Floats.Length; k++)
			{
				Scribe_Values.Look(ref Floats[k], FloatNames[k], 0f, forceSave: true);
			}
			Scribe_Collections.Look(ref Stats, "unitStats", LookMode.Deep);
			if (Scribe.mode == LoadSaveMode.PostLoadInit && Stats == null)
			{
				Stats = new List<StatRow>();
			}
		}
	}

	internal sealed class StatRow : IExposable
	{
		internal int Table;

		internal string Key;

		internal float[] Values = new float[StatNames.Length];

		internal bool Valid
		{
			get
			{
				if (Table >= 0 && Table < StatTables.Length && !string.IsNullOrEmpty(Key) && Values.Length == StatNames.Length)
				{
					return Values.All((float v) => float.IsFinite(v) && v >= -1f);
				}
				return false;
			}
		}

		public void ExposeData()
		{
			Scribe_Values.Look(ref Table, "table", 0);
			Scribe_Values.Look(ref Key, "key");
			for (int i = 0; i < Values.Length; i++)
			{
				Scribe_Values.Look(ref Values[i], StatNames[i], -1f);
			}
		}
	}

	private static Dictionary<MethodBase, Action> worldActions;

	private static Dictionary<Action, MethodInfo> worldMethods;

	private static MethodInfo findNation;

	private static MethodInfo findDivision;

	private static MethodInfo findFleet;

	private static MethodInfo findProvince;

	private static MethodInfo findDistrict;

	private static MethodInfo findSea;

	private static MethodInfo railRoute;

	private static MethodInfo marketListings;

	private static MethodInfo marketDirty;

	private static MethodInfo marketDef;

	private static FieldInfo divisionNation;

	private static FieldInfo fleetNation;

	private static FieldInfo divisionId;

	private static FieldInfo fleetId;

	private static FieldInfo districtId;

	private static FieldInfo districtNation;

	private static FieldInfo provinceController;

	private static FieldInfo listingSeller;

	private static FieldInfo listingKey;

	private static FieldInfo listingKind;

	private static PropertyInfo nationId;

	private static PropertyInfo divisionAlive;

	private static PropertyInfo fleetAlive;

	private static FieldInfo projects;

	private static FieldInfo projectCap;

	private static FieldInfo projectAssigned;

	private static MethodInfo distribute;

	private const string Namespace = "HOR.";

	private const int SyncVersion = 34;

	private static Assembly authorAssembly;

	private static Func<object> worldState;

	private static FieldInfo multiplayerGame;

	private static FieldInfo worldComponent;

	private static FieldInfo spectatorFaction;

	private static FieldInfo difficulty;

	private static FieldInfo difficultyChosen;

	private static FieldInfo initialised;

	private static FieldInfo nations;

	private static FieldInfo nationFaction;

	private static AccessTools.FieldRef<object, int> nextInitTick;

	private static AccessTools.FieldRef<object, int> initFailures;

	private static AccessTools.FieldRef<object, int> navalRetry;

	private static Func<bool> isReplay;

	private static Func<bool> warmingCaches;

	private static ConstructorInfo difficultyDialog;

	private static MethodInfo ensureCampaign;

	private static bool runningWorldTick;

	internal static bool Installed;

	private static readonly ConditionalWeakTable<object, LineKey> LineKeys = new ConditionalWeakTable<object, LineKey>();

	private static FieldInfo landLines;

	private static FieldInfo navalLines;

	private static FieldInfo templates;

	private static FieldInfo templateId;

	private static FieldInfo manpower;

	private static FieldInfo nextId;

	private static FieldInfo item;

	private static FieldInfo factories;

	private static FieldInfo progress;

	private static FieldInfo efficiency;

	private static FieldInfo dailyDelivery;

	private static FieldInfo dialogNation;

	private static FieldInfo dialogLine;

	private static FieldInfo dialogSelected;

	private static FieldInfo dialogNaval;

	private static MethodInfo productionButton;

	private static MethodInfo researched;

	private static MethodInfo freeMilitary;

	private static MethodInfo freePorts;

	private static MethodInfo isShip;

	private static MethodInfo linesFor;

	private static MethodInfo train;

	private static Type productionLineType;

	private static ProductionButton nativeProductionButton;

	private static FieldInfo lineGroup;

	private static FieldInfo groups;

	private static FieldInfo foldedGroups;

	private static FieldInfo exportCivs;

	private static FieldInfo addNation;

	private static FieldInfo renameNation;

	private static FieldInfo renameOuter;

	private static FieldInfo renameGroup;

	private static FieldInfo moveNation;

	private static FieldInfo moveLine;

	private static FieldInfo existingOuter;

	private static FieldInfo existingGroup;

	private static MethodInfo exports;

	private static MethodInfo maxExport;

	private static ConditionalWeakTable<object, List<string>> localFolds = new ConditionalWeakTable<object, List<string>>();

	private const string SettingsKey = "shengchipangxieyaotuke.heartsofrimworld.settings";

	private static readonly string[] BooleanNames = new string[8] { "ambientWars", "specialists", "medicGoJuice", "companySkills", "reconRange", "ceMm", "ceAmmoMix", "ceSmartAmmo" };

	private static readonly string[] IntegerNames = new string[2] { "maxOnField", "maxPlanes" };

	private static readonly string[] FloatNames = new string[3] { "speed", "routAt", "navalDamage" };

	private static readonly string[] StatTables = new string[3] { "statVehicles", "statWeapons", "statApparel" };

	private static readonly string[] StatNames = new string[4] { "light", "heavy", "armor", "aa" };

	private static FieldInfo settings;

	private static FieldInfo[] booleanSettings;

	private static FieldInfo[] integerSettings;

	private static FieldInfo[] floatSettings;

	private static FieldInfo[] statSettings;

	private static FieldInfo[] statFields;

	private static bool InterceptProduction
	{
		get
		{
			if (MP.IsInMultiplayer)
			{
				return MP.InInterface;
			}
			return false;
		}
	}

	private static ModSettings PersonalSettings => (ModSettings)settings.GetValue(null);

	private static void InstallWorldCommands()
	{
		findNation = M("HWorld", "Nation");
		findDivision = M("HWorld", "D");
		findFleet = M("HWorld", "F");
		findProvince = M("HWorld", "P");
		findDistrict = M("HWorld", "District");
		findSea = M("HWorld", "Sea");
		railRoute = M("RailWorks", "Route");
		marketListings = M("Market", "Listings");
		marketDirty = M("Market", "Dirty");
		marketDef = M("Economy", "DefOf");
		nationId = AccessTools.Property(T("Nation"), "Id");
		divisionAlive = AccessTools.Property(T("Division"), "Alive");
		fleetAlive = AccessTools.Property(T("Fleet"), "Alive");
		divisionNation = F("Division", "nation");
		fleetNation = F("Fleet", "nation");
		divisionId = F("Division", "id");
		fleetId = F("Fleet", "id");
		districtId = F("District", "id");
		districtNation = F("District", "nation");
		provinceController = F("HProvince", "controller");
		listingSeller = F("Listing", "seller");
		listingKey = F("Listing", "key");
		listingKind = F("Listing", "kind");
		Target[] array = new Target[13]
		{
			new Target(Action.MoveDivision, M("HWorld", "Order"), "InstanceResult"),
			new Target(Action.DisbandDivision, M("Army", "Disband"), "StaticVoid"),
			new Target(Action.MoveFleet, M("HWorld", "OrderFleet"), "InstanceResult"),
			new Target(Action.ReturnFleet, M("HWorld", "ReturnToPort"), "InstanceResult"),
			new Target(Action.DisbandFleet, M("HWorld", "DisbandFleet"), "InstanceVoid"),
			new Target(Action.Build, M("Economy", "Queue"), "StaticVoid"),
			new Target(Action.Rail, M("RailWorks", "Queue"), "StaticObject"),
			new Target(Action.Justify, M("Diplomacy", "Justify"), "StaticResult"),
			new Target(Action.Declare, M("Diplomacy", "Declare"), "StaticResult"),
			new Target(Action.Offer, M("Diplomacy", "Offer"), "StaticResult"),
			new Target(Action.OfferPeace, M("Diplomacy", "OfferPeace"), "StaticResult"),
			new Target(Action.Buy, M("Market", "Buy"), "StaticResult"),
			new Target(Action.Sell, M("Market", "Sell"), "StaticResult")
		};
		worldActions = ((IEnumerable<Target>)array).ToDictionary((Func<Target, MethodBase>)((Target t) => t.Method), (Func<Target, Action>)((Target t) => t.Action));
		worldMethods = array.ToDictionary((Target t) => t.Action, (Target t) => t.Method);
		MP.RegisterSyncMethod(typeof(HeartsOfRimWorld), "ApplyWorldAction").SetVersion(34);
		Target[] array2 = array;
		foreach (Target target in array2)
		{
			Patch(target.Method, target.Prefix);
		}
		Patch(M("HWorld", "FleetPos"), null, null, null, "WorldClockTranspiler");
		Patch(marketListings, null, null, null, "WorldClockTranspiler");
		Patch(M("Market", "Demand"), null, null, null, "WorldClockTranspiler");
		SessionServices.RegisterLifecycle("heartsofrimworld.market", null, () =>
		{
			marketDirty.Invoke(null, null);
		});
	}

	private static bool CaptureAction(object world, object[] args, MethodBase method, out string error)
	{
		error = null;
		if (!MP.IsInMultiplayer || !MP.InInterface)
		{
			return true;
		}
		if (world != worldState() || world == null || !(bool)initialised.GetValue(world))
		{
			return false;
		}
		Action action = worldActions[method];
		int num = 0;
		int second = 0;
		int amount = 0;
		string key = null;
		int num2;
		switch (action)
		{
		case Action.MoveDivision:
		case Action.DisbandDivision:
			if (args[0] == null)
			{
				return false;
			}
			num2 = (int)divisionNation.GetValue(args[0]);
			num = (int)divisionId.GetValue(args[0]);
			if (action == Action.MoveDivision)
			{
				second = (int)args[1];
			}
			break;
		case Action.MoveFleet:
		case Action.ReturnFleet:
		case Action.DisbandFleet:
			if (args[0] == null)
			{
				return false;
			}
			num2 = (int)fleetNation.GetValue(args[0]);
			num = (int)fleetId.GetValue(args[0]);
			if (action == Action.MoveFleet)
			{
				second = (int)args[1];
			}
			break;
		case Action.Build:
			num2 = Id(args[0]);
			if (args[1] == null)
			{
				return false;
			}
			num = (int)districtId.GetValue(args[1]);
			second = Convert.ToInt32(args[2]);
			break;
		case Action.Rail:
			num2 = Id(args[0]);
			if (!(args[1] is List<int> { Count: >=2 } list))
			{
				return false;
			}
			num = list[0];
			second = list[list.Count - 1];
			break;
		case Action.Declare:
			num2 = (int)args[0];
			num = (int)args[1];
			second = (((bool)args[2]) ? 1 : 0);
			break;
		case Action.Justify:
		case Action.Offer:
		case Action.OfferPeace:
			num2 = Id(args[0]);
			num = Id(args[1]);
			if (action != Action.Justify)
			{
				second = Convert.ToInt32(args[2]);
			}
			break;
		case Action.Buy:
			num2 = Id(args[0]);
			if (args[1] == null)
			{
				return false;
			}
			num = Id(listingSeller.GetValue(args[1]));
			second = Convert.ToInt32(listingKind.GetValue(args[1]));
			key = (string)listingKey.GetValue(args[1]);
			amount = (int)args[2];
			break;
		case Action.Sell:
			num2 = Id(args[0]);
			num = Id(args[1]);
			key = (string)args[2];
			amount = (int)args[3];
			second = Convert.ToInt32(args[4]);
			break;
		default:
			return false;
		}
		if (CommandAuthorityBridge.CanActForFaction(NationOwner(findNation.Invoke(world, new object[1] { num2 }))))
		{
			ApplyWorldAction((byte)action, num2, num, second, amount, key);
		}
		else
		{
			error = "This Hearts of RimWorld order belongs to another faction.";
		}
		return false;
	}

	private static bool InstanceResult(object __instance, object[] __args, MethodBase __originalMethod, ref string __result)
	{
		return CaptureAction(__instance, __args, __originalMethod, out __result);
	}

	private static bool InstanceVoid(object __instance, object[] __args, MethodBase __originalMethod)
	{
		string error;
		return CaptureAction(__instance, __args, __originalMethod, out error);
	}

	private static bool StaticResult(object[] __args, MethodBase __originalMethod, ref string __result)
	{
		if (MP.IsInMultiplayer && MP.InInterface)
		{
			return CaptureAction(__args[0], __args[1..], __originalMethod, out __result);
		}
		return true;
	}

	private static bool StaticVoid(object[] __args, MethodBase __originalMethod)
	{
		string error;
		if (MP.IsInMultiplayer && MP.InInterface)
		{
			return CaptureAction(__args[0], __args[1..], __originalMethod, out error);
		}
		return true;
	}

	private static bool StaticObject(object[] __args, MethodBase __originalMethod)
	{
		string error;
		if (MP.IsInMultiplayer && MP.InInterface)
		{
			return CaptureAction(__args[0], __args[1..], __originalMethod, out error);
		}
		return true;
	}

	private static int Id(object nation)
	{
		if (nation != null)
		{
			return (int)nationId.GetValue(nation);
		}
		return -1;
	}

	private static object EnumValue(MethodInfo method, int parameter, int value)
	{
		Type parameterType = method.GetParameters()[parameter].ParameterType;
		if (!Enum.IsDefined(parameterType, value))
		{
			return null;
		}
		return Enum.ToObject(parameterType, value);
	}

	private static void ApplyWorldAction(byte code, int owner, int first, int second, int amount, string key)
	{
		if (!MP.IsExecutingSyncCommand)
		{
			return;
		}
		object obj = worldState();
		if (obj == null || !(bool)initialised.GetValue(obj))
		{
			return;
		}
		Action action = (Action)code;
		if (!worldMethods.TryGetValue(action, out var value))
		{
			return;
		}
		object obj2 = findNation.Invoke(obj, new object[1] { owner });
		if (obj2 != null && CommandAuthorityBridge.CanActForFaction(NationOwner(obj2)))
		{
			object[] array;
			switch (action)
			{
			case Action.MoveDivision:
			case Action.DisbandDivision:
				array = DivisionArgs(obj, action, owner, first, second);
				break;
			case Action.MoveFleet:
			case Action.ReturnFleet:
			case Action.DisbandFleet:
				array = FleetArgs(obj, action, owner, first, second);
				break;
			case Action.Build:
				array = BuildArgs(obj, value, obj2, first, second);
				break;
			case Action.Rail:
				array = RailArgs(obj, obj2, first, second);
				break;
			default:
				array = TradeOrDiplomacyArgs(obj, value, action, obj2, first, second, amount, key);
				break;
			}
			object[] array2 = array;
			if (array2 != null && value.Invoke(value.IsStatic ? null : obj, array2) is string text && MP.IsExecutingSyncCommandIssuedBySelf)
			{
				Messages.Message(text, MessageTypeDefOf.RejectInput, historical: false);
			}
		}
	}

	private static object[] DivisionArgs(object world, Action action, int owner, int id, int destination)
	{
		object obj = findDivision.Invoke(world, new object[1] { id });
		if (obj == null || (int)divisionNation.GetValue(obj) != owner || !(bool)divisionAlive.GetValue(obj))
		{
			return null;
		}
		if (action == Action.DisbandDivision)
		{
			return new object[2] { world, obj };
		}
		if (findProvince.Invoke(world, new object[1] { destination }) != null)
		{
			return new object[2] { obj, destination };
		}
		return null;
	}

	private static object[] FleetArgs(object world, Action action, int owner, int id, int destination)
	{
		object obj = findFleet.Invoke(world, new object[1] { id });
		if (obj == null || (int)fleetNation.GetValue(obj) != owner || !(bool)fleetAlive.GetValue(obj))
		{
			return null;
		}
		if (action != Action.MoveFleet)
		{
			return new object[1] { obj };
		}
		if (findSea.Invoke(world, new object[1] { destination }) != null)
		{
			return new object[2] { obj, destination };
		}
		return null;
	}

	private static object[] BuildArgs(object world, MethodInfo method, object nation, int id, int type)
	{
		object obj = findDistrict.Invoke(world, new object[1] { id });
		if (obj != null && (int)districtNation.GetValue(obj) == Id(nation))
		{
			object obj2 = EnumValue(method, 3, type);
			if (obj2 != null)
			{
				return new object[4] { world, nation, obj, obj2 };
			}
		}
		return null;
	}

	private static object[] RailArgs(object world, object nation, int start, int end)
	{
		int num = Id(nation);
		if (start != end)
		{
			object obj = findProvince.Invoke(world, new object[1] { start });
			if (obj != null)
			{
				object obj2 = findProvince.Invoke(world, new object[1] { end });
				if (obj2 != null && (int)provinceController.GetValue(obj) == num && (int)provinceController.GetValue(obj2) == num && railRoute.Invoke(null, new object[3] { world, start, end }) is List<int> { Count: >=2 } list)
				{
					return new object[3] { world, nation, list };
				}
			}
		}
		return null;
	}

	private static object[] TradeOrDiplomacyArgs(object world, MethodInfo method, Action action, object actor, int id, int mode, int amount, string key)
	{
		object other = findNation.Invoke(world, new object[1] { id });
		if (other == null || actor == other)
		{
			return null;
		}
		switch (action)
		{
		case Action.Justify:
			return new object[3] { world, actor, other };
		case Action.Declare:
		{
			int num = mode;
			if ((uint)num > 1u)
			{
				return null;
			}
			return new object[4]
			{
				world,
				Id(actor),
				id,
				mode != 0
			};
		}
		case Action.Offer:
		case Action.OfferPeace:
		{
			object obj3 = EnumValue(method, 3, mode);
			if (obj3 == null)
			{
				return null;
			}
			return new object[4] { world, actor, other, obj3 };
		}
		case Action.Buy:
		case Action.Sell:
			if (!string.IsNullOrWhiteSpace(key) && amount > 0)
			{
				Faction faction = NationOwner(other);
				if (faction != null && !faction.IsPlayer && marketDef.Invoke(null, new object[1] { key }) != null)
				{
					marketDirty.Invoke(null, null);
					if (action == Action.Sell)
					{
						object obj = EnumValue(method, 5, mode);
						if (obj == null)
						{
							return null;
						}
						return new object[6] { world, actor, other, key, amount, obj };
					}
					object obj2 = ((IEnumerable)marketListings.Invoke(null, new object[1] { world })).Cast<object>().FirstOrDefault((object item) => listingSeller.GetValue(item) == other && (string)listingKey.GetValue(item) == key && Convert.ToInt32(listingKind.GetValue(item)) == mode);
					if (obj2 != null)
					{
						return new object[4] { world, actor, obj2, amount };
					}
					return null;
				}
			}
			return null;
		default:
			return null;
		}
	}

	private static void InstallConstructionEdits()
	{
		projects = F("Nation", "projects");
		projectCap = F("ConstructionProject", "maxFactories");
		projectAssigned = F("ConstructionProject", "assigned");
		distribute = M("Economy", "Distribute");
		MP.RegisterSyncMethod(typeof(HeartsOfRimWorld), "ApplyProjectEdit").SetVersion(34);
		Patch(M("ConstructionProject", "ExposeData"), null, "ExposeProjectKey");
		Patch(M("MainTabWindow_Nation", "Construction"), "BeginConstructionEdit", null, "EndConstructionEdit");
	}

	private static IList Projects(object nation)
	{
		return (IList)projects.GetValue(nation);
	}

	private static void ExposeProjectKey(object __instance)
	{
		if (MP.IsInMultiplayer)
		{
			Scribe_Values.Look(ref LineKeys.GetOrCreateValue(__instance).Id, "mpHorProjectId", 0);
		}
	}

	private static void BeginConstructionEdit(object n, out ConstructionView __state)
	{
		__state = null;
		if (InterceptProduction && Projects(n).Count > 0)
		{
			__state = new ConstructionView(n, (from object project in Projects(n)
				select new ProjectView(project, (int)projectCap.GetValue(project), (int)projectAssigned.GetValue(project))).ToArray());
		}
	}

	private static Exception EndConstructionEdit(Exception __exception, ConstructionView __state)
	{
		if (__state == null)
		{
			return __exception;
		}
		IList current = Projects(__state.Nation);
		ProjectView[] array = __state.Projects;
		int num = ((current.Count < array.Length) ? Array.FindIndex(array, (ProjectView row) => !current.Contains(row.Project)) : (-1));
		int num2 = -1;
		for (int num3 = 0; num3 < Math.Min(array.Length, current.Count); num3++)
		{
			if (array[num3].Project != current[num3])
			{
				num2 = num3;
				break;
			}
		}
		int num4 = ((num < 0 && num2 >= 0 && num2 + 1 < current.Count && array[num2].Project == current[num2 + 1] && array[num2 + 1].Project == current[num2]) ? num2 : (-1));
		ProjectView[] array2;
		if (num >= 0 || num2 >= 0)
		{
			current.Clear();
			array2 = array;
			foreach (ProjectView projectView in array2)
			{
				current.Add(projectView.Project);
			}
		}
		int owner = Id(__state.Nation);
		bool flag = __exception == null && CommandAuthorityBridge.CanActForFaction(NationOwner(__state.Nation));
		array2 = array;
		foreach (ProjectView projectView2 in array2)
		{
			int num6 = (int)projectCap.GetValue(projectView2.Project) - projectView2.Cap;
			if (num6 != 0)
			{
				projectCap.SetValue(projectView2.Project, projectView2.Cap);
			}
			if ((int)projectAssigned.GetValue(projectView2.Project) != projectView2.Assigned)
			{
				projectAssigned.SetValue(projectView2.Project, projectView2.Assigned);
			}
			bool flag2 = flag;
			if (flag2)
			{
				bool flag3 = ((num6 == -1 || num6 == 1) ? true : false);
				flag2 = flag3;
			}
			if (flag2 && LineId(projectView2.Project) > 0)
			{
				ApplyProjectEdit(0, owner, LineId(projectView2.Project), num6);
			}
		}
		if (flag && num >= 0 && LineId(array[num].Project) > 0)
		{
			ApplyProjectEdit(2, owner, LineId(array[num].Project), 0);
		}
		else if (flag && num4 >= 0 && LineId(array[num4].Project) > 0 && LineId(array[num4 + 1].Project) > 0)
		{
			ApplyProjectEdit(1, owner, LineId(array[num4].Project), LineId(array[num4 + 1].Project));
		}
		return __exception;
	}

	private static void ApplyProjectEdit(byte action, int owner, int id, int value)
	{
		if (!MP.IsExecutingSyncCommand)
		{
			return;
		}
		object obj = worldState();
		if (obj == null || !(bool)initialised.GetValue(obj))
		{
			return;
		}
		object obj2 = findNation.Invoke(obj, new object[1] { owner });
		if (obj2 == null || !CommandAuthorityBridge.CanActForFaction(NationOwner(obj2)))
		{
			return;
		}
		IList list = Projects(obj2);
		object obj3 = ((id > 0) ? list.Cast<object>().FirstOrDefault((object p) => LineId(p) == id) : null);
		if (obj3 == null)
		{
			return;
		}
		switch ((ProjectAction)action)
		{
		case ProjectAction.Assign:
		{
			int num2 = (int)projectCap.GetValue(obj3) + value;
			bool flag = ((value == -1 || value == 1) ? true : false);
			bool flag2 = !flag;
			if (!flag2)
			{
				bool flag3 = ((num2 < 0 || num2 > 15) ? true : false);
				flag2 = flag3;
			}
			if (!flag2)
			{
				projectCap.SetValue(obj3, num2);
				distribute.Invoke(null, new object[2] { obj, obj2 });
			}
			break;
		}
		case ProjectAction.Cancel:
			list.Remove(obj3);
			break;
		case ProjectAction.Swap:
		{
			int num = list.IndexOf(obj3);
			if (num + 1 < list.Count && value > 0 && LineId(list[num + 1]) == value)
			{
				object value2 = list[num + 1];
				list[num] = value2;
				list[num + 1] = obj3;
			}
			break;
		}
		}
	}

	public HeartsOfRimWorld(ModContentPack mod)
	{
		authorAssembly = mod.assemblies.loadedAssemblies.Single((Assembly a) => a.GetName().Name == "HeartsOfRimWorld");
		worldState = AccessTools.PropertyGetter(T("HWorld"), "W").CreateDelegate<Func<object>>();
		spectatorFaction = AccessTools.Field("Multiplayer.Client.MultiplayerWorldComp:spectatorFaction") ?? throw new MissingFieldException("MultiplayerWorldComp.spectatorFaction");
		multiplayerGame = AccessTools.Field("Multiplayer.Client.Multiplayer:game");
		worldComponent = AccessTools.Field("Multiplayer.Client.MultiplayerGame:worldComp");
		isReplay = AccessTools.PropertyGetter("Multiplayer.Client.Multiplayer:IsReplay").CreateDelegate<Func<bool>>();
		warmingCaches = AccessTools.PropertyGetter("Multiplayer.Client.LoadPatch:IsInitializingCaches").CreateDelegate<Func<bool>>();
		difficulty = F("HWorld", "difficulty");
		difficultyChosen = F("HWorld", "difficultyChosen");
		initialised = F("HWorld", "initialised");
		nations = F("HWorld", "nations");
		nationFaction = F("Nation", "faction");
		nextInitTick = AccessTools.FieldRefAccess<object, int>(F("HWorld", "nextInitFrame"));
		initFailures = AccessTools.FieldRefAccess<object, int>(F("HWorld", "initFails"));
		navalRetry = AccessTools.FieldRefAccess<object, int>(F("HWorld", "navalTry"));
		difficultyDialog = AccessTools.Constructor(T("Dialog_Difficulty"), new Type[1] { T("HWorld") });
		ensureCampaign = M("HWorld", "Ensure");
		PatchingUtilities.SetupAsyncTime();
		SessionServices.Install();
		CommandAuthorityBridge.Install();
		InstallSettings();
		InstallWorldCommands();
		InstallProduction();
		MP.RegisterSyncMethod(typeof(HeartsOfRimWorld), "ChooseDifficulty").SetHostOnly().SetVersion(34);
		Patch(M("HWorld", "WorldComponentUpdate"), "UpdateCampaignPresentation");
		Patch(M("HWorld", "WorldComponentTick"), "BeginCampaignTick", null, "EndCampaignTick");
		Patch(ensureCampaign, "EnsureDuringSimulation", null, null, "EnsureClock");
		Patch(M("HWorld", "ExposeData"), null, "ScribeCampaignScheduling");
		Patch(M("UpdateNotes", "Check"), "VanillaOnly");
		Patch(M("VanillaBridge", "Sync"), "UseCampaignFaction", null, "EndFactionScope");
		Patch(M("WorldTech", "Adopt"), null, null, null, "DeterministicWorldChoices");
		Patch(M("FactionRules", "Adopt"), null, null, null, "DeterministicWorldChoices");
		Patch(M("Dialog_Difficulty", "DoWindowContents"), null, null, null, "CommitDifficultyChoice");
		Patch(AccessTools.PropertyGetter(T("HWorld"), "Player"), "ResolvePlayerNation");
		Patch(AccessTools.PropertyGetter(T("Nation"), "Player"), null, "ExcludeSpectatorNation");
		Patch(AccessTools.PropertyGetter(T("DivisionMarker"), "DrawPos"), null, null, null, "WorldClockTranspiler");
		Installed = true;
	}

	private static Type T(string name)
	{
		return authorAssembly.GetType("HOR." + name, throwOnError: true);
	}

	private static FieldInfo F(string type, string name)
	{
		return AccessTools.Field(T(type), name) ?? throw new MissingFieldException(type, name);
	}

	private static MethodInfo M(string type, string name)
	{
		return AccessTools.DeclaredMethod(T(type), name) ?? throw new MissingMethodException(type, name);
	}

	private static void Patch(MethodBase target, string prefix = null, string postfix = null, string finalizer = null, string transpiler = null)
	{
		MpCompat.harmony.Patch(target, Hook(prefix), Hook(postfix), finalizer: Hook(finalizer), transpiler: Hook(transpiler));
		static HarmonyMethod Hook(string name)
		{
			if (name != null)
			{
				return new HarmonyMethod(typeof(HeartsOfRimWorld), name);
			}
			return null;
		}
	}

	private static Faction Spectator()
	{
		object value = multiplayerGame.GetValue(null);
		if (value != null)
		{
			object value2 = worldComponent.GetValue(value);
			if (value2 != null)
			{
				return (Faction)spectatorFaction.GetValue(value2);
			}
		}
		return null;
	}

	private static bool IsPlayer(Faction faction)
	{
		if (faction != null && faction.IsPlayer)
		{
			return faction != Spectator();
		}
		return false;
	}

	private static Faction NationOwner(object nation)
	{
		if (nation != null)
		{
			return (Faction)nationFaction.GetValue(nation);
		}
		return null;
	}

	private static Map CampaignHome()
	{
		if (MP.IsInMultiplayer)
		{
			return (from map in Find.Maps.Where((Map map) =>
				{
					if (!map.Disposed)
					{
						MapParent parent = map.Parent;
						if (parent != null)
						{
							WorldObjectDef def = parent.def;
							if (def != null && def.canBePlayerHome)
							{
								return IsPlayer(parent.Faction);
							}
						}
					}
					return false;
				})
				orderby map.uniqueID
				select map).FirstOrDefault();
		}
		return Find.AnyPlayerHomeMap;
	}

	private static bool BeginCampaignTick(out IDisposable __state)
	{
		__state = null;
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (warmingCaches() || Find.TickManager.TicksGame % 60 != 0)
		{
			return false;
		}
		UseCampaignFaction(out __state);
		runningWorldTick = true;
		return true;
	}

	private static void EndCampaignTick(IDisposable __state)
	{
		runningWorldTick = false;
		EndFactionScope(__state);
	}

	private static void UseCampaignFaction(out IDisposable __state)
	{
		Faction faction = ((!MP.IsInMultiplayer) ? null : CampaignHome()?.Parent.Faction);
		__state = ((faction != null && faction != Faction.OfPlayer) ? CommandAuthorityBridge.EnterFaction(faction) : null);
	}

	private static void EndFactionScope(IDisposable __state)
	{
		__state?.Dispose();
	}

	private static bool EnsureDuringSimulation(object __instance, ref bool __result)
	{
		if (!MP.IsInMultiplayer || (runningWorldTick && (bool)difficultyChosen.GetValue(__instance)))
		{
			return true;
		}
		__result = (bool)initialised.GetValue(__instance);
		return false;
	}

	private static bool UpdateCampaignPresentation(object __instance)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (MP.InInterface && MP.IsHosting && !isReplay() && !(bool)difficultyChosen.GetValue(__instance))
		{
			WindowStack windowStack = Find.WindowStack;
			if (windowStack != null && !windowStack.Windows.Any((Window window) => window.GetType() == difficultyDialog.DeclaringType) && CampaignHome() != null)
			{
				windowStack.Add((Window)difficultyDialog.Invoke(new object[1] { __instance }));
			}
		}
		return false;
	}

	private static void ChooseDifficulty(int choice)
	{
		if (MP.IsExecutingSyncCommand)
		{
			object obj = worldState();
			if (obj != null && !(bool)difficultyChosen.GetValue(obj) && Enum.IsDefined(difficulty.FieldType, choice) && CampaignHome() != null)
			{
				difficulty.SetValue(obj, Enum.ToObject(difficulty.FieldType, choice));
				difficultyChosen.SetValue(obj, true);
			}
		}
	}

	private static void SetDifficulty(object world, int choice)
	{
		if (!MP.IsInMultiplayer)
		{
			difficulty.SetValue(world, Enum.ToObject(difficulty.FieldType, choice));
		}
		else if (MP.InInterface && MP.IsHosting && world == worldState())
		{
			ChooseDifficulty(choice);
		}
	}

	private static void FinishDifficulty(object world, bool chosen)
	{
		if (!MP.IsInMultiplayer)
		{
			difficultyChosen.SetValue(world, chosen);
		}
	}

	private static bool VanillaOnly()
	{
		return !MP.IsInMultiplayer;
	}

	private static bool UsePendingWorldChoice(bool pending)
	{
		return !MP.IsInMultiplayer & pending;
	}

	private static int CampaignRetryClock()
	{
		if (!MP.IsInMultiplayer)
		{
			return Time.frameCount;
		}
		return Find.TickManager.TicksGame;
	}

	private static int CampaignClock(TickManager manager)
	{
		if (!MP.IsInMultiplayer)
		{
			return manager.TicksGame;
		}
		return FortifiedFeaturesFramework.WorldTicks();
	}

	private static void ScribeCampaignScheduling(object __instance)
	{
		if (MP.IsInMultiplayer)
		{
			Scribe_Values.Look(ref nextInitTick(__instance), "mpHorNextInitTick", 0);
			Scribe_Values.Look(ref initFailures(__instance), "mpHorInitFailures", 0);
			Scribe_Values.Look(ref navalRetry(__instance), "mpHorNavalRetry", 0);
		}
	}

	private static void ExcludeSpectatorNation(object __instance, ref bool __result)
	{
		if (MP.IsInMultiplayer & __result)
		{
			__result = IsPlayer(NationOwner(__instance));
		}
	}

	private static bool ResolvePlayerNation(object __instance, ref object __result)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		Faction owner = (MP.InInterface ? MP.RealPlayerFaction : Faction.OfPlayer);
		if (!IsPlayer(owner) && !MP.InInterface && !MP.IsExecutingSyncCommand)
		{
			owner = CampaignHome()?.Parent.Faction;
		}
		__result = (IsPlayer(owner) ? ((IEnumerable)nations.GetValue(__instance)).Cast<object>().FirstOrDefault((object n) => NationOwner(n) == owner) : null);
		return false;
	}

	private static IEnumerable<CodeInstruction> CommitDifficultyChoice(IEnumerable<CodeInstruction> instructions)
	{
		int writes = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.StoresField(difficulty) || instruction.StoresField(difficultyChosen))
			{
				writes++;
				bool flag = instruction.StoresField(difficulty);
				instruction.opcode = OpCodes.Call;
				instruction.operand = AccessTools.DeclaredMethod(typeof(HeartsOfRimWorld), flag ? "SetDifficulty" : "FinishDifficulty");
			}
			yield return instruction;
		}
		if (writes != 2)
		{
			throw new InvalidOperationException("Hearts of RimWorld difficulty confirmation changed");
		}
	}

	private static IEnumerable<CodeInstruction> DeterministicWorldChoices(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
	{
		FieldInfo pending = AccessTools.Field(__originalMethod.DeclaringType, "pagePending");
		foreach (CodeInstruction instruction in instructions)
		{
			yield return instruction;
			if (instruction.LoadsField(pending))
			{
				yield return CodeInstruction.Call(typeof(HeartsOfRimWorld), "UsePendingWorldChoice");
			}
		}
	}

	private static IEnumerable<CodeInstruction> EnsureClock(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo frame = AccessTools.PropertyGetter(typeof(Time), "frameCount");
		MethodInfo home = AccessTools.PropertyGetter(typeof(Find), "AnyPlayerHomeMap");
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(frame))
			{
				instruction.operand = AccessTools.DeclaredMethod(typeof(HeartsOfRimWorld), "CampaignRetryClock");
			}
			else if (instruction.Calls(home))
			{
				instruction.operand = AccessTools.DeclaredMethod(typeof(HeartsOfRimWorld), "CampaignHome");
			}
			yield return instruction;
		}
	}

	private static IEnumerable<CodeInstruction> WorldClockTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo ticks = AccessTools.PropertyGetter(typeof(TickManager), "TicksGame");
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(ticks))
			{
				instruction.opcode = OpCodes.Call;
				instruction.operand = AccessTools.DeclaredMethod(typeof(HeartsOfRimWorld), "CampaignClock");
			}
			yield return instruction;
		}
	}

	private static void InstallProduction()
	{
		landLines = F("Nation", "lines");
		navalLines = F("Nation", "navalLines");
		templates = F("Nation", "templates");
		templateId = F("DivisionTemplate", "id");
		manpower = F("Nation", "manpower");
		nextId = F("HWorld", "nextId");
		productionLineType = T("ProductionLine");
		item = F("ProductionLine", "item");
		factories = F("ProductionLine", "factories");
		progress = F("ProductionLine", "progress");
		efficiency = F("ProductionLine", "efficiency");
		dailyDelivery = F("ProductionLine", "toColony");
		dialogNation = F("Dialog_ProductionMenu", "n");
		dialogLine = F("Dialog_ProductionMenu", "line");
		dialogSelected = F("Dialog_ProductionMenu", "selected");
		dialogNaval = F("Dialog_ProductionMenu", "naval");
		productionButton = M("HUI", "SmallButton");
		researched = M("Economy", "Researched");
		freeMilitary = M("Economy", "FreeMil");
		freePorts = M("Economy", "FreePorts");
		isShip = M("Ships", "IsShip");
		linesFor = M("ProductionHelper", "LinesFor");
		train = M("Army", "Queue");
		MP.RegisterSyncMethod(typeof(HeartsOfRimWorld), "ApplyProduction").SetVersion(34);
		Patch(M("ProductionLine", "ExposeData"), null, "ExposeLineKey");
		Patch(M("HWorld", "WorldComponentTick"), null, "AssignCampaignLineKeys");
		Patch(M("Dialog_ProductionMenu", "DoWindowContents"), null, null, null, "ReplaceProductionConfirmation");
		Patch(M("MainTabWindow_Nation", "LineCard"), "CaptureLineCard", null, "CommitLineCard");
		Patch(linesFor, "QueueTemplateLines");
		Patch(train, "QueueTraining");
		InstallProductionGroups();
		InstallConstructionEdits();
	}

	private static IList Lines(object nation, bool naval)
	{
		return (IList)(naval ? navalLines : landLines).GetValue(nation);
	}

	private static int LineId(object line)
	{
		if (line == null || !LineKeys.TryGetValue(line, out var value))
		{
			return 0;
		}
		return value.Id;
	}

	private static void ExposeLineKey(object __instance)
	{
		if (MP.IsInMultiplayer)
		{
			Scribe_Values.Look(ref LineKeys.GetOrCreateValue(__instance).Id, "mpHorLineId", 0);
		}
	}

	private static void AssignCampaignLineKeys(object __instance)
	{
		if (!MP.IsInMultiplayer || !runningWorldTick || !(bool)initialised.GetValue(__instance))
		{
			return;
		}
		foreach (object item in (IEnumerable)nations.GetValue(__instance))
		{
			AssignLineKeys(__instance, item);
			AssignRecordKeys(__instance, Projects(item));
			ReconcileGroups(item);
		}
	}

	private static void AssignLineKeys(object world, object nation)
	{
		AssignRecordKeys(world, Lines(nation, naval: false));
		AssignRecordKeys(world, Lines(nation, naval: true));
	}

	private static void AssignRecordKeys(object world, IList records)
	{
		int num = (int)nextId.GetValue(world);
		int num2 = num;
		foreach (object record in records)
		{
			if (LineId(record) == 0)
			{
				LineKeys.GetOrCreateValue(record).Id = num++;
			}
		}
		if (num != num2)
		{
			nextId.SetValue(world, num);
		}
	}

	private static IEnumerable<CodeInstruction> ReplaceProductionConfirmation(IEnumerable<CodeInstruction> instructions)
	{
		List<CodeInstruction> list = instructions.ToList();
		int num = list.FindLastIndex((CodeInstruction instruction) => instruction.Calls(productionButton));
		if (num < 0)
		{
			throw new InvalidOperationException("Hearts production confirmation button changed");
		}
		CodeInstruction codeInstruction = list[num];
		list.Insert(num, new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(codeInstruction).MoveBlocksFrom(codeInstruction));
		codeInstruction.operand = AccessTools.DeclaredMethod(typeof(HeartsOfRimWorld), "ProductionConfirmation");
		return list;
	}

	private static bool ProductionConfirmation(Rect rect, string label, bool active, Window dialog)
	{
		if (nativeProductionButton == null)
		{
			nativeProductionButton = productionButton.CreateDelegate<ProductionButton>();
		}
		if (!nativeProductionButton(rect, label, active))
		{
			return false;
		}
		if (!InterceptProduction)
		{
			return true;
		}
		object value = dialogNation.GetValue(dialog);
		object value2 = dialogLine.GetValue(dialog);
		int num = LineId(value2);
		if (CommandAuthorityBridge.CanActForFaction(NationOwner(value)) && (value2 == null || num > 0))
		{
			ApplyProduction((value2 != null) ? ((byte)1) : ((byte)0), Id(value), (bool)dialogNaval.GetValue(dialog), num, 0, 0, 0, (string)dialogSelected.GetValue(dialog));
			dialog.Close();
		}
		return false;
	}

	private static void CaptureLineCard(object n, object l, bool naval, out LineEdit __state)
	{
		__state = null;
		if (InterceptProduction)
		{
			IList list = Lines(n, naval);
			__state = new LineEdit(n, l, list, list.IndexOf(l), list.Count, (int)factories.GetValue(l), (bool)dailyDelivery.GetValue(l), naval);
		}
	}

	private static Exception CommitLineCard(Exception __exception, LineEdit __state)
	{
		if (__state == null || __state.Index < 0)
		{
			return __exception;
		}
		int num = __state.Lines.IndexOf(__state.Line);
		int num2 = (int)factories.GetValue(__state.Line);
		if (num != __state.Index)
		{
			if (num >= 0)
			{
				__state.Lines.RemoveAt(num);
			}
			__state.Lines.Insert(__state.Index, __state.Line);
		}
		if (num2 != __state.Factories)
		{
			factories.SetValue(__state.Line, __state.Factories);
		}
		if ((bool)dailyDelivery.GetValue(__state.Line) != __state.Daily)
		{
			dailyDelivery.SetValue(__state.Line, __state.Daily);
		}
		if (__exception != null || LineId(__state.Line) <= 0 || !CommandAuthorityBridge.CanActForFaction(NationOwner(__state.Nation)))
		{
			return __exception;
		}
		if (num == -1)
		{
			SubmitLineEdit(ProductionAction.Delete, __state);
		}
		else if (num == __state.Index - 1 && __state.Lines.Count == __state.Count)
		{
			SubmitLineEdit(ProductionAction.MoveUp, __state);
		}
		else if (Math.Abs(num2 - __state.Factories) == 1)
		{
			SubmitLineEdit(ProductionAction.Assign, __state, num2 - __state.Factories);
		}
		return __exception;
	}

	private static void SubmitLineEdit(ProductionAction action, LineEdit edit, int value = 0)
	{
		ApplyProduction((byte)action, Id(edit.Nation), edit.Naval, LineId(edit.Line), value, 0, 0, null);
	}

	private static bool QueueTemplateLines(object w, object n, object t)
	{
		if (!InterceptProduction)
		{
			return true;
		}
		if (w == worldState() && t != null && CommandAuthorityBridge.CanActForFaction(NationOwner(n)))
		{
			ApplyProduction(5, Id(n), naval: false, (int)templateId.GetValue(t), 0, 0, 0, null);
		}
		return false;
	}

	private static bool QueueTraining(object w, object n, object t, object d, int count, int repeat)
	{
		if (!InterceptProduction)
		{
			return true;
		}
		if (w == worldState() && t != null && d != null && CommandAuthorityBridge.CanActForFaction(NationOwner(n)))
		{
			ApplyProduction(6, Id(n), naval: false, (int)templateId.GetValue(t), (int)districtId.GetValue(d), count, repeat, null);
		}
		return false;
	}

	private static void ApplyProduction(byte code, int owner, bool naval, int target, int value, int count, int repeat, string product)
	{
		if (!MP.IsExecutingSyncCommand)
		{
			return;
		}
		object obj = worldState();
		if (obj == null || !(bool)initialised.GetValue(obj))
		{
			return;
		}
		object obj2 = findNation.Invoke(obj, new object[1] { owner });
		if (obj2 == null || !CommandAuthorityBridge.CanActForFaction(NationOwner(obj2)))
		{
			return;
		}
		ProductionAction productionAction = (ProductionAction)code;
		if (productionAction - 5 <= ProductionAction.ChangeProduct)
		{
			object obj3 = ((IEnumerable)templates.GetValue(obj2)).Cast<object>().FirstOrDefault((object t) => (int)templateId.GetValue(t) == target);
			if (obj3 == null)
			{
				return;
			}
			if (productionAction == ProductionAction.FromTemplate)
			{
				linesFor.Invoke(null, new object[3] { obj, obj2, obj3 });
				AssignLineKeys(obj, obj2);
			}
			else if (count >= 1 && count <= 10 && repeat >= -1 && repeat <= 3 && (int)manpower.GetValue(obj2) > 0)
			{
				object obj4 = findDistrict.Invoke(obj, new object[1] { value });
				if (obj4 != null && (int)districtNation.GetValue(obj4) == owner)
				{
					train.Invoke(null, new object[6] { obj, obj2, obj3, obj4, count, repeat });
				}
			}
			return;
		}
		IList list = Lines(obj2, naval);
		object obj5 = ((target > 0) ? list.Cast<object>().FirstOrDefault((object l) => LineId(l) == target) : null);
		if (productionAction != ProductionAction.Create && obj5 == null)
		{
			return;
		}
		switch (productionAction)
		{
		case ProductionAction.Create:
		case ProductionAction.ChangeProduct:
			if (!string.IsNullOrWhiteSpace(product) && product.Length <= 256 && (bool)isShip.Invoke(null, new object[1] { product }) == naval && (bool)researched.Invoke(null, new object[1] { product }))
			{
				if (productionAction == ProductionAction.Create)
				{
					obj5 = Activator.CreateInstance(productionLineType);
					factories.SetValue(obj5, Math.Clamp(FreeProduction(obj, obj2, naval), 1, 5));
					item.SetValue(obj5, product);
					list.Add(obj5);
					AssignLineKeys(obj, obj2);
				}
				else if ((string)item.GetValue(obj5) != product)
				{
					item.SetValue(obj5, product);
					progress.SetValue(obj5, 0f);
					efficiency.SetValue(obj5, Math.Max(0.25f, (float)efficiency.GetValue(obj5) * 0.3f));
				}
			}
			break;
		case ProductionAction.Assign:
		{
			int num2 = (int)factories.GetValue(obj5) + value;
			bool flag = ((value == -1 || value == 1) ? true : false);
			if (flag && num2 >= 0 && num2 <= (naval ? 5 : 15) && (value < 0 || FreeProduction(obj, obj2, naval) > 0))
			{
				factories.SetValue(obj5, num2);
			}
			break;
		}
		case ProductionAction.MoveUp:
		{
			int num = list.IndexOf(obj5);
			if (num > 0)
			{
				list.RemoveAt(num);
				list.Insert(num - 1, obj5);
			}
			break;
		}
		case ProductionAction.Delete:
			list.Remove(obj5);
			break;
		}
	}

	private static int FreeProduction(object world, object nation, bool naval)
	{
		return (int)(naval ? freePorts : freeMilitary).Invoke(null, new object[2] { world, nation });
	}

	private static void InstallProductionGroups()
	{
		lineGroup = F("ProductionLine", "group");
		groups = F("Nation", "lineGroups");
		foldedGroups = F("Nation", "foldedGroups");
		exportCivs = F("Nation", "exportCivs");
		exports = M("Economy", "Exports");
		maxExport = M("Economy", "MaxExport");
		string type = "MainTabWindow_Nation+<>c__DisplayClass84_4";
		string type2 = "MainTabWindow_Nation+<>c__DisplayClass84_3";
		string type3 = "MainTabWindow_Nation+<>c__DisplayClass86_0";
		string type4 = "MainTabWindow_Nation+<>c__DisplayClass86_1";
		addNation = F("MainTabWindow_Nation+<>c__DisplayClass84_0", "n");
		renameOuter = F(type, "CS$<>8__locals3");
		renameNation = F(type2, "CS$<>8__locals2");
		renameGroup = F(type, "gg");
		moveNation = F(type3, "n");
		moveLine = F(type3, "l");
		existingOuter = F(type4, "CS$<>8__locals1");
		existingGroup = F(type4, "gg");
		MP.RegisterSyncMethod(typeof(HeartsOfRimWorld), "ApplyProductionGroup").SetVersion(34);
		MP.RegisterSyncMethod(typeof(HeartsOfRimWorld), "ChangeExports").SetVersion(34);
		Patch(M("MainTabWindow_Nation", "Production"), "BeginProductionGroups", null, "EndProductionGroups");
		Patch(M("MainTabWindow_Nation+<>c__DisplayClass84_0", "<Production>b__0"), "AddGroup");
		Patch(M(type, "<Production>b__9"), "RenameGroup");
		Patch(M(type3, "<LineCard>b__1"), "ClearLineGroup");
		Patch(M(type3, "<LineCard>b__3"), "CreateLineGroup");
		Patch(M(type4, "<LineCard>b__2"), "MoveToGroup");
		Patch(M("MainTabWindow_Nation", "Construction"), null, null, null, "RouteExportEdits");
		SessionServices.RegisterLifecycle("heartsofrimworld.productionGroups", null, () =>
		{
			localFolds = new ConditionalWeakTable<object, List<string>>();
		});
	}

	private static List<string> Groups(object nation)
	{
		return (List<string>)groups.GetValue(nation);
	}

	private static string GroupOf(object line)
	{
		return (string)lineGroup.GetValue(line);
	}

	private static void ReconcileGroups(object nation)
	{
		List<string> list = Groups(nation);
		foreach (object item in Lines(nation, naval: false))
		{
			string text = GroupOf(item);
			if (text != null && !list.Contains(text))
			{
				list.Add(text);
			}
		}
	}

	private static void BeginProductionGroups(object n, out GroupView __state)
	{
		__state = null;
		if (InterceptProduction)
		{
			List<string> list = Groups(n);
			List<string> folds = (List<string>)foldedGroups.GetValue(n);
			(object, string)[] lines = (from object line in Lines(n, naval: false)
				select (line: line, GroupOf(line))).ToArray();
			__state = new GroupView(n, list, folds, list.ToArray(), lines);
			groups.SetValue(n, new List<string>(list));
			foldedGroups.SetValue(n, localFolds.GetValue(n, (object _) => new List<string>(folds)));
		}
	}

	private static Exception EndProductionGroups(Exception __exception, GroupView __state)
	{
		if (__state == null)
		{
			return __exception;
		}
		string text = __state.Names.FirstOrDefault((string name) => !Groups(__state.Nation).Contains(name));
		groups.SetValue(__state.Nation, __state.Groups);
		foldedGroups.SetValue(__state.Nation, __state.Folds);
		(object, string)[] lines = __state.Lines;
		for (int num = 0; num < lines.Length; num++)
		{
			(object, string) tuple = lines[num];
			if (GroupOf(tuple.Item1) != tuple.Item2)
			{
				lineGroup.SetValue(tuple.Item1, tuple.Item2);
			}
		}
		if (__exception == null && text != null)
		{
			SubmitGroup(GroupAction.Ungroup, __state.Nation, 0, text, null);
		}
		return __exception;
	}

	private static bool AddGroup(object __instance, string __0)
	{
		if (!InterceptProduction)
		{
			return true;
		}
		SubmitGroup(GroupAction.Add, addNation.GetValue(__instance), 0, null, __0);
		return false;
	}

	private static bool RenameGroup(object __instance, string __0)
	{
		if (!InterceptProduction)
		{
			return true;
		}
		object value = addNation.GetValue(renameNation.GetValue(renameOuter.GetValue(__instance)));
		SubmitGroup(GroupAction.Rename, value, 0, (string)renameGroup.GetValue(__instance), __0);
		return false;
	}

	private static bool ClearLineGroup(object __instance)
	{
		return SubmitLineGroup(__instance, null, GroupAction.MoveLine);
	}

	private static bool CreateLineGroup(object __instance, string __0)
	{
		return SubmitLineGroup(__instance, __0, GroupAction.MoveLineAndCreate);
	}

	private static bool MoveToGroup(object __instance)
	{
		if (!InterceptProduction)
		{
			return true;
		}
		return SubmitLineGroup(existingOuter.GetValue(__instance), (string)existingGroup.GetValue(__instance), GroupAction.MoveLine);
	}

	private static bool SubmitLineGroup(object closure, string name, GroupAction action)
	{
		if (!InterceptProduction)
		{
			return true;
		}
		object value = moveLine.GetValue(closure);
		if (LineId(value) > 0)
		{
			SubmitGroup(action, moveNation.GetValue(closure), LineId(value), GroupOf(value), name);
		}
		return false;
	}

	private static void SubmitGroup(GroupAction action, object nation, int line, string before, string after)
	{
		if (CommandAuthorityBridge.CanActForFaction(NationOwner(nation)))
		{
			ApplyProductionGroup((byte)action, Id(nation), line, before, after);
		}
	}

	private static bool ValidGroupName(string name)
	{
		if (!string.IsNullOrWhiteSpace(name) && name.Length <= 40)
		{
			return name.Trim() == name;
		}
		return false;
	}

	private static void ApplyProductionGroup(byte action, int owner, int target, string before, string after)
	{
		if (!MP.IsExecutingSyncCommand)
		{
			return;
		}
		object obj = worldState();
		if (obj == null || !(bool)initialised.GetValue(obj))
		{
			return;
		}
		object obj2 = findNation.Invoke(obj, new object[1] { owner });
		if (obj2 == null || !CommandAuthorityBridge.CanActForFaction(NationOwner(obj2)))
		{
			return;
		}
		List<string> list = Groups(obj2);
		switch ((GroupAction)action)
		{
		case GroupAction.Add:
			if (ValidGroupName(after) && !list.Contains(after))
			{
				list.Add(after);
			}
			break;
		case GroupAction.Rename:
		case GroupAction.Ungroup:
		{
			int num = list.IndexOf(before);
			bool flag = action == 1;
			if (num < 0 || (flag && (!ValidGroupName(after) || list.Contains(after))))
			{
				break;
			}
			if (flag)
			{
				list[num] = after;
			}
			else
			{
				list.RemoveAt(num);
			}
			{
				foreach (object item in Lines(obj2, naval: false))
				{
					if (GroupOf(item) == before)
					{
						lineGroup.SetValue(item, flag ? after : null);
					}
				}
				break;
			}
		}
		case GroupAction.MoveLine:
		case GroupAction.MoveLineAndCreate:
		{
			object obj3 = ((target > 0) ? Lines(obj2, naval: false).Cast<object>().FirstOrDefault((object line) => LineId(line) == target) : null);
			if (obj3 == null || GroupOf(obj3) != before)
			{
				break;
			}
			if (action == 4)
			{
				if (!ValidGroupName(after))
				{
					break;
				}
				if (!list.Contains(after))
				{
					list.Add(after);
				}
			}
			else if (after != null && !list.Contains(after))
			{
				break;
			}
			lineGroup.SetValue(obj3, after);
			break;
		}
		}
	}

	private static IEnumerable<CodeInstruction> RouteExportEdits(IEnumerable<CodeInstruction> instructions)
	{
		int writes = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.StoresField(exportCivs))
			{
				yield return new CodeInstruction(OpCodes.Ldarg_2).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
				instruction.opcode = OpCodes.Call;
				instruction.operand = AccessTools.DeclaredMethod(typeof(HeartsOfRimWorld), "SubmitExports");
				writes++;
			}
			yield return instruction;
		}
		if (writes != 2)
		{
			throw new InvalidOperationException("Hearts export controls changed");
		}
	}

	private static void SubmitExports(object nation, int selected, object world)
	{
		if (!InterceptProduction)
		{
			exportCivs.SetValue(nation, selected);
		}
		else if (world == worldState() && CommandAuthorityBridge.CanActForFaction(NationOwner(nation)))
		{
			ChangeExports(Id(nation), selected - (int)exports.Invoke(null, new object[2] { world, nation }));
		}
	}

	private static void ChangeExports(int owner, int delta)
	{
		bool flag = !MP.IsExecutingSyncCommand;
		if (!flag)
		{
			bool flag2 = ((delta == -1 || delta == 1) ? true : false);
			flag = !flag2;
		}
		if (flag)
		{
			return;
		}
		object obj = worldState();
		if (obj == null || !(bool)initialised.GetValue(obj))
		{
			return;
		}
		object obj2 = findNation.Invoke(obj, new object[1] { owner });
		if (obj2 != null && CommandAuthorityBridge.CanActForFaction(NationOwner(obj2)))
		{
			int num = (int)exports.Invoke(null, new object[2] { obj, obj2 }) + delta;
			if (num >= 0 && num <= (int)maxExport.Invoke(null, new object[2] { obj, obj2 }))
			{
				exportCivs.SetValue(obj2, num);
			}
		}
	}

	private static void InstallSettings()
	{
		settings = F("HORMod", "S");
		booleanSettings = GetFields("HORSettings", BooleanNames);
		integerSettings = GetFields("HORSettings", IntegerNames);
		floatSettings = GetFields("HORSettings", FloatNames);
		statSettings = GetFields("HORSettings", StatTables);
		statFields = GetFields("StatOverride", StatNames);
		SessionServices.RegisterSettings("shengchipangxieyaotuke.heartsofrimworld.settings", typeof(SettingsSnapshot), () => CaptureSettings(PersonalSettings), () => CaptureSettings((ModSettings)Activator.CreateInstance(settings.FieldType)), (IExposable value) =>
		{
			ApplySettings((SettingsSnapshot)value);
		}, (IExposable value) => value is SettingsSnapshot settingsSnapshot && settingsSnapshot.Valid, () => PersonalSettings, "mpHeartsOfRimWorldSettings");
		MP.RegisterSyncMethod(typeof(HeartsOfRimWorld), "SyncSettings").SetHostOnly().SetVersion(34);
		Patch(M("HORMod", "DoSettingsWindowContents"), "BeforeSettings", null, "AfterSettings");
		Patch(M("UnitStats", "Set"), "VanillaOnly");
		Patch(M("UnitStats", "Reset"), "VanillaOnly");
		static FieldInfo[] GetFields(string type, string[] names)
		{
			return names.Select((string name) => F(type, name)).ToArray();
		}
	}

	private static SettingsSnapshot CaptureSettings(ModSettings source)
	{
		SettingsSnapshot settingsSnapshot = CaptureOptions(source);
		for (int i = 0; i < statSettings.Length; i++)
		{
			IDictionary dictionary = (IDictionary)statSettings[i].GetValue(source);
			foreach (string item in dictionary.Keys.Cast<string>().OrderBy((string key) => key, StringComparer.Ordinal))
			{
				object row = dictionary[item];
				if (row != null)
				{
					settingsSnapshot.Stats.Add(new StatRow
					{
						Table = i,
						Key = item,
						Values = statFields.Select((FieldInfo f) => (float)f.GetValue(row)).ToArray()
					});
				}
			}
		}
		return settingsSnapshot;
	}

	private static SettingsSnapshot CaptureOptions(ModSettings source)
	{
		return new SettingsSnapshot
		{
			Booleans = booleanSettings.Select((FieldInfo f) => (bool)f.GetValue(source)).ToArray(),
			Integers = integerSettings.Select((FieldInfo f) => (int)f.GetValue(source)).ToArray(),
			Floats = floatSettings.Select((FieldInfo f) => (float)f.GetValue(source)).ToArray()
		};
	}

	private static void ApplySettings(SettingsSnapshot snapshot)
	{
		ApplyOptions(snapshot);
		IDictionary[] array = statSettings.Select((FieldInfo field) => (IDictionary)Activator.CreateInstance(field.FieldType)).ToArray();
		foreach (StatRow stat in snapshot.Stats)
		{
			object obj = Activator.CreateInstance(statFields[0].DeclaringType);
			for (int num = 0; num < statFields.Length; num++)
			{
				statFields[num].SetValue(obj, stat.Values[num]);
			}
			array[stat.Table].Add(stat.Key, obj);
		}
		ModSettings personalSettings = PersonalSettings;
		for (int num2 = 0; num2 < array.Length; num2++)
		{
			statSettings[num2].SetValue(personalSettings, array[num2]);
		}
	}

	private static void ApplyOptions(SettingsSnapshot snapshot)
	{
		ModSettings personalSettings = PersonalSettings;
		for (int i = 0; i < booleanSettings.Length; i++)
		{
			booleanSettings[i].SetValue(personalSettings, snapshot.Booleans[i]);
		}
		for (int j = 0; j < integerSettings.Length; j++)
		{
			integerSettings[j].SetValue(personalSettings, snapshot.Integers[j]);
		}
		for (int k = 0; k < floatSettings.Length; k++)
		{
			floatSettings[k].SetValue(personalSettings, snapshot.Floats[k]);
		}
	}

	internal static IExposable SettingsState(Game game)
	{
		return SessionServices.SettingsForGame("shengchipangxieyaotuke.heartsofrimworld.settings", game);
	}

	private static void BeforeSettings(out SettingsSnapshot __state)
	{
		__state = (MP.IsInMultiplayer ? CaptureOptions(PersonalSettings) : null);
	}

	private static Exception AfterSettings(Exception __exception, SettingsSnapshot __state)
	{
		if (__state == null)
		{
			return __exception;
		}
		SettingsSnapshot settingsSnapshot = CaptureOptions(PersonalSettings);
		ApplyOptions(__state);
		if (__exception == null && MP.IsHosting && MP.InInterface && !settingsSnapshot.SameOptions(__state))
		{
			SyncSettings(settingsSnapshot.Booleans, settingsSnapshot.Integers, settingsSnapshot.Floats);
		}
		return __exception;
	}

	private static void SyncSettings(bool[] booleans, int[] integers, float[] floats)
	{
		if (MP.IsExecutingSyncCommand && Current.Game != null)
		{
			SettingsSnapshot settingsSnapshot = CaptureSettings(PersonalSettings);
			settingsSnapshot.Booleans = booleans;
			settingsSnapshot.Integers = integers;
			settingsSnapshot.Floats = floats;
			if (settingsSnapshot.Valid)
			{
				SessionServices.ApplySettings(SettingsState(Current.Game), settingsSnapshot);
			}
		}
	}
}
