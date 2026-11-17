using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("CptOhu.CorruptionPsyker")]
public class CorruptionPsykers
{
	private static FastInvokeHandler getDefByShortHash;

	private static Type psykerWindowType;

	private static AccessTools.FieldRef<object, ThingComp> psykerWindowCompField;

	private static FastInvokeHandler compPsykerTryLearnAbilityMethod;

	private static AccessTools.FieldRef<object, Def> compPsykerMainDisciplineField;

	private static AccessTools.FieldRef<object, IList> compPsykerMinorDisciplinesField;

	private static ISyncField compPsykerXpSyncField;

	private static AccessTools.FieldRef<object, IList> psykerDisciplineDefAbilitiesField;

	private static Type psykerDisciplineWindowType;

	private static AccessTools.FieldRef<object, ThingComp> psykerDisciplineWindowCompField;

	private static AccessTools.FieldRef<object, Def> psykerDisciplineWindowSelectedDefField;

	private static Type psykerDisciplineMinorWindowType;

	public CorruptionPsykers(ModContentPack mod)
	{
		psykerDisciplineWindowType = AccessTools.TypeByName("Corruption.Psykers.Window_PsykerDiscipline");
		psykerDisciplineWindowCompField = AccessTools.FieldRefAccess<ThingComp>(psykerDisciplineWindowType, "comp");
		psykerDisciplineWindowSelectedDefField = AccessTools.FieldRefAccess<Def>(psykerDisciplineWindowType, "selectedDef");
		MP.RegisterSyncMethod(psykerDisciplineWindowType, "ChoosePower");
		MP.RegisterSyncWorker<object>(SyncPsykerDisciplineWindow, psykerDisciplineWindowType);
		psykerDisciplineMinorWindowType = AccessTools.TypeByName("Corruption.Psykers.Window_PsykerDisciplineMinor");
		MP.RegisterSyncMethod(psykerDisciplineMinorWindowType, "ChoosePower");
		MP.RegisterSyncWorker<object>(SyncPsykerDisciplineMinorWindow, psykerDisciplineMinorWindowType);
		LongEventHandler.ExecuteWhenFinished(LatePatch);
	}

	private static void LatePatch()
	{
		psykerWindowType = AccessTools.TypeByName("Corruption.Psykers.Window_Psyker");
		psykerWindowCompField = AccessTools.FieldRefAccess<ThingComp>(psykerWindowType, "comp");
		MP.RegisterSyncMethod(typeof(CorruptionPsykers), "SyncedAddMinorDiscipline");
		MpCompat.harmony.Patch(AccessTools.Method(psykerWindowType, "DoWindowContents"), new HarmonyMethod(typeof(CorruptionPsykers), "PrePsykerDoWindowContents"), new HarmonyMethod(typeof(CorruptionPsykers), "PostPsykerDoWindowContents"));
		MP.RegisterSyncMethod(typeof(CorruptionPsykers), "SyncedTryLearnPower");
		Type type = AccessTools.TypeByName("Corruption.Core.Abilities.LearnableAbility");
		Type type2 = AccessTools.TypeByName("Corruption.Psykers.CompPsyker");
		compPsykerTryLearnAbilityMethod = HarmonyLib.MethodInvoker.GetHandler(AccessTools.Method(type2, "TryLearnAbility", new Type[1] { type }));
		compPsykerMainDisciplineField = AccessTools.FieldRefAccess<Def>(type2, "MainDiscipline");
		compPsykerMinorDisciplinesField = AccessTools.FieldRefAccess<IList>(type2, "minorDisciplines");
		compPsykerXpSyncField = MP.RegisterSyncField(type2, "PsykerXP");
		type2 = AccessTools.TypeByName("Corruption.Psykers.PsykerDisciplineDef");
		psykerDisciplineDefAbilitiesField = AccessTools.FieldRefAccess<IList>(type2, "abilities");
		getDefByShortHash = HarmonyLib.MethodInvoker.GetHandler(AccessTools.Method(typeof(DefDatabase<>).MakeGenericType(type2), "GetByShortHash"));
		MpCompat.harmony.Patch(AccessTools.Method(psykerWindowType, "DrawSelectedPower"), null, null, new HarmonyMethod(typeof(CorruptionPsykers), "Transpiler"));
	}

