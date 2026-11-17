using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("sindre0830.rimnauts2")]
public class RimNauts2
{
	private static bool isTickingComponent = false;

	private static bool isCameraJumpAllowed = true;

	private static AccessTools.FieldRef<Vector3> renderingManagerCenterField;

	private static FastInvokeHandler renderingManagerUpdateMethod;

	private static FastInvokeHandler renderingManagerGetFrameData;

	public RimNauts2(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
		Type type = AccessTools.TypeByName("RimNauts2.World.TypeExtension");
		PatchingUtilities.PatchUnityRand(AccessTools.DeclaredMethod(type, "rotation_angle"));
		PatchingUtilities.PatchUnityRand(AccessTools.DeclaredMethod(type, "transformation_rotation_angle"));
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod("RimNauts2.Things.SatelliteDish:generate_moon"));
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod("RimNauts2.Things.TransportPod:launch"));
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod("RimNauts2.Things.Comps.Mode:change_mode"));
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod("RimNauts2.Things.Comps.Mode:order_fire"));
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod("RimNauts2.Things.Comps.Targeter:chose_cell_target"));
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod("RimNauts2.World.Comps.DestroyObjectHolder:destroy_object")).SetDebugOnly();
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod("RimNauts2.World.Comps.GenerateObjectMap:generate_map")).SetDebugOnly();
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod("RimNauts2.World.Comps.RandomizeObjectHolder:randomize_object")).SetDebugOnly();
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod("RimNauts2.World.Patch.SettleInEmptyTileUtility_SettleCommand:settle"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod("RimNauts2.World.Patch.TravelingTransportPods_TraveledPctStepPerTick:Postfix"), new HarmonyMethod(typeof(RimNauts2), "PreTraveledPctStepPerTickPatch"), new HarmonyMethod(typeof(RimNauts2), "PostTraveledPctStepPerTickPatch"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod("RimNauts2.Things.Comps.Targeter:chose_cell_target"), new HarmonyMethod(typeof(RimNauts2), "PreJumpSetup"), null, new HarmonyMethod(typeof(RimNauts2), "ReplaceTryJumpTranspiler"));
		Type type2 = AccessTools.TypeByName("RimNauts2.World.Patch.SettleInEmptyTileUtility_SettleCommand");
		MethodInfo original = AccessTools.DeclaredMethod(type2, "settle");
		MpCompat.harmony.Patch(original, new HarmonyMethod(typeof(RimNauts2), "PreJumpSetup"));
		MpCompat.harmony.Patch(MpMethodUtil.GetLambda(type2, "settle"), null, null, new HarmonyMethod(typeof(RimNauts2), "ReplaceTryJumpTranspiler"));
	}

	private static void LatePatch()
	{
		MpCompat.RegisterLambdaMethod("RimNauts2.Things.Building.PodLauncher", "GetGizmos", default(int));
		Type type = AccessTools.TypeByName("RimNauts2.World.RenderingManager");
		renderingManagerCenterField = AccessTools.StaticFieldRefAccess<Vector3>(AccessTools.DeclaredField(type, "center"));
		renderingManagerGetFrameData = HarmonyLib.MethodInvoker.GetHandler(AccessTools.DeclaredMethod(type, "get_frame_data"));
		MethodInfo methodInfo = AccessTools.DeclaredMethod(type, "update");
		renderingManagerUpdateMethod = HarmonyLib.MethodInvoker.GetHandler(methodInfo);
		MpCompat.harmony.Patch(methodInfo, new HarmonyMethod(typeof(RimNauts2), "CancelOutsideOfTicking"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, "GameComponentTick"), null, new HarmonyMethod(typeof(RimNauts2), "PostTick"));
	}

	private static bool CancelOutsideOfTicking()
	{
		if (MP.IsInMultiplayer)
		{
			return isTickingComponent;
		}
		return true;
	}

	private static void PostTick(GameComponent __instance, ref bool ___unpaused, ref bool ___force_update)
	{
		if (!MP.IsInMultiplayer)
		{
			return;
		}
		isCameraJumpAllowed = true;
		isTickingComponent = true;
		try
		{
			renderingManagerGetFrameData(__instance);
			___unpaused = (___force_update = true);
			renderingManagerUpdateMethod(__instance);
		}
		finally
		{
			isTickingComponent = false;
		}
	}

	private static void PreTraveledPctStepPerTickPatch(ref Vector3? __state)
	{
		if (MP.IsInMultiplayer)
		{
			__state = renderingManagerCenterField();
			renderingManagerCenterField() = Vector3.zero;
		}
	}

	private static void PostTraveledPctStepPerTickPatch(Vector3? __state)
	{
		if (__state.HasValue)
		{
			renderingManagerCenterField() = __state.Value;
		}
	}

	private static void PreJumpSetup()
	{
		isCameraJumpAllowed = !MP.IsInMultiplayer || !MP.IsExecutingSyncCommand || MP.IsExecutingSyncCommandIssuedBySelf;
	}

	private static void ReplacedTryJump(GlobalTargetInfo target, CameraJumper.MovementMode mode = CameraJumper.MovementMode.Pan)
	{
		if (isCameraJumpAllowed)
		{
			CameraJumper.TryJump(target, mode);
		}
		else
		{
			isCameraJumpAllowed = true;
		}
	}

	private static IEnumerable<CodeInstruction> ReplaceTryJumpTranspiler(IEnumerable<CodeInstruction> instr, MethodBase baseMethod)
	{
		MethodInfo target = AccessTools.DeclaredMethod(typeof(CameraJumper), "TryJump", new Type[2]
		{
			typeof(GlobalTargetInfo),
			typeof(CameraJumper.MovementMode)
		});
		MethodInfo replacement = AccessTools.DeclaredMethod(typeof(RimNauts2), "ReplacedTryJump");
		int replacedCount = 0;
		foreach (CodeInstruction item in instr)
		{
			if ((item.opcode == OpCodes.Call || item.opcode == OpCodes.Callvirt) && item.operand is MethodInfo methodInfo && methodInfo == target)
			{
				item.opcode = OpCodes.Call;
				item.operand = replacement;
				replacedCount++;
			}
			yield return item;
		}
		if (replacedCount != 1)
		{
			string value = ((baseMethod.DeclaringType?.Namespace).NullOrEmpty() ? baseMethod.Name : (baseMethod.DeclaringType.Name + ":" + baseMethod.Name));
			Log.Warning($"Patched incorrect number of TryJump calls (patched {replacedCount}, expected {1}) for method {value}");
		}
	}
}
