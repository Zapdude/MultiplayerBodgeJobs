using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

internal static class IsekaiAsyncPassives
{
	private sealed class State
	{
		internal bool HasHit;

		internal bool HasLevel;

		internal bool HasCombat;

		internal bool HasTitle;
	}

	private static readonly ConditionalWeakTable<object, State> states = new ConditionalWeakTable<object, State>();

	private static readonly string[] passiveFields = new string[9] { "retributionStoredDamage", "lastHitTick", "huntMarkTargetId", "huntMarkStacks", "counterStrikeCharges", "frenzyStacks", "lastFrenzyKillTick", "eurekaInsightStacks", "lastEurekaTendTick" };

	private static AccessTools.FieldRef<object, int>[] passive;

	private static AccessTools.FieldRef<object, int> level;

	private static AccessTools.FieldRef<object, int> points;

	private static AccessTools.FieldRef<object, int> combat;

	private static AccessTools.FieldRef<object, int> title;

	private static Type trackerType;

	private static Type componentType;

	private static Func<int, int, float> calcInnerCalm;

	private static FieldInfo windowPawn;

	private static bool installed;

	[ThreadStatic]
	private static object calmTracker;

	private static bool Active
	{
		get
		{
			if (MP.IsInMultiplayer)
			{
				return PatchingUtilities.IsAsyncTime;
			}
			return false;
		}
	}

	private static State GetState(object owner)
	{
		return states.GetValue(owner, (object _) => new State());
	}

	private static HarmonyMethod Hook(string name)
	{
		return new HarmonyMethod(typeof(IsekaiAsyncPassives), name);
	}

	private static MethodInfo Required(Type type, string name, params Type[] args)
	{
		return AccessTools.DeclaredMethod(type, name, args) ?? throw new MissingMethodException(type.FullName, name);
	}

