using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using UnityEngine;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Mlie.HuntersUseMelee")]
public sealed class HuntersUseMelee
{
	private sealed class PreviewRequest
	{
		internal Game Game;

		internal int Frame;
	}

	private static ConditionalWeakTable<CompEquippable, PreviewRequest> previewRequests = new ConditionalWeakTable<CompEquippable, PreviewRequest>();

	private static readonly AccessTools.FieldRef<VerbTracker, List<Verb>> savedVerbs = AccessTools.FieldRefAccess<VerbTracker, List<Verb>>("verbs");

	private static bool runtimeInstalled;

	public HuntersUseMelee(ModContentPack mod)
	{
		HuntersUseMeleeSettings.Install();
		InstallRuntime();
	}

	internal static void InstallRuntime()
	{
		if (!runtimeInstalled)
		{
			PatchingUtilities.SetupAsyncTime();
			MP.RegisterSyncMethod(typeof(HuntersUseMelee), "SyncInitVerbsForComp").SetVersion(18);
			SessionServices.RegisterLifecycle("mlie.huntersusemelee.verb-preview", null, () =>
			{
				previewRequests = new ConditionalWeakTable<CompEquippable, PreviewRequest>();
			});
			MethodInfo original = AccessTools.DeclaredMethod("HuntersUseMelee.WorkGiver_HunterHunt_HasHuntingWeapon:Postfix") ?? throw new MissingMethodException("HuntersUseMelee.WorkGiver_HunterHunt_HasHuntingWeapon", "Postfix");
			MpCompat.harmony.Patch(original, null, null, new HarmonyMethod(typeof(HuntersUseMelee), "HuntQueries"));
			runtimeInstalled = true;
		}
	}

	private static IEnumerable<CodeInstruction> HuntQueries(IEnumerable<CodeInstruction> instructions)
	{
		List<CodeInstruction> code = instructions.ToList();
		MethodInfo primary = AccessTools.PropertyGetter(typeof(CompEquippable), "PrimaryVerb");
		MethodInfo harms = AccessTools.DeclaredMethod(typeof(VerbUtility), "HarmsHealth", new Type[1] { typeof(Verb) });
		MethodInfo ticks = AccessTools.PropertyGetter(typeof(TickManager), "TicksGame");
		int verbPairs = 0;
		int clockReads = 0;
		for (int i = 0; i < code.Count; i++)
		{
			CodeInstruction codeInstruction = code[i];
			if (codeInstruction.Calls(primary) && i + 1 < code.Count && code[i + 1].Calls(harms))
			{
				if (code[i + 1].labels.Count != 0 || code[i + 1].blocks.Count != 0)
				{
					throw new InvalidOperationException("Hunters Use Melee verb query gained an internal branch");
				}
				verbPairs++;
				i++;
				yield return new CodeInstruction(codeInstruction)
				{
					opcode = OpCodes.Call,
					operand = AccessTools.DeclaredMethod(typeof(HuntersUseMelee), "SafeHarmsHealth")
				};
			}
			else if (codeInstruction.Calls(ticks))
			{
				clockReads++;
				CodeInstruction codeInstruction2 = new CodeInstruction(OpCodes.Ldarg_0);
				codeInstruction2.labels.AddRange(codeInstruction.labels);
				codeInstruction2.blocks.AddRange(codeInstruction.blocks);
				yield return codeInstruction2;
				yield return new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(HuntersUseMelee), "OwnedTicks"));
			}
			else
			{
				yield return codeInstruction;
			}
		}
		if (verbPairs != 1 || clockReads != 1)
		{
			throw new InvalidOperationException($"Hunters Use Melee hunt queries changed: verb={verbPairs}, clock={clockReads}");
		}
	}

	private static bool SafeHarmsHealth(CompEquippable equipment)
	{
		if (MP.IsInMultiplayer && MP.InInterface && savedVerbs(equipment.verbTracker) == null)
		{
			if (equipment.parent == null || equipment.parent.Destroyed)
			{
				return false;
			}
			PreviewRequest value = previewRequests.GetValue(equipment, (CompEquippable _) => new PreviewRequest());
			int frameCount = Time.frameCount;
			if (value.Game != Current.Game || frameCount - value.Frame >= 60)
			{
				value.Game = Current.Game;
				value.Frame = frameCount;
				SyncInitVerbsForComp(equipment);
			}
			return false;
		}
		return equipment.PrimaryVerb.HarmsHealth();
	}

	private static void SyncInitVerbsForComp(CompEquippable equipment)
	{
		if (MP.IsInMultiplayer && equipment?.parent != null && !equipment.parent.Destroyed)
		{
			if (savedVerbs(equipment.verbTracker) == null)
			{
				equipment.verbTracker.InitVerbsFromZero();
			}
			previewRequests.Remove(equipment);
		}
	}

	private static int OwnedTicks(TickManager manager, Pawn pawn)
	{
		if (!MP.IsInMultiplayer || !PatchingUtilities.IsAsyncTime)
		{
			return manager.TicksGame;
		}
		PatchingUtilities.TimeSnapshot? andSetFromPawn = PatchingUtilities.TimeSnapshot.GetAndSetFromPawn(pawn);
		try
		{
			return manager.TicksGame;
		}
		finally
		{
			andSetFromPawn?.Set();
		}
	}
}
