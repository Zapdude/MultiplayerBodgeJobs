using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;
using Verse.AI.Group;

namespace Multiplayer.Compat;

[MpCompatFor("CptVHOhu970.CorruptionWorship")]
public class CorruptionWorship
{
	private static FastInvokeHandler getDefByShortHash;

	public static ISyncField altarNameSyncField;

	public static AccessTools.FieldRef<object, IList> altarSermonTemplatesField;

	public static FastInvokeHandler altarEndSermonMethod;

	public static FastInvokeHandler altarTryStartSermonMethod;

	private static AccessTools.FieldRef<object, string> sermonTemplateNameField;

	private static AccessTools.FieldRef<object, int> sermonTemplatePreferredStartTimeField;

	private static AccessTools.FieldRef<object, float> sermonTemplateSermonDurationHoursField;

	private static AccessTools.FieldRef<object, bool> sermonTemplateActiveField;

	private static Type assignPreacherType;

	private static AccessTools.FieldRef<object, object> assignPreacherSermonField;

	private static AccessTools.FieldRef<object, Building> assignPreacherAltarField;

	private static Type assignAssistantType;

	private static AccessTools.FieldRef<object, Building> templeCardUtilityInnerAltarField;

	private static AccessTools.FieldRef<object, object> templeCardUtilityInnerSermonField;

	private static AccessTools.FieldRef<object, Def> wonderWorkerDefField;

	private static AccessTools.FieldRef<object, TargetInfo> wonderWorkerTargetableTargetField;

	private static AccessTools.FieldRef<object, bool> wonderWorkerTargetableCanceledField;

	private static AccessTools.FieldRef<object, object> wonderWorkerTargetableInnerBaseField;

	private static bool shouldCallCheckCancelled = true;

	private static AccessTools.FieldRef<object, object> wonderDefWorkerIntField;

	private static Type religiousRiotType;

