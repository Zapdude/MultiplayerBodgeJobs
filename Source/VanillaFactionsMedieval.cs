using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("OskarPotocki.VanillaFactionsExpanded.MedievalModule")]
public class VanillaFactionsMedieval
{
	public VanillaFactionsMedieval(ModContentPack mod)
	{
		PatchingUtilities.PatchPushPopRand("VFEMedieval.JobDriver_PlayArchery:ThrowObjectAt");
	}
}
