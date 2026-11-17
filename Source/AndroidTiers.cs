using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Atlas.AndroidTiers")]
internal class AndroidTiers
{
	private static Type compSurrogateOwnerType;

	private static AccessTools.FieldRef<object, Thing> surrogateOwnerSkyCloudHostField;

	private static Type compSkyCloudCoreType;

	private static AccessTools.FieldRef<object, List<Pawn>> skyCloudCoreStoredMindsField;

	private static FastInvokeHandler skyCloudCoreGetGizmos6;

	private static FastInvokeHandler skyCloudCoreGetGizmos11;

	private static FastInvokeHandler skyCloudCoreGetGizmos27;

	private static ConstructorInfo surrogateToHackConstructor;

	private static AccessTools.FieldRef<object, IntVec3> surrogateToHackPosField;

	private static AccessTools.FieldRef<object, Pawn> surrogateToHackTargetField;

	private static AccessTools.FieldRef<object, Map> surrogateToHackMapField;

	private static AccessTools.FieldRef<object, int> surrogateToHackHackTypeField;

	private static ConstructorInfo androidToControlConstructor;

	private static AccessTools.FieldRef<object, Pawn> androidToControlPawnField;

	private static AccessTools.FieldRef<object, bool> androidToControlFromSkyCloudField;

	private static AccessTools.FieldRef<object, int> androidToControlKindOfTargetField;

	private static AccessTools.FieldRef<object, IntVec3> androidToControlPosField;

	private static AccessTools.FieldRef<object, Thing> androidToControlTargetField;

	private static AccessTools.FieldRef<object, Map> androidToControlMapField;

	private static ConstructorInfo skillUpDialogConstructor;

	private static FastInvokeHandler skillUpGetNbPointsWantedToBuyMethod;

	private static AccessTools.FieldRef<object, Pawn> skillUpAndroidField;

	private static AccessTools.FieldRef<object, List<SkillDef>> skillUpSkillDefListField;

	private static AccessTools.FieldRef<object, List<int>> skillUpPointsField;

	private static AccessTools.FieldRef<object, List<int>> skillUpPassionsStateField;

	private static AccessTools.FieldRef<object, int> skillUpPointsNeededPerSkillField;

	private static AccessTools.FieldRef<object, int> skillUppointsNeededToIncreasePassionField;

	private static AccessTools.FieldRef<GameComponent> gcAttpCompField;

	private static FastInvokeHandler gameComponentDecSkillPointsMethod;

	private static FastInvokeHandler gameComponentConnectUserMethod;

	private static AccessTools.FieldRef<object, HashSet<Thing>> gameComponentConnectedThingField;

	private static AccessTools.FieldRef<object, int> gameComponentNbSlotField;

