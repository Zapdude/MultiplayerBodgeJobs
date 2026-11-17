using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Andromeda.NiceBillTab")]
internal sealed class NiceBillTab
{
	private static Type drawer;

	private static FieldInfo workTable;

	private static FieldInfo selectedRecipe;

	private static FieldInfo material;

	private static FieldInfo style;

	private static FieldInfo autoNaming;

	private static FieldInfo refresh;

	private static FieldInfo draggedIndex;

	private static FieldInfo billHeights;

	private static MethodInfo dropIndex;

	private static readonly AccessTools.FieldRef<Pawn_AgeTracker, long> PreviewAge = AccessTools.FieldRefAccess<Pawn_AgeTracker, long>("ageBiologicalTicksInt");

	private static readonly AccessTools.FieldRef<Pawn_AgeTracker, int> PreviewStage = AccessTools.FieldRefAccess<Pawn_AgeTracker, int>("cachedLifeStageIndex");

	private static readonly AccessTools.FieldRef<Thing, ThingOwner> PreviewHolder = AccessTools.FieldRefAccess<Thing, ThingOwner>("holdingOwner");

	public NiceBillTab(ModContentPack mod)
	{
		drawer = AccessTools.TypeByName("NiceBillTab.TabBillsDrawer") ?? throw new TypeLoadException("NiceBillTab.TabBillsDrawer");
		Type type = AccessTools.TypeByName("NiceBillTab.RecipeSelection") ?? throw new TypeLoadException("NiceBillTab.RecipeSelection");
		workTable = Field(type, "workTable");
		selectedRecipe = Field(type, "SelectedRecipe");
		material = Field(type, "material");
		style = Field(type, "style");
		autoNaming = Field(AccessTools.TypeByName("NiceBillTab.Settings"), "EnableAutoNaming");
		refresh = Field(drawer, "shouldRefreshFilter");
		draggedIndex = Field(drawer, "draggedBillIndex");
		billHeights = Field(drawer, "lastBillHeight");
		dropIndex = NiceTabSync.Required(drawer, "GetDropIndex");
		Patch("TryAddBillToQueue", "CreateBefore");
		Patch("SetMaterialToBill", "MaterialBefore");
		Patch("SuspendBill", "SuspendBefore");
		Patch("InsertBill", "InsertBefore");
		Patch("HandleBillDrop", "DropBefore");
		Patch("PlusAction", "PlusBefore");
		Patch("MinusAction", "MinusBefore");
		NiceTabSync.Patch(NiceTabSync.Required(AccessTools.TypeByName("NiceBillTab.Utils"), "TryAddBillToQueue"), typeof(NiceBillTab), "IngredientBefore");
		string[] array = new string[5] { "Create", "SetMaterial", "Suspend", "Move", "ChangeCount" };
		foreach (string methodOrPropertyName in array)
		{
			MP.RegisterSyncMethod(typeof(NiceBillTab), methodOrPropertyName).SetVersion(32);
		}
		MP.RegisterSyncMethod(typeof(NiceBillTab), "Insert").ExposeParameter(1).SetVersion(32);
		SessionServices.Install();
		MethodInfo methodInfo = NiceTabSync.Required(AccessTools.TypeByName("NiceBillTab.StatMechanoidWorker"), "GetMechStats");
		NiceTabSync.Patch(methodInfo, typeof(NiceBillTab), "PreviewBefore", "PreviewAfter");
		MpCompat.harmony.Patch(methodInfo, null, null, new HarmonyMethod(typeof(NiceBillTab), "PreviewPawnCall"));
		static FieldInfo Field(Type type2, string name)
		{
			return AccessTools.Field(type2, name) ?? throw new MissingFieldException(type2.FullName, name);
		}
		static void Patch(string name, string hook)
		{
			NiceTabSync.Patch(NiceTabSync.Required(drawer, name), typeof(NiceBillTab), hook);
		}
	}

