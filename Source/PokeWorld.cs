using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Gargamiel.PokeWorld")]
[MpCompatFor("eth0net.PokeWorld")]
internal class PokeWorld
{
	private static AccessTools.FieldRef<object, object> pokemonFormTrackerField;

	private static AccessTools.FieldRef<object, object> pokemonLevelTrackerField;

	private static AccessTools.FieldRef<object, object> pokemonMoveTrackerField;

	private static AccessTools.FieldRef<object, ThingComp> formTrackerCompField;

	private static AccessTools.FieldRef<object, ThingComp> levelTrackerCompField;

	private static AccessTools.FieldRef<object, ThingComp> moveTrackerCompField;

	private static AccessTools.FieldRef<object, ThingComp> shinyTrackerCompField;

	private static AccessTools.FieldRef<object, IList> propsFormsListField;

	private static AccessTools.FieldRef<object, object> innerClassFormField;

	private static AccessTools.FieldRef<object, object> innerClassParentField;

	private static ConstructorInfo storageTabConstructor;

	private static FastInvokeHandler interfaceDropMethod;

	private static Type storageSystemType;

	public PokeWorld(ModContentPack mod)
	{
		Type type = AccessTools.TypeByName("PokeWorld.CompPokemon");
		pokemonFormTrackerField = AccessTools.FieldRefAccess<object>(type, "formTracker");
		pokemonLevelTrackerField = AccessTools.FieldRefAccess<object>(type, "levelTracker");
		pokemonMoveTrackerField = AccessTools.FieldRefAccess<object>(type, "moveTracker");
		type = AccessTools.TypeByName("PokeWorld.CompProperties_Pokemon");
		propsFormsListField = AccessTools.FieldRefAccess<IList>(type, "forms");
		type = AccessTools.TypeByName("PokeWorld.PutInBallUtility");
		MP.RegisterSyncMethod(type, "UpdatePutInBallDesignation");
		type = AccessTools.TypeByName("PokeWorld.PutInPortableComputerUtility");
		MP.RegisterSyncMethod(type, "UpdatePutInPortableComputerDesignation");
		type = AccessTools.TypeByName("PokeWorld.FormTracker");
		formTrackerCompField = AccessTools.FieldRefAccess<ThingComp>(type, "comp");
		MethodInfo lambda = MpMethodUtil.GetLambda(type, "ProcessInput");
		innerClassFormField = AccessTools.FieldRefAccess<object>(lambda.DeclaringType, "form");
		innerClassParentField = AccessTools.FieldRefAccess<object>(lambda.DeclaringType, "<>4__this");
		MP.RegisterSyncMethod(lambda);
		MP.RegisterSyncWorker<object>(SyncFormTrackerInnerClass, lambda.DeclaringType, isImplicit: false, shouldConstruct: true);
		type = AccessTools.TypeByName("PokeWorld.LevelTracker");
		MpCompat.RegisterLambdaMethod(type, "GetGizmos", 0, 1);
		levelTrackerCompField = AccessTools.FieldRefAccess<ThingComp>(type, "comp");
		MP.RegisterSyncWorker<object>(SyncLevelTracker, type);
		type = AccessTools.TypeByName("PokeWorld.ITab_ContentsPokeball");
		MP.RegisterSyncMethod(type, "OnDropThing").SetContext(SyncContext.MapSelected);
		MP.RegisterSyncWorker<object>(NoSync, type, isImplicit: false, shouldConstruct: true);
		type = AccessTools.TypeByName("PokeWorld.ITab_ContentsStorageSystem");
		MethodInfo methodInfo = AccessTools.Method(type, "InterfaceDrop");
		storageTabConstructor = AccessTools.DeclaredConstructor(type);
		interfaceDropMethod = HarmonyLib.MethodInvoker.GetHandler(methodInfo);
		MpCompat.harmony.Patch(methodInfo, new HarmonyMethod(typeof(PokeWorld), "PreInterfaceDrop"));
		MP.RegisterSyncMethod(typeof(PokeWorld), "SyncedInterfaceDrop").SetContext(SyncContext.MapSelected);
		storageSystemType = AccessTools.TypeByName("PokeWorld.StorageSystem");
		type = AccessTools.TypeByName("PokeWorld.MoveTracker");
		MP.RegisterSyncMethod(type, "SetWanted");
		moveTrackerCompField = AccessTools.FieldRefAccess<ThingComp>(type, "comp");
		MP.RegisterSyncWorker<object>(SyncMoveTracker, type);
		type = AccessTools.TypeByName("PokeWorld.ShinyTracker");
		shinyTrackerCompField = AccessTools.FieldRefAccess<ThingComp>(type, "comp");
		MpCompat.harmony.Patch(AccessTools.Method(type, "TryMakeShinyMote"), new HarmonyMethod(typeof(PokeWorld), "PreTryMakeShinyMote"), new HarmonyMethod(typeof(PokeWorld), "PostTryMakeShinyMote"));
	}

	private static bool PreInterfaceDrop(Thing t)
	{
		if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
		{
			return true;
		}
		SyncedInterfaceDrop(t.thingIDNumber);
		return false;
	}

	private static void PreTryMakeShinyMote(object __instance)
	{
		if (MP.IsInMultiplayer)
		{
			Rand.PushState(Gen.HashCombineInt(shinyTrackerCompField(__instance).parent.GetHashCode(), Find.TickManager.TicksGame));
		}
	}

	private static void PostTryMakeShinyMote()
	{
		if (MP.IsInMultiplayer)
		{
			Rand.PopState();
		}
	}

	private static void SyncedInterfaceDrop(int id)
	{
		Thing thing = ((IThingHolder)Find.World.GetComponent(storageSystemType)).GetDirectlyHeldThings().FirstOrDefault((Thing x) => x.thingIDNumber == id);
		interfaceDropMethod(storageTabConstructor.Invoke(Array.Empty<object>()), thing);
	}

	private static void NoSync(SyncWorker sync, ref object obj)
	{
	}

	private static void SyncLevelTracker(SyncWorker sync, ref object tracker)
	{
		if (sync.isWriting)
		{
			sync.Write(levelTrackerCompField(tracker));
		}
		else
		{
			tracker = pokemonLevelTrackerField(sync.Read<ThingComp>());
		}
	}

	private static void SyncMoveTracker(SyncWorker sync, ref object tracker)
	{
		if (sync.isWriting)
		{
			sync.Write(moveTrackerCompField(tracker));
		}
		else
		{
			tracker = pokemonMoveTrackerField(sync.Read<ThingComp>());
		}
	}

	private static void SyncFormTrackerInnerClass(SyncWorker sync, ref object inner)
	{
		if (sync.isWriting)
		{
			object instance = innerClassParentField(inner);
			ThingComp thingComp = formTrackerCompField(instance);
			sync.Write(thingComp);
			int obj = propsFormsListField(thingComp.props).IndexOf(innerClassFormField(inner));
			sync.Write(obj);
			return;
		}
		ThingComp thingComp2 = sync.Read<ThingComp>();
		object obj2 = pokemonFormTrackerField(thingComp2);
		innerClassParentField(inner) = obj2;
		int num = sync.Read<int>();
		if (num >= 0)
		{
			IList list = propsFormsListField(thingComp2.props);
			innerClassFormField(inner) = list[num];
		}
	}
}