	public AndroidTiers(ModContentPack mod)
	{
		PatchingUtilities.PatchSystemRand(new string[10] { "MOARANDROIDS.CompHeatSensitive:PostSpawnSetup", "MOARANDROIDS.CompHeatSensitive:CheckTemperature", "MOARANDROIDS.StartingPawnUtility_Patch+NewGeneratedStartingPawn_Patch:Listener", "MOARANDROIDS.AndroidFactionDialogOverride:Postfix", "MOARANDROIDS.MultiplePawnRacesAtStart:Postfix", "MOARANDROIDS.Recipe_MemoryCorruptionChance:RandomCorruption", "MOARANDROIDS.Recipe_AndroidRewireSurgery:RandomCorruption", "MOARANDROIDS.Recipe_RemoveSentience:RandomCorruption", "MOARANDROIDS.Recipe_RerollTraits:RandomCorruption", "MOARANDROIDS.StockGenerator_SlaveAndroids+<GenerateThings>d__0:MoveNext" }, patchPushPop: false);
		Type parentType = AccessTools.TypeByName("MOARANDROIDS.CompAndroidState");
		MpCompat.RegisterLambdaDelegate(parentType, "CompGetGizmosExtra", 0, 2, 4, 5, 7, 10, 11, 12, 14);
		parentType = AccessTools.TypeByName("MOARANDROIDS.CompAutoDoor");
		MpCompat.RegisterLambdaMethod(parentType, "CompGetGizmosExtra", 0, 1);
		parentType = AccessTools.TypeByName("MOARANDROIDS.CompRemotelyControlledTurret");
		MpCompat.RegisterLambdaMethod(parentType, "CompGetGizmosExtra", default(int));
		parentType = (compSkyCloudCoreType = AccessTools.TypeByName("MOARANDROIDS.CompSkyCloudCore"));
		skyCloudCoreStoredMindsField = AccessTools.FieldRefAccess<List<Pawn>>(parentType, "storedMinds");
		MpCompat.RegisterLambdaMethod(parentType, "CompGetGizmosExtra", 6, 10, 11, 12, 13, 26, 27);
		foreach (MethodInfo lambdum in MpMethodUtil.GetLambda(parentType, "CompGetGizmosExtra", MethodType.Normal, null, 4, 16, 24))
		{
			MP.RegisterSyncMethod(lambdum);
			MP.RegisterSyncWorker<object>(SyncInnerClassWithPawn, lambdum.DeclaringType, isImplicit: false, shouldConstruct: true);
		}
		MethodInfo[] array = MpMethodUtil.GetLambda(parentType, "CompGetGizmosExtra", MethodType.Normal, null, 6, 11, 27).ToArray();
		skyCloudCoreGetGizmos6 = HarmonyLib.MethodInvoker.GetHandler(array[0]);
		skyCloudCoreGetGizmos11 = HarmonyLib.MethodInvoker.GetHandler(array[1]);
		skyCloudCoreGetGizmos27 = HarmonyLib.MethodInvoker.GetHandler(array[2]);
		MP.RegisterSyncMethod(typeof(AndroidTiers), "SyncedGetGizmos6");
		MP.RegisterSyncMethod(typeof(AndroidTiers), "SyncedGetGizmos11");
		MP.RegisterSyncMethod(typeof(AndroidTiers), "SyncedGetGizmos27");
		MpCompat.harmony.Patch(array[0], new HarmonyMethod(typeof(AndroidTiers), "PreCompGetGizmos6"));
		MpCompat.harmony.Patch(array[1], new HarmonyMethod(typeof(AndroidTiers), "PreCompGetGizmos11"));
		MpCompat.harmony.Patch(array[2], new HarmonyMethod(typeof(AndroidTiers), "PreCompGetGizmos27"));
		parentType = AccessTools.TypeByName("MOARANDROIDS.CompSkyMind");
		MpCompat.RegisterLambdaMethod(parentType, "CompGetGizmosExtra", 1, 3);
		parentType = (compSurrogateOwnerType = AccessTools.TypeByName("MOARANDROIDS.CompSurrogateOwner"));
		surrogateOwnerSkyCloudHostField = AccessTools.FieldRefAccess<Thing>(parentType, "skyCloudHost");
		MpCompat.RegisterLambdaDelegate(parentType, "CompGetGizmosExtra", 1, 3, 5, 9, 10, 13, 16, 19, 23);
		parentType = AccessTools.TypeByName("MOARANDROIDS.CPaths");
		MpCompat.RegisterLambdaDelegate(parentType, "QEE_BuildingPawnVatGrower_GetGizmosPostfix", 2, 5, 8);
		Type type = AccessTools.TypeByName("MOARANDROIDS.Designator_SurrogateToHack");
		surrogateToHackConstructor = AccessTools.Constructor(type, new Type[1] { typeof(int) });
		surrogateToHackPosField = AccessTools.FieldRefAccess<IntVec3>(type, "pos");
		surrogateToHackTargetField = AccessTools.FieldRefAccess<Pawn>(type, "target");
		surrogateToHackMapField = AccessTools.FieldRefAccess<Map>(type, "cmap");
		surrogateToHackHackTypeField = AccessTools.FieldRefAccess<int>(type, "hackType");
		MP.RegisterSyncWorker<Designator>(SyncSurrogateToHackDesignator, type);
		type = AccessTools.TypeByName("MOARANDROIDS.Designator_AndroidToControl");
		androidToControlConstructor = AccessTools.Constructor(type, new Type[2]
		{
			typeof(Pawn),
			typeof(bool)
		});
		androidToControlPawnField = AccessTools.FieldRefAccess<Pawn>(type, "controller");
		androidToControlFromSkyCloudField = AccessTools.FieldRefAccess<bool>(type, "fromSkyCloud");
		androidToControlKindOfTargetField = AccessTools.FieldRefAccess<int>(type, "kindOfTarget");
		androidToControlPosField = AccessTools.FieldRefAccess<IntVec3>(type, "pos");
		androidToControlTargetField = AccessTools.FieldRefAccess<Thing>(type, "target");
		androidToControlMapField = AccessTools.FieldRefAccess<Map>(type, "cmap");
		MP.RegisterSyncWorker<Designator>(SyncAndroidToControlDesignator, type);
		MP.RegisterSyncMethod(typeof(AndroidTiers), "SyncedConnectToSkyMind");
		MpCompat.harmony.Patch(AccessTools.Method(type, "CanDesignateThing"), null, null, new HarmonyMethod(typeof(AndroidTiers), "ReplaceConnectToSkyMind"));
		Type type2 = AccessTools.TypeByName("MOARANDROIDS.Dialog_SkillUp");
		skillUpDialogConstructor = AccessTools.Constructor(type2, new Type[2]
		{
			typeof(Pawn),
			typeof(bool)
		});
		skillUpGetNbPointsWantedToBuyMethod = HarmonyLib.MethodInvoker.GetHandler(AccessTools.Method(type2, "getNbPointsWantedToBuy"));
		skillUpAndroidField = AccessTools.FieldRefAccess<Pawn>(type2, "android");
		skillUpSkillDefListField = AccessTools.FieldRefAccess<List<SkillDef>>(type2, "sd");
		skillUpPointsField = AccessTools.FieldRefAccess<List<int>>(type2, "points");
		skillUpPassionsStateField = AccessTools.FieldRefAccess<List<int>>(type2, "points");
		skillUpPointsNeededPerSkillField = AccessTools.FieldRefAccess<int>(type2, "pointsNeededPerSkill");
		skillUppointsNeededToIncreasePassionField = AccessTools.FieldRefAccess<int>(type2, "pointsNeededToIncreasePassion");
		MP.RegisterSyncMethod(typeof(AndroidTiers), "SyncedSkillUpDialogAccept");
		MP.RegisterSyncWorker<Window>(SyncSkillUpDialog, type2);
		MpCompat.harmony.Patch(AccessTools.Method(type2, "DoWindowContents"), null, null, new HarmonyMethod(typeof(AndroidTiers), "ReplaceButton"));
		type2 = AccessTools.TypeByName("MOARANDROIDS.Utils");
		gcAttpCompField = AccessTools.StaticFieldRefAccess<GameComponent>(AccessTools.Field(type2, "GCATPP"));
		type2 = AccessTools.TypeByName("MOARANDROIDS.GC_ATPP");
		gameComponentDecSkillPointsMethod = HarmonyLib.MethodInvoker.GetHandler(AccessTools.Method(type2, "decSkillPoints"));
		gameComponentConnectUserMethod = HarmonyLib.MethodInvoker.GetHandler(AccessTools.Method(type2, "connectUser"));
		gameComponentConnectedThingField = AccessTools.FieldRefAccess<HashSet<Thing>>(type2, "connectedThing");
		gameComponentNbSlotField = AccessTools.FieldRefAccess<int>(type2, "nbSlot");
		Type type3 = AccessTools.TypeByName("Multiplayer.Client.Patches.CloseDialogsForExpiredLetters");
		Dictionary<Type, MethodInfo> dictionary = (Dictionary<Type, MethodInfo>)AccessTools.Field(type3, "rejectMethods").GetValue(null);
		string[] array2 = new string[2] { "MOARANDROIDS.ChoiceLetter_RansomDemand", "MOARANDROIDS.ChoiceLetter_RansomwareDemand" };
		for (int i = 0; i < array2.Length; i++)
		{
			type3 = AccessTools.TypeByName(array2[i]);
			dictionary.Add(type3, AccessTools.Method(typeof(ChoiceLetter), "Option_Reject"));
			MP.RegisterSyncMethod(MpMethodUtil.GetLambda(type3, "Choices", MethodType.Getter));
		}
	}