	private static bool Recipe(Building_WorkTable table, RecipeDef recipe, ThingDef stuff, Precept_ThingStyle thingStyle)
	{
		if (NiceTabSync.Table(table) && recipe != null && table.def.AllRecipes.Contains(recipe) && recipe.AvailableNow && recipe.AvailableOnNow(table))
		{
			if (stuff == null)
			{
				goto IL_0071;
			}
			ThingDef producedThingDef = recipe.ProducedThingDef;
			if (producedThingDef != null && producedThingDef.MadeFromStuff && GenStuff.AllowedStuffsFor(recipe.ProducedThingDef).Contains(stuff))
			{
				ThingFilter fixedIngredientFilter = recipe.fixedIngredientFilter;
				if (fixedIngredientFilter == null || fixedIngredientFilter.Allows(stuff))
				{
					goto IL_0071;
				}
			}
		}
		return false;
		IL_0071:
		if (thingStyle != null)
		{
			return thingStyle.ThingDef == recipe.ProducedThingDef;
		}
		return true;
	}

	private static bool CreateBefore(object selection, ref bool __result)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		__result = false;
		if (selection == null)
		{
			return false;
		}
		Building_WorkTable table = workTable.GetValue(selection) as Building_WorkTable;
		RecipeDef recipe = selectedRecipe.GetValue(selection) as RecipeDef;
		ThingDef thingDef = material.GetValue(selection) as ThingDef;
		Precept_ThingStyle thingStyle = style.GetValue(selection) as Precept_ThingStyle;
		if (!Recipe(table, recipe, thingDef, thingStyle))
		{
			return false;
		}
		string name = (((bool)autoNaming.GetValue(null) && thingDef != null) ? thingDef.LabelCap.ToString() : null);
		Create(table, recipe, thingDef, thingStyle, 0, name);
		refresh.SetValue(null, true);
		__result = true;
		return false;
	}

	private static bool IngredientBefore(ThingWithComps workbench, RecipeDef recipe, int count, Map map)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (workbench is Building_WorkTable building_WorkTable && building_WorkTable.Map == map && count > 0 && count <= 1000000 && Recipe(building_WorkTable, recipe, null, null) && map.mapPawns.FreeColonists.Any(recipe.PawnSatisfiesSkillRequirements))
		{
			Create(building_WorkTable, recipe, null, null, count, null);
		}
		return false;
	}

	private static void Create(Building_WorkTable table, RecipeDef recipe, ThingDef stuff, Precept_ThingStyle thingStyle, int productCount, string name)
	{
		if (!Recipe(table, recipe, stuff, thingStyle) || productCount < 0 || productCount > 1000000 || (name != null && name.Length > 256) || (productCount != 0 && (recipe.products.NullOrEmpty() || recipe.products[0].count <= 0 || !table.Map.mapPawns.FreeColonists.Any(recipe.PawnSatisfiesSkillRequirements))) || (recipe.mechanitorOnlyRecipe && !table.Map.mapPawns.FreeColonists.Any(MechanitorUtility.IsMechanitor)))
		{
			return;
		}
		Bill bill = recipe.MakeNewBill(thingStyle);
		ApplyMaterial(bill, stuff);
		if (bill is Bill_Production bill_Production)
		{
			if (productCount > 0)
			{
				bill_Production.repeatMode = BillRepeatModeDefOf.RepeatCount;
				bill_Production.repeatCount = (int)Math.Ceiling((double)productCount / (double)recipe.products[0].count);
			}
			if (!name.NullOrEmpty())
			{
				bill_Production.RenamableLabel = name;
			}
		}
		table.billStack.AddBill(bill);
	}

	private static bool MaterialBefore(object selection, RecipeDef recipe, Bill bill)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (selection != null && bill?.recipe == recipe)
		{
			SetMaterial(bill, material.GetValue(selection) as ThingDef);
		}
		return false;
	}

	private static void SetMaterial(Bill bill, ThingDef stuff)
	{
		if (NiceTabSync.Bill(bill) && Recipe((Building_WorkTable)bill.billStack.billGiver, bill.recipe, stuff, null))
		{
			ApplyMaterial(bill, stuff);
		}
	}

	private static void ApplyMaterial(Bill bill, ThingDef stuff)
	{
		if (stuff != null && bill.ingredientFilter != null)
		{
			ThingDef producedThingDef = bill.recipe.ProducedThingDef;
			if (producedThingDef != null && producedThingDef.MadeFromStuff)
			{
				bill.ingredientFilter.SetDisallowAll();
				bill.ingredientFilter.SetAllow(stuff, allow: true);
			}
		}
	}

	private static bool SuspendBefore(Bill bill, bool flag)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		Suspend(bill, flag);
		return false;
	}

	private static void Suspend(Bill bill, bool suspended)
	{
		if (NiceTabSync.Bill(bill))
		{
			bill.suspended = suspended;
		}
	}

	private static bool PlusBefore(Bill_Production billProd, RecipeDef recipe)
	{
		return CountBefore(billProd, recipe, plus: true);
	}

	private static bool MinusBefore(Bill_Production billProd, RecipeDef recipe)
	{
		return CountBefore(billProd, recipe, plus: false);
	}

	private static bool CountBefore(Bill_Production bill, RecipeDef recipe, bool plus)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (bill?.recipe == recipe)
		{
			ChangeCount(bill, Math.Max(1, Math.Min(10000, GenUI.CurrentAdjustmentMultiplier())), plus);
		}
		return false;
	}

	private static void ChangeCount(Bill_Production bill, int multiplier, bool plus)
	{
		if (NiceTabSync.Bill(bill) && multiplier >= 1 && multiplier <= 10000)
		{
			if (bill.repeatMode == BillRepeatModeDefOf.Forever)
			{
				bill.repeatMode = BillRepeatModeDefOf.RepeatCount;
				bill.repeatCount = 1;
			}
			else if (bill.repeatMode == BillRepeatModeDefOf.RepeatCount)
			{
				bill.repeatCount = Add(bill.repeatCount, plus ? multiplier : (-multiplier), clamp: true);
			}
			else if (bill.repeatMode == BillRepeatModeDefOf.TargetCount || bill.repeatMode?.defName == "TD_PersonCount" || bill.repeatMode?.defName == "TD_XPerPerson")
			{
				long amount = (long)bill.recipe.targetCountAdjustment * (long)multiplier * (plus ? 1 : (-1));
				bool clamp = (bill.repeatMode?.defName != "TD_PersonCount") | plus;
				bill.targetCount = Add(bill.targetCount, amount, clamp);
				bill.unpauseWhenYouHave = Add(bill.unpauseWhenYouHave, amount, clamp);
			}
		}
		static int Add(int original, long num, bool clamp)
		{
			return (int)Math.Max((!clamp) ? int.MinValue : 0, Math.Min(2147483647L, original + num));
		}
	}

	private static bool InsertBefore(Building_WorkTable SelTable, Bill bill, int index)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (NiceTabSync.Table(SelTable) && index >= 0 && index <= SelTable.billStack.Count)
		{
			Insert(SelTable, bill, SelTable.billStack.Bills.ElementAtOrDefault(index));
		}
		return false;
	}

	private static void Insert(Building_WorkTable table, Bill bill, Bill before)
	{
		if (NiceTabSync.Table(table) && bill != null && !table.billStack.Bills.Contains(bill) && (before == null || table.billStack.Bills.Contains(before)) && Recipe(table, bill.recipe, null, null))
		{
			table.billStack.AddBill(bill);
			if (before != null)
			{
				table.billStack.Bills.Remove(bill);
				table.billStack.Bills.Insert(table.billStack.Bills.IndexOf(before), bill);
			}
		}
	}

	private static bool DropBefore(List<Bill> filteredBills, Vector2 mousePosition, float width)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		int num = (int)draggedIndex.GetValue(null);
		if (filteredBills == null || filteredBills.Count > 512 || num < 0 || num >= filteredBills.Count || !(billHeights.GetValue(null) is IList list) || list.Count < filteredBills.Count)
		{
			return false;
		}
		int num2 = (int)dropIndex.Invoke(null, new object[3] { filteredBills, mousePosition, width });
		if (num2 >= 0 && num2 < filteredBills.Count && num != num2)
		{
			Move(filteredBills[num], filteredBills[num2], num2 > num);
		}
		refresh.SetValue(null, true);
		return false;
	}

	private static void Move(Bill bill, Bill anchor, bool after)
	{
		if (NiceTabSync.Bill(bill) && NiceTabSync.Bill(anchor) && bill != anchor && bill.billStack == anchor.billStack)
		{
			List<Bill> bills = bill.billStack.Bills;
			bills.Remove(bill);
			bills.Insert(bills.IndexOf(anchor) + (after ? 1 : 0), bill);
		}
	}

	private static void PreviewBefore(out IDisposable __state)
	{
		__state = (MP.IsInMultiplayer ? SessionServices.EnterLocalOnly() : null);
	}

	private static Exception PreviewAfter(Exception __exception, IDisposable __state)
	{
		__state?.Dispose();
		return __exception;
	}

	private static IEnumerable<CodeInstruction> PreviewPawnCall(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo original = AccessTools.DeclaredMethod(typeof(PawnGenerator), "GeneratePawn", new Type[1] { typeof(PawnGenerationRequest) });
		MethodInfo replacement = AccessTools.DeclaredMethod(typeof(NiceBillTab), "PreviewPawn");
		int count = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(original))
			{
				instruction.opcode = OpCodes.Call;
				instruction.operand = replacement;
				count++;
			}
			yield return instruction;
		}
		if (count != 1)
		{
			throw new InvalidOperationException("Nice Bill mech preview generation call changed: " + count);
		}
	}

	private static Pawn PreviewPawn(PawnGenerationRequest request)
	{
		if (!MP.IsInMultiplayer)
		{
			return PawnGenerator.GeneratePawn(request);
		}
		PawnKindDef kind = request.KindDef;
		if (kind?.race?.race == null)
		{
			return null;
		}
		Pawn pawn = new Pawn
		{
			def = kind.race,
			kindDef = kind
		};
		pawn.ageTracker = new Pawn_AgeTracker(pawn);
		PreviewAge(pawn.ageTracker) = (long)Math.Max(18, kind.minGenerationAge) * 3600000L;
		int num = 0;
		for (int i = 0; i < pawn.RaceProps.lifeStageAges.Count; i++)
		{
			if (pawn.RaceProps.lifeStageAges[i].minAge <= (float)PreviewAge(pawn.ageTracker) / 3600000f)
			{
				num = i;
			}
		}
		PreviewStage(pawn.ageTracker) = num;
		pawn.health = new Pawn_HealthTracker(pawn);
		pawn.equipment = new Pawn_EquipmentTracker(pawn);
		pawn.meleeVerbs = new Pawn_MeleeVerbs(pawn);
		pawn.verbTracker = new VerbTracker(pawn);
		pawn.stances = new Pawn_StanceTracker(pawn);
		pawn.needs = new Pawn_NeedsTracker(pawn);
		ThingDef thingDef = (kind.weaponTags.NullOrEmpty() ? null : DefDatabase<ThingDef>.AllDefs.Where((ThingDef d) => d.IsWeapon && !d.weaponTags.NullOrEmpty() && d.weaponTags.Any(kind.weaponTags.Contains)).OrderBy((ThingDef d) => d.defName, StringComparer.Ordinal).FirstOrDefault());
		if (thingDef != null)
		{
			ThingWithComps thingWithComps = new ThingWithComps
			{
				def = thingDef
			};
			CompProperties compProperties = thingDef.comps?.FirstOrDefault((CompProperties p) => typeof(CompEquippable).IsAssignableFrom(p.compClass));
			if (compProperties != null)
			{
				thingWithComps.comps = new List<ThingComp>
				{
					new CompEquippable
					{
						parent = thingWithComps,
						props = compProperties
					}
				};
				pawn.equipment.AllEquipmentListForReading.Add(thingWithComps);
				PreviewHolder(thingWithComps) = pawn.equipment.GetDirectlyHeldThings();
			}
		}
		return pawn;
	}
}
