using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("LWM.DeepStorage")]
internal class DeepStorage
{
	public DeepStorage(ModContentPack mod)
	{
		MP.RegisterSyncMethod(AccessTools.TypeByName("LWM.DeepStorage.ITab_DeepStorage_Inventory"), "EjectTarget").CancelIfAnyArgNull();
	}
}