	private static void SyncSurrogateToHackDesignator(SyncWorker sync, ref Designator designator)
	{
		if (sync.isWriting)
		{
			sync.Write((byte)surrogateToHackHackTypeField(designator));
			sync.Write(surrogateToHackTargetField(designator));
			Map map = surrogateToHackMapField(designator);
			if (map == null)
			{
				sync.Write(IntVec3.Invalid);
				return;
			}
			sync.Write(surrogateToHackPosField(designator));
			sync.Write(map);
			return;
		}
		designator = (Designator)surrogateToHackConstructor.Invoke(new object[1] { (int)sync.Read<byte>() });
		surrogateToHackTargetField(designator) = sync.Read<Pawn>();
		IntVec3 intVec = sync.Read<IntVec3>();
		if (intVec.IsValid)
		{
			surrogateToHackPosField(designator) = intVec;
			surrogateToHackMapField(designator) = sync.Read<Map>();
		}
	}

	private static void SyncAndroidToControlDesignator(SyncWorker sync, ref Designator designator)
	{
		if (sync.isWriting)
		{
			Pawn pawn = androidToControlPawnField(designator);
			ThingComp instance = pawn.AllComps.FirstOrDefault((ThingComp c) => c.GetType() == compSurrogateOwnerType);
			ThingWithComps thingWithComps = (ThingWithComps)surrogateOwnerSkyCloudHostField(instance);
			sync.Write(thingWithComps == null);
			if (thingWithComps == null)
			{
				sync.Write(pawn);
			}
			else
			{
				sync.Write(thingWithComps);
			}
			sync.Write(pawn.thingIDNumber);
			sync.Write(androidToControlFromSkyCloudField(designator));
			sync.Write(androidToControlKindOfTargetField(designator));
			sync.Write(androidToControlTargetField(designator));
			Map map = androidToControlMapField(designator);
			if (map == null)
			{
				sync.Write(IntVec3.Invalid);
				return;
			}
			sync.Write(androidToControlPosField(designator));
			sync.Write(map);
			return;
		}
		Pawn pawn2;
		if (sync.Read<bool>())
		{
			pawn2 = sync.Read<Pawn>();
		}
		else
		{
			ThingComp comp = sync.Read<ThingWithComps>().AllComps.FirstOrDefault((ThingComp c) => c.GetType() == compSkyCloudCoreType);
			int id = sync.Read<int>();
			pawn2 = GetPawnFromComp(comp, id);
		}
		bool flag = sync.Read<bool>();
		designator = (Designator)androidToControlConstructor.Invoke(new object[2] { pawn2, flag });
		androidToControlKindOfTargetField(designator) = sync.Read<int>();
		androidToControlTargetField(designator) = sync.Read<Thing>();
		IntVec3 intVec = sync.Read<IntVec3>();
		if (intVec.IsValid)
		{
			androidToControlPosField(designator) = intVec;
			androidToControlMapField(designator) = sync.Read<Map>();
		}
	}

