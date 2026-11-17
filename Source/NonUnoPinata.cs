using System;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("avilmask.NonUnoPinata")]
public class NonUnoPinata
{
	private static Type compStripCheckerType;

	public NonUnoPinata(ModContentPack mod)
	{
		compStripCheckerType = AccessTools.TypeByName("NonUnoPinata.CompStripChecker");
		MP.RegisterSyncWorker<ThingComp>(SyncWorkerForCompStripChecker, compStripCheckerType);
		LongEventHandler.ExecuteWhenFinished(LatePatch);
	}

	private static void LatePatch()
	{
		MP.RegisterSyncMethod(AccessTools.Method("NonUnoPinata.NUPUtility:SetShouldStrip"));
		InjectComp();
	}

	private static void SyncWorkerForCompStripChecker(SyncWorker sw, ref ThingComp comp)
	{
		if (sw.isWriting)
		{
			sw.Write(comp.parent);
			return;
		}
		comp = sw.Read<ThingWithComps>().AllComps.First((ThingComp c) => c.GetType() == compStripCheckerType);
	}

	private static void InjectComp()
	{
		CompProperties item = new CompProperties
		{
			compClass = compStripCheckerType
		};
		foreach (ThingDef item2 in DefDatabase<ThingDef>.AllDefs.Where((ThingDef def) => typeof(ThingWithComps).IsAssignableFrom(def.thingClass) && def.EverHaulable && !def.HasComp(compStripCheckerType)))
		{
			item2.comps.Add(item);
		}
	}
}
