using System;
using System.Linq;
using HarmonyLib;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Mlie.CustomPrisonerInteractions")]
public class CustomPrisonerInteractions
{
	public CustomPrisonerInteractions(ModContentPack mod)
	{
		Type parentType = AccessTools.TypeByName("CustomPrisonerInteractions.ITab_Pawn_Visitor_FillTab");
		MpCompat.RegisterLambdaDelegate(parentType, "Prefix", Enumerable.Range(0, 11).ToArray());
		MpCompat.RegisterLambdaDelegate(parentType, "Postfix", default(int));
	}
}
