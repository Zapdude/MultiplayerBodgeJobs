using System;
using System.Collections;
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

[MpCompatFor("Aoba.DeadManSwitch.Taktikmagier")]
public sealed class DeadMansSwitchTaktikmagier
{
	private sealed class Notification
	{
		internal float Last = -99999f;
	}

	private static Type trackerType;

	private static FieldInfo downed;

	private static FieldInfo corpses;

	private static FieldInfo detected;

	private static FieldInfo notified;

	private static MethodInfo rescue;

	private static MethodInfo recover;

	private static MethodInfo asyncTime;

	private static FieldInfo tickingMap;

	private static Func<bool> initializingCaches;

	private static readonly ConditionalWeakTable<Map, Notification> notifications = new ConditionalWeakTable<Map, Notification>();

	[ThreadStatic]
	private static DmsTaktikRecall currentRecall;

	internal static bool Installed => trackerType != null;

	internal static object Tracker
	{
		get
		{
			if (Installed && Current.Game != null)
			{
				return Current.Game.components.FirstOrDefault((GameComponent c) => c?.GetType() == trackerType);
			}
			return null;
		}
	}

	internal static IList Downed
	{
		get
		{
			if (Tracker != null)
			{
				return (IList)downed.GetValue(Tracker);
			}
			return null;
		}
	}

	internal static IList Corpses
	{
		get
		{
			if (Tracker != null)
			{
				return (IList)corpses.GetValue(Tracker);
			}
			return null;
		}
	}

	private static Type T(string name)
	{
		return AccessTools.TypeByName("Militarmagier." + name) ?? throw new TypeLoadException("Militarmagier." + name);
	}

	private static MethodInfo M(string type, string method)
	{
		return AccessTools.DeclaredMethod(T(type), method) ?? throw new MissingMethodException(type, method);
	}

	public DeadMansSwitchTaktikmagier(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(Install);
	}

	private static void Install()
	{
		trackerType = T("GameComponent_MagierSigil");
		downed = AccessTools.DeclaredField(trackerType, "downed");
		corpses = AccessTools.DeclaredField(trackerType, "corpses");
		rescue = M("GameComponent_MagierSigil", "RescueDowned");
		recover = M("GameComponent_MagierSigil", "RecoverCorpse");
		asyncTime = AccessTools.DeclaredMethod("Multiplayer.Client.Extensions:AsyncTime");
		tickingMap = AccessTools.Field(AccessTools.TypeByName("Multiplayer.Client.AsyncTimeComp"), "tickingMap");
		initializingCaches = (Func<bool>)AccessTools.PropertyGetter(AccessTools.TypeByName("Multiplayer.Client.LoadPatch"), "IsInitializingCaches").CreateDelegate(typeof(Func<bool>));
		MP.RegisterSyncMethod(M("CompMagierLink", "Dissolve")).SetVersion(13).CancelIfAnyArgNull();
		MpCompat.harmony.Patch(M("CompMagierLink", "Dissolve"), new HarmonyMethod(typeof(DeadMansSwitchTaktikmagier), "OwnedLink"));
		ISyncMethod[] array = MpCompat.RegisterLambdaMethod(T("HediffComp_MagierCamouflage"), "CompGetGizmos", 1);
		for (int i = 0; i < array.Length; i++)
		{
			array[i].SetVersion(13);
		}
		MpCompat.harmony.Patch(MpMethodUtil.GetLambda(T("HediffComp_MagierCamouflage"), "CompGetGizmos", MethodType.Normal, null, 1), new HarmonyMethod(typeof(DeadMansSwitchTaktikmagier), "OwnedCamouflage"));
		detected = AccessTools.DeclaredField(T("HediffComp_MagierCamouflage"), "lastDetectedTick");
		PatchingUtilities.RegisterTimestampFields(T("HediffComp_MagierCamouflage"), (object value, int offset) =>
		{
			detected.SetValue(value, (int)detected.GetValue(value) + offset);
		});
		notified = AccessTools.DeclaredField(T("HediffComp_MagierCamouflage"), "lastNotifiedRealTime");
		MpCompat.harmony.Patch(M("HediffComp_MagierCamouflage", "AnnounceRevealed"), null, null, new HarmonyMethod(typeof(DeadMansSwitchTaktikmagier), "NotificationIl"));
		MpCompat.harmony.Patch(M("GameComponent_MagierSigil", "GameComponentTick"), new HarmonyMethod(typeof(DeadMansSwitchTaktikmagier), "WorldTick"));
		MethodInfo[] array2 = new MethodInfo[2] { rescue, recover };
		foreach (MethodInfo original in array2)
		{
			MpCompat.harmony.Patch(original, null, null, new HarmonyMethod(typeof(DeadMansSwitchTaktikmagier), "RecallFactionIl"));
		}
		MpCompat.harmony.Patch(M("MagierSigilUtility", "TryFindSpotBesideBed"), new HarmonyMethod(typeof(DeadMansSwitchTaktikmagier), "RecallSpot"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(Map), "MapPostTick"), null, new HarmonyMethod(typeof(DeadMansSwitchTaktikmagier), "MapTick"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(Map), "ExposeData"), null, new HarmonyMethod(typeof(DeadMansSwitchTaktikmagier), "MapExpose"));
	}