	private static void SyncSkillUpDialog(SyncWorker sync, ref Window dialog)
	{
		if (sync.isWriting)
		{
			sync.Write(skillUpAndroidField(dialog));
			sync.Write(skillUpSkillDefListField(dialog));
			sync.Write(skillUpPointsField(dialog));
			sync.Write(skillUpPassionsStateField(dialog));
			sync.Write(skillUpPointsNeededPerSkillField(dialog));
			sync.Write(skillUppointsNeededToIncreasePassionField(dialog));
		}
		else
		{
			Pawn pawn = sync.Read<Pawn>();
			dialog = (Window)skillUpDialogConstructor.Invoke(new object[2] { pawn, false });
			skillUpSkillDefListField(dialog) = sync.Read<List<SkillDef>>();
			skillUpPointsField(dialog) = sync.Read<List<int>>();
			skillUpPassionsStateField(dialog) = sync.Read<List<int>>();
			skillUpPointsNeededPerSkillField(dialog) = sync.Read<int>();
			skillUppointsNeededToIncreasePassionField(dialog) = sync.Read<int>();
		}
	}

	private static void SyncInnerClassWithPawn(SyncWorker sync, ref object inner)
	{
		Traverse traverse = Traverse.Create(inner);
		if (sync.isWriting)
		{
			ThingComp obj = (ThingComp)traverse.Field("<>4__this").GetValue();
			Pawn pawn = (Pawn)traverse.Field("p").GetValue();
			sync.Write(obj);
			sync.Write(pawn.thingIDNumber);
			return;
		}
		ThingComp thingComp = sync.Read<ThingComp>();
		int id = sync.Read<int>();
		Pawn pawnFromComp = GetPawnFromComp(thingComp, id);
		traverse.Field("<>4__this").SetValue(thingComp);
		if (pawnFromComp != null)
		{
			traverse.Field("p").SetValue(pawnFromComp);
		}
	}