	internal static void Install()
	{
		if (installed)
		{
			return;
		}
		PatchingUtilities.SetupAsyncTime();
		trackerType = AccessTools.TypeByName("IsekaiLeveling.SkillTree.PassiveTreeTracker") ?? throw new TypeLoadException("Isekai PassiveTreeTracker");
		componentType = AccessTools.TypeByName("IsekaiLeveling.IsekaiComponent") ?? throw new TypeLoadException("IsekaiComponent");
		passive = passiveFields.Select((string fieldName) => AccessTools.FieldRefAccess<int>(trackerType, fieldName)).ToArray();
		level = AccessTools.FieldRefAccess<int>(componentType, "lastLevelUpTick");
		points = AccessTools.FieldRefAccess<int>(componentType, "lastPointsGainedTick");
		combat = AccessTools.FieldRefAccess<int>(componentType, "lastCombatTick");
		title = AccessTools.FieldRefAccess<int>(componentType, "titleTierChangedTick");
		PatchingUtilities.RegisterTimestampFields(trackerType, ShiftPassive);
		PatchingUtilities.RegisterTimestampFields(componentType, ShiftActor);
		Harmony harmony = MpCompat.harmony;
		harmony.Patch(Required(trackerType, "ExposeData"), null, Hook("ExposePassive"));
		harmony.Patch(Required(componentType, "PostExposeData"), null, Hook("ExposeActor"));
		harmony.Patch(Required(trackerType, "ResetGimmickState"), null, Hook("ClearHit"));
		harmony.Patch(Required(trackerType, "ResetCalmTimer"), null, null, Hook("TrackHitWrite"));
		harmony.Patch(Required(trackerType, "CalcInnerCalm", typeof(int), typeof(int)), null, null, Hook("KeepPassiveMarker"));
		harmony.Patch(Required(trackerType, "CalcInnerCalm"), null, null, Hook("KeepCalmOwner"));
		calcInnerCalm = (Func<int, int, float>)Required(trackerType, "CalcInnerCalm", typeof(int), typeof(int)).CreateDelegate(typeof(Func<int, int, float>));
		string[] array = new string[4] { "AccumulateFrenzy", "AccumulateEurekaInsight", "CalcBloodFrenzy", "CalcEurekaSynthesis" };
		foreach (string name in array)
		{
			foreach (MethodInfo item in from m in trackerType.GetMethods(AccessTools.allDeclared)
				where m.Name == name
				select m)
			{
				harmony.Patch(item, null, null, Hook("KeepPassiveMarker"));
			}
		}
		array = new string[2] { "CalcBloodFrenzy", "CalcEurekaSynthesis" };
		foreach (string name2 in array)
		{
			harmony.Patch(Required(trackerType, name2), Hook("BeginUiStackRead"), null, null, Hook("EndUiStackRead"));
		}
		array = new string[3] { "ResearchSpeed", "MedicalTendQuality", "SurgerySuccess" };
		foreach (string text in array)
		{
			Type type = AccessTools.TypeByName("IsekaiLeveling.Stats.StatPart_Isekai" + text) ?? throw new TypeLoadException(text);
			string[] array2 = new string[2] { "TransformValueCore", "ExplanationPartCore" };
			foreach (string text2 in array2)
			{
				harmony.Patch(AccessTools.DeclaredMethod(type, text2) ?? throw new MissingMethodException(text, text2), null, null, Hook("KeepCalmOwner"));
			}
		}
		Type type2 = AccessTools.TypeByName("IsekaiLeveling.Stats.StatPart_IsekaiBase");
		array = new string[2] { "TransformValue", "ExplanationPart" };
		foreach (string text3 in array)
		{
			harmony.Patch(AccessTools.DeclaredMethod(type2, text3) ?? throw new MissingMethodException(text3), Hook("BeginStatRead"), null, null, Hook("EndReadClock"));
		}
		Type type3 = AccessTools.TypeByName("IsekaiLeveling.UI.Window_SkillTree");
		windowPawn = AccessTools.Field(type3, "pawn") ?? throw new MissingFieldException("SkillTree.pawn");
		harmony.Patch(AccessTools.DeclaredMethod(type3, "DoWindowContents") ?? throw new MissingMethodException("DoWindowContents"), Hook("BeginWindowRead"), null, null, Hook("EndReadClock"));
		harmony.Patch(Required(componentType, "LevelUp"), null, null, Hook("TrackActorWrite"));
		Type type4 = AccessTools.TypeByName("IsekaiLeveling.MobRanking.MobRankRewards");
		harmony.Patch(AccessTools.DeclaredMethod(type4, "GiveKillMoods") ?? throw new MissingMethodException("GiveKillMoods"), null, null, Hook("TrackActorWrite"));
		Type type5 = AccessTools.TypeByName("IsekaiLeveling.IsekaiTitleApplier");
		harmony.Patch(AccessTools.DeclaredMethod(type5, "Refresh") ?? throw new MissingMethodException("Refresh"), null, null, Hook("TrackActorWrite"));
		Type type6 = AccessTools.TypeByName("IsekaiLeveling.ThoughtWorker_IsekaiIdentity");
		harmony.Patch(AccessTools.DeclaredMethod(type6, "CurrentStateInternal") ?? throw new MissingMethodException("CurrentStateInternal"), Hook("BeginThoughtRead"), null, null, Hook("EndReadClock"));
		array = new string[2] { "Stagnating", "UnfulfilledCalling" };
		foreach (string text4 in array)
		{
			harmony.Patch(AccessTools.DeclaredMethod(type6, text4) ?? throw new MissingMethodException(text4), null, null, Hook("KeepActorMarker"));
		}
		Type type7 = AccessTools.TypeByName("IsekaiLeveling.TitleDuelTracker");
		harmony.Patch(AccessTools.DeclaredMethod(type7, "SendChallengeLetter") ?? throw new MissingMethodException("SendChallengeLetter"), null, null, Hook("KeepActorMarker"));
		installed = true;
	}

