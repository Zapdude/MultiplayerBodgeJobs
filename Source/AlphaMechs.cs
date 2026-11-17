using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("sarg.alphamechs")]
public class AlphaMechs
{
	public AlphaMechs(ModContentPack mod)
	{
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod("AlphaMechs.Pawn_HemogenVat:EjectContents"));
	}
}