	private static void SyncedSkillUpDialogAccept(Pawn android, List<SkillDef> sd, List<int> points, List<int> passionsState, int pointsPerSkill, int pointsPerPassion)
	{
		Window window = (Window)skillUpDialogConstructor.Invoke(new object[2] { android, false });
		skillUpSkillDefListField(window) = sd;
		skillUpPointsField(window) = points;
		skillUpPassionsStateField(window) = passionsState;
		skillUpPointsNeededPerSkillField(window) = pointsPerSkill;
		skillUppointsNeededToIncreasePassionField(window) = pointsPerPassion;
		int num = (int)skillUpGetNbPointsWantedToBuyMethod(window, -1);
		if (num == 0)
		{
			return;
		}
		gameComponentDecSkillPointsMethod(gcAttpCompField(), num * pointsPerSkill);
		for (int i = 0; i != sd.Count; i++)
		{
			SkillRecord skill = android.skills.GetSkill(sd[i]);
			if (skill != null)
			{
				skill.levelInt += points[i];
				if (passionsState[i] - (int)skill.passion > 0)
				{
					skill.passion = (Passion)passionsState[i];
				}
			}
		}
		if (MP.IsExecutingSyncCommandIssuedBySelf)
		{
			Messages.Message("ATPP_SkillsWorkshopPointsConverted".Translate(num, android.LabelShortCap), MessageTypeDefOf.PositiveEvent);
			Find.WindowStack.TryRemove(window.GetType());
		}
	}

	private static bool RedirectedMethod(Rect rect, string label, bool drawBackground, bool doMouseoverSound, bool active, Window instance)
	{
		if (Widgets.ButtonText(rect, label, drawBackground, doMouseoverSound, active))
		{
			if (!MP.IsInMultiplayer)
			{
				return true;
			}
			Pawn android = skillUpAndroidField(instance);
			List<SkillDef> sd = skillUpSkillDefListField(instance);
			List<int> points = skillUpPointsField(instance);
			List<int> passionsState = skillUpPassionsStateField(instance);
			int pointsPerSkill = skillUpPointsNeededPerSkillField(instance);
			int pointsPerPassion = skillUppointsNeededToIncreasePassionField(instance);
			SyncedSkillUpDialogAccept(android, sd, points, passionsState, pointsPerSkill, pointsPerPassion);
		}
		return false;
	}