	private static void ShiftPassive(object owner, int offset)
	{
		State state = GetState(owner);
		state.HasHit |= passive[1](owner) >= 0;
		if (state.HasHit)
		{
			passive[1](owner) += offset;
		}
		if (passive[5](owner) > 0)
		{
			passive[6](owner) += offset;
		}
		if (passive[7](owner) > 0)
		{
			passive[8](owner) += offset;
		}
	}

	private static void ShiftActor(object owner, int offset)
	{
		State state = GetState(owner);
		state.HasLevel |= level(owner) > 0;
		state.HasCombat |= combat(owner) > 0;
		state.HasTitle |= title(owner) > 0;
		if (state.HasLevel)
		{
			level(owner) += offset;
		}
		if (state.HasCombat)
		{
			combat(owner) += offset;
		}
		if (state.HasTitle)
		{
			title(owner) += offset;
		}
		points(owner) += offset;
	}

	private static void ExposePassive(object __instance)
	{
		if (MP.IsInMultiplayer)
		{
			State state = GetState(__instance);
			if (Scribe.mode == LoadSaveMode.Saving)
			{
				state.HasHit |= passive[1](__instance) >= 0;
			}
			Scribe_Values.Look(ref state.HasHit, "mpPassiveHasHit", defaultValue: false);
			for (int i = 0; i < passive.Length; i++)
			{
				bool flag = (((uint)(i - 1) <= 1u || i == 6 || i == 8) ? true : false);
				int defaultValue = (flag ? (-1) : 0);
				string label = "mp" + char.ToUpperInvariant(passiveFields[i][0]) + passiveFields[i].Substring(1);
				Scribe_Values.Look(ref passive[i](__instance), label, defaultValue);
			}
		}
	}

	private static void ExposeActor(object __instance)
	{
		if (MP.IsInMultiplayer)
		{
			State state = GetState(__instance);
			if (Scribe.mode == LoadSaveMode.Saving)
			{
				state.HasLevel |= level(__instance) > 0;
				state.HasCombat |= combat(__instance) > 0;
				state.HasTitle |= title(__instance) > 0;
			}
			Scribe_Values.Look(ref state.HasLevel, "mpHasLevelUpTimestamp", defaultValue: false);
			Scribe_Values.Look(ref state.HasCombat, "mpHasCombatTimestamp", defaultValue: false);
			Scribe_Values.Look(ref state.HasTitle, "mpHasTitleChangeTimestamp", defaultValue: false);
		}
	}

	private static void SetHit(object owner, int value)
	{
		passive[1](owner) = value;
		if (MP.IsInMultiplayer)
		{
			GetState(owner).HasHit = true;
		}
	}

	private static void SetLevel(object owner, int value)
	{
		level(owner) = value;
		if (MP.IsInMultiplayer)
		{
			GetState(owner).HasLevel = true;
		}
	}

	private static void SetCombat(object owner, int value)
	{
		combat(owner) = value;
		if (MP.IsInMultiplayer)
		{
			GetState(owner).HasCombat = true;
		}
	}

	private static void SetTitle(object owner, int value)
	{
		title(owner) = value;
		if (MP.IsInMultiplayer)
		{
			GetState(owner).HasTitle = true;
		}
	}

	private static void ClearHit(object __instance)
	{
		if (states.TryGetValue(__instance, out var value))
		{
			value.HasHit = false;
		}
	}

	private static float CalcOwnedCalm(object owner, int tier)
	{
		object obj = calmTracker;
		calmTracker = owner;
		try
		{
			return calcInnerCalm(passive[1](owner), tier);
		}
		finally
		{
			calmTracker = obj;
		}
	}

	private static void BeginStatRead(StatRequest req, ref PatchingUtilities.TimeSnapshot? __state)
	{
		if (Active && req.Thing is Pawn pawn)
		{
			__state = PatchingUtilities.TimeSnapshot.GetAndSetFromPawn(pawn);
		}
	}