	private static bool WorldTick()
	{
		return !MP.IsInMultiplayer;
	}

	private static bool OwnedLink(ThingComp __instance)
	{
		if (MP.IsExecutingSyncCommand)
		{
			return __instance?.parent.Faction == Faction.OfPlayer;
		}
		return true;
	}

	private static bool OwnedCamouflage(HediffComp __instance)
	{
		if (MP.IsExecutingSyncCommand)
		{
			return __instance?.Pawn.Faction == Faction.OfPlayer;
		}
		return true;
	}

	private static bool Paused(Map map)
	{
		if (map != null && Find.Maps.Contains(map))
		{
			return (bool)AccessTools.Property(asyncTime.ReturnType, "Paused").GetValue(asyncTime.Invoke(null, new object[1] { map }));
		}
		return true;
	}

	private static void MapTick(Map __instance)
	{
		if (!MP.IsInMultiplayer || !Installed || initializingCaches() || Tracker == null || tickingMap.GetValue(null) != __instance)
		{
			return;
		}
		DmsTaktikRecallComponent component = Current.Game.GetComponent<DmsTaktikRecallComponent>();
		Thing[] array = Downed.Cast<Pawn>().Cast<Thing>().Concat(Corpses.Cast<Corpse>())
			.ToArray();
		foreach (Thing thing in array)
		{
			if (thing != null && !component.Pending.Any((DmsTaktikRecall p) => p.Thing == thing))
			{
				component.Pending.Add(new DmsTaktikRecall
				{
					Thing = thing
				});
			}
		}
		DmsTaktikRecall[] array2 = (from p in component.Pending
			where p.Thing == null || p.Thing.Destroyed || !p.Thing.Spawned || p.Thing.Map == __instance
			orderby p.Thing?.thingIDNumber ?? (-1)
			select p).ToArray();
		foreach (DmsTaktikRecall dmsTaktikRecall in array2)
		{
			Thing thing2 = dmsTaktikRecall.Thing;
			if (thing2 == null || thing2.Destroyed || !thing2.Spawned || (thing2 is Pawn pawn && (pawn.Dead || !pawn.Downed)))
			{
				Forget(component, dmsTaktikRecall);
				continue;
			}
			Faction faction = ((thing2 as Pawn) ?? (thing2 as Corpse)?.InnerPawn)?.Faction;
			if (faction != null && faction.IsPlayer)
			{
				if (dmsTaktikRecall.Selected && dmsTaktikRecall.Target != null && (!Find.Maps.Contains(dmsTaktikRecall.Target) || dmsTaktikRecall.Target.ParentFaction != faction || !dmsTaktikRecall.Cell.InBounds(dmsTaktikRecall.Target) || !dmsTaktikRecall.Cell.Standable(dmsTaktikRecall.Target)))
				{
					dmsTaktikRecall.Selected = false;
				}
				if (!dmsTaktikRecall.Selected)
				{
					dmsTaktikRecall.Selected = true;
					SelectBed(faction, out dmsTaktikRecall.Target, out dmsTaktikRecall.Cell);
				}
				if (dmsTaktikRecall.Target != null && dmsTaktikRecall.Target != __instance && Paused(dmsTaktikRecall.Target))
				{
					continue;
				}
			}
			DmsTaktikRecall dmsTaktikRecall2 = currentRecall;
			currentRecall = dmsTaktikRecall;
			try
			{
				((thing2 is Pawn) ? rescue : recover).Invoke(null, new object[1] { thing2 });
			}
			finally
			{
				currentRecall = dmsTaktikRecall2;
			}
			Forget(component, dmsTaktikRecall);
		}
	}

	private static void Forget(DmsTaktikRecallComponent state, DmsTaktikRecall pending)
	{
		if (pending.Thing is Pawn value)
		{
			Downed.Remove(value);
		}
		if (pending.Thing is Corpse value2)
		{
			Corpses.Remove(value2);
		}
		state.Pending.Remove(pending);
	}

