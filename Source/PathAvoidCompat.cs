using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using UnityEngine;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("pathavoid.kv.rw")]
public class PathAvoidCompat
{
	private static Type pathAvoidDefType;

	private static Type designator_PathAvoidType;

	private static Type pathAvoidGridType;

	private static AccessTools.FieldRef<object, int> levelField;

	private static AccessTools.FieldRef<object, Def> pathAvoidDefField;

	private static AccessTools.FieldRef<object, TerrainDef> selectedTerrainDefField;

	private static AccessTools.FieldRef<object, Def> selectedPathAvoidDefField;

	private static AccessTools.FieldRef<object, Def> selectedPathAvoidDefForResetField;

	private static MethodInfo pathAvoidGridSetValueMethod;

	public PathAvoidCompat(ModContentPack mod)
	{
		pathAvoidDefType = AccessTools.TypeByName("PathAvoid.PathAvoidDef");
		levelField = AccessTools.FieldRefAccess<int>(pathAvoidDefType, "level");
		designator_PathAvoidType = AccessTools.TypeByName("PathAvoid.Designator_PathAvoid");
		pathAvoidDefField = AccessTools.FieldRefAccess<Def>(designator_PathAvoidType, "def");
		MP.RegisterSyncWorker<Designator>(PathAvoidDesignatorSyncWorker, designator_PathAvoidType);
		Type type = AccessTools.TypeByName("PathAvoid.MapSettingsDialog");
		selectedTerrainDefField = AccessTools.FieldRefAccess<TerrainDef>(type, "selectedTerrainDef");
		selectedPathAvoidDefField = AccessTools.FieldRefAccess<Def>(type, "selectedPathAvoidDef");
		selectedPathAvoidDefForResetField = AccessTools.FieldRefAccess<Def>(type, "selectedPathAvoidDefForReset");
		MpCompat.harmony.Patch(AccessTools.Method(type, "DoWindowContents"), null, null, new HarmonyMethod(typeof(PathAvoidCompat), "ReplaceAcceptButtons"));
		pathAvoidGridType = AccessTools.TypeByName("PathAvoid.PathAvoidGrid");
		pathAvoidGridSetValueMethod = AccessTools.Method(pathAvoidGridType, "SetValue", new Type[2]
		{
			typeof(int),
			typeof(byte)
		});
		MP.RegisterSyncMethod(typeof(PathAvoidCompat), "SyncedSetAllToDef");
	}

	private static void PathAvoidDesignatorSyncWorker(SyncWorker sw, ref Designator designator)
	{
		Def def = null;
		if (sw.isWriting)
		{
			def = pathAvoidDefField(designator);
			sw.Write(def.defName);
			return;
		}
		string defName = sw.Read<string>();
		def = GenDefDatabase.GetDef(pathAvoidDefType, defName, errorOnFail: false);
		designator = (Designator)Activator.CreateInstance(designator_PathAvoidType, def);
	}

	private static bool SetAllTerrainToDefButton(Rect rect, string label, bool drawBackground, bool doMouseoverSounds, bool active, TextAnchor? overrideTextAnchor, Window instance)
	{
		bool flag = Widgets.ButtonText(rect, label, drawBackground, doMouseoverSounds, active, overrideTextAnchor);
		if (!MP.IsInMultiplayer)
		{
			return flag;
		}
		if (flag)
		{
			SyncedSetAllToDef(Current.Game.CurrentMap, (byte)levelField(selectedPathAvoidDefField(instance)), selectedTerrainDefField(instance));
		}
		return false;
	}

	private static bool SetAllToDefButton(Rect rect, string label, bool drawBackground, bool doMouseoverSounds, bool active, TextAnchor? overrideTextAnchor, Window instance)
	{
		bool flag = Widgets.ButtonText(rect, label, drawBackground, doMouseoverSounds, active, overrideTextAnchor);
		if (!MP.IsInMultiplayer)
		{
			return flag;
		}
		if (flag)
		{
			SyncedSetAllToDef(Current.Game.CurrentMap, (byte)levelField(selectedPathAvoidDefForResetField(instance)));
		}
		return false;
	}

	private static void SyncedSetAllToDef(Map map, byte pathAvoidLevel, TerrainDef filter = null)
	{
		MapComponent component = map.GetComponent(pathAvoidGridType);
		if (component != null)
		{
			for (int i = 0; i < map.terrainGrid.topGrid.Length; i++)
			{
				if (filter == null || map.terrainGrid.TerrainAt(i) == filter)
				{
					pathAvoidGridSetValueMethod.Invoke(component, new object[2] { i, pathAvoidLevel });
				}
			}
		}
		else
		{
			Log.Error("failed to get path avoid grid");
		}
	}

	private static IEnumerable<CodeInstruction> ReplaceAcceptButtons(IEnumerable<CodeInstruction> instr)
	{
		MethodInfo buttonMethod = AccessTools.Method(typeof(Widgets), "ButtonText", new Type[6]
		{
			typeof(Rect),
			typeof(string),
			typeof(bool),
			typeof(bool),
			typeof(bool),
			typeof(TextAnchor?)
		});
		bool isAccept = false;
		bool isSecond = false;
		int patchedCount = 0;
		foreach (CodeInstruction ci in instr)
		{
			if (isAccept && ci.opcode == OpCodes.Call && (MethodInfo)ci.operand == buttonMethod)
			{
				yield return new CodeInstruction(OpCodes.Ldarg_0);
				if (isSecond)
				{
					ci.operand = AccessTools.Method(typeof(PathAvoidCompat), "SetAllToDefButton");
					patchedCount++;
				}
				else
				{
					ci.operand = AccessTools.Method(typeof(PathAvoidCompat), "SetAllTerrainToDefButton");
					patchedCount++;
				}
				isAccept = false;
				isSecond = true;
			}
			else if (ci.opcode == OpCodes.Ldstr && (string)ci.operand == "Accept")
			{
				isAccept = true;
			}
			yield return ci;
		}
		switch (patchedCount)
		{
		case 0:
			Log.Warning("Failed to patch PathAvoid accept buttons");
			break;
		case 1:
			Log.Warning("Failed to patch both PathAvoid buttons");
			break;
		}
	}
}
