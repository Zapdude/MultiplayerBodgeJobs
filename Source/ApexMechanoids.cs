using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Multiplayer.Compat;

[MpCompatFor("Mortis.ApexMechanoids")]
public class ApexMechanoids
{
	private sealed class QueueSaveData
	{
		public List<Thing> Targets;

		public List<IntVec3> Cells;

		public List<int> Actions;
	}

	private const string Ns = "ApexMechanoids.";

	private static Type remoteType;

	private static readonly ConditionalWeakTable<object, QueueSaveData> QueueSaveStates = new ConditionalWeakTable<object, QueueSaveData>();

	private static readonly string[] StartNames = new string[6] { null, "StartToConnect", "StartToDisconnect", "StartToRepair", "StartToShield", "StartToHackStasisContainer" };

	private static readonly string[] QueueNames = new string[6] { null, "AddQuedActionConnect", "AddQuedActionDisconnect", "AddQuedActionRepair", "AddQuedActionShield", "AddQuedActionOpenStasisContainer" };

	private static bool Intercept
	{
		get
		{
			if (MP.IsInMultiplayer && MP.InInterface)
			{
				return !MP.IsExecutingSyncCommand;
			}
			return false;
		}
	}

	public ApexMechanoids(ModContentPack mod)
	{
		remoteType = TypeOf("CompRemoteMechCasketAbilities");
		string[] array = new string[15]
		{
			"SetToggle", "SetRocket", "SetPurifierMode", "SetRepairToggle", "CancelRepair", "SelectRepairPawn", "CycleRepairInterval", "MobilizeDynamo", "RemoteCommand", "FinishCasket",
			"OrderGazerShot", "EnterContainer", "OrderTinkerRepair", "CallBoss", "InstantReconnect"
		};
		foreach (string text in array)
		{
			ISyncMethod syncMethod = MP.RegisterSyncMethod(typeof(ApexMechanoids), text);
			switch (text)
			{
			case "CycleRepairInterval":
			case "CallBoss":
			case "InstantReconnect":
				syncMethod.SetDebugOnly();
				break;
			}
		}
		array = new string[8] { "CompIngestorAbsorbSettings", "CompGazerLaserController", "CompRavagerArtilleryController", "CompJavelinRocketRack", "CompToxicPurifier", "CompPowerPlantDynamo", "Comp_MechanoidContainerControlled", "CompUseEffect_CallMulBossgroup" };
		for (int i = 0; i < array.Length; i++)
		{
			Patch(TypeOf(array[i]), "CompGetGizmosExtra", null, "CompGizmos");
		}
		Patch(TypeOf("Building_RepairStation"), "GetGizmos", null, "RepairGizmos");
		Patch(TypeOf("Building_RepairStation"), "SelectPawn", "RepairSelectPrefix");
		Patch(TypeOf("Building_MechCommandCasket"), "Finish", "FinishPrefix");
		Patch(TypeOf("Building_GazerEmplacement"), "TryOrderShot", "GazerShotPrefix");
		MP.RegisterSyncMethod(TypeOf("Building_GazerEmplacement"), "ClearForcedTarget");
		MP.RegisterSyncMethod(TypeOf("Comp_MechanoidContainer"), "DeployMech").SetDebugOnly();
		MP.RegisterSyncMethod(TypeOf("FloatMenuOptionProvider_PickUp_Frostivus"), "TryOrderTakeFood");
		Patch(TypeOf("FloatMenuOptionProvider_TinkerRepair"), "MakeRepairOption", null, "TinkerMenu");
		for (int j = 1; j < StartNames.Length; j++)
		{
			Patch(remoteType, StartNames[j], "RemoteStartPrefix");
			Patch(remoteType, QueueNames[j], "RemoteQueuePrefix");
		}
		Patch(remoteType, "AddQuedAction", "RemoteQueuePrefix");
		Patch(remoteType, "EndActionWithSound", "RemoteEndPrefix");
		foreach (MethodInfo item in from m in AccessTools.GetDeclaredMethods(remoteType)
			where (m.Name == "ForceSetTargetPawn" || m.Name == "ForceSetTargetThing") && m.GetParameters().Length == 2
			select m)
		{
			MpCompat.harmony.Patch(item, new HarmonyMethod(typeof(ApexMechanoids), "ForceTargetPrefix"));
		}
		Patch(remoteType, "StartQuedAction", "StartQueuedPrefix");
		Patch(remoteType, "PostExposeData", null, "RemoteExpose", "QueueScribeTranspiler");
		Patch(TypeOf("OverwriteSelectOverseer"), "GetGizmos", null, "OverseerGizmos");
		ApexMechanoidsDeterminism.Install();
		ApexMechanoidsGameplay.Install();
		ApexMechanoidsState.Install();
		ApexMechanoidsAsyncTime.Install();
	}

