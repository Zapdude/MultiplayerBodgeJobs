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
using Verse.AI;

namespace Multiplayer.Compat;

[MpCompatFor("Andromeda.NiceInventoryTab")]
public class NiceInventoryTab
{
	internal sealed class PreviewScope : IDisposable
	{
		[ThreadStatic]
		internal static int Depth;

		private readonly Pawn pawn;

		private readonly HediffSet hediffs;

		private readonly PawnCapacitiesHandler capacities;

		internal static bool Active => Depth > 0;

		internal PreviewScope(Pawn pawn)
		{
			this.pawn = pawn;
			hediffs = pawn?.health?.hediffSet;
			capacities = pawn?.health?.capacities;
			Rand.PushState();
			Depth++;
			if (pawn?.health != null)
			{
				pawn.health.capacities = new PawnCapacitiesHandler(pawn);
			}
		}

		public void Dispose()
		{
			try
			{
				if (pawn?.health != null)
				{
					pawn.health.hediffSet = hediffs;
					pawn.health.capacities = capacities;
				}
			}
			finally
			{
				Depth--;
				Rand.PopState();
			}
		}
	}

	private static readonly MethodInfo SetForced = AccessTools.Method(typeof(OutfitForcedHandler), "SetForced", new Type[2]
	{
		typeof(Apparel),
		typeof(bool)
	});

	internal static bool OriginalRelease;

	private static Type commands;

	private static Type settings;

	private static Type gear;

	private static Type bills;

	[ThreadStatic]
	private static int loadingGame;

	[ThreadStatic]
	private static List<Thing> produced;

	[ThreadStatic]
	private static Pawn producer;

	internal static IDictionary AuthorRequests
	{
		get
		{
			if (!(bills == null))
			{
				return (IDictionary)Get(bills, "wearRequests");
			}
			return null;
		}
	}

	public NiceInventoryTab(ModContentPack mod)
	{
		commands = AccessTools.TypeByName("NiceInventoryTab.CommandUtility") ?? throw new TypeLoadException("Nice Inventory Tab command type was not found.");
		OriginalRelease = AccessTools.DeclaredMethod(commands, "CommandCreateForPawn") == null;
		Register(commands, "CommandWear", typeof(Pawn), typeof(Thing));
		Register(commands, "CommandRemoveToInventory", typeof(Pawn), typeof(Thing), typeof(bool));
		Register(commands, "CommandWearFromInventory", typeof(Pawn), typeof(Thing));
		Register(commands, "CommandDrop", typeof(Pawn), typeof(Thing));
		Register(commands, "CommandEquipWeaponFromInventory", typeof(Pawn), typeof(ThingWithComps));
		Register(commands, "CommandMoveWeaponToInventory", typeof(Pawn), typeof(ThingWithComps));
		if (!OriginalRelease)
		{
			Register(commands, "CommandCreateForPawn", typeof(Pawn), typeof(Building_WorkTable), typeof(ThingDef), typeof(RecipeDef), typeof(ApparelLayerDef), typeof(bool), typeof(bool));
			Register(commands, "CommandOptimize", typeof(Pawn), typeof(Apparel[]));
			Register(commands, "CommandDevCreate", typeof(Pawn), typeof(ThingDef)).SetDebugOnly();
		}
		else
		{
			Register(typeof(NiceInventoryTab), "LegacyCreateForPawn", typeof(Pawn), typeof(Building_WorkTable), typeof(ThingDef), typeof(RecipeDef), typeof(ApparelLayerDef), typeof(bool), typeof(bool));
			Register(typeof(NiceInventoryTab), "LegacyOptimize", typeof(Pawn), typeof(Apparel[]));
			Register(typeof(NiceInventoryTab), "LegacyDevCreate", typeof(Pawn), typeof(ThingDef)).SetDebugOnly();
		}
		MP.RegisterSyncMethod(typeof(NiceInventoryTab), "SyncedSetForced");
		PatchForcedApparelButtons("NiceInventoryTab.ApparelGridSlot");
		PatchForcedApparelButtons("NiceInventoryTab.EquippedItem");
		if (OriginalRelease)
		{
			InstallOriginal();
		}
	}

