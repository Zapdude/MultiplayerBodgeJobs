using System.Collections.Generic;
using System.Linq;
using CashRegister;
using CashRegister.Shifts;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Orion.CashRegister")]
internal class CashRegister
{
	private const int CheckInterval = 60;

	public CashRegister(ModContentPack mod)
	{
		Extensions.SetContext((IEnumerable<ISyncMethod>)MpCompat.RegisterLambdaMethod(typeof(ITab_Register_Shifts), "GetGizmos", new int[1] { 1 }), SyncContext.MapSelected);
		MP.RegisterSyncWorker<object>(NoSync, typeof(ITab_Register_Shifts), isImplicit: false, shouldConstruct: true);
		MP.RegisterSyncMethod(typeof(CashRegister), "SyncedSetShifts").MinTime(100);
		MpCompat.harmony.Patch(AccessTools.Method(typeof(ITab_Register_Shifts), "FillTab"), new HarmonyMethod(typeof(CashRegister), "PreFillTab"), new HarmonyMethod(typeof(CashRegister), "PostFillTab"));
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod(typeof(Gizmo_Radius), "ButtonDown"));
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod(typeof(Gizmo_Radius), "ButtonUp"));
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod(typeof(Gizmo_Radius), "ButtonCenter"));
		MP.RegisterSyncWorker<Gizmo_Radius>(SyncGizmoRadius);
		MP.RegisterSyncWorker<Shift>(SyncShift);
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(CompAssignableToPawn), "TryAssignPawn"), new HarmonyMethod(typeof(CashRegister), "PreTryAssignPawn"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(CompAssignableToPawn), "TryUnassignPawn"), new HarmonyMethod(typeof(CashRegister), "PreTryUnassignPawn"));
		MP.RegisterSyncMethod(typeof(CashRegister), "SyncedTryAssignPawnShifts");
		MP.RegisterSyncMethod(typeof(CashRegister), "SyncedTryUnassignPawnShifts");
		MpCompat.harmony.Patch(AccessTools.DeclaredPropertyGetter(typeof(Building_CashRegister), "IsActive"), new HarmonyMethod(typeof(CashRegister), "PreIsActive"), new HarmonyMethod(typeof(CashRegister), "PostIsActive"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(Building_CashRegister), "HasToWork"), new HarmonyMethod(typeof(CashRegister), "PreHasToWork"), new HarmonyMethod(typeof(CashRegister), "PostHasToWork"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(Building_CashRegister), "ExposeData"), new HarmonyMethod(typeof(CashRegister), "InitFieldsExposeData"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(Building_CashRegister), "PostMake"), new HarmonyMethod(typeof(CashRegister), "InitFieldsPostMake"));
	}

	private static void NoSync(SyncWorker sync, ref object obj)
	{
	}

	private static void SyncGizmoRadius(SyncWorker sync, ref Gizmo_Radius gizmo)
	{
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Expected Obj, but got Unknown
		if (sync.isWriting)
		{
			sync.Write(((Gizmo_ModifyNumber<Building_CashRegister>)(object)gizmo).selection);
			return;
		}
		Building_CashRegister[] array = sync.Read<Building_CashRegister[]>();
		gizmo = new Gizmo_Radius(array);
	}

	private static void PreFillTab(ITab_Register_Shifts __instance, ref object[] __state)
	{
		List<Shift> shifts = ((ITab_Register)__instance).Register.shifts;
		__state = new object[3];
		__state[0] = shifts.Count;
		bool[][] array = new bool[shifts.Count][];
		for (int i = 0; i < shifts.Count; i++)
		{
			array[i] = shifts[i].timetable.times.ToArray();
		}
		__state[1] = array;
		Pawn[][] array2 = new Pawn[shifts.Count][];
		for (int j = 0; j < shifts.Count; j++)
		{
			array2[j] = shifts[j].assigned.ToArray();
		}
		__state[2] = array2;
	}

	private static void PostFillTab(ITab_Register_Shifts __instance, object[] __state)
	{
		List<Shift> shifts = ((ITab_Register)__instance).Register.shifts;
		bool[][] times = (bool[][])__state[1];
		Pawn[][] pawns = (Pawn[][])__state[2];
		if (shifts.Count != (int)__state[0])
		{
			Sync();
			return;
		}
		for (int i = 0; i < shifts.Count; i++)
		{
			List<bool> times2 = shifts[i].timetable.times;
			bool[] array = times[i];
			for (int j = 0; j < times2.Count; j++)
			{
				if (times2[j] != array[j])
				{
					Sync();
					return;
				}
			}
			List<Pawn> assigned = shifts[i].assigned;
			Pawn[] array2 = pawns[i];
			if (assigned.Count != array2.Length)
			{
				Sync();
				break;
			}
			for (int k = 0; k < assigned.Count; k++)
			{
				if (assigned[k] != array2[k])
				{
					Sync();
					return;
				}
			}
		}
		void Sync()
		{
			//IL_0056: Unknown result type (might be due to invalid IL or missing references)
			//IL_005b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0073: Unknown result type (might be due to invalid IL or missing references)
			//IL_0086: Unknown result type (might be due to invalid IL or missing references)
			//IL_0092: Expected Obj, but got Unknown
			Shift[] array3 = new Shift[shifts.Count];
			for (int l = 0; l < shifts.Count; l++)
			{
				array3[l] = shifts[l];
			}
			SyncedSetShifts(((ITab_Register)__instance).Register, array3);
			shifts.Clear();
			for (int m = 0; m < times.Length; m++)
			{
				Shift item = new Shift
				{
					timetable = 
					{
						times = times[m].ToList()
					},
					assigned = pawns[m].ToList(),
					map = Find.CurrentMap
				};
				shifts.Add(item);
			}
		}
	}

	private static void SyncedSetShifts(Building_CashRegister register, Shift[] shifts)
	{
		register.shifts.Clear();
		register.shifts.AddRange(shifts);
	}

	private static void SyncShift(SyncWorker sync, ref Shift shift)
	{
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0047: Unknown result type (might be due to invalid IL or missing references)
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		//IL_0065: Expected Obj, but got Unknown
		if (sync.isWriting)
		{
			sync.Write(shift.map);
			sync.Write(shift.assigned);
			sync.Write(shift.timetable.times);
		}
		else
		{
			shift = new Shift
			{
				map = sync.Read<Map>(),
				assigned = sync.Read<List<Pawn>>(),
				timetable = 
				{
					times = sync.Read<List<bool>>()
				}
			};
		}
	}

	private static bool PreTryAssignPawn(CompAssignableToPawn __instance, Pawn pawn)
	{
		if (MP.IsInMultiplayer && MP.InInterface)
		{
			CompAssignableToPawn_Shifts val = (CompAssignableToPawn_Shifts)(object)((__instance is CompAssignableToPawn_Shifts) ? __instance : null);
			if (val != null)
			{
				SyncedTryAssignPawnShifts(val, pawn, GetRegisterShiftToIndex(val));
				return false;
			}
		}
		return true;
	}

	private static bool PreTryUnassignPawn(CompAssignableToPawn __instance, Pawn pawn)
	{
		if (MP.IsInMultiplayer && MP.InInterface)
		{
			CompAssignableToPawn_Shifts val = (CompAssignableToPawn_Shifts)(object)((__instance is CompAssignableToPawn_Shifts) ? __instance : null);
			if (val != null)
			{
				SyncedTryUnassignPawnShifts(val, pawn, GetRegisterShiftToIndex(val));
				return false;
			}
		}
		return true;
	}

	private static void SyncedTryAssignPawnShifts(CompAssignableToPawn_Shifts comp, Pawn pawn, byte shiftIndex)
	{
		List<Pawn> registerIndexToShift = GetRegisterIndexToShift(comp, shiftIndex);
		if (registerIndexToShift == null)
		{
			return;
		}
		List<Pawn> assignedPawns = ((CompAssignableToPawn)(object)comp).assignedPawns;
		try
		{
			((CompAssignableToPawn)(object)comp).assignedPawns = registerIndexToShift;
			((CompAssignableToPawn)(object)comp).TryAssignPawn(pawn);
		}
		finally
		{
			((CompAssignableToPawn)(object)comp).assignedPawns = assignedPawns;
		}
	}

	private static void SyncedTryUnassignPawnShifts(CompAssignableToPawn_Shifts comp, Pawn pawn, byte shiftIndex)
	{
		List<Pawn> registerIndexToShift = GetRegisterIndexToShift(comp, shiftIndex);
		if (registerIndexToShift == null)
		{
			return;
		}
		List<Pawn> assignedPawns = ((CompAssignableToPawn)(object)comp).assignedPawns;
		try
		{
			((CompAssignableToPawn)(object)comp).assignedPawns = registerIndexToShift;
			((CompAssignableToPawn)(object)comp).TryUnassignPawn(pawn, true, false);
		}
		finally
		{
			((CompAssignableToPawn)(object)comp).assignedPawns = assignedPawns;
		}
	}

	private static byte GetRegisterShiftToIndex(CompAssignableToPawn_Shifts comp)
	{
		int num = comp.Register.shifts.FirstIndexOf((Shift s) => s.assigned == ((CompAssignableToPawn)(object)comp).assignedPawns);
		if (num >= 0)
		{
			return (byte)num;
		}
		Log.Error(string.Format("Trying to send shift data for {0} for {1}, but there is no matching shift.", "CompAssignableToPawn_Shifts", comp.Register));
		return byte.MaxValue;
	}

	private static List<Pawn> GetRegisterIndexToShift(CompAssignableToPawn_Shifts comp, byte shiftIndex)
	{
		if (shiftIndex < byte.MaxValue && shiftIndex < comp.Register.shifts.Count)
		{
			return comp.Register.shifts[shiftIndex].assigned;
		}
		Log.Error(string.Format("Trying to read shift data for {0} for {1}, but received a data for a nonexistent shift.", "CompAssignableToPawn_Shifts", comp.Register));
		return null;
	}

	private static bool PreIsActive(ref float ___lastActiveCheck, bool ___isActive, ref bool __result, out bool __state)
	{
		if (!MP.IsInMultiplayer)
		{
			__state = false;
			return true;
		}
		if (!MP.InInterface && ___lastActiveCheck <= (float)Find.TickManager.TicksGame)
		{
			___lastActiveCheck = float.MinValue;
			__state = true;
			return true;
		}
		__state = false;
		__result = ___isActive;
		return false;
	}

	private static void PostIsActive(ref float ___lastActiveCheck, bool __state)
	{
		if (__state)
		{
			___lastActiveCheck = Find.TickManager.TicksGame + 60;
		}
	}

	private static bool PreHasToWork(Pawn pawn, ref float ___lastCheckActivePawns, HashSet<Pawn> ___activePawns, ref bool __result, out bool __state)
	{
		if (!MP.IsInMultiplayer)
		{
			__state = false;
			return true;
		}
		if (!MP.InInterface && ___lastCheckActivePawns <= (float)Find.TickManager.TicksGame)
		{
			___lastCheckActivePawns = float.MinValue;
			__state = true;
			return true;
		}
		__state = false;
		__result = ___activePawns.Contains(pawn);
		return false;
	}

	private static void PostHasToWork(ref float ___lastCheckActivePawns, bool __state)
	{
		if (__state)
		{
			___lastCheckActivePawns = Find.TickManager.TicksGame + 60;
		}
	}

	private static void InitFieldsPostMake(Building_CashRegister __instance, ref float ___lastActiveCheck, ref float ___lastCheckActivePawns)
	{
		if (MP.IsInMultiplayer)
		{
			InitCheckTicks(__instance, out ___lastActiveCheck, out ___lastCheckActivePawns);
		}
	}

	private static void InitFieldsExposeData(Building_CashRegister __instance, ref float ___lastActiveCheck, ref float ___lastCheckActivePawns)
	{
		if (MP.IsInMultiplayer && Scribe.mode == LoadSaveMode.PostLoadInit)
		{
			InitCheckTicks(__instance, out ___lastActiveCheck, out ___lastCheckActivePawns);
		}
	}

	private static void InitCheckTicks(Building_CashRegister instance, out float lastActiveCheck, out float lastCheckActivePawns)
	{
		int num = ((Thing)(object)instance).HashOffset() % 60;
		lastActiveCheck = Find.TickManager.TicksGame + num;
		lastCheckActivePawns = Find.TickManager.TicksGame + num + 7;
	}
}
