using HarmonyLib;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("lovelydovey.recreation.witheuterpe")]
public class RecreationalSexWithEuterpe
{
	public RecreationalSexWithEuterpe(ModContentPack mod)
	{
		MpCompat.RegisterLambdaDelegate(AccessTools.TypeByName("RecreationalSexWithEuterpe.PawnColumnWorker_MealTime"), "GenerateDropdownElements", default(int));
		MpCompat.RegisterLambdaMethod(AccessTools.TypeByName("RecreationalSexWithEuterpe.HediffComp_Birthday"), "CompGetGizmos", 1);
	}
}
