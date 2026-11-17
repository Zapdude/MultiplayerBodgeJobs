using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("onyxae.dragonsdescent")]
public class DragonsDescent
{
	private static ConstructorInfo ritualEffectCommandCtor;

	private static AccessTools.FieldRef<object, Thing> ritualEffectCommandSourceField;

	private static AccessTools.FieldRef<object, object> ritualEffectCommandRitualField;

	private static AccessTools.FieldRef<object, object> ritualEffectCommandRitualRequestField;

	private static FastInvokeHandler ritualEffectCommandCreateSetupMethod;

	private static Type mapComponentTrackerType;

	private static AccessTools.FieldRef<object, object> mapComponentTrackerRitualsField;

	private static AccessTools.FieldRef<object, Def> ritualDefField;

	private static FastInvokeHandler ritualActivatorInitializeMethod;

	private static AccessTools.FieldRef<object, Map> ritualTrackerMapField;

	public DragonsDescent(ModContentPack mod)
	{
		MpCompat.RegisterLambdaMethod("DD.CompEggIncubator", "CompGetGizmosExtra", 1, 2, 3, 4).Skip(1).SetDebugOnly();
		MpCompat.RegisterLambdaDelegate("DD.CompProperties_EggIncubator", "CreateGizmo", default(int));
		mapComponentTrackerType = AccessTools.TypeByName("DD.MapComponent_Tracker");
		mapComponentTrackerRitualsField = AccessTools.FieldRefAccess<object>(mapComponentTrackerType, "rituals");
		ritualDefField = AccessTools.FieldRefAccess<Def>("DD.Ritual:def");
		ritualActivatorInitializeMethod = HarmonyLib.MethodInvoker.GetHandler(AccessTools.Method("DD.RitualActivator:Initialize"));
		Type type = AccessTools.TypeByName("DD.RitualTracker");
		ritualTrackerMapField = AccessTools.FieldRefAccess<Map>(type, "map");
		MP.RegisterSyncWorker<object>(SyncRitualTracker, type);
		LongEventHandler.ExecuteWhenFinished(LatePatch);
	}

	private static void LatePatch()
	{
		Type type = AccessTools.TypeByName("DD.Command_RitualEffect");
		Type type2 = AccessTools.TypeByName("DD.RitualTracker");
		Type type3 = AccessTools.TypeByName("DD.RitualDef");
		ritualEffectCommandCtor = AccessTools.DeclaredConstructor(type, new Type[3]
		{
			typeof(Thing),
			type2,
			type3
		});
		ritualEffectCommandSourceField = AccessTools.FieldRefAccess<Thing>(type, "source");
		ritualEffectCommandRitualField = AccessTools.FieldRefAccess<object>(type, "ritual");
		ritualEffectCommandRitualRequestField = AccessTools.FieldRefAccess<object>(type, "ritualRequest");
		ritualEffectCommandCreateSetupMethod = HarmonyLib.MethodInvoker.GetHandler(AccessTools.PropertyGetter(type, "CreateSetup"));
		MP.RegisterSyncMethod(type, "ActivateOnNoTarget").SetPreInvoke(PreActivateRitual);
		MP.RegisterSyncMethod(type, "ActivateOnLocalTarget").SetPreInvoke(PreActivateRitual);
		MP.RegisterSyncMethod(type, "ActivateOnGlobalTarget").SetPreInvoke(PreActivateRitual);
		MP.RegisterSyncWorker<Command>(SyncRitualEffectCommand, type);
	}

	private static void SyncRitualTracker(SyncWorker sync, ref object tracker)
	{
		if (sync.isWriting)
		{
			sync.Write(ritualTrackerMapField(tracker));
			return;
		}
		MapComponent component = sync.Read<Map>().GetComponent(mapComponentTrackerType);
		tracker = mapComponentTrackerRitualsField(component);
	}

	private static void PreActivateRitual(object instance, object[] _)
	{
		object obj = ritualEffectCommandCreateSetupMethod(instance);
		Thing thing = ritualEffectCommandSourceField(instance);
		ritualActivatorInitializeMethod(obj, thing);
		ritualEffectCommandRitualRequestField(instance) = obj;
	}

	private static void SyncRitualEffectCommand(SyncWorker sync, ref Command command)
	{
		if (sync.isWriting)
		{
			sync.Write(ritualEffectCommandSourceField(command));
			sync.Write(Find.CurrentMap.GetComponent(mapComponentTrackerType));
			object instance = ritualEffectCommandRitualField(command);
			sync.Write(ritualDefField(instance));
			return;
		}
		Thing thing = sync.Read<Thing>();
		MapComponent instance2 = sync.Read<MapComponent>();
		object obj = mapComponentTrackerRitualsField(instance2);
		Def def = sync.Read<Def>();
		command = (Command)ritualEffectCommandCtor.Invoke(new object[3] { thing, obj, def });
	}
}
