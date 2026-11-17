using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("SomewhereOutInSpace.Spaceports")]
[MpCompatFor("zal.spaceports")]
internal class RimsentialSpaceports
{
	public RimsentialSpaceports(ModContentPack mod)
	{
		Type type = AccessTools.TypeByName("Spaceports.Buildings.Building_Beacon");
		MpCompat.RegisterLambdaMethod(type, "GetGizmos", 1, 2, 3);
		MP.RegisterSyncMethod(type, "ConfirmAction");
		type = AccessTools.TypeByName("Spaceports.Buildings.Building_Shuttle");
		MpCompat.RegisterLambdaMethod(type, "GetGizmos", 0, 1);
		type = AccessTools.TypeByName("Spaceports.Buildings.Building_ShuttlePad");
		MP.RegisterSyncMethod(type, "SetAccessState");
		type = AccessTools.TypeByName("Spaceports.Buildings.Building_ShuttleSpot");
		MP.RegisterSyncMethod(type, "SetAccessState");
		Type type2 = AccessTools.TypeByName("Spaceports.Letters.PrisonerTransferLetter");
		MethodInfo[] array = MpMethodUtil.GetLambda(type2, "Choices", MethodType.Getter, null, 0, 1, 2).ToArray();
		MP.RegisterSyncMethod(array[0]);
		MP.RegisterSyncMethod(array[1]);
		MP.RegisterSyncMethod(array[2]);
		MP.RegisterDefaultLetterChoice(array[2], type2);
		string[] array2 = new string[4] { "Spaceports.Letters.InterstellarDerelictLetter", "Spaceports.Letters.MedevacLetter", "Spaceports.Letters.MysteryCargoLetter", "Spaceports.Letters.SpicyPawnLendingLetter" };
		for (int i = 0; i < array2.Length; i++)
		{
			type2 = AccessTools.TypeByName(array2[i]);
			MethodInfo[] array3 = MpMethodUtil.GetLambda(type2, "Choices", MethodType.Getter, null, 0, 1).ToArray();
			MP.RegisterSyncMethod(array3[0]);
			MP.RegisterSyncMethod(array3[1]);
			MP.RegisterDefaultLetterChoice(array3[1], type2);
		}
	}
}
