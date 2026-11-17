using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("ReBuild.COTR.DoorsAndCorners")]
public class ReBuildDoorsAndCorners
{
	private static Type roomExtensionType;

	private static FieldInfo closesRoomsField;

	private static HashSet<ThingDef> roomClosingDefs = new HashSet<ThingDef>();

	public ReBuildDoorsAndCorners(ModContentPack mod)
	{
		Type type = AccessTools.TypeByName("ReBuildDoorsAndCorners.RegionTypeUtility_GetExpectedRegionType_Patch") ?? throw new TypeLoadException("ReBuild room-closure postfix could not be resolved.");
		roomExtensionType = AccessTools.TypeByName("ReBuildDoorsAndCorners.ThingExtension") ?? throw new TypeLoadException("ReBuild room-closure extension could not be resolved.");
		closesRoomsField = AccessTools.Field(roomExtensionType, "closesRooms") ?? throw new MissingFieldException(roomExtensionType.FullName, "closesRooms");
		if (closesRoomsField.FieldType != typeof(bool))
		{
			throw new InvalidOperationException("ReBuild closesRooms is no longer a bool.");
		}
		MethodInfo original = AccessTools.DeclaredMethod(type, "Postfix", new Type[3]
		{
			typeof(IntVec3),
			typeof(Map),
			typeof(RegionType).MakeByRefType()
		}) ?? throw new MissingMethodException(type.FullName, "Postfix");
		MpCompat.harmony.Patch(original, null, null, new HarmonyMethod(typeof(ReBuildDoorsAndCorners), "SelectRoomClosingBuilding"));
		LongEventHandler.ExecuteWhenFinished(CacheRoomClosingDefs);
	}

	private static void CacheRoomClosingDefs()
	{
		HashSet<ThingDef> hashSet = new HashSet<ThingDef>();
		foreach (ThingDef item in DefDatabase<ThingDef>.AllDefsListForReading)
		{
			if (item.modExtensions == null)
			{
				continue;
			}
			foreach (DefModExtension modExtension in item.modExtensions)
			{
				if (modExtension != null && roomExtensionType.IsInstanceOfType(modExtension))
				{
					if ((bool)closesRoomsField.GetValue(modExtension))
					{
						hashSet.Add(item);
					}
					break;
				}
			}
		}
		roomClosingDefs = hashSet;
	}

	private static IEnumerable<CodeInstruction> SelectRoomClosingBuilding(IEnumerable<CodeInstruction> instructions)
	{
		List<CodeInstruction> list = instructions.ToList();
		MethodInfo method = AccessTools.DeclaredMethod(typeof(GridsUtility), "GetFirstBuilding", new Type[2]
		{
			typeof(IntVec3),
			typeof(Map)
		});
		MethodInfo operand = AccessTools.DeclaredMethod(typeof(ReBuildDoorsAndCorners), "RoomClosingBuilding");
		int num = 0;
		foreach (CodeInstruction item in list)
		{
			if (item.Calls(method))
			{
				item.opcode = OpCodes.Call;
				item.operand = operand;
				num++;
			}
		}
		if (num != 1)
		{
			throw new InvalidOperationException($"[ReBuild] Expected one GetFirstBuilding in the author's room postfix, found {num}.");
		}
		return list;
	}

	private static Building RoomClosingBuilding(IntVec3 cell, Map map)
	{
		if (MP.IsInMultiplayer)
		{
			List<Thing> thingList = cell.GetThingList(map);
			for (int i = 0; i < thingList.Count; i++)
			{
				if (thingList[i] is Building building && roomClosingDefs.Contains(building.def))
				{
					return building;
				}
			}
		}
		return cell.GetFirstBuilding(map);
	}
}
