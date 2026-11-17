using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Fluffy.WorkTab")]
[MpCompatFor("arof.fluffy.worktab")]
[MpCompatFor("arof.fluffy.worktab.continued")]
internal class WorkTab
{
	private delegate void PasteTo(PawnColumnWorker_CopyPasteWorkPriorities instance, Pawn pawn);

	private static Type copyPasteColumnWorkerType;

	private static AccessTools.FieldRef<Dictionary<WorkGiverDef, int[]>> clipboardField;

	private static PasteTo pasteToMethod;

	public WorkTab(ModContentPack mod)
	{
		Type type = AccessTools.TypeByName("WorkTab.PriorityManager");
		MP.RegisterSyncMethod(AccessTools.PropertySetter(type, "ShowPriorities"));
		type = AccessTools.TypeByName("WorkTab.Pawn_Extensions");
		MP.RegisterSyncMethod(AccessTools.Method(type, "SetPriority", new Type[4]
		{
			typeof(Pawn),
			typeof(WorkTypeDef),
			typeof(int),
			typeof(List<int>)
		}));
		MP.RegisterSyncMethod(AccessTools.Method(type, "SetPriority", new Type[5]
		{
			typeof(Pawn),
			typeof(WorkTypeDef),
			typeof(int),
			typeof(int),
			typeof(bool)
		}));
		MP.RegisterSyncMethod(AccessTools.Method(type, "SetPriority", new Type[4]
		{
			typeof(Pawn),
			typeof(WorkGiverDef),
			typeof(int),
			typeof(List<int>)
		}));
		MP.RegisterSyncMethod(AccessTools.Method(type, "SetPriority", new Type[5]
		{
			typeof(Pawn),
			typeof(WorkGiverDef),
			typeof(int),
			typeof(int),
			typeof(bool)
		}));
		MP.RegisterSyncMethod(type, "DisableAll");
		type = (copyPasteColumnWorkerType = AccessTools.TypeByName("WorkTab.PawnColumnWorker_CopyPasteDetailedWorkPriorities"));
		clipboardField = AccessTools.StaticFieldRefAccess<Dictionary<WorkGiverDef, int[]>>(AccessTools.Field(type, "clipboard"));
		MethodInfo methodInfo = AccessTools.Method(type, "PasteTo");
		pasteToMethod = AccessTools.MethodDelegate<PasteTo>(methodInfo);
		MpCompat.harmony.Patch(methodInfo, new HarmonyMethod(typeof(WorkTab), "PrePasteTo"));
		MP.RegisterSyncMethod(typeof(WorkTab), "SyncedPasteTo");
		(string, Type)[] array = new (string, Type)[2]
		{
			("WorkTab.WorkType_Extensions", typeof(WorkTypeDef)),
			("WorkTab.WorkGiver_Extensions", typeof(WorkGiverDef))
		};
		for (int i = 0; i < array.Length; i++)
		{
			(string, Type) tuple = array[i];
			string item = tuple.Item1;
			Type item2 = tuple.Item2;
			type = AccessTools.TypeByName(item);
			MP.RegisterSyncMethod(AccessTools.Method(type, "DecrementPriority", new Type[5]
			{
				item2,
				typeof(List<Pawn>),
				typeof(int),
				typeof(List<int>),
				typeof(bool)
			}));
			MP.RegisterSyncMethod(AccessTools.Method(type, "IncrementPriority", new Type[5]
			{
				item2,
				typeof(List<Pawn>),
				typeof(int),
				typeof(List<int>),
				typeof(bool)
			}));
		}
		type = AccessTools.TypeByName("WorkTab.PawnColumnWorker_WorkTabLabel");
		MP.RegisterSyncMethod(type, "Decrement");
		MP.RegisterSyncMethod(type, "Increment");
		MP.RegisterSyncWorker<PawnColumnWorker_Label>(SyncPawnColumnLabel, type, isImplicit: false, shouldConstruct: true);
	}

	private static bool PrePasteTo(Pawn pawn)
	{
		if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
		{
			return true;
		}
		SyncedPasteTo(pawn, clipboardField());
		return false;
	}

	private static void SyncedPasteTo(Pawn pawn, Dictionary<WorkGiverDef, int[]> targetClipboard)
	{
		Dictionary<WorkGiverDef, int[]> dictionary = clipboardField();
		try
		{
			clipboardField() = targetClipboard;
			PawnColumnWorker_CopyPasteWorkPriorities instance = (PawnColumnWorker_CopyPasteWorkPriorities)Activator.CreateInstance(copyPasteColumnWorkerType);
			pasteToMethod(instance, pawn);
		}
		finally
		{
			clipboardField() = dictionary;
		}
	}

	private static void SyncPawnColumnLabel(SyncWorker sync, ref PawnColumnWorker_Label obj)
	{
	}
}
