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

[MpCompatFor("VanillaExpanded.VPsycastsE")]
public class VanillaPsycastsExpandedTimeEffects
{
	private static FieldInfo multiplayerGame;

	private static FieldInfo worldTime;

	private static FieldInfo mapTimes;

	private static FieldInfo worldTicks;

	private static FieldInfo mapTicks;

	private static FieldInfo mapOfTime;

	private static FieldInfo previousTime;

	private static FieldInfo snapshotTicks;

	private static FieldInfo tickingMap;

	private static FieldInfo executingMap;

	private static Action<int> notifyTimeJump;

	public VanillaPsycastsExpandedTimeEffects(ModContentPack mod)
	{
		Type type = RequiredType("Multiplayer.Client.Multiplayer");
		Type type2 = RequiredType("Multiplayer.Client.MultiplayerGame");
		Type type3 = RequiredType("Multiplayer.Client.AsyncTimeComp");
		Type type4 = RequiredType("Multiplayer.Client.AsyncTime.AsyncWorldTimeComp");
		multiplayerGame = RequiredField(type, "game");
		worldTime = RequiredField(type2, "asyncWorldTimeComp");
		mapTimes = RequiredField(type2, "asyncTimeComps");
		worldTicks = RequiredField(type4, "worldTicks");
		mapTicks = RequiredField(type3, "mapTicks");
		mapOfTime = RequiredField(type3, "map");
		MethodInfo methodInfo = AccessTools.DeclaredMethod(type3, "NotifyTimeJump", new Type[1] { typeof(int) });
		notifyTimeJump = ((methodInfo == null) ? null : ((Action<int>)methodInfo.CreateDelegate(typeof(Action<int>))));
		if (notifyTimeJump == null)
		{
			previousTime = RequiredField(type3, "prevTime");
			snapshotTicks = RequiredField(Nullable.GetUnderlyingType(previousTime.FieldType) ?? throw new TypeLoadException("Multiplayer map time snapshot must be nullable"), "ticks");
		}
		tickingMap = RequiredField(type3, "tickingMap");
		executingMap = RequiredField(type3, "executingCmdMap");
		Type type5 = RequiredType("VanillaPsycastsExpanded.Chronopath.Ability_AdvanceSeason");
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type5, "Tick") ?? throw new MissingMethodException(type5.FullName, "Tick"), null, null, new HarmonyMethod(typeof(VanillaPsycastsExpandedTimeEffects), "ReplaceSeasonClockWrite"));
	}

	private static Type RequiredType(string name)
	{
		return AccessTools.TypeByName(name) ?? throw new TypeLoadException(name);
	}

	private static FieldInfo RequiredField(Type type, string name)
	{
		return AccessTools.Field(type, name) ?? throw new MissingFieldException(type.FullName, name);
	}

	private static IEnumerable<CodeInstruction> ReplaceSeasonClockWrite(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo setter = AccessTools.DeclaredMethod(typeof(TickManager), "DebugSetTicksGame", new Type[1] { typeof(int) });
		MethodInfo replacement = AccessTools.DeclaredMethod(typeof(VanillaPsycastsExpandedTimeEffects), "AdvanceSeasonClocks");
		int replaced = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(setter))
			{
				instruction.opcode = OpCodes.Call;
				instruction.operand = replacement;
				replaced++;
			}
			yield return instruction;
		}
		if (replaced != 1)
		{
			throw new InvalidOperationException("VPE season timeskip clock-write contract changed: " + replaced);
		}
	}

	private static void AdvanceSeasonClocks(TickManager manager, int newTicks)
	{
		if (!MP.IsInMultiplayer)
		{
			manager.DebugSetTicksGame(newTicks);
			return;
		}
		object value = multiplayerGame.GetValue(null);
		object obj = ((value == null) ? null : worldTime.GetValue(value));
		IEnumerable enumerable = ((value == null) ? null : (mapTimes.GetValue(value) as IEnumerable));
		if (obj == null || enumerable == null)
		{
			throw new InvalidOperationException("Season timeskip requires initialized Multiplayer clocks");
		}
		int num = newTicks - manager.TicksGame;
		object[] array = enumerable.Cast<object>().ToArray();
		object value2 = tickingMap.GetValue(null);
		object value3 = executingMap.GetValue(null);
		List<(object, int, object)> list = new List<(object, int, object)>();
		object[] array2 = array;
		foreach (object obj2 in array2)
		{
			object obj3 = null;
			object value4 = mapOfTime.GetValue(obj2);
			if (notifyTimeJump == null && value4 != null && (value4 == value2 || value4 == value3))
			{
				obj3 = previousTime.GetValue(obj2);
				if (obj3 != null)
				{
					snapshotTicks.SetValue(obj3, (int)snapshotTicks.GetValue(obj3) + num);
				}
			}
			list.Add((obj2, (int)mapTicks.GetValue(obj2) + num, obj3));
		}
		int num2 = (int)worldTicks.GetValue(obj) + num;
		worldTicks.SetValue(obj, num2);
		foreach (var item in list)
		{
			mapTicks.SetValue(item.Item1, item.Item2);
			if (item.Item3 != null)
			{
				previousTime.SetValue(item.Item1, item.Item3);
			}
		}
		manager.DebugSetTicksGame(newTicks);
		notifyTimeJump?.Invoke(num);
	}
}