	private static void SelectBed(Faction actor, out Map map, out IntVec3 cell)
	{
		foreach (Building_Bed bed in (from b in (from m in Find.Maps
				where m.ParentFaction == actor && m.Parent.def.canBePlayerHome
				orderby m.uniqueID
				select m).SelectMany((Map m) => m.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial).OfType<Building_Bed>())
			where b.Faction == actor && b.def.building.bed_humanlike && !b.ForPrisoners
			orderby b.thingIDNumber
			select b).ToList().InRandomOrder())
		{
			if (CellFinder.TryFindRandomCellNear(bed.Position, bed.Map, 2, (IntVec3 c) => c.Standable(bed.Map) && !c.Fogged(bed.Map), out cell))
			{
				map = bed.Map;
				return;
			}
		}
		map = null;
		cell = IntVec3.Invalid;
	}

	private static bool RecallSpot(ref Map map, ref IntVec3 cell, ref bool __result)
	{
		if (!MP.IsInMultiplayer || currentRecall == null)
		{
			return true;
		}
		map = currentRecall.Target;
		cell = currentRecall.Cell;
		__result = map != null;
		return false;
	}

	private static Faction RecallFaction()
	{
		Thing thing = currentRecall?.Thing;
		Faction faction = ((thing as Pawn) ?? (thing as Corpse)?.InnerPawn)?.Faction;
		if (!MP.IsInMultiplayer || faction == null || !faction.IsPlayer)
		{
			return Faction.OfPlayer;
		}
		return faction;
	}

	private static IEnumerable<CodeInstruction> RecallFactionIl(IEnumerable<CodeInstruction> instructions)
	{
		int count = 0;
		MethodInfo original = AccessTools.PropertyGetter(typeof(Faction), "OfPlayer");
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(original))
			{
				count++;
				yield return new CodeInstruction(instruction)
				{
					opcode = OpCodes.Call,
					operand = AccessTools.DeclaredMethod(typeof(DeadMansSwitchTaktikmagier), "RecallFaction")
				};
			}
			else
			{
				yield return instruction;
			}
		}
		if (count != 1)
		{
			throw new InvalidOperationException($"Taktikmagier recall faction IL changed: {count}");
		}
	}

	private static float NotifyClock()
	{
		if (!MP.IsInMultiplayer)
		{
			return RealTime.LastRealTime;
		}
		return (float)GenTicks.TicksGame / 60f;
	}

	private static float LastNotice(HediffComp comp)
	{
		if (!MP.IsInMultiplayer || comp.Pawn.MapHeld == null)
		{
			return (float)notified.GetValue(null);
		}
		return notifications.GetValue(comp.Pawn.MapHeld, (Map _) => new Notification()).Last;
	}

	private static void SetNotice(float value, HediffComp comp)
	{
		if (MP.IsInMultiplayer && comp.Pawn.MapHeld != null)
		{
			notifications.GetValue(comp.Pawn.MapHeld, (Map _) => new Notification()).Last = value;
		}
		else
		{
			notified.SetValue(null, value);
		}
	}

	private static IEnumerable<CodeInstruction> NotificationIl(IEnumerable<CodeInstruction> instructions)
	{
		int reads = 0;
		int writes = 0;
		int clocks = 0;
		MethodInfo clock = AccessTools.PropertyGetter(typeof(RealTime), "LastRealTime");
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.LoadsField(notified))
			{
				reads++;
				yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
				yield return CodeInstruction.Call(typeof(DeadMansSwitchTaktikmagier), "LastNotice");
			}
			else if (instruction.StoresField(notified))
			{
				writes++;
				yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
				yield return CodeInstruction.Call(typeof(DeadMansSwitchTaktikmagier), "SetNotice");
			}
			else if (instruction.Calls(clock))
			{
				clocks++;
				yield return new CodeInstruction(instruction)
				{
					opcode = OpCodes.Call,
					operand = AccessTools.DeclaredMethod(typeof(DeadMansSwitchTaktikmagier), "NotifyClock")
				};
			}
			else
			{
				yield return instruction;
			}
		}
		if (reads != 1 || writes != 1 || clocks != 2)
		{
			throw new InvalidOperationException($"Taktikmagier notice IL changed: {reads}/{writes}/{clocks}");
		}
	}

	private static void MapExpose(Map __instance)
	{
		if (Installed)
		{
			Scribe_Values.Look(ref notifications.GetValue(__instance, (Map _) => new Notification()).Last, "mpTaktikLastNotice", -99999f);
		}
	}
}
