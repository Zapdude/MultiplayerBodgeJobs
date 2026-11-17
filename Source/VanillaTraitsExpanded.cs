using System.Collections;
using HarmonyLib;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("VanillaExpanded.VanillaTraitsExpanded")]
public class VanillaTraitsExpanded
{
	private static AccessTools.FieldRef<IDictionary> mapPawnsAnimalListField;

	public VanillaTraitsExpanded(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
		mapPawnsAnimalListField = AccessTools.StaticFieldRefAccess<IDictionary>(AccessTools.DeclaredField("VanillaTraitsExpanded.ThoughtWorker_AnimalsInColony:mapPawns"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(GameComponentUtility), "FinalizeInit"), null, new HarmonyMethod(typeof(VanillaTraitsExpanded), "ClearCache"));
	}

	private static void LatePatch()
	{
		PatchingUtilities.PatchCancelInInterface(AccessTools.DeclaredMethod("VanillaTraitsExpanded.TryTakeOrderedJob_Patch:Postfix"));
	}

	private static void ClearCache()
	{
		mapPawnsAnimalListField().Clear();
	}
}
