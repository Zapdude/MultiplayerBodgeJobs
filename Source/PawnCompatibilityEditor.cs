using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("jomar.pawncompatibilityeditor")]
public class PawnCompatibilityEditor
{
	public PawnCompatibilityEditor(ModContentPack mod)
	{
		Type type = AccessTools.TypeByName("CompatibilityEditor.CompatibilityOverrideStore");
		MP.RegisterSyncMethod(type, "SetOverride");
		MP.RegisterSyncMethod(type, "RemoveOverride");
	}
}