	public CorruptionWorship(ModContentPack mod)
	{
		Type type = AccessTools.TypeByName("Corruption.Worship.BuildingAltar");
		altarNameSyncField = MP.RegisterSyncField(type, "RoomName");
		altarSermonTemplatesField = AccessTools.FieldRefAccess<IList>(type, "Templates");
		altarEndSermonMethod = HarmonyLib.MethodInvoker.GetHandler(AccessTools.Method(type, "EndSermon"));
		altarTryStartSermonMethod = HarmonyLib.MethodInvoker.GetHandler(AccessTools.Method(type, "TryStartSermon"));
		type = AccessTools.TypeByName("Corruption.Worship.SermonTemplate");
		sermonTemplateNameField = AccessTools.FieldRefAccess<string>(type, "Name");
		sermonTemplatePreferredStartTimeField = AccessTools.FieldRefAccess<int>(type, "preferredStartTime");
		sermonTemplateSermonDurationHoursField = AccessTools.FieldRefAccess<float>(type, "SermonDurationHours");
		sermonTemplateActiveField = AccessTools.FieldRefAccess<bool>(type, "Active");
		type = AccessTools.TypeByName("Corruption.Worship.Dialog_RenameTemple");
		MP.RegisterSyncMethod(typeof(CorruptionWorship), "SyncSermonName");
		MpCompat.harmony.Patch(AccessTools.Method(type, "DoWindowContents"), new HarmonyMethod(typeof(CorruptionWorship), "RenameTemplePrefix"), new HarmonyMethod(typeof(CorruptionWorship), "RenameTemplePostfix"));
		type = AccessTools.TypeByName("Corruption.Worship.Dialog_RenameSermon");
		MpCompat.harmony.Patch(AccessTools.Method(type, "DoWindowContents"), new HarmonyMethod(typeof(CorruptionWorship), "RenameSermonPrefix"), new HarmonyMethod(typeof(CorruptionWorship), "RenameSermonPostfix"));
		assignPreacherType = AccessTools.TypeByName("Corruption.Worship.Dialog_AssignPreacher");
		assignPreacherAltarField = AccessTools.FieldRefAccess<Building>(assignPreacherType, "altar");
		assignPreacherSermonField = AccessTools.FieldRefAccess<object>(assignPreacherType, "sermon");
		MP.RegisterSyncMethod(assignPreacherType, "AssignPawn");
		MP.RegisterSyncMethod(assignPreacherType, "UnassignPawn");
		MP.RegisterSyncWorker<Window>(SyncDialogAssignPreacher, assignPreacherType);
		assignAssistantType = AccessTools.TypeByName("Corruption.Worship.Dialog_AssignAssistant");
		MP.RegisterSyncMethod(assignAssistantType, "AssignPawn");
		MP.RegisterSyncMethod(assignAssistantType, "UnassignPawn");
		MP.RegisterSyncWorker<Window>(SyncDialogAssignPreacher, assignAssistantType);
		Type type2 = AccessTools.TypeByName("Corruption.Worship.Wonders.WonderWorker");
		Type wonderWorkerTargetableType = AccessTools.TypeByName("Corruption.Worship.Wonders.WonderWorker_Targetable");
		wonderWorkerDefField = AccessTools.FieldRefAccess<Def>(type2, "Def");
		wonderWorkerTargetableTargetField = AccessTools.FieldRefAccess<TargetInfo>(wonderWorkerTargetableType, "target");
		wonderWorkerTargetableCanceledField = AccessTools.FieldRefAccess<bool>(wonderWorkerTargetableType, "cancelled");
		MP.RegisterSyncWorker<object>(SyncWonderWorker, type2, isImplicit: true);
		MP.RegisterSyncWorker<object>(SyncWonderTargetableWorker, wonderWorkerTargetableType, isImplicit: true);
		MP.RegisterSyncMethod(wonderWorkerTargetableType, "StartTargeting");
		MP.RegisterSyncMethod(wonderWorkerTargetableType, "CheckCancelled");
		foreach (Type item in from x in type2.AllSubclasses()
			where !x.IsAssignableFrom(wonderWorkerTargetableType)
			select x)
		{
			MethodInfo methodInfo = AccessTools.Method(item, "TryExecuteWonder", new Type[2]
			{
				typeof(Def),
				typeof(int)
			});
			if (methodInfo != null)
			{
				MP.RegisterSyncMethod(methodInfo);
			}
		}
		MethodInfo[] array = MpMethodUtil.GetLambda(wonderWorkerTargetableType, "TryExecuteWonderInt", MethodType.Normal, null, 0, 1).ToArray();
		wonderWorkerTargetableInnerBaseField = AccessTools.FieldRefAccess<object>(array[0].DeclaringType, "<>4__this");
		MpCompat.harmony.Patch(array[0], new HarmonyMethod(typeof(CorruptionWorship), "PreStartTargetting"));
		MpCompat.harmony.Patch(array[1], new HarmonyMethod(typeof(CorruptionWorship), "PreCheckCancelled"));
		Type type3 = AccessTools.TypeByName("Corruption.Worship.Wonders.WonderDef");
		wonderDefWorkerIntField = AccessTools.FieldRefAccess<object>(type3, "workerInt");
		getDefByShortHash = HarmonyLib.MethodInvoker.GetHandler(AccessTools.Method(typeof(DefDatabase<>).MakeGenericType(type3), "GetByShortHash"));
		type3 = AccessTools.TypeByName("Corruption.Worship.GlobalWorshipTracker");
		MP.RegisterSyncMethod(type3, "ConsumeFavourFor");
		LongEventHandler.ExecuteWhenFinished(LatePatch);
	}

	private static void LatePatch()
	{
		Type type = AccessTools.TypeByName("Corruption.Worship.TempleCardUtility");
		MP.RegisterSyncDelegate(type, "<>c__DisplayClass4_1", "<OpenDedicationSelectMenu>b__1");
		MpCompat.harmony.Patch(AccessTools.Method(type, "DrawSermonTemplate"), new HarmonyMethod(typeof(CorruptionWorship), "DrawSermonTemplatePrefix"), new HarmonyMethod(typeof(CorruptionWorship), "DrawSermonTemplatePostfix"), new HarmonyMethod(typeof(CorruptionWorship), "PatchDrawSermonTemplate"));
		Type type2 = AccessTools.Inner(type, "<>c__DisplayClass4_0");
		templeCardUtilityInnerAltarField = AccessTools.FieldRefAccess<Building>(type2, "altar");
		templeCardUtilityInnerSermonField = AccessTools.FieldRefAccess<object>(type2, "template");
		MP.RegisterSyncMethod(typeof(CorruptionWorship), "SyncedInterceptedReceiveMemo");
		MP.RegisterSyncMethod(typeof(CorruptionWorship), "SyncedInterceptedEndSermon");
		MP.RegisterSyncMethod(typeof(CorruptionWorship), "SyncedTryStartSermon").SetDebugOnly();
		MP.RegisterSyncMethod(typeof(CorruptionWorship), "SyncSimpleSermonData");
		religiousRiotType = AccessTools.TypeByName("Corruption.Worship.Dialog_ReligiousRiot");
		MP.RegisterSyncMethod(religiousRiotType, "ChooseNewReligion");
		MP.RegisterSyncMethod(religiousRiotType, "PreserveReligion");
		MP.RegisterSyncWorker<Window>(SyncReligiousRiotDialog, religiousRiotType);
		MpCompat.RegisterLambdaMethod(AccessTools.TypeByName("Corruption.Worship.Building_WorshipStatue"), "GetGizmos", default(int));
		MpCompat.RegisterLambdaMethod(AccessTools.TypeByName("Corruption.Worship.CompBellTower"), "CompGetGizmosExtra", default(int)).SetDebugOnly();
		MP.RegisterSyncMethod(AccessTools.TypeByName("Corruption.Worship.CompShrine"), "DropEffigy");
	}

