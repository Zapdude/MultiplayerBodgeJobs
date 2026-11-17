using System;
using System.Collections;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("vanillaexpanded.skills")]
public class VanillaSkillsExpanded
{
	private static IDictionary expertiseTrackers;

	public VanillaSkillsExpanded(ModContentPack mod)
	{
		expertiseTrackers = AccessTools.StaticFieldRefAccess<IDictionary>("VSE.ExpertiseTrackers:trackers");
		Type type = AccessTools.TypeByName("VSE.ExpertiseTracker");
		MP.RegisterSyncMethod(type, "AddExpertise");
		MP.RegisterSyncMethod(type, "ClearExpertise").SetDebugOnly();
		MP.RegisterSyncWorker<object>(SyncExpertiseTracker, type);
	}

	public static void SyncExpertiseTracker(SyncWorker sync, ref object expertiseTracker)
	{
		if (sync.isWriting)
		{
			bool flag = false;
			foreach (DictionaryEntry expertiseTracker2 in expertiseTrackers)
			{
				if (expertiseTracker2.Value == expertiseTracker)
				{
					sync.Write(((Pawn_SkillTracker)expertiseTracker2.Key).pawn);
					flag = true;
					break;
				}
			}
			if (!flag)
			{
				sync.Write<Pawn>(null);
			}
		}
		else
		{
			Pawn pawn = sync.Read<Pawn>();
			expertiseTracker = ((pawn?.skills == null) ? null : expertiseTrackers[pawn.skills]);
		}
	}
}