	private static void SyncedConnectToSkyMind(Thing pawn, bool broadcastEvent)
	{
		GameComponent target = gcAttpCompField();
		gameComponentConnectUserMethod(target, pawn, broadcastEvent);
	}

	private static bool InjectedConnectUser(GameComponent comp, Thing pawn, bool broadcastEvent)
	{
		if (!MP.IsInMultiplayer)
		{
			return (bool)gameComponentConnectUserMethod(comp, pawn, broadcastEvent);
		}
		HashSet<Thing> hashSet = gameComponentConnectedThingField(comp);
		if (hashSet.Contains(pawn))
		{
			return true;
		}
		if (hashSet.Count >= gameComponentNbSlotField(comp))
		{
			return false;
		}
		SyncedConnectToSkyMind(pawn, broadcastEvent);
		return true;
	}

	private static Pawn GetPawnFromComp(ThingComp comp, int id)
	{
		return skyCloudCoreStoredMindsField(comp).FirstOrDefault((Pawn x) => x.thingIDNumber == id);
	}

	private static void SyncedGetGizmos6(ThingComp comp, int id)
	{
		skyCloudCoreGetGizmos6(comp, GetPawnFromComp(comp, id));
	}

	private static void SyncedGetGizmos11(ThingComp comp, int id)
	{
		skyCloudCoreGetGizmos11(comp, GetPawnFromComp(comp, id));
	}

	private static void SyncedGetGizmos27(ThingComp comp, int id)
	{
		skyCloudCoreGetGizmos27(comp, GetPawnFromComp(comp, id));
	}

	private static bool PreCompGetGizmos6(ThingComp __instance, Pawn p)
	{
		if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
		{
			return true;
		}
		SyncedGetGizmos6(__instance, p.thingIDNumber);
		return false;
	}

	private static bool PreCompGetGizmos11(ThingComp __instance, Pawn p)
	{
		if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
		{
			return true;
		}
		SyncedGetGizmos11(__instance, p.thingIDNumber);
		return false;
	}

	private static bool PreCompGetGizmos27(ThingComp __instance, Pawn p)
	{
		if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
		{
			return true;
		}
		SyncedGetGizmos27(__instance, p.thingIDNumber);
		return false;
	}

	private static IEnumerable<CodeInstruction> ReplaceButton(IEnumerable<CodeInstruction> instr)
	{
		MethodInfo method = AccessTools.Method(typeof(Widgets), "ButtonText", new Type[5]
		{
			typeof(Rect),
			typeof(string),
			typeof(bool),
			typeof(bool),
			typeof(bool)
		});
		bool toReplace = false;
		bool finished = false;
		foreach (CodeInstruction ci in instr)
		{
			if (!finished)
			{
				if (toReplace)
				{
					if (ci.opcode == OpCodes.Call && (MethodInfo)ci.operand == method)
					{
						yield return new CodeInstruction(OpCodes.Ldarg_0);
						ci.operand = AccessTools.Method(typeof(AndroidTiers), "RedirectedMethod");
						finished = true;
					}
				}
				else if (ci.opcode == OpCodes.Ldstr && (string)ci.operand == "OK")
				{
					toReplace = true;
				}
			}
			yield return ci;
		}
	}

	private static IEnumerable<CodeInstruction> ReplaceConnectToSkyMind(IEnumerable<CodeInstruction> instr)
	{
		MethodInfo method = AccessTools.Method("MOARANDROIDS.GC_ATPP:connectUser");
		MethodInfo replacement = AccessTools.Method(typeof(AndroidTiers), "InjectedConnectUser");
		foreach (CodeInstruction item in instr)
		{
			if (item.opcode == OpCodes.Callvirt && (MethodInfo)item.operand == method)
			{
				item.opcode = OpCodes.Call;
				item.operand = replacement;
			}
			yield return item;
		}
	}
}