	private static bool TryGetDataForSermon(object sermon, out Building altar, out int index)
	{
		if (Find.Selector.SingleSelectedThing is Building building)
		{
			altar = building;
			return TryGetDataForSermon(sermon, altar, out index);
		}
		altar = null;
		index = -1;
		return false;
	}

	private static bool TryGetDataForSermon(object sermon, Building altar, out int index)
	{
		IList list = altarSermonTemplatesField(altar);
		index = list.IndexOf(sermon);
		return index >= 0;
	}

	private static object GetSermon(Building altar, int index)
	{
		return altarSermonTemplatesField(altar)[index];
	}

	private static void RenameTemplePrefix(Building ___Altar)
	{
		if (MP.enabled)
		{
			MP.WatchBegin();
			altarNameSyncField.Watch(___Altar);
		}
	}

	private static void RenameTemplePostfix()
	{
		if (MP.enabled)
		{
			MP.WatchEnd();
		}
	}

	private static void DrawSermonTemplatePrefix(object sermon, ref object[] __state)
	{
		if (MP.IsInMultiplayer)
		{
			__state = new object[3]
			{
				sermonTemplatePreferredStartTimeField(sermon),
				sermonTemplateSermonDurationHoursField(sermon),
				sermonTemplateActiveField(sermon)
			};
		}
	}

	private static void DrawSermonTemplatePostfix(Building altar, object sermon, ref object[] __state)
	{
		if (MP.IsInMultiplayer && TryGetDataForSermon(sermon, altar, out var index))
		{
			int num = sermonTemplatePreferredStartTimeField(sermon);
			float num2 = sermonTemplateSermonDurationHoursField(sermon);
			bool flag = sermonTemplateActiveField(sermon);
			if (num != (int)__state[0] || num2 != (float)__state[1] || flag != (bool)__state[2])
			{
				sermonTemplatePreferredStartTimeField(sermon) = (int)__state[0];
				sermonTemplateSermonDurationHoursField(sermon) = (float)__state[1];
				sermonTemplateActiveField(sermon) = (bool)__state[2];
				SyncSimpleSermonData(altar, index, num, num2, flag);
			}
		}
	}

	private static void SyncSimpleSermonData(Building altar, int index, int startTime, float duration, bool active)
	{
		object sermon = GetSermon(altar, index);
		sermonTemplatePreferredStartTimeField(sermon) = startTime;
		sermonTemplateSermonDurationHoursField(sermon) = duration;
		sermonTemplateActiveField(sermon) = active;
	}

	private static void RenameSermonPrefix(object ___Sermon, ref string __state)
	{
		if (MP.IsInMultiplayer)
		{
			__state = sermonTemplateNameField(___Sermon);
		}
	}

	private static void RenameSermonPostfix(object ___Sermon, string __state)
	{
		if (MP.IsInMultiplayer)
		{
			string text = sermonTemplateNameField(___Sermon);
			if (text != __state && TryGetDataForSermon(___Sermon, out var altar, out var index))
			{
				sermonTemplateNameField(___Sermon) = __state;
				SyncSermonName(altar, index, text);
			}
		}
	}

	private static void SyncSermonName(Building altar, int sermonIndex, string newSermonName)
	{
		sermonTemplateNameField(GetSermon(altar, sermonIndex)) = newSermonName;
	}

	private static void SyncDialogAssignPreacher(SyncWorker sync, ref Window obj)
	{
		if (sync.isWriting)
		{
			object sermon = assignPreacherSermonField(obj);
			Building building = assignPreacherAltarField(obj);
			if (TryGetDataForSermon(sermon, building, out var index))
			{
				sync.Write(index);
				sync.Write(building);
				sync.Write(obj.GetType() == assignPreacherType);
			}
			else
			{
				sync.Write(-1);
			}
			return;
		}
		int num = sync.Read<int>();
		if (num >= 0)
		{
			Building building2 = sync.Read<Building>();
			object sermon2 = GetSermon(building2, num);
			Type type = (sync.Read<bool>() ? assignPreacherType : assignAssistantType);
			obj = Activator.CreateInstance(type, building2, sermon2) as Window;
		}
	}

