using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Owlchemist.PerspectiveBuildings")]
[MpCompatFor("Mlie.PerspectiveBuildings")]
internal class PerspectiveBuildings
{
	public PerspectiveBuildings(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
	}

	private static void LatePatch()
	{
		Type type = AccessTools.TypeByName("Perspective.CompOffsetter");
		MP.RegisterSyncMethod(type, "SetCurrentOffset");
		MP.RegisterSyncMethod(type, "SetMirroredState");
	}
}
