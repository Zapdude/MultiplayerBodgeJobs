using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Mlie.MarkThatPawn")]
public class MarkThatPawn
{
	public MarkThatPawn(ModContentPack mod)
	{
		Type parentType = AccessTools.TypeByName("MarkThatPawn.MarkThatPawn");
		MP.RegisterSyncDelegateLocalFunc(parentType, "GetMarkingOptions", "Action").SetContext(SyncContext.MapSelected);
		MP.RegisterSyncDelegateLocalFunc(parentType, "GetMarkingOptions", "action").SetContext(SyncContext.MapSelected);
	}
}
