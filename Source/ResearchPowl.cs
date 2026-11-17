using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Owlchemist.ResearchPowl")]
public class ResearchPowl
{
	private static FastInvokeHandler queueSanityCheck;

	private static FastInvokeHandler researchNodeGetAvailable;

	private static AccessTools.FieldRef<object, ResearchProjectDef> researchNodeProjectField;

	private static FastInvokeHandler treeGetResearchNodes;

	private static Dictionary<ResearchProjectDef, object> nodeCache;

	public ResearchPowl(ModContentPack mod)
	{
		Type type = AccessTools.TypeByName("ResearchPowl.Queue");
		string[] array = new string[11]
		{
			"UnsafeConcat", "UnsafeInsert", "Remove", "Replace", "ReplaceMore", "Finish", "DoMove", "UnsafeAppend", "Prepend", "Insert",
			"UpdateCurrentResearch"
		};
		queueSanityCheck = MethodInvoker.GetHandler(AccessTools.DeclaredMethod(type, "SanityCheck"));
		MP.RegisterSyncMethod(typeof(ResearchPowl), "SyncedSanityCheck");
		string[] array2 = array;
		foreach (string methodOrPropertyName in array2)
		{
			MP.RegisterSyncMethod(type, methodOrPropertyName);
		}
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, "SanityCheck"), new HarmonyMethod(typeof(ResearchPowl), "PreSanityCheck"));
		type = AccessTools.TypeByName("ResearchPowl.ResearchNode");
		researchNodeProjectField = AccessTools.FieldRefAccess<ResearchProjectDef>(type, "Research");
		researchNodeGetAvailable = MethodInvoker.GetHandler(AccessTools.DeclaredMethod(type, "GetAvailable"));
		MP.RegisterSyncWorker<object>(SyncResearchNode, type);
		type = AccessTools.TypeByName("ResearchPowl.Tree");
		treeGetResearchNodes = MethodInvoker.GetHandler(AccessTools.Method(type, "ResearchNodes"));
		MpCompat.harmony.Patch(AccessTools.Method(type, "ResearchNodes"), new HarmonyMethod(typeof(ResearchPowl), "PreInitializeNodesStructures"));
	}

	private static void PreInitializeNodesStructures()
	{
		nodeCache = null;
	}

	private static void ForceInitializeCache()
	{
		nodeCache = ((IList)treeGetResearchNodes(null)).Cast<object>().ToDictionary((object node) => researchNodeProjectField(node));
	}

	private static void SyncResearchNode(SyncWorker sync, ref object node)
	{
		if (sync.isWriting)
		{
			sync.Write(researchNodeProjectField(node));
			return;
		}
		ResearchProjectDef key = sync.Read<ResearchProjectDef>();
		if (nodeCache == null)
		{
			ForceInitializeCache();
		}
		node = nodeCache[key];
	}

	private static bool PreSanityCheck(WorldComponent __instance, IList ____queue)
	{
		if (!MP.InInterface)
		{
			return true;
		}
		foreach (object item in ____queue)
		{
			if (researchNodeProjectField(item).IsFinished || !(bool)researchNodeGetAvailable(item))
			{
				SyncedSanityCheck(__instance);
				return false;
			}
		}
		return false;
	}

	private static void SyncedSanityCheck(WorldComponent queue)
	{
		queueSanityCheck(queue);
	}
}