	private static void BeginThoughtRead(Pawn p, ref PatchingUtilities.TimeSnapshot? __state)
	{
		if (Active && p != null)
		{
			__state = PatchingUtilities.TimeSnapshot.GetAndSetFromPawn(p);
		}
	}

	private static void BeginWindowRead(object __instance, ref PatchingUtilities.TimeSnapshot? __state)
	{
		if (Active && windowPawn.GetValue(__instance) is Pawn pawn)
		{
			__state = PatchingUtilities.TimeSnapshot.GetAndSetFromPawn(pawn);
		}
	}

	private static void EndReadClock(PatchingUtilities.TimeSnapshot? __state)
	{
		__state?.Set();
	}

	private static void BeginUiStackRead(object __instance, ref (bool restore, int stacks, int tick) __state, MethodBase __originalMethod)
	{
		if (MP.IsInMultiplayer && MP.InInterface)
		{
			bool flag = __originalMethod.Name == "CalcBloodFrenzy";
			__state = (restore: true, stacks: passive[flag ? 5 : 7](__instance), tick: passive[flag ? 6 : 8](__instance));
		}
	}

	private static void EndUiStackRead(object __instance, (bool restore, int stacks, int tick) __state, MethodBase __originalMethod)
	{
		if (__state.restore)
		{
			bool flag = __originalMethod.Name == "CalcBloodFrenzy";
			passive[flag ? 5 : 7](__instance) = __state.stacks;
			passive[flag ? 6 : 8](__instance) = __state.tick;
		}
	}

	private static int ReadCalmMarker(int tick)
	{
		if (tick >= 0 || !Active || calmTracker == null || !GetState(calmTracker).HasHit)
		{
			return tick;
		}
		return 0;
	}

	private static int ReadStackMarker(int tick)
	{
		if (!Active)
		{
			return tick;
		}
		return 0;
	}

	private static int ReadLevelMarker(object owner)
	{
		if (!Active || level(owner) > 0 || !GetState(owner).HasLevel)
		{
			return level(owner);
		}
		return 1;
	}

	private static int ReadCombatMarker(object owner)
	{
		if (!Active || combat(owner) > 0 || !GetState(owner).HasCombat)
		{
			return combat(owner);
		}
		return 1;
	}

	private static int ReadTitleMarker(object owner)
	{
		if (!Active || title(owner) > 0 || !GetState(owner).HasTitle)
		{
			return title(owner);
		}
		return 1;
	}

	private static IEnumerable<CodeInstruction> TrackHitWrite(IEnumerable<CodeInstruction> code)
	{
		return ReplaceWrite(code, AccessTools.Field(trackerType, "lastHitTick"), "SetHit");
	}

	private static IEnumerable<CodeInstruction> TrackActorWrite(IEnumerable<CodeInstruction> code, MethodBase __originalMethod)
	{
		string name = __originalMethod.Name switch
		{
			"LevelUp" => "lastLevelUpTick", 
			"GiveKillMoods" => "lastCombatTick", 
			"Refresh" => "titleTierChangedTick", 
			_ => throw new InvalidOperationException("Unknown actor timestamp writer"), 
		};
		string name2 = __originalMethod.Name;
		string text;
		if (name2 == "LevelUp")
		{
			text = "SetLevel";
		}
		else
		{
			text = ((!(name2 == "GiveKillMoods")) ? "SetTitle" : "SetCombat");
		}
		string setter = text;
		return ReplaceWrite(code, AccessTools.Field(componentType, name), setter);
	}

