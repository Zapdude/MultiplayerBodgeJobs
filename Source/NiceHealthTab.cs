using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Andromeda.NiceHealthTab")]
internal sealed class NiceHealthTab
{
	private static ISyncField medicalCare;

	private static FieldInfo widgetPawn;

	private static MethodInfo showMedicine;

	private static Func<Pawn, bool> autoExtractVisible;

	public NiceHealthTab(ModContentPack mod)
	{
		Type type = AccessTools.TypeByName("NiceHealthTab.NHTSync") ?? throw new TypeLoadException("NiceHealthTab.NHTSync");
		autoExtractVisible = (Func<Pawn, bool>)NiceTabSync.Required(AccessTools.TypeByName("NiceHealthTab.AutoExtractGenes_Compatibility"), "ShouldShow").CreateDelegate(typeof(Func<Pawn, bool>));
		string[] array = new string[3] { "SetSelfTend", "SetFoodPolicy", "SetAutoExtract" };
		foreach (string name in array)
		{
			MethodInfo methodInfo = NiceTabSync.Required(type, name);
			MP.RegisterSyncMethod(methodInfo).SetVersion(32);
			NiceTabSync.Patch(methodInfo, typeof(NiceHealthTab), "ValidatePawn");
		}
		NiceTabSync.Patch(NiceTabSync.Required(type, "SetBillSuspended"), typeof(NiceHealthTab), "SuspendBefore");
		NiceTabSync.Patch(NiceTabSync.Required(type, "ReorderMedicalBills"), typeof(NiceHealthTab), "ReorderBefore");
		MP.RegisterSyncMethod(typeof(NiceHealthTab), "Suspend").SetVersion(32);
		MP.RegisterSyncMethod(typeof(NiceHealthTab), "Reorder").SetVersion(32);
		Type type2 = AccessTools.TypeByName("NiceHealthTab.OverviewTabWidget") ?? throw new TypeLoadException("NiceHealthTab.OverviewTabWidget");
		widgetPawn = AccessTools.Field(type2, "Pawn") ?? throw new MissingFieldException(type2.FullName, "Pawn");
		showMedicine = NiceTabSync.Required(type2, "ShowMedicineRow", Type.EmptyTypes);
		medicalCare = (ISyncField)(AccessTools.Field(AccessTools.TypeByName("Multiplayer.Client.SyncFields"), "SyncMedCare") ?? throw new MissingFieldException("SyncMedCare")).GetValue(null);
		NiceTabSync.Patch(NiceTabSync.Required(type2, "DrawMedicineRow"), typeof(NiceHealthTab), "CareBefore", "CareAfter");
	}

	private static bool ValidatePawn(Pawn pawn, MethodBase __originalMethod, object[] __args)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (!NiceTabSync.Patient(pawn))
		{
			return false;
		}
		switch (__originalMethod.Name)
		{
		case "SetSelfTend":
			if (pawn.IsColonist && pawn.Faction == Faction.OfPlayer && pawn.playerSettings != null && !DevelopmentalStageExtensions.Baby(pawn.DevelopmentalStage))
			{
				if ((bool)__args[1])
				{
					return !pawn.WorkTypeIsDisabled(WorkTypeDefOf.Doctor);
				}
				return true;
			}
			return false;
		case "SetFoodPolicy":
		{
			Pawn_FoodRestrictionTracker foodRestriction = pawn.foodRestriction;
			if (foodRestriction != null && foodRestriction.Configurable && !DevelopmentalStageExtensions.Baby(pawn.DevelopmentalStage) && pawn.needs?.food != null)
			{
				if (pawn.IsMutant)
				{
					return !pawn.mutant.Def.disablePolicies;
				}
				return true;
			}
			return false;
		}
		case "SetAutoExtract":
			return autoExtractVisible(pawn);
		default:
			throw new InvalidOperationException("Unexpected Nice Health command");
		}
	}

	private static bool SuspendBefore(Pawn pawn, int billIndex, bool value)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		List<Bill> list = pawn?.BillStack?.Bills;
		if (NiceTabSync.Patient(pawn) && list != null && billIndex >= 0 && billIndex < list.Count && list[billIndex] is Bill_Medical bill)
		{
			Suspend(pawn, bill, value);
		}
		return false;
	}

	private static bool ReorderBefore(Pawn pawn, int[] newMedicalOrder)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		object obj;
		if (pawn == null)
		{
			obj = null;
		}
		else
		{
			BillStack billStack = pawn.BillStack;
			obj = ((billStack != null) ? billStack.Bills.OfType<Bill_Medical>().ToArray() : null);
		}
		Bill_Medical[] current = (Bill_Medical[])obj;
		if (!NiceTabSync.Patient(pawn) || current == null || newMedicalOrder == null || current.Length > 256 || newMedicalOrder.Length != current.Length || newMedicalOrder.Distinct().Count() != current.Length || newMedicalOrder.Any((int i) => i < 0 || i >= current.Length))
		{
			return false;
		}
		Reorder(pawn, newMedicalOrder.Select((int i) => current[i]).ToArray());
		return false;
	}

	private static void Suspend(Pawn pawn, Bill_Medical bill, bool value)
	{
		if (NiceTabSync.Patient(pawn) && bill != null && bill.billStack == pawn.BillStack && pawn.BillStack.Bills.Contains(bill))
		{
			bill.suspended = value;
		}
	}

	private static void Reorder(Pawn pawn, Bill_Medical[] order)
	{
		if (!NiceTabSync.Patient(pawn) || order == null || order.Length > 256)
		{
			return;
		}
		Bill_Medical[] current = pawn.BillStack.Bills.OfType<Bill_Medical>().ToArray();
		if (current.Length != order.Length || order.Distinct().Count() != order.Length || order.Any((Bill_Medical b) => b == null || !current.Contains(b, null)))
		{
			return;
		}
		int num = 0;
		for (int num2 = 0; num2 < pawn.BillStack.Bills.Count; num2++)
		{
			if (pawn.BillStack.Bills[num2] is Bill_Medical)
			{
				pawn.BillStack.Bills[num2] = order[num++];
			}
		}
	}

	private static void CareBefore(object __instance, out bool __state)
	{
		__state = false;
		if (MP.IsInMultiplayer && (bool)showMedicine.Invoke(__instance, null))
		{
			MP.WatchBegin();
			__state = true;
			medicalCare.Watch(widgetPawn.GetValue(__instance));
		}
	}

	private static Exception CareAfter(Exception __exception, bool __state)
	{
		if (__state)
		{
			MP.WatchEnd();
		}
		return __exception;
	}
}