	private static void PrePsykerDoWindowContents(Window __instance, ref object[] __state)
	{
		if (MP.IsInMultiplayer)
		{
			ThingComp thingComp = psykerWindowCompField(__instance);
			MP.WatchBegin();
			compPsykerXpSyncField.Watch(thingComp);
			IList list = compPsykerMinorDisciplinesField(thingComp);
			__state = new object[list.Count];
			list.CopyTo(__state, 0);
		}
	}

	private static void PostPsykerDoWindowContents(Window __instance, ref object[] __state)
	{
		if (!MP.IsInMultiplayer)
		{
			return;
		}
		MP.WatchEnd();
		ThingComp thingComp = psykerWindowCompField(__instance);
		IList list = compPsykerMinorDisciplinesField(thingComp);
		if (__state.Length == list.Count)
		{
			return;
		}
		foreach (object item in list)
		{
			if (!__state.Contains(item, null))
			{
				SyncedAddMinorDiscipline(thingComp, ((Def)item).shortHash);
				break;
			}
		}
	}

	private static void SyncedAddMinorDiscipline(ThingComp comp, ushort hash)
	{
		compPsykerMinorDisciplinesField(comp).Add(getDefByShortHash(null, hash));
	}

	private static void SyncPsykerDisciplineWindow(SyncWorker sync, ref object obj)
	{
		SyncPsykerDisciplineWindowAny(sync, ref obj, psykerDisciplineWindowType);
	}

	private static void SyncPsykerDisciplineMinorWindow(SyncWorker sync, ref object obj)
	{
		SyncPsykerDisciplineWindowAny(sync, ref obj, psykerDisciplineMinorWindowType);
	}

	private static void SyncPsykerDisciplineWindowAny(SyncWorker sync, ref object obj, Type windowType)
	{
		if (sync.isWriting)
		{
			sync.Write(psykerDisciplineWindowCompField(obj));
			sync.Write(psykerDisciplineWindowSelectedDefField(obj).shortHash);
			return;
		}
		ThingComp comp = sync.Read<ThingComp>();
		ushort num = sync.Read<ushort>();
		object obj2 = getDefByShortHash(null, num);
		obj = Find.WindowStack.Windows.FirstOrDefault((Window x) => x.GetType() == windowType && psykerDisciplineWindowCompField(x) == comp);
		if (obj == null)
		{
			object obj3 = Activator.CreateInstance(psykerWindowType, comp);
			obj = Activator.CreateInstance(windowType, comp, obj3);
		}
		psykerDisciplineWindowSelectedDefField(obj) = (Def)obj2;
	}

	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instr)
	{
		Type type = AccessTools.TypeByName("Corruption.Psykers.PsykerLearnablePower");
		Type type2 = AccessTools.TypeByName("Corruption.Psykers.CompPsyker");
		MethodInfo tryLearnAbilityMethod = AccessTools.Method(type2, "TryLearnAbility", new Type[1] { type });
		foreach (CodeInstruction item in instr)
		{
			if (item.opcode == OpCodes.Callvirt && item.operand is MethodInfo methodInfo && methodInfo == tryLearnAbilityMethod)
			{
				item.opcode = OpCodes.Call;
				item.operand = AccessTools.Method(typeof(CorruptionPsykers), "InjectedTryLearnPower");
			}
			yield return item;
		}
	}

	private static bool InjectedTryLearnPower(ThingComp comp, object selectedPower)
	{
		Def instance = compPsykerMainDisciplineField(comp);
		int num = psykerDisciplineDefAbilitiesField(instance).IndexOf(selectedPower);
		if (num >= 0)
		{
			SyncedTryLearnPower(comp, int.MinValue, num);
		}
		else
		{
			IList list = compPsykerMinorDisciplinesField(comp);
			for (int i = 0; i < list.Count; i++)
			{
				num = psykerDisciplineDefAbilitiesField(list[i]).IndexOf(selectedPower);
				if (num >= 0)
				{
					SyncedTryLearnPower(comp, i, num);
					break;
				}
			}
		}
		return true;
	}

	private static void SyncedTryLearnPower(ThingComp comp, int disciplineIndex, int abilityIndex)
	{
		IList list2;
		if (disciplineIndex >= 0)
		{
			IList list = compPsykerMinorDisciplinesField(comp);
			list2 = psykerDisciplineDefAbilitiesField(list[disciplineIndex]);
		}
		else
		{
			Def instance = compPsykerMainDisciplineField(comp);
			list2 = psykerDisciplineDefAbilitiesField(instance);
		}
		compPsykerTryLearnAbilityMethod(comp, list2[abilityIndex]);
	}
}