	private static void InstallOriginal()
	{
		settings = AccessTools.TypeByName("NiceInventoryTab.Settings");
		gear = AccessTools.TypeByName("NiceInventoryTab.ITab_Pawn_Gear_Patch");
		bills = AccessTools.TypeByName("NiceInventoryTab.BillFinished_Patch");
		string[] array = new string[6] { "CommandWear", "CommandRemoveToInventory", "CommandWearFromInventory", "CommandDrop", "CommandEquipWeaponFromInventory", "CommandMoveWeaponToInventory" };
		foreach (string name in array)
		{
			MpCompat.harmony.Patch(AccessTools.DeclaredMethod(commands, name), new HarmonyMethod(typeof(NiceInventoryTab), "ValidateOriginalCommand")
			{
				priority = 0
			});
		}
		Patch(AccessTools.DeclaredMethod(commands, "CommandCreate"), "CreateBefore");
		Patch(AccessTools.DeclaredMethod(AccessTools.TypeByName("NiceInventoryTab.Dialog_OptimizeEquipment"), "CommandWear", Type.EmptyTypes), "OptimizeBefore");
		MethodInfo[] array2 = (from m in NestedTypes(AccessTools.TypeByName("NiceInventoryTab.ApparelSlotUtility")).SelectMany(AccessTools.GetDeclaredMethods)
			where m.GetMethodBody() != null && PatchProcessor.GetOriginalInstructions(m).Any((CodeInstruction codeInstruction) => codeInstruction.operand is MethodInfo methodInfo && methodInfo.DeclaringType == typeof(ThingMaker) && methodInfo.Name == "MakeThing")
			select m).ToArray();
		if (array2.Length != 1 || AccessTools.Field(array2[0].DeclaringType, "x")?.FieldType != typeof(ThingDef))
		{
			throw new MissingMethodException("NiceInventoryTab DevCreate closure changed");
		}
		Patch(array2[0], "DevBefore");
		Type type = AccessTools.TypeByName("NiceInventoryTab.HediffUtility");
		Patch(AccessTools.DeclaredMethod(type, "GetStatValueFilterHediffs"), "PreviewBefore", null, "PreviewAfter");
		Patch(AccessTools.DeclaredMethod(type, "EvaluateWithFilteredHediffs").MakeGenericMethod(typeof(float)), "PreviewBefore", null, "PreviewAfter");
		Patch(AccessTools.DeclaredMethod(type, "CalculateCapacityLevel"), "PreviewBefore", null, "PreviewAfter");
		Patch(AccessTools.Method(typeof(StatWorker), "GetValue", new Type[3]
		{
			typeof(Thing),
			typeof(bool),
			typeof(int)
		}), "PreviewStatBefore");
		Patch(AccessTools.DeclaredMethod(bills, "Clear"), "ClearBefore");
		Patch(AccessTools.DeclaredMethod(AccessTools.TypeByName("NiceInventoryTab.ModIntegration"), "LoadedGame"), "LoadedBefore", null, "LoadedAfter");
		Patch(AccessTools.DeclaredMethod(bills, "Postfix"), "AuthorFinishBefore");
		Patch(AccessTools.Method(typeof(Toils_Recipe), "FinishRecipeAndStartStoringProduct"), null, "FinishAfter");
		Patch(AccessTools.Method(typeof(GenRecipe), "MakeRecipeProducts"), null, "ProductsAfter");
		Patch(AccessTools.Method(typeof(BackCompatibility), "GetBackCompatibleType"), "LegacyTypeBefore");
		Patch(AccessTools.Method(typeof(BackCompatibility), "GetBackCompatibleTypeDirect"), "LegacyTypeBefore");
		LongEventHandler.ExecuteWhenFinished(() =>
		{
			string[] array3 = new string[3] { "CreateForPawn", "Optimize", "DevCreate" };
			foreach (string text in array3)
			{
				ISyncMethod syncMethod = MP.RegisterSyncMethod(typeof(NiceInventoryTab), text).SetContext(SyncContext.QueueOrder_Down).SetVersion(14);
				if (text == "DevCreate")
				{
					syncMethod.SetDebugOnly();
				}
			}
		});
	}

