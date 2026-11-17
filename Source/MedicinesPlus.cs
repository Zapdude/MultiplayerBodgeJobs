using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("TedDraws.MedicinesPlus.AT")]
internal class MedicinesPlus
{
	public MedicinesPlus(ModContentPack mod)
	{
		PatchingUtilities.PatchSystemRand("AT_MedicinesPlus.Hediff_HypnotolAddiction:PostRemoved", patchPushPop: false);
	}
}
