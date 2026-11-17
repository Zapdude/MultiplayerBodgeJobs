using System;
using System.Collections.Generic;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Sarg.AlphaMemes")]
internal class AlphaMemes
{
	private static Type thoughtCatharsisType;

	private static Precept_Ritual lastPrecept;

	private static Type funeralFrameworkBehaviorWorkerType;

	private static AccessTools.FieldRef<RitualBehaviorWorker, ThingDef> funeralFrameworkBehaviorWorkerStuffToUseField;

	private static AccessTools.FieldRef<RitualBehaviorWorker, int> funeralFrameworkBehaviorWorkerStuffCountField;

	public AlphaMemes(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
		MpCompatPatchLoader.LoadPatch(this);
		Type type = AccessTools.TypeByName("AlphaMemes.Comp_CorpseContainer");
		AccessTools.FieldRef<object, Corpse> corpseContainterCompCorpseField = AccessTools.FieldRefAccess<Corpse>(type, "innerCorpse");
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod(type, "RemoveCorpse")).CancelIfAnyArgNull().TransformArgument(0, Serializer.New((Corpse _, object instance, object[] _) => (ThingComp)instance, (ThingComp comp) => corpseContainterCompCorpseField(comp)));
		type = AccessTools.TypeByName("AlphaMemes.Comp_CorpseContainerMulti");
		AccessTools.FieldRef<object, List<Corpse>> multiCorpseContainerCompCorpseListField = AccessTools.FieldRefAccess<List<Corpse>>(type, "innerCorpses");
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod(type, "RemoveCorpse")).CancelIfAnyArgNull().TransformArgument(0, Serializer.New((Corpse corpse, object instance, object[] _) => (comp: (ThingComp)instance, id: corpse.thingIDNumber), ((ThingComp comp, int id) data) => multiCorpseContainerCompCorpseListField(data.comp).Find((Corpse c) => c.thingIDNumber == data.id)));
		thoughtCatharsisType = AccessTools.TypeByName("AlphaMemes.Thought_Catharsis");
		if (thoughtCatharsisType != null)
		{
			PatchingUtilities.PatchTryGainMemory(TryGainThoughtCatharsis);
		}
		else
		{
			Log.Error("Trying to patch `AlphaMemes.Thought_Catharsis`, but the type is null. Did it get moved, renamed, or removed?");
		}
		funeralFrameworkBehaviorWorkerType = AccessTools.TypeByName("AlphaMemes.RitualBehaviorWorker_FuneralFramework");
		Type type2 = AccessTools.TypeByName("AlphaMemes.RitualBehaviorWorker_FuneralFramework");
		funeralFrameworkBehaviorWorkerStuffToUseField = AccessTools.FieldRefAccess<ThingDef>(type2, "stuffToUse");
		funeralFrameworkBehaviorWorkerStuffCountField = AccessTools.FieldRefAccess<int>(type2, "stuffCount");
	}

	private static void LatePatch()
	{
		MpCompat.RegisterLambdaMethod("AlphaMemes.Pawn_Detonator", "GetGizmos", default(int));
	}

	private static bool TryGainThoughtCatharsis(Thought_Memory thought)
	{
		if (!thoughtCatharsisType.IsInstanceOfType(thought))
		{
			return false;
		}
		thought.MoodOffset();
		return true;
	}

	[MpCompatSyncMethod(SyncContext.None)]
	private static void SyncPreceptChanges(Precept_Ritual precept, ThingDef stuff, int count)
	{
		funeralFrameworkBehaviorWorkerStuffToUseField(precept.behavior) = stuff;
		funeralFrameworkBehaviorWorkerStuffCountField(precept.behavior) = count;
	}

	[MpCompatPrefix("AlphaMemes.Dialog_BeginRitual_DoWindowContents_Patch", "Postfix", MethodType.Normal)]
	private static void PreFuneralRitualDialog(Precept_Ritual __2, ref (ThingDef stuff, int count)? __state)
	{
		if (!MP.IsInMultiplayer || Find.IdeoManager.classicMode)
		{
			lastPrecept = null;
			return;
		}
		if (__2?.behavior == null || !funeralFrameworkBehaviorWorkerType.IsInstanceOfType(__2.behavior))
		{
			lastPrecept = null;
			return;
		}
		lastPrecept = __2;
		__state = (funeralFrameworkBehaviorWorkerStuffToUseField(lastPrecept.behavior), funeralFrameworkBehaviorWorkerStuffCountField(lastPrecept.behavior));
	}

	[MpCompatPrefix("AlphaMemes.Dialog_BeginRitual_DoWindowContents_Patch", "Postfix", 3, MethodType.Normal)]
	private static void PreStuffCountChanged(ref (ThingDef stuff, int count)? __state)
	{
		if (lastPrecept != null)
		{
			__state = (funeralFrameworkBehaviorWorkerStuffToUseField(lastPrecept.behavior), funeralFrameworkBehaviorWorkerStuffCountField(lastPrecept.behavior));
		}
	}

	[MpCompatFinalizer("AlphaMemes.Dialog_BeginRitual_DoWindowContents_Patch", "Postfix", MethodType.Normal)]
	[MpCompatFinalizer("AlphaMemes.Dialog_BeginRitual_DoWindowContents_Patch", "Postfix", 3, MethodType.Normal)]
	private static void PostStuffCountChanged((ThingDef stuff, int count)? __state)
	{
		if (__state.HasValue && lastPrecept != null)
		{
			(ThingDef stuff, int count) value = __state.Value;
			ThingDef item = value.stuff;
			int item2 = value.count;
			ThingDef thingDef = funeralFrameworkBehaviorWorkerStuffToUseField(lastPrecept.behavior);
			int num = funeralFrameworkBehaviorWorkerStuffCountField(lastPrecept.behavior);
			if (thingDef != item || num != item2)
			{
				SyncPreceptChanges(lastPrecept, thingDef, num);
			}
		}
	}

	[MpCompatPrefix("AlphaMemes.RitualBehaviorWorker_FuneralFramework", "CanStartRitualNow", MethodType.Normal)]
	private static void PreCanStartFuneral(TargetInfo target, ref int ___checkTick, ref bool __state)
	{
		if (MP.IsInMultiplayer)
		{
			if (!MP.InInterface)
			{
				___checkTick = -1;
			}
			__state = true;
			Rand.PushState(Gen.HashCombineInt(Find.TickManager.TicksGame, target.GetHashCode()));
		}
	}

	[MpCompatFinalizer("AlphaMemes.RitualBehaviorWorker_FuneralFramework", "CanStartRitualNow", MethodType.Normal)]
	private static void PostCanStartFuneral(bool __state)
	{
		if (__state)
		{
			Rand.PopState();
		}
	}
}