	private static void Patch(MethodInfo method, string prefix = null, string postfix = null, string finalizer = null)
	{
		if (method == null)
		{
			throw new MissingMethodException("Nice Inventory 1.7 target missing");
		}
		MpCompat.harmony.Patch(method, Hook(prefix), Hook(postfix), null, Hook(finalizer));
		static HarmonyMethod Hook(string name)
		{
			if (name != null)
			{
				return new HarmonyMethod(typeof(NiceInventoryTab), name);
			}
			return null;
		}
	}

	private static object Get(Type type, string field)
	{
		return AccessTools.Field(type, field).GetValue(null);
	}

	private static bool Control(Pawn pawn, bool colonist = true)
	{
		if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned || pawn.Map == null || pawn.jobs == null || pawn.Downed || pawn.InMentalState || pawn.CarriedBy != null)
		{
			return false;
		}
		if (!colonist)
		{
			return (bool)AccessTools.DeclaredMethod(commands, "CanControl").Invoke(null, new object[1] { pawn });
		}
		return pawn.IsColonistPlayerControlled;
	}

	private static bool ValidateOriginalCommand(MethodBase __originalMethod, object[] __args)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		Pawn pawn = __args[0] as Pawn;
		Thing thing = __args[1] as Thing;
		if (!Control(pawn, __originalMethod.Name != "CommandDrop") || thing == null || thing.Destroyed)
		{
			return false;
		}
		bool flag = thing is Apparel apparel && (pawn.apparel?.IsLocked(apparel) ?? false);
		bool flag2 = pawn.IsQuestLodger() && !EquipmentUtility.QuestLodgerCanUnequip(thing, pawn);
		switch (__originalMethod.Name)
		{
		case "CommandWear":
			if (thing is Apparel && pawn.apparel != null && thing.Spawned && thing.Map == pawn.Map && ApparelUtility.HasPartsToWear(pawn, thing.def))
			{
				return pawn.CanReserveAndReach(thing, PathEndMode.Touch, Danger.Some);
			}
			return false;
		case "CommandRemoveToInventory":
			if (thing is Apparel item)
			{
				Pawn_ApparelTracker apparel2 = pawn.apparel;
				if (apparel2 != null && apparel2.WornApparel.Contains(item) && pawn.inventory != null && !flag)
				{
					return !flag2;
				}
			}
			return false;
		case "CommandWearFromInventory":
			if (thing is Apparel && pawn.apparel != null)
			{
				Pawn_InventoryTracker inventory = pawn.inventory;
				if (inventory != null && inventory.innerContainer.Contains(thing))
				{
					return ApparelUtility.HasPartsToWear(pawn, thing.def);
				}
			}
			return false;
		case "CommandDrop":
			if (!flag && !flag2)
			{
				Pawn_InventoryTracker inventory2 = pawn.inventory;
				if (inventory2 == null || !inventory2.innerContainer.Contains(thing))
				{
					if (thing is Apparel item2)
					{
						Pawn_ApparelTracker apparel3 = pawn.apparel;
						if (apparel3 != null && apparel3.WornApparel.Contains(item2))
						{
							goto IL_0213;
						}
					}
					if (thing is ThingWithComps item3)
					{
						return pawn.equipment?.AllEquipmentListForReading.Contains(item3) ?? false;
					}
					return false;
				}
				goto IL_0213;
			}
			return false;
		case "CommandEquipWeaponFromInventory":
			if (thing is ThingWithComps thingWithComps && thingWithComps.def.IsWeapon && pawn.equipment != null)
			{
				Pawn_InventoryTracker inventory3 = pawn.inventory;
				if (inventory3 != null && inventory3.innerContainer.Contains(thingWithComps) && EquipmentUtility.CanEquip(thingWithComps, pawn))
				{
					if (pawn.equipment.Primary != null && pawn.IsQuestLodger())
					{
						return EquipmentUtility.QuestLodgerCanUnequip(pawn.equipment.Primary, pawn);
					}
					return true;
				}
			}
			return false;
		case "CommandMoveWeaponToInventory":
			if (pawn.inventory != null && pawn.equipment?.Primary == thing)
			{
				return !flag2;
			}
			return false;
		default:
			{
				throw new InvalidOperationException("Unexpected Nice Inventory command");
			}
			IL_0213:
			return true;
		}
	}

	private static bool CreateBefore(Building_WorkTable wtable, ThingDef appdef, RecipeDef recipe, ApparelLayerDef apparelLayer)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		CreateForPawn((Pawn)Get(gear, "lastPawn"), wtable, appdef, recipe, apparelLayer, (bool)Get(settings, "EnableAutoWear"), (bool)Get(settings, "AutoRenameBillLabel"));
		return false;
	}

	private static bool OptimizeBefore(object __instance)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		Optimize((Pawn)AccessTools.Field(__instance.GetType(), "pawn").GetValue(__instance), ((List<Apparel>)AccessTools.Field(__instance.GetType(), "FinalApparel").GetValue(__instance))?.ToArray());
		return false;
	}

	private static bool DevBefore(object __instance)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		DevCreate((Pawn)Get(gear, "lastPawn"), (ThingDef)AccessTools.Field(__instance.GetType(), "x").GetValue(__instance));
		return false;
	}

	private static void LegacyCreateForPawn(Pawn pawn, Building_WorkTable table, ThingDef def, RecipeDef recipe, ApparelLayerDef layer, bool autoWear, bool autoRename)
	{
		CreateForPawn(pawn, table, def, recipe, layer, autoWear, autoRename);
	}

	private static void LegacyOptimize(Pawn pawn, Apparel[] apparel)
	{
		Optimize(pawn, apparel);
	}

	private static void LegacyDevCreate(Pawn pawn, ThingDef def)
	{
		DevCreate(pawn, def);
	}

	private static void CreateForPawn(Pawn pawn, Building_WorkTable table, ThingDef def, RecipeDef recipe, ApparelLayerDef layer, bool autoWear, bool autoRename)
	{
		if (!Control(pawn) || table == null || table.Destroyed || !table.Spawned || table.Map != pawn.Map || table.Faction != pawn.Faction || recipe == null)
		{
			return;
		}
		ThingDef thingDef = def;
		if (thingDef == null || !thingDef.IsApparel)
		{
			return;
		}
		List<ThingDefCountClass> products = recipe.products;
		if (products != null && products.Any((ThingDefCountClass p) => p.thingDef == def) && table.def.AllRecipes.Contains(recipe))
		{
			Bill bill = recipe.MakeNewBill();
			if (autoWear && bill is Bill_ProductionWithUft bill_ProductionWithUft)
			{
				AccessTools.DeclaredMethod(bills, "Register").Invoke(null, new object[3] { bill_ProductionWithUft, pawn, layer });
			}
			if (autoRename && bill is Bill_Production bill_Production)
			{
				bill_Production.RenamableLabel = def.defName + " - " + pawn.Name.ToStringFull;
			}
			table.billStack.AddBill(bill);
		}
	}

	private static void Optimize(Pawn pawn, Apparel[] apparel)
	{
		if (!Control(pawn) || pawn.apparel == null || apparel == null)
		{
			return;
		}
		List<Apparel> desired = apparel.Where((Apparel a) => a != null && !a.Destroyed).Distinct().ToList();
		if (desired.Count != apparel.Length || desired.Any((Apparel a) => !pawn.apparel.WornApparel.Contains(a) && (!a.Spawned || a.Map != pawn.Map || !ApparelUtility.HasPartsToWear(pawn, a.def) || !pawn.CanReserveAndReach(a, PathEndMode.Touch, Danger.Some))))
		{
			return;
		}
		for (int num = 0; num < desired.Count; num++)
		{
			for (int num2 = num + 1; num2 < desired.Count; num2++)
			{
				if (!ApparelUtility.CanWearTogether(desired[num].def, desired[num2].def, pawn.RaceProps.body))
				{
					return;
				}
			}
		}
		List<Apparel> list = pawn.apparel.WornApparel.Where((Apparel a) => !desired.Contains(a)).ToList();
		if (list.Any((Apparel a) => pawn.apparel.IsLocked(a) || (pawn.IsQuestLodger() && !EquipmentUtility.QuestLodgerCanUnequip(a, pawn))))
		{
			return;
		}
		List<Apparel> list2 = desired.Where((Apparel a) => !pawn.apparel.WornApparel.Contains(a)).ToList();
		if (list.Count == 0 && list2.Count == 0)
		{
			return;
		}
		pawn.jobs.StopAll();
		foreach (Apparel item in list.Concat(list2))
		{
			Job job = JobMaker.MakeJob(list.Contains(item) ? JobDefOf.RemoveApparel : JobDefOf.Wear, item);
			job.playerForced = true;
			pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc, requestQueueing: true);
		}
	}

	private static void DevCreate(Pawn pawn, ThingDef def)
	{
		if (Control(pawn) && pawn.apparel != null && def != null && def.IsApparel && ApparelUtility.HasPartsToWear(pawn, def) && ThingMaker.MakeThing(def, GenStuff.DefaultStuffFor(def)) is Apparel newApparel)
		{
			pawn.apparel.Wear(newApparel);
		}
	}

	private static void PreviewBefore(Pawn pawn, out PreviewScope __state)
	{
		__state = (MP.IsInMultiplayer ? new PreviewScope(pawn) : null);
	}

	private static void PreviewAfter(PreviewScope __state)
	{
		__state?.Dispose();
	}

	private static bool PreviewStatBefore(StatWorker __instance, Thing thing, bool applyPostProcess, ref float __result)
	{
		if (PreviewScope.Depth == 0)
		{
			return true;
		}
		__result = __instance.GetValue(StatRequest.For(thing), applyPostProcess);
		return false;
	}

	internal static object AuthorRequest(Pawn pawn, ApparelLayerDef layer)
	{
		object obj = Activator.CreateInstance(bills.GetNestedType("WearRequest", BindingFlags.NonPublic));
		AccessTools.Field(obj.GetType(), "TargetPawn").SetValue(obj, pawn);
		AccessTools.Field(obj.GetType(), "Layer").SetValue(obj, layer);
		return obj;
	}

	private static void LoadedBefore(out int __state)
	{
		__state = loadingGame;
		loadingGame++;
	}

	private static void LoadedAfter(int __state)
	{
		loadingGame = __state;
	}

	private static bool ClearBefore()
	{
		if (loadingGame != 0)
		{
			Game game = Current.Game;
			if (game == null)
			{
				return true;
			}
			return game.GetComponent<NiceInventoryUpstreamState>()?.LoadedRequestsNode != true;
		}
		return true;
	}

	private static bool AuthorFinishBefore()
	{
		return !MP.IsInMultiplayer;
	}

	private static void ProductsAfter(Pawn worker, ref IEnumerable<Thing> __result)
	{
		if (produced != null && worker == producer)
		{
			__result = CaptureProducts(__result, produced);
		}
	}

	private static IEnumerable<Thing> CaptureProducts(IEnumerable<Thing> source, List<Thing> target)
	{
		foreach (Thing item in source)
		{
			target.Add(item);
			yield return item;
		}
	}

	private static void FinishAfter(Toil __result)
	{
		if (!MP.IsInMultiplayer || __result == null)
		{
			return;
		}
		Toil toil = __result;
		Action original = toil.initAction;
		toil.initAction = () =>
		{
			Pawn actor = toil.actor;
			if (!(actor?.jobs?.curDriver is JobDriver_DoBill jobDriver_DoBill) || !(jobDriver_DoBill.job.bill is Bill_ProductionWithUft bill_ProductionWithUft) || AuthorRequests == null || !AuthorRequests.Contains(bill_ProductionWithUft))
			{
				original?.Invoke();
			}
			else
			{
				object obj = AuthorRequests[bill_ProductionWithUft];
				AuthorRequests.Remove(bill_ProductionWithUft);
				Pawn pawn = (Pawn)AccessTools.Field(obj.GetType(), "TargetPawn").GetValue(obj);
				ApparelLayerDef layer = (ApparelLayerDef)AccessTools.Field(obj.GetType(), "Layer").GetValue(obj);
				if (!Control(pawn) || pawn.apparel == null || pawn.Map != actor.Map)
				{
					original?.Invoke();
				}
				else
				{
					List<Thing> list = produced;
					Pawn pawn2 = producer;
					List<Thing> source = new List<Thing>();
					BillStoreModeDef storeMode = bill_ProductionWithUft.GetStoreMode();
					try
					{
						produced = source;
						producer = actor;
						bill_ProductionWithUft.SetStoreMode(BillStoreModeDefOf.DropOnFloor);
						original?.Invoke();
					}
					finally
					{
						bill_ProductionWithUft.SetStoreMode(storeMode);
						produced = list;
						producer = pawn2;
					}
					Apparel apparel = source.OfType<Apparel>().FirstOrDefault((Apparel a) => a.def.apparel?.layers.Contains(layer) ?? false);
					if (apparel != null && apparel.Spawned && apparel.Map == pawn.Map && !apparel.WornByCorpse && pawn.apparel.CanWearWithoutDroppingAnything(apparel.def))
					{
						Job job = JobMaker.MakeJob(JobDefOf.Wear, apparel);
						job.count = 1;
						job.playerForced = true;
						pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc, requestQueueing: true);
					}
				}
			}
		};
	}

	private static bool LegacyTypeBefore(Type baseType, string providedClassName, ref Type __result)
	{
		Type type;
		if (providedClassName == "NiceInventoryTab.WearRequestGameComponent")
		{
			type = typeof(NiceInventoryUpstreamState);
		}
		else
		{
			type = ((providedClassName == "NiceInventoryTab.WearRequestGameComponent+WearRequest") ? typeof(NiceInventoryUpstreamState.Request) : null);
		}
		if (!OriginalRelease || type == null || !baseType.IsAssignableFrom(type))
		{
			return true;
		}
		__result = type;
		return false;
	}

	private static ISyncMethod Register(Type type, string name, params Type[] parameters)
	{
		return MP.RegisterSyncMethod(AccessTools.DeclaredMethod(type, name, parameters) ?? throw new MissingMethodException(type.FullName, name)).SetContext(SyncContext.QueueOrder_Down);
	}

	private static void PatchForcedApparelButtons(string name)
	{
		MethodInfo[] array = (from method in NestedTypes(AccessTools.TypeByName(name) ?? throw new TypeLoadException(name)).SelectMany(AccessTools.GetDeclaredMethods)
			where !method.ContainsGenericParameters && method.GetMethodBody() != null && PatchProcessor.GetOriginalInstructions(method).Any((CodeInstruction instruction) => instruction.Calls(SetForced))
			select method).ToArray();
		if (array.Length == 0)
		{
			throw new MissingMethodException(name, "forced apparel button calls");
		}
		MethodInfo[] array2 = array;
		foreach (MethodInfo original in array2)
		{
			MpCompat.harmony.Patch(original, null, null, new HarmonyMethod(typeof(NiceInventoryTab), "ReplaceForcedApparelCall"));
		}
	}

	private static IEnumerable<Type> NestedTypes(Type type)
	{
		yield return type;
		Type[] nestedTypes = type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic);
		foreach (Type type2 in nestedTypes)
		{
			foreach (Type item in NestedTypes(type2))
			{
				yield return item;
			}
		}
	}

	private static IEnumerable<CodeInstruction> ReplaceForcedApparelCall(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo replacement = AccessTools.DeclaredMethod(typeof(NiceInventoryTab), "SetForcedFromNiceUi");
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(SetForced))
			{
				instruction.opcode = OpCodes.Call;
				instruction.operand = replacement;
			}
			yield return instruction;
		}
	}

	private static void SetForcedFromNiceUi(OutfitForcedHandler handler, Apparel apparel, bool forced)
	{
		if (!MP.IsInMultiplayer)
		{
			handler.SetForced(apparel, forced);
			return;
		}
		Pawn pawn = apparel?.Wearer;
		if (handler != null && pawn?.outfits?.forcedHandler == handler)
		{
			SyncedSetForced(pawn, apparel, forced);
		}
	}

	private static void SyncedSetForced(Pawn pawn, Apparel apparel, bool forced)
	{
		if (pawn != null && !pawn.Destroyed && !pawn.Dead && pawn.Spawned && pawn.Map != null && pawn.IsColonistPlayerControlled && apparel != null && !apparel.Destroyed && apparel.Wearer == pawn)
		{
			Pawn_ApparelTracker apparel2 = pawn.apparel;
			if (apparel2 != null && apparel2.WornApparel.Contains(apparel))
			{
				pawn.outfits?.forcedHandler?.SetForced(apparel, forced);
			}
		}
	}
}
