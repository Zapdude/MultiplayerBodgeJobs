using System;
using System.Collections.Generic;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

public sealed class WorldDominationSimulationComponent : GameComponent
{
	private string snapshot;

	private List<ILoadReferenceable> references;

	private int goodwillDrainLastPulseTick = -1;

	private bool goodwillPulseRestored;

	internal bool Restored { get; private set; }

	internal void RestoreAfterNativeInitialization()
	{
		if (MP.IsInMultiplayer)
		{
			if (Restored && snapshot != null)
			{
				WorldDomination.RestoreSimulation(Convert.FromBase64String(snapshot), references);
			}
			if (goodwillPulseRestored)
			{
				PrepareGoodwillPulse();
			}
		}
	}

	internal void PrepareGoodwillPulse()
	{
		if (goodwillDrainLastPulseTick == int.MinValue)
		{
			goodwillDrainLastPulseTick = WorldDomination.MigrateLegacyGoodwillPulse();
		}
		WorldDomination.SetGoodwillPulseTick(goodwillDrainLastPulseTick);
	}

	internal void CaptureGoodwillPulse()
	{
		goodwillDrainLastPulseTick = WorldDomination.GetGoodwillPulseTick();
	}

	public WorldDominationSimulationComponent(Game game)
	{
		if (Scribe.mode == LoadSaveMode.LoadingVars)
		{
			goodwillDrainLastPulseTick = int.MinValue;
		}
	}

	public override void ExposeData()
	{
		if (!WorldDomination.HasSettings)
		{
			return;
		}
		if (Scribe.mode == LoadSaveMode.Saving)
		{
			snapshot = Convert.ToBase64String(WorldDomination.CaptureSimulation(out references));
			if (!MP.IsInMultiplayer)
			{
				CaptureGoodwillPulse();
			}
		}
		Scribe_Values.Look(ref snapshot, "mpWdScheduler");
		Scribe_Collections.Look(ref references, "mpWdSchedulerReferences", LookMode.Reference);
		Scribe_Values.Look(ref goodwillDrainLastPulseTick, "mpWdGoodwillDrainLastPulseTick", int.MinValue, forceSave: true);
		if (Scribe.mode == LoadSaveMode.PostLoadInit && MP.IsInMultiplayer)
		{
			PrepareGoodwillPulse();
			goodwillPulseRestored = true;
		}
		if (Scribe.mode == LoadSaveMode.PostLoadInit && MP.IsInMultiplayer && snapshot != null)
		{
			WorldDomination.RestoreSimulation(Convert.FromBase64String(snapshot), references);
			Restored = true;
		}
	}
}