	private static void SyncTemplaCardUtilityInnerClass(SyncWorker sync, object obj)
	{
		if (sync.isWriting)
		{
			Building building = templeCardUtilityInnerAltarField(obj);
			if (TryGetDataForSermon(templeCardUtilityInnerSermonField(obj), building, out var index))
			{
				sync.Write(index);
				sync.Write(building);
			}
			else
			{
				sync.Write(-1);
			}
			return;
		}
		int num = sync.Read<int>();
		if (num >= 0)
		{
			Building building2 = sync.Read<Building>();
			templeCardUtilityInnerAltarField(obj) = building2;
			templeCardUtilityInnerSermonField(obj) = GetSermon(building2, num);
		}
	}

	private static void SyncWonderWorker(SyncWorker sync, ref object obj)
	{
		if (sync.isWriting)
		{
			sync.Write(wonderWorkerDefField(obj).shortHash);
			return;
		}
		obj = wonderDefWorkerIntField(getDefByShortHash(null, sync.Read<ushort>()));
	}

	private static void SyncWonderTargetableWorker(SyncWorker sync, ref object obj)
	{
		SyncWonderWorker(sync, ref obj);
		if (sync.isWriting)
		{
			sync.Write(wonderWorkerTargetableCanceledField(obj));
			sync.Write(wonderWorkerTargetableTargetField(obj));
		}
		else
		{
			wonderWorkerTargetableCanceledField(obj) = sync.Read<bool>();
			wonderWorkerTargetableTargetField(obj) = sync.Read<TargetInfo>();
		}
	}

	private static void SyncReligiousRiotDialog(SyncWorker sync, ref Window obj)
	{
		if (!sync.isWriting)
		{
			obj = Find.WindowStack.Windows.First((Window x) => x.GetType() == religiousRiotType);
		}
	}

	private static IEnumerable<CodeInstruction> PatchDrawSermonTemplate(IEnumerable<CodeInstruction> instr)
	{
		Type type = AccessTools.TypeByName("Corruption.Worship.BuildingAltar");
		MethodInfo endSermonMethod = AccessTools.Method(type, "EndSermon");
		MethodInfo tryStartSermonMethod = AccessTools.Method(type, "TryStartSermon");
		MethodInfo receiveMemo = AccessTools.Method(typeof(Lord), "ReceiveMemo");
		foreach (CodeInstruction item in instr)
		{
			if (item.opcode == OpCodes.Callvirt)
			{
				MethodInfo methodInfo = (MethodInfo)item.operand;
				if (methodInfo == receiveMemo)
				{
					item.opcode = OpCodes.Call;
					item.operand = AccessTools.Method(typeof(CorruptionWorship), "SyncedInterceptedReceiveMemo");
				}
				else if (methodInfo == endSermonMethod)
				{
					item.opcode = OpCodes.Call;
					item.operand = AccessTools.Method(typeof(CorruptionWorship), "SyncedInterceptedEndSermon");
				}
				else if (methodInfo == tryStartSermonMethod)
				{
					item.opcode = OpCodes.Call;
					item.operand = AccessTools.Method(typeof(CorruptionWorship), "InterceptedTryStartSermon");
				}
			}
			yield return item;
		}
	}

	private static void SyncedInterceptedReceiveMemo(Lord lord, string memo)
	{
		lord?.ReceiveMemo(memo);
	}

	private static void SyncedInterceptedEndSermon(Building altar)
	{
		if (altar != null)
		{
			altarEndSermonMethod(altar);
		}
	}

	private static bool InterceptedTryStartSermon(Building altar, object sermon)
	{
		if (TryGetDataForSermon(sermon, altar, out var index))
		{
			SyncedTryStartSermon(altar, index);
		}
		return true;
	}

	private static void SyncedTryStartSermon(Building altar, int index)
	{
		if (altarTryStartSermonMethod != null)
		{
			altarTryStartSermonMethod(altar, GetSermon(altar, index));
		}
	}

	private static void PreStartTargetting(object __instance)
	{
		if (MP.IsInMultiplayer)
		{
			object instance = wonderWorkerTargetableInnerBaseField(__instance);
			wonderWorkerTargetableCanceledField(instance) = false;
			shouldCallCheckCancelled = false;
		}
	}

	private static bool PreCheckCancelled()
	{
		bool flag = shouldCallCheckCancelled;
		shouldCallCheckCancelled = true;
		return !MP.IsInMultiplayer | flag;
	}
}
