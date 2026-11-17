using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Fluffy.AnimalTab")]
internal class AnimalTab
{
	private static ConstructorInfo commandCtor;

	private static AccessTools.FieldRef<object, ThingComp> compField;

	private static Type compHandlerSettingsType;

	private static ISyncField modeField;

	private static ISyncField levelField;

	public AnimalTab(ModContentPack mod)
	{
		Type type = AccessTools.TypeByName("AnimalTab.Command_HandlerSettings");
		commandCtor = AccessTools.Constructor(type, new Type[1] { AccessTools.TypeByName("AnimalTab.CompHandlerSettings") });
		compField = AccessTools.FieldRefAccess<ThingComp>(type, "comp");
		MP.RegisterSyncMethod(type, "MassSetMode").SetContext(SyncContext.MapSelected);
		MP.RegisterSyncMethod(type, "MassSetHandler").SetContext(SyncContext.MapSelected);
		MP.RegisterSyncMethod(type, "MassSetLevel").SetContext(SyncContext.MapSelected);
		MP.RegisterSyncWorker<Command_Action>(SyncHandlerSettingsCommand, type);
		type = AccessTools.TypeByName("AnimalTab.PawnColumnWorker_Handler");
		MpCompat.RegisterLambdaDelegate(type, "DoHandlerFloatMenu", 0, 1, 2);
		MpCompat.RegisterLambdaDelegate(type, "DoMassHandlerFloatMenu", 0, 1, 4);
		MpCompat.harmony.Patch(AccessTools.Method(type, "DoHeader"), new HarmonyMethod(typeof(AnimalTab), "PreDoHeader"), new HarmonyMethod(typeof(AnimalTab), "StopWatch"));
		MpCompat.harmony.Patch(AccessTools.Method(type, "DoCell"), new HarmonyMethod(typeof(AnimalTab), "PreDoCell"), new HarmonyMethod(typeof(AnimalTab), "StopWatch"));
		type = (compHandlerSettingsType = AccessTools.TypeByName("AnimalTab.CompHandlerSettings"));
		modeField = MP.RegisterSyncField(type, "_mode");
		levelField = MP.RegisterSyncField(type, "_level");
	}

	private static void PreDoHeader(PawnTable table)
	{
		if (!MP.IsInMultiplayer)
		{
			return;
		}
		MP.WatchBegin();
		foreach (Pawn item in table.PawnsListForReading)
		{
			ThingComp thingComp = ((ThingWithComps)item).comps.FirstOrDefault((ThingComp c) => c.GetType() == compHandlerSettingsType);
			if (thingComp != null)
			{
				modeField.Watch(thingComp);
				levelField.Watch(thingComp);
			}
		}
	}

	private static void PreDoCell(Pawn target)
	{
		if (MP.IsInMultiplayer)
		{
			MP.WatchBegin();
			ThingComp thingComp = ((ThingWithComps)target).comps.FirstOrDefault((ThingComp c) => c.GetType() == compHandlerSettingsType);
			if (thingComp != null)
			{
				modeField.Watch(thingComp);
				levelField.Watch(thingComp);
			}
		}
	}

	private static void StopWatch()
	{
		if (MP.IsInMultiplayer)
		{
			MP.WatchEnd();
		}
	}

	private static void SyncHandlerSettingsCommand(SyncWorker sync, ref Command_Action obj)
	{
		if (sync.isWriting)
		{
			sync.Write(compField(obj));
			return;
		}
		obj = (Command_Action)commandCtor.Invoke(new object[1] { sync.Read<ThingComp>() });
	}
}
