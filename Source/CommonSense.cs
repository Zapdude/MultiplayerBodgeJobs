using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("avilmask.CommonSense")]
internal class CommonSense
{
	private delegate ThingComp GetChecker(Thing thing, bool initShouldUnload, bool initWasInInventory);

	private static ISyncField shouldUnloadSyncField;

	private static GetChecker getCompUnlockerCheckerMethod;

	private static Type rpgStyleInventoryTabType;

	public CommonSense(ModContentPack mod)
	{
		Type type = AccessTools.TypeByName("CommonSense.CompUnloadChecker");
		MP.RegisterSyncWorker<ThingComp>(SyncComp, type);
		shouldUnloadSyncField = MP.RegisterSyncField(AccessTools.Field(type, "ShouldUnload"));
		getCompUnlockerCheckerMethod = AccessTools.MethodDelegate<GetChecker>(AccessTools.Method(type, "GetChecker"));
		PatchingUtilities.PatchUnityRand("CommonSense.JobGiver_Wander_TryGiveJob_CommonSensePatch:Postfix", patchPushPop: false);
		MpCompat.RegisterLambdaMethod("CommonSense.DoCleanComp", "CompGetGizmosExtra", 1);
		rpgStyleInventoryTabType = AccessTools.TypeByName("Sandy_Detailed_RPG_Inventory.Sandy_Detailed_RPG_GearTab");
		if (rpgStyleInventoryTabType != null)
		{
			type = AccessTools.TypeByName("CommonSense.RPGStyleInventory_PopupMenu_CommonSensePatch");
			MethodInfo lambda = MpMethodUtil.GetLambda(type, "Postfix");
			MP.RegisterSyncDelegate(type, lambda.DeclaringType.Name, lambda.Name, new string[3] { "pawn", "thing", "c" }).SetContext(SyncContext.MapSelected);
			MpCompat.harmony.Patch(lambda, new HarmonyMethod(typeof(CommonSense), "RpgStyleCompatPrefix"));
			MpCompat.RegisterLambdaDelegate("CommonSense.RPGStyleInventory_PopupMenu_CommonSensePatch", "Postfix", new string[1] { "c" }, 1);
		}
		LongEventHandler.ExecuteWhenFinished(LatePatch);
	}

	private static void LatePatch()
	{
		MpCompat.harmony.Patch(AccessTools.Method("CommonSense.Utility:DrawThingRow"), new HarmonyMethod(typeof(CommonSense), "CommonSensePatchPrefix"), new HarmonyMethod(typeof(CommonSense), "CommonSensePatchPostfix"));
	}

	private static void CommonSensePatchPrefix(Thing thing, ref bool __state)
	{
		if (MP.IsInMultiplayer)
		{
			ThingComp thingComp = getCompUnlockerCheckerMethod(thing, initShouldUnload: false, initWasInInventory: false);
			if (thingComp != null)
			{
				__state = true;
				MP.WatchBegin();
				shouldUnloadSyncField.Watch(thingComp);
			}
		}
	}

	private static void CommonSensePatchPostfix(bool __state)
	{
		if (__state)
		{
			MP.WatchEnd();
		}
	}

	private static void RpgStyleCompatPrefix(ref object _____instance)
	{
		if (MP.IsInMultiplayer && _____instance == null)
		{
			_____instance = Activator.CreateInstance(rpgStyleInventoryTabType);
		}
	}

	private static void SyncComp(SyncWorker sync, ref ThingComp thing)
	{
		if (sync.isWriting)
		{
			sync.Write((Thing)thing.parent);
		}
		else
		{
			thing = getCompUnlockerCheckerMethod(sync.Read<Thing>(), initShouldUnload: false, initWasInInventory: false);
		}
	}
}
