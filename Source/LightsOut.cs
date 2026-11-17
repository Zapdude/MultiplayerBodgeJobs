using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("juanlopez2008.LightsOut")]
internal class LightsOut
{
	public LightsOut(ModContentPack mod)
	{
		MP.RegisterSyncMethod(AccessTools.PropertySetter("LightsOut.ThingComps.KeepOnComp:KeepOn"));
		PatchingUtilities.PatchCancelInInterface("LightsOut.Common.TablesHelper:PatchTableOff", "LightsOut.Patches.ModCompatibility.Biotech.PatchGeneAssembly:OnStart", "LightsOut.Patches.ModCompatibility.Biotech.PatchWasteAtomiser:UpdateWorking");
	}
}