	private static Type TypeOf(string name)
	{
		return AccessTools.TypeByName("ApexMechanoids." + name) ?? throw new TypeLoadException("ApexMechanoids." + name);
	}

	private static object Get(object obj, string name)
	{
		object? obj2 = AccessTools.Field(obj.GetType(), name)?.GetValue(obj);
		if (obj2 == null)
		{
			PropertyInfo propertyInfo = AccessTools.Property(obj.GetType(), name);
			if ((object)propertyInfo == null)
			{
				return null;
			}
			obj2 = propertyInfo.GetValue(obj);
		}
		return obj2;
	}

	private static object Invoke(object obj, string name, params object[] args)
	{
		return AccessTools.Method(obj.GetType(), name).Invoke(obj, args);
	}

	private static ThingComp Comp(Thing parent, string name)
	{
		return (parent as ThingWithComps)?.AllComps.FirstOrDefault((ThingComp c) => TypeOf(name).IsInstanceOfType(c));
	}

	private static void Patch(Type type, string method, string prefix = null, string postfix = null, string transpiler = null)
	{
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, method), (prefix == null) ? null : new HarmonyMethod(typeof(ApexMechanoids), prefix), (postfix == null) ? null : new HarmonyMethod(typeof(ApexMechanoids), postfix), (transpiler == null) ? null : new HarmonyMethod(typeof(ApexMechanoids), transpiler));
	}

	private static bool Valid(Thing thing)
	{
		if (thing != null && !thing.Destroyed)
		{
			return thing.Spawned;
		}
		return false;
	}

	private static void CompGizmos(ThingComp __instance, ref IEnumerable<Gizmo> __result)
	{
		__result = WrapCompGizmos(__instance, __result);
	}

	private static IEnumerable<Gizmo> WrapCompGizmos(ThingComp comp, IEnumerable<Gizmo> gizmos)
	{
		int toggle = 0;
		string type = comp.GetType().Name;
		foreach (Gizmo gizmo in gizmos)
		{
			if (!MP.IsInMultiplayer)
			{
				yield return gizmo;
				continue;
			}
			if (gizmo is Command_Toggle command_Toggle && (type == "CompIngestorAbsorbSettings" || type == "CompGazerLaserController" || type == "CompRavagerArtilleryController"))
			{
				string field;
				if (type == "CompIngestorAbsorbSettings")
				{
					field = ((toggle++ == 0) ? "stripCorpseBeforeAbsorb" : "onlyProcessMarkedCorpses");
				}
				else
				{
					field = ((type == "CompGazerLaserController") ? "autoLaserEnabled" : "autoFireEnabled");
				}
				command_Toggle.toggleAction = () =>
				{
					SetToggle(comp.parent, type, field);
				};
			}
			if (gizmo is Command_Action command_Action)
			{
				if (type == "CompJavelinRocketRack")
				{
					command_Action.action = () =>
					{
						RocketMenu(comp);
					};
				}
				else if (type == "CompToxicPurifier")
				{
					command_Action.action = () =>
					{
						PurifierMenu(comp);
					};
				}
				else if (type == "CompPowerPlantDynamo")
				{
					command_Action.action = () =>
					{
						MobilizeDynamo(comp.parent);
					};
				}
				else if (type == "Comp_MechanoidContainerControlled" && command_Action.defaultLabel == (string)Get(Get(comp, "Props"), "ChooseMechLabel"))
				{
					command_Action.action = () =>
					{
						ContainerTargeter(comp);
					};
				}
				else if (type == "CompUseEffect_CallMulBossgroup" && command_Action.defaultLabel == "DEV: Call with Points")
				{
					command_Action.action = () =>
					{
						Find.WindowStack.Add(new FloatMenu((from i in Enumerable.Range(1, 10)
							select new FloatMenuOption((i * 1000).ToString(), () =>
							{
								CallBoss(comp.parent, i * 1000);
							})).ToList()));
					};
				}
			}
			yield return gizmo;
		}
	}

	private static void SetToggle(Thing parent, string compName, string fieldName)
	{
		if (Valid(parent) && ((compName == "CompIngestorAbsorbSettings" && (fieldName == "stripCorpseBeforeAbsorb" || fieldName == "onlyProcessMarkedCorpses")) || (compName == "CompGazerLaserController" && fieldName == "autoLaserEnabled") || (compName == "CompRavagerArtilleryController" && fieldName == "autoFireEnabled")))
		{
			ThingComp thingComp = Comp(parent, compName);
			if (thingComp != null)
			{
				FieldInfo fieldInfo = AccessTools.Field(thingComp.GetType(), fieldName);
				fieldInfo.SetValue(thingComp, !(bool)fieldInfo.GetValue(thingComp));
			}
		}
	}

	private static void RocketMenu(ThingComp comp)
	{
		List<FloatMenuOption> list = new List<FloatMenuOption>();
		foreach (object item in (IEnumerable)Get(comp, "Rockets"))
		{
			if ((bool)Invoke(comp, "CanSelect", item))
			{
				string key = (string)Get(item, "key");
				string text = (string)Get(item, "label");
				int num = (int)Get(item, "uraniumCost");
				string text2 = ((num > 0) ? "APM.Javelin.RocketCost".Translate(text, num) : "APM.Javelin.RocketFree".Translate(text));
				bool flag = (bool)Invoke(comp, "CanFire", item);
				if (!flag)
				{
					text2 += " " + "APM.Javelin.NoUranium".Translate();
				}
				FloatMenuOption floatMenuOption = new FloatMenuOption(text2, () =>
				{
					SetRocket(comp.parent, key);
				});
				floatMenuOption.Disabled = !flag;
				string text3 = (string)Get(item, "description");
				if (!text3.NullOrEmpty())
				{
					floatMenuOption.tooltip = new TipSignal(text3);
				}
				list.Add(floatMenuOption);
			}
		}
		if (list.Count != 0)
		{
			Find.WindowStack.Add(new FloatMenu(list));
		}
	}

	private static void SetRocket(Thing parent, string key)
	{
		ThingComp thingComp = ((parent != null && !parent.Destroyed && parent.SpawnedOrAnyParentSpawned) ? Comp(parent, "CompJavelinRocketRack") : null);
		if (thingComp == null || !(bool)Get(thingComp, "IsPlayerLauncher"))
		{
			return;
		}
		foreach (object item in (IEnumerable)Get(thingComp, "Rockets"))
		{
			if ((string)Get(item, "key") == key && (bool)Invoke(thingComp, "CanSelect", item) && (bool)Invoke(thingComp, "CanFire", item))
			{
				AccessTools.Field(thingComp.GetType(), "loadedKey").SetValue(thingComp, key);
				break;
			}
		}
	}

	private static void PurifierMenu(ThingComp comp)
	{
		List<FloatMenuOption> list = new List<FloatMenuOption>();
		Type fieldType = AccessTools.Field(comp.GetType(), "mode").FieldType;
		MethodInfo methodInfo = AccessTools.Method(TypeOf("PurifierModeUtility"), "GetLabel");
		MethodInfo methodInfo2 = AccessTools.Method(TypeOf("PurifierModeUtility"), "GetIcon");
		foreach (object value2 in Enum.GetValues(fieldType))
		{
			int value = Convert.ToInt32(value2);
			list.Add(new FloatMenuOption((string)methodInfo.Invoke(null, new object[1] { value2 }), () =>
			{
				SetPurifierMode(comp.parent, value);
			}, (Texture2D)methodInfo2.Invoke(null, new object[1] { value2 }), Color.white));
		}
		Find.WindowStack.Add(new FloatMenu(list));
	}

	private static void SetPurifierMode(Thing parent, int mode)
	{
		ThingComp thingComp = (Valid(parent) ? Comp(parent, "CompToxicPurifier") : null);
		if (thingComp != null)
		{
			FieldInfo fieldInfo = AccessTools.Field(thingComp.GetType(), "mode");
			if (Enum.IsDefined(fieldInfo.FieldType, mode))
			{
				fieldInfo.SetValue(thingComp, Enum.ToObject(fieldInfo.FieldType, mode));
				Invoke(thingComp, "InvalidateCellToUnpolluteCache");
			}
		}
	}

	private static void MobilizeDynamo(Thing parent)
	{
		if (Valid(parent) && Comp(parent, "CompPowerPlantDynamo") != null)
		{
			parent.Destroy();
		}
	}

	private static void RepairGizmos(Thing __instance, ref IEnumerable<Gizmo> __result)
	{
		__result = WrapRepairGizmos(__instance, __result);
	}

	private static IEnumerable<Gizmo> WrapRepairGizmos(Thing station, IEnumerable<Gizmo> gizmos)
	{
		foreach (Gizmo gizmo in gizmos)
		{
			if (MP.IsInMultiplayer)
			{
				if (gizmo is Command_Toggle command_Toggle && command_Toggle.defaultLabel == "APM_Gizmo_AutoRepair".Translate())
				{
					command_Toggle.toggleAction = () =>
					{
						ToggleRepair(station);
					};
				}
				else if (gizmo is Command_Action command_Action)
				{
					if (command_Action.defaultLabel == "APM_CommandCancelRepair".Translate())
					{
						command_Action.action = () =>
						{
							CancelRepair(station);
						};
					}
					else if (command_Action.defaultLabel == "APM_InsertMech".Translate() + "...")
					{
						command_Action.action = () =>
						{
							RepairPawnMenu(station);
						};
					}
					else if (command_Action.defaultLabel.StartsWith("DEV: ") && command_Action.action?.Target == station)
					{
						command_Action.action = () =>
						{
							CycleRepairInterval(station);
						};
					}
				}
			}
			yield return gizmo;
		}
	}

	private static void ToggleRepair(Thing station)
	{
		SetRepairToggle(station);
	}

	private static void SetRepairToggle(Thing station)
	{
		if (Valid(station))
		{
			FieldInfo fieldInfo = AccessTools.Field(station.GetType(), "autoRepairEnabled");
			fieldInfo.SetValue(station, !(bool)fieldInfo.GetValue(station));
		}
	}

	private static void RepairPawnMenu(Thing station)
	{
		Building_Enterable enterable = (Building_Enterable)station;
		List<FloatMenuOption> list = (from p in station.Map.mapPawns.AllPawnsSpawned
			where p.RaceProps.IsMechanoid && p.Faction == Faction.OfPlayer && enterable.CanAcceptPawn(p).Accepted
			select new FloatMenuOption(p.LabelCap, () =>
			{
				SelectRepairPawn(station, p);
			}, p, Color.white)).ToList();
		if (list.Count == 0)
		{
			list.Add(new FloatMenuOption("APM_NoMechsAvailable".Translate(), null));
		}
		Find.WindowStack.Add(new FloatMenu(list));
	}

	private static bool RepairSelectPrefix(Thing __instance, Pawn pawn)
	{
		if (!Intercept)
		{
			return true;
		}
		SelectRepairPawn(__instance, pawn);
		return false;
	}

	private static void SelectRepairPawn(Thing station, Pawn pawn)
	{
		if (Valid(station) && Valid(pawn) && pawn.Map == station.Map && ((Building_Enterable)station).CanAcceptPawn(pawn).Accepted)
		{
			Invoke(station, "SelectPawn", pawn);
		}
	}

	private static void CancelRepair(Thing station)
	{
		if (Valid(station))
		{
			if (((Building_Enterable)station).Working)
			{
				Invoke(station, "EjectContents");
				return;
			}
			Invoke(station, "ClearQueuedRepair", true);
		}
	}

	private static void CycleRepairInterval(Thing station)
	{
		if (Valid(station))
		{
			int[] array = (int[])AccessTools.Field(station.GetType(), "IntervalOptions").GetValue(null);
			FieldInfo fieldInfo = AccessTools.Field(station.GetType(), "autoRepairIntervalTicks");
			fieldInfo.SetValue(station, array[(Array.IndexOf(array, (int)fieldInfo.GetValue(station)) + 1) % array.Length]);
		}
	}

	private static bool ForceTargetPrefix(Thing __0, ref LocalTargetInfo __1)
	{
		if (!Intercept)
		{
			return true;
		}
		__1 = new LocalTargetInfo(__0);
		return false;
	}

	private static bool RemoteStartPrefix(ThingComp __instance, LocalTargetInfo target, MethodBase __originalMethod)
	{
		if (!Intercept)
		{
			return true;
		}
		RemoteCommand(__instance.parent, target.Thing?.thingIDNumber ?? (-1), Array.IndexOf(StartNames, __originalMethod.Name), queued: false);
		return false;
	}

	private static bool RemoteQueuePrefix(ThingComp __instance, LocalTargetInfo __0, object[] __args, MethodBase __originalMethod)
	{
		if (!Intercept)
		{
			return true;
		}
		int action = ((__originalMethod.Name == "AddQuedAction") ? ((int)__args[1]) : Array.IndexOf(QueueNames, __originalMethod.Name));
		RemoteCommand(__instance.parent, __0.Thing?.thingIDNumber ?? (-1), action, queued: true);
		return false;
	}

	private static bool RemoteEndPrefix(ThingComp __instance)
	{
		if (!Intercept)
		{
			return true;
		}
		RemoteCommand(__instance.parent, -1, 0, queued: false);
		return false;
	}

	private static void RemoteCommand(Thing casket, int targetId, int action, bool queued)
	{
		ThingComp thingComp = (Valid(casket) ? Comp(casket, "CompRemoteMechCasketAbilities") : null);
		if (thingComp == null || action < 0 || action >= StartNames.Length)
		{
			return;
		}
		if (action == 0)
		{
			Invoke(thingComp, "EndActionWithSound");
		}
		else
		{
			if (!MP.TryGetThingById(targetId, out var value) || !Valid(value) || !(Get(thingComp, "User") is Pawn { Dead: false }) || !(bool)Get(thingComp, "CanUseAbilities"))
			{
				return;
			}
			LocalTargetInfo localTargetInfo = new LocalTargetInfo(value);
			string name = action switch
			{
				4 => "CanRemoteShield", 
				3 => "CanRemoteRepair", 
				2 => "CanRemoteDisconnect", 
				1 => "CanRemoteConnect", 
				_ => "IsStasisContainerWithContent", 
			};
			if (action == 5)
			{
				ThingComp thingComp2 = Comp(value, "Comp_MechanoidContainer");
				if (thingComp2 == null || (bool)Get(thingComp2, "IsEmpty"))
				{
					return;
				}
			}
			else if (!(bool)Invoke(thingComp, name, localTargetInfo))
			{
				return;
			}
			if ((action != 3 || (bool)Invoke(thingComp, "HasImplantRepair")) && (action != 4 || ((bool)Invoke(thingComp, "HasImplantShield") && (queued || !((float)Get(thingComp, "TicksForShieldcooldown") > 0f)))))
			{
				Invoke(thingComp, queued ? QueueNames[action] : StartNames[action], localTargetInfo);
			}
		}
	}

	private static bool FinishPrefix(Thing __instance)
	{
		if (!Intercept)
		{
			return true;
		}
		FinishCasket(__instance);
		return false;
	}

	private static void FinishCasket(Thing casket)
	{
		if (Valid(casket))
		{
			Invoke(casket, "Finish");
		}
	}

	private static bool StartQueuedPrefix(ThingComp __instance)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		IList list = (IList)Get(__instance, "QuedActions");
		if (list == null || list.Count == 0)
		{
			return false;
		}
		object obj = list[0];
		list.RemoveAt(0);
		if (obj != null)
		{
			LocalTargetInfo localTargetInfo = (LocalTargetInfo)Get(obj, "targetinfo");
			RemoteCommand(__instance.parent, localTargetInfo.Thing?.thingIDNumber ?? (-1), (int)Get(obj, "action"), queued: false);
		}
		return false;
	}

	private static void RemoteExpose(ThingComp __instance)
	{
		if (Scribe.mode == LoadSaveMode.PostLoadInit)
		{
			Thing thing = Get(__instance, "curTarget") as Thing;
			AccessTools.Field(remoteType, "curLocalTargetInfo").SetValue(__instance, (thing == null) ? LocalTargetInfo.Invalid : new LocalTargetInfo(thing));
		}
	}

	private static IEnumerable<CodeInstruction> QueueScribeTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.operand is MethodInfo methodInfo && methodInfo.DeclaringType == typeof(Scribe_Collections) && methodInfo.IsGenericMethod && methodInfo.GetGenericArguments()[0].FullName == "ApexMechanoids.MechCasketQuedAction")
			{
				instruction.operand = AccessTools.Method(typeof(ApexMechanoids), "ScribeQueue").MakeGenericMethod(methodInfo.GetGenericArguments());
			}
			yield return instruction;
		}
	}

	private static void ScribeQueue<T>(ref List<T> queue, string label, LookMode unused, params object[] ctorArgs)
	{
		if (queue == null)
		{
			queue = new List<T>();
		}
		QueueSaveData orCreateValue = QueueSaveStates.GetOrCreateValue(queue);
		if (Scribe.mode == LoadSaveMode.Saving)
		{
			orCreateValue.Targets = queue.Select((T q) => ((LocalTargetInfo)Get(q, "targetinfo")).Thing).ToList();
			orCreateValue.Cells = queue.Select((T q) => ((LocalTargetInfo)Get(q, "targetinfo")).Cell).ToList();
			orCreateValue.Actions = queue.Select((T q) => (int)Get(q, "action")).ToList();
		}
		Scribe_Collections.Look(ref orCreateValue.Targets, label + "_mpTargets", LookMode.Reference);
		Scribe_Collections.Look(ref orCreateValue.Cells, label + "_mpCells", LookMode.Value);
		Scribe_Collections.Look(ref orCreateValue.Actions, label + "_mpActions", LookMode.Value);
		if (Scribe.mode != LoadSaveMode.ResolvingCrossRefs)
		{
			return;
		}
		queue = new List<T>();
		if (orCreateValue.Targets == null || orCreateValue.Cells == null || orCreateValue.Actions == null)
		{
			return;
		}
		for (int num = 0; num < Math.Min(orCreateValue.Targets.Count, Math.Min(orCreateValue.Cells.Count, orCreateValue.Actions.Count)); num++)
		{
			if (orCreateValue.Actions[num] >= 1 && orCreateValue.Actions[num] <= 5)
			{
				LocalTargetInfo localTargetInfo = ((orCreateValue.Targets[num] == null) ? new LocalTargetInfo(orCreateValue.Cells[num]) : new LocalTargetInfo(orCreateValue.Targets[num]));
				if (localTargetInfo.IsValid)
				{
					queue.Add((T)Activator.CreateInstance(typeof(T), localTargetInfo, orCreateValue.Actions[num]));
				}
			}
		}
	}

	private static bool GazerShotPrefix(Thing __instance, LocalTargetInfo target, ref string failReason, ref bool __result)
	{
		if (!Intercept)
		{
			return true;
		}
		OrderGazerShot(__instance, target);
		failReason = null;
		__result = true;
		return false;
	}

	private static void OrderGazerShot(Thing emplacement, LocalTargetInfo target)
	{
		if (Valid(emplacement) && (!target.HasThing || (Valid(target.Thing) && target.Thing.Map == emplacement.Map)))
		{
			Invoke(emplacement, "TryOrderShot", target, null);
		}
	}

	private static void ContainerTargeter(ThingComp comp)
	{
		TargetingParameters targetingParameters = TargetingParameters.ForPawns();
		targetingParameters.mapBoundsContractedBy = 1;
		targetingParameters.validator = (TargetInfo t) => t.Thing is Pawn { Spawned: not false } pawn && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving) && pawn.CanReach(comp.parent, PathEndMode.Touch, Danger.Deadly) && (bool)AccessTools.Method(TypeOf("Comp_MechanoidContainerControlled"), "CanBeSentInside").Invoke(null, new object[1] { pawn });
		Find.Targeter.BeginTargeting(targetingParameters, (LocalTargetInfo t) =>
		{
			if (t.Pawn != null)
			{
				EnterContainer(comp.parent, t.Pawn);
			}
		}, (LocalTargetInfo t) =>
		{
			Widgets.MouseAttachedLabel("APM.MechanoidContainer.Gizmo.ChooseMech.MouseLabel".Translate());
		});
	}

	private static void EnterContainer(Thing container, Pawn pawn)
	{
		ThingComp thingComp = (Valid(container) ? Comp(container, "Comp_MechanoidContainerControlled") : null);
		if (thingComp != null && Valid(pawn) && pawn.Map == container.Map && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving) && ((AcceptanceReport)Invoke(thingComp, "CanAcceptMech", pawn)).Accepted && pawn.CanReach(container, PathEndMode.Touch, Danger.Deadly))
		{
			pawn.MentalState?.RecoverFromState();
			Job job = JobMaker.MakeJob((JobDef)Get(Get(thingComp, "Props"), "enterJobDef"), container);
			pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
		}
	}

	private static void TinkerMenu(Pawn tinker, Thing target, JobDef jobDef, ref FloatMenuOption __result)
	{
		if (MP.IsInMultiplayer && __result != null)
		{
			__result.action = () =>
			{
				OrderTinkerRepair(tinker, target, jobDef);
			};
		}
	}

	private static void OrderTinkerRepair(Pawn tinker, Thing target, JobDef jobDef)
	{
		if (Valid(tinker) && Valid(target) && tinker.Map == target.Map && jobDef != null)
		{
			string name = ((target is Pawn) ? "CanRepairMechNow" : "CanRepairBuildingNow");
			if ((bool)AccessTools.Method(TypeOf("TinkerRepairUtility"), name).Invoke(null, new object[3] { tinker, target, true }))
			{
				Job job = JobMaker.MakeJob(jobDef, target);
				job.playerForced = true;
				tinker.jobs.TryTakeOrderedJob(job, JobTag.Misc);
			}
		}
	}

	private static void CallBoss(Thing parent, int points)
	{
		ThingComp thingComp = (Valid(parent) ? Comp(parent, "CompUseEffect_CallMulBossgroup") : null);
		if (thingComp != null && points >= 1000 && points <= 10000 && points % 1000 == 0)
		{
			IncidentDef incidentDef = (IncidentDef)Get(Get(thingComp, "Props"), "incidentDef");
			IncidentParms incidentParms = StorytellerUtility.DefaultParmsNow(incidentDef.category, parent.Map);
			incidentParms.forced = true;
			incidentParms.faction = Faction.OfMechanoids;
			incidentParms.points = points;
			Find.Storyteller.incidentQueue.Add(incidentDef, Find.TickManager.TicksGame + Rand.Range(2500, 7500), incidentParms);
		}
	}

	private static void OverseerGizmos(Pawn mech, ref IEnumerable<Gizmo> __0)
	{
		if (!MP.IsInMultiplayer)
		{
			return;
		}
		List<Gizmo> list = __0.ToList();
		foreach (Command_Action item in list.OfType<Command_Action>())
		{
			if (!(item.defaultLabel == "DEV: Instant reconnect"))
			{
				continue;
			}
			item.action = () =>
			{
				IEnumerable enumerable = (IEnumerable)AccessTools.Method(TypeOf("Utils"), "MechanitorsInCommandCaskets").Invoke(null, null);
				List<FloatMenuOption> list2 = new List<FloatMenuOption>();
				foreach (Pawn item2 in enumerable)
				{
					Pawn selected = item2;
					string text = item2.LabelShortCap;
					if (mech.GetStatValue(StatDefOf.BandwidthCost) > (float)(item2.mechanitor.TotalBandwidth - item2.mechanitor.UsedBandwidth))
					{
						text += "APM.CommandCasket.Mech.Gizmo.Reconnect.Floatmenu".Translate();
					}
					list2.Add(new FloatMenuOption(text, () =>
					{
						InstantReconnect(mech, selected);
					}));
				}
				Find.WindowStack.Add(new FloatMenu(list2));
			};
		}
		__0 = list;
	}

	private static void InstantReconnect(Pawn mech, Pawn mechanitor)
	{
		if (Valid(mech) && mechanitor != null && !mechanitor.Dead && mechanitor.mechanitor != null)
		{
			mech.GetOverseer()?.relations.RemoveDirectRelation(PawnRelationDefOf.Overseer, mech);
			mech.SetFaction(Faction.OfPlayer);
			mechanitor.relations.AddDirectRelation(PawnRelationDefOf.Overseer, mech);
		}
	}
}