	private static IEnumerable<CodeInstruction> ReplaceWrite(IEnumerable<CodeInstruction> code, FieldInfo field, string setter)
	{
		int count = 0;
		foreach (CodeInstruction item in code)
		{
			if (item.StoresField(field))
			{
				item.opcode = OpCodes.Call;
				item.operand = AccessTools.DeclaredMethod(typeof(IsekaiAsyncPassives), setter);
				count++;
			}
			yield return item;
		}
		if (count != 1)
		{
			throw new InvalidOperationException($"Isekai timestamp writer changed: {field.Name}/{count}");
		}
	}

	private static IEnumerable<CodeInstruction> KeepPassiveMarker(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
	{
		List<CodeInstruction> list = instructions.ToList();
		bool flag = __originalMethod.Name == "CalcInnerCalm";
		string name = (__originalMethod.Name.Contains("Frenzy") ? "lastFrenzyKillTick" : "lastEurekaTendTick");
		int num = 0;
		for (int i = 0; i + 2 < list.Count; i++)
		{
			if ((__originalMethod.IsStatic ? (list[i].opcode == (flag ? OpCodes.Ldarg_0 : OpCodes.Ldarg_1)) : list[i].LoadsField(AccessTools.Field(trackerType, name))) && list[i + 1].LoadsConstant(0L) && (!(list[i + 2].opcode != OpCodes.Blt_S) || !(list[i + 2].opcode != OpCodes.Bge_S)))
			{
				CodeInstruction codeInstruction = new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(IsekaiAsyncPassives), flag ? "ReadCalmMarker" : "ReadStackMarker"));
				codeInstruction.MoveLabelsFrom(list[i + 1]).MoveBlocksFrom(list[i + 1]);
				list.Insert(++i, codeInstruction);
				num++;
			}
		}
		if (num != 1)
		{
			throw new InvalidOperationException($"Isekai passive marker changed: {__originalMethod}/{num}");
		}
		return list;
	}

	private static IEnumerable<CodeInstruction> KeepCalmOwner(IEnumerable<CodeInstruction> instructions)
	{
		int fields = 0;
		int calls = 0;
		FieldInfo hit = AccessTools.Field(trackerType, "lastHitTick");
		MethodInfo calc = Required(trackerType, "CalcInnerCalm", typeof(int), typeof(int));
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.LoadsField(hit))
			{
				instruction.opcode = OpCodes.Nop;
				instruction.operand = null;
				fields++;
			}
			if (instruction.Calls(calc))
			{
				instruction.operand = AccessTools.DeclaredMethod(typeof(IsekaiAsyncPassives), "CalcOwnedCalm");
				calls++;
			}
			yield return instruction;
		}
		if (fields != 1 || calls != 1)
		{
			throw new InvalidOperationException($"Isekai owned InnerCalm call changed: {fields}/{calls}");
		}
	}

	private static IEnumerable<CodeInstruction> KeepActorMarker(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
	{
		string name = __originalMethod.Name;
		string text;
		if (name == "Stagnating")
		{
			text = "lastLevelUpTick";
		}
		else
		{
			text = ((!(name == "UnfulfilledCalling")) ? "titleTierChangedTick" : "lastCombatTick");
		}
		string name2 = text;
		text = __originalMethod.Name;
		if (text == "Stagnating")
		{
			name = "ReadLevelMarker";
		}
		else
		{
			name = ((!(text == "UnfulfilledCalling")) ? "ReadTitleMarker" : "ReadCombatMarker");
		}
		string name3 = name;
		List<CodeInstruction> list = instructions.ToList();
		int num = 0;
		for (int i = 0; i + 1 < list.Count; i++)
		{
			if (list[i].LoadsField(AccessTools.Field(componentType, name2)) && list[i + 1].LoadsConstant(0L))
			{
				list[i].opcode = OpCodes.Call;
				list[i].operand = AccessTools.DeclaredMethod(typeof(IsekaiAsyncPassives), name3);
				num++;
			}
		}
		if (num != 1)
		{
			throw new InvalidOperationException($"Isekai actor marker changed: {__originalMethod}/{num}");
		}
		return list;
	}
}
