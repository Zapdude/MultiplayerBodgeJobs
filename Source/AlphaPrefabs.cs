using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Sarg.AlphaPrefabs")]
public class AlphaPrefabs
{
	private static Type windowPrefabType;

	private static AccessTools.FieldRef<Window, Def> windowPrefabDefField;

	private static AccessTools.FieldRef<Window, Building> windowPrefabCatalogField;

	private static FastInvokeHandler checkModsSilverAndResearchMethod;

	public AlphaPrefabs(ModContentPack mod)
	{
		MpCompatPatchLoader.LoadPatch(this);
		MpCompat.RegisterLambdaMethod("AlphaPrefabs.Building_DeployedPrefab", "GetGizmos", 0, 1);
		MpCompat.RegisterLambdaDelegate("AlphaPrefabs.CompTargetable_Ground", "SelectedUseOption", default(int));
		Type type = (windowPrefabType = AccessTools.TypeByName("AlphaPrefabs.Window_Prefab"));
		windowPrefabDefField = AccessTools.FieldRefAccess<Def>(type, "prefab");
		windowPrefabCatalogField = AccessTools.FieldRefAccess<Building>(type, "building");
		checkModsSilverAndResearchMethod = HarmonyLib.MethodInvoker.GetHandler(AccessTools.DeclaredMethod(type, "CheckModsSilverAndResearch"));
		MP.RegisterSyncMethod(type, "OrderPrefab").SetContext(SyncContext.CurrentMap);
	}

	[MpCompatPrefix("AlphaPrefabs.Window_Prefab", "OrderPrefab", MethodType.Normal)]
	private static bool PreOrderPrefab(Window __instance)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		object[] array = new object[1];
		if ((bool)checkModsSilverAndResearchMethod(__instance, array))
		{
			__instance.Close();
			return true;
		}
		string text = (string)array[0];
		Building building = windowPrefabCatalogField(__instance);
		Messages.Message(text, (building != null) ? ((LookTargets)building) : null, MessageTypeDefOf.RejectInput, historical: false);
		return false;
	}

	private static IntVec3 ReplaceDropPosition(CameraDriver instance)
	{
		if (!MP.IsInMultiplayer)
		{
			return instance.MapPosition;
		}
		return DropCellFinder.TradeDropSpot(Find.CurrentMap);
	}

	[MpCompatTranspiler("AlphaPrefabs.Window_Prefab", "GetPosition", MethodType.Normal)]
	private static IEnumerable<CodeInstruction> ReplaceCameraMapPosition(IEnumerable<CodeInstruction> instr, MethodBase baseMethod)
	{
		MethodInfo target = AccessTools.PropertyGetter(typeof(CameraDriver), "MapPosition");
		MethodInfo replacement = AccessTools.DeclaredMethod(typeof(AlphaPrefabs), "ReplaceDropPosition");
		int replacedCount = 0;
		foreach (CodeInstruction item in instr)
		{
			if (item.Calls(target))
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
			Log.Warning($"Patched incorrect number of Find.CameraDriver.MapPosition calls (patched {replacedCount}, expected {1}) for method {value}");
		}
	}

	[MpCompatSyncWorker("AlphaPrefabs.Window_Prefab")]
	public static void SyncWindowPrefab(SyncWorker sync, ref Window dialog)
	{
		if (sync.isWriting)
		{
			sync.Write(windowPrefabDefField(dialog));
			sync.Write(windowPrefabCatalogField(dialog));
			return;
		}
		Def def = sync.Read<Def>();
		Building building = sync.Read<Building>();
		if (dialog == null)
		{
			dialog = (Window)Activator.CreateInstance(windowPrefabType, def, building);
		}
	}
}
