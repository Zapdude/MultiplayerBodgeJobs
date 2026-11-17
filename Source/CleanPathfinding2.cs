using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Owlchemist.CleanPathfinding")]
public class CleanPathfinding2
{
	public CleanPathfinding2(ModContentPack mod)
	{
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod("CleanPathfinding.MapComponent_DoorPathing:SwitchDoorType"));
	}
}
