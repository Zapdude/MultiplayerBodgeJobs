using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("vanillaracesexpanded.insector")]
public class VanillaRacesInsector
{
	private static AccessTools.FieldRef<GameComponent> genelinesInstanceField;

	private static AccessTools.FieldRef<GameComponent, IList> genelinesListField;

	private static FastInvokeHandler createGenelineMethod;

	private static FastInvokeHandler addGenelineMethod;

	private static AccessTools.FieldRef<object, int> genelineIdField;

	private static FastInvokeHandler editGenelineMethod;

	public VanillaRacesInsector(ModContentPack mod)
	{
		MpCompatPatchLoader.LoadPatch(this);
		Type type = AccessTools.TypeByName("VanillaRacesExpandedInsector.GameComponent_Genelines");
		genelinesInstanceField = AccessTools.StaticFieldRefAccess<GameComponent>(AccessTools.DeclaredField(type, "Instance"));
		genelinesListField = AccessTools.FieldRefAccess<IList>(type, "genelines");
		createGenelineMethod = MethodInvoker.GetHandler(AccessTools.DeclaredMethod(type, "CreateGeneline"));
		addGenelineMethod = MethodInvoker.GetHandler(AccessTools.DeclaredMethod(type, "AddGeneline"));
		MP.RegisterSyncMethod(type, "DeleteGeneline");
		Type type2 = AccessTools.TypeByName("VanillaRacesExpandedInsector.Geneline");
		genelineIdField = AccessTools.FieldRefAccess<int>(type2, "id");
		editGenelineMethod = MethodInvoker.GetHandler(AccessTools.DeclaredMethod(type2, "EditGeneline"));
		MP.RegisterSyncMethod(type2, "AddPawnWithMetapod");
		MP.RegisterSyncMethodLambda(AccessTools.TypeByName("VanillaRacesExpandedInsector.Metapod"), "GetGizmos", 0).SetDebugOnly();
		string[] array = new string[2] { "VanillaRacesExpandedInsector.HediffComp_ChestburstPregnancyVictim", "VanillaRacesExpandedInsector.HediffComp_ChestburstPregnancyVictimHidden" };
		for (int i = 0; i < array.Length; i++)
		{
			MP.RegisterSyncMethodLambda(AccessTools.TypeByName(array[i]), "CompGetGizmos", 0).SetDebugOnly();
		}
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod("VanillaRacesExpandedInsector.HediffComp_Parthenogenesis:TryParthenogenesis"));
		PatchingUtilities.PatchPushPopRand("VanillaRacesExpandedInsector.IncomingSmoker:ThrowBlackSmoke");
	}

	[MpCompatSyncWorker("VanillaRacesExpandedInsector.Geneline")]
	private static void SyncGeneline(SyncWorker sync, ref object geneline)
	{
		if (sync.isWriting)
		{
			if (geneline != null)
			{
				sync.Write(genelineIdField(geneline));
			}
			else
			{
				sync.Write(-1);
			}
			return;
		}
		int num = sync.Read<int>();
		if (num < 0)
		{
			return;
		}
		foreach (object item in genelinesListField(genelinesInstanceField()))
		{
			if (genelineIdField(item) == num)
			{
				geneline = item;
				break;
			}
		}
	}

	[MpCompatSyncMethod(SyncContext.None)]
	private static void SyncedCreateGeneline(List<GeneDef> selectedGenes, string genelineName)
	{
		GameComponent target = genelinesInstanceField();
		object obj = createGenelineMethod(target);
		editGenelineMethod(obj, selectedGenes, genelineName);
		addGenelineMethod(target, obj);
	}

	[MpCompatSyncMethod(SyncContext.None)]
	private static void SyncedEditGeneline(int genelineId, List<GeneDef> selectedGenes, string genelineName)
	{
		foreach (object item in genelinesListField(genelinesInstanceField()))
		{
			if (genelineId == genelineIdField(item))
			{
				editGenelineMethod(item, selectedGenes, genelineName);
				return;
			}
		}
		Log.Warning($"Trying to modify geneline with ID {genelineId}, but it did not exist. Was it removed?");
	}

	[MpCompatPrefix("VanillaRacesExpandedInsector.Window_EditGeneline", "Accept", MethodType.Normal)]
	private static bool PreAcceptEditGeneline(Window __instance, Action ___callback, object ___geneline, IList ___selectedGenes, string ___genelineName)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		List<GeneDef> selectedGenes = ___selectedGenes.Cast<GeneDef>().ToList();
		if (___callback == null)
		{
			SyncedEditGeneline(genelineIdField(___geneline), selectedGenes, ___genelineName);
		}
		else
		{
			SyncedCreateGeneline(selectedGenes, ___genelineName);
		}
		__instance.Close();
		return false;
	}
}
