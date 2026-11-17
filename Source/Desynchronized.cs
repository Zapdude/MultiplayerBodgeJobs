using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Mlie.Desynchronized")]
public class Desynchronized
{
	public Desynchronized(ModContentPack mod)
	{
		PatchingUtilities.PatchPushPopRand(new string[6] { "Desynchronized.TNDBS.Pawn_NewsKnowledgeTracker:ForgetRandom", "Desynchronized.TNDBS.Pawn_NewsKnowledgeTracker:ForgetRandomly", "Desynchronized.TNDBS.Utilities.NewsSpreadUtility:SelectNewsRandomly", "Desynchronized.TNDBS.Utilities.NewsSpreadUtility:SelectNewsDistinctly", "Desynchronized.Patches.NewsTransmit.PostFix_InteractionWorker:ExecuteNewsTarnsmission", "Desynchronized.TNDBS.TaleNewsPawnDied:CalculateNewsImportanceForPawn" });
	}
}
