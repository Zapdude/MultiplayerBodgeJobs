using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("aRandomKiwi.HuntForMe")]
internal class HuntForMe
{
	public HuntForMe(ModContentPack mod)
	{
		MpCompat.RegisterLambdaDelegate(AccessTools.Inner(AccessTools.TypeByName("aRandomKiwi.HFM.Pawn_MindState_Patch"), "GetGizmos"), "Listener", 1, 3, 5, 6, 7, 8).Take(3).ToArray()
			.SetDebugOnly();
		string[] array = new string[4] { "aRandomKiwi.HFM.PawnColumnWorker_Hunting", "aRandomKiwi.HFM.PawnColumnWorker_HuntingCanAssist", "aRandomKiwi.HFM.PawnColumnWorker_PreyModeHunting", "aRandomKiwi.HFM.PawnColumnWorker_SupervisedHunting" };
		for (int i = 0; i < array.Length; i++)
		{
			MP.RegisterSyncMethod(AccessTools.TypeByName(array[i]), "SetValue");
		}
	}
}
