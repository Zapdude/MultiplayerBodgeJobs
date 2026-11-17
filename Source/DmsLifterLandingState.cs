using System;
using System.Collections.Generic;
using System.Linq;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Compat;

public sealed class DmsLifterLandingState : GameComponent
{
	public List<DmsPendingLifterLanding> pending = new List<DmsPendingLifterLanding>();

	private int nextId = 1;

	private readonly Game game;

	private readonly HashSet<(int, int)> presented = new HashSet<(int, int)>();

	public DmsLifterLandingState(Game game)
	{
		this.game = game;
	}

	internal void Remember(List<ActiveTransporterInfo> pods, PlanetTile tile, string owner)
	{
		if (pods != null && pods.Count != 0)
		{
			DmsPendingLifterLanding dmsPendingLifterLanding = new DmsPendingLifterLanding
			{
				id = nextId++,
				owner = (owner ?? ""),
				tile = tile,
				pods = new List<ActiveTransporterInfo>(pods)
			};
			pending.Add(dmsPendingLifterLanding);
			DeadMansSwitchII.AttachLifterCarrier(dmsPendingLifterLanding, pods);
			DmsLifterLandingLetter dmsLifterLandingLetter = new DmsLifterLandingLetter();
			dmsLifterLandingLetter.def = LetterDefOf.NeutralEvent;
			dmsLifterLandingLetter.recordId = dmsPendingLifterLanding.id;
			dmsLifterLandingLetter.Label = DeadMansSwitchII.LifterText("Title");
			dmsLifterLandingLetter.title = DeadMansSwitchII.LifterText("Title");
			dmsLifterLandingLetter.Text = DeadMansSwitchII.LifterText("Pending");
			dmsLifterLandingLetter.lookTargets = new LookTargets(new GlobalTargetInfo(tile));
			DmsLifterLandingLetter dmsLifterLandingLetter2 = dmsLifterLandingLetter;
			Find.LetterStack.ReceiveLetter(dmsLifterLandingLetter2);
			dmsPendingLifterLanding.map = DeadMansSwitchII.LifterMap(tile);
			dmsPendingLifterLanding.fallback = DeadMansSwitchII.LifterFallback(dmsPendingLifterLanding.map, dmsPendingLifterLanding.Size);
		}
	}

	public override void ExposeData()
	{
		if (!DeadMansSwitchII.LifterAvailable)
		{
			return;
		}
		Scribe_Collections.Look(ref pending, "mpDmsLifterLandings", LookMode.Deep);
		Scribe_Values.Look(ref nextId, "mpDmsLifterNextId", 1);
		if (pending == null)
		{
			pending = new List<DmsPendingLifterLanding>();
		}
		if (Scribe.mode != LoadSaveMode.PostLoadInit)
		{
			return;
		}
		foreach (DmsPendingLifterLanding item in pending)
		{
			DeadMansSwitchII.RebindLifterCarrier(item);
		}
		nextId = Math.Max(nextId, pending.Select((DmsPendingLifterLanding p) => p.id).DefaultIfEmpty(0).Max() + 1);
	}

	public override void GameComponentUpdate()
	{
		if (!DeadMansSwitchII.LifterAvailable || Current.Game != game || !DeadMansSwitchII.LifterCanPresent || Current.ProgramState != ProgramState.Playing || Find.WindowStack == null || Find.Targeter.IsTargeting)
		{
			return;
		}
		DmsPendingLifterLanding[] array = pending.ToArray();
		foreach (DmsPendingLifterLanding dmsPendingLifterLanding in array)
		{
			if ((!MP.IsInMultiplayer || string.Equals(dmsPendingLifterLanding.owner, MP.PlayerName, StringComparison.Ordinal)) && presented.Add((dmsPendingLifterLanding.id, dmsPendingLifterLanding.revision)))
			{
				Present(dmsPendingLifterLanding);
				break;
			}
		}
	}

	internal void Reopen(int id)
	{
		DmsPendingLifterLanding dmsPendingLifterLanding = pending.FirstOrDefault((DmsPendingLifterLanding r) => r.id == id);
		if (dmsPendingLifterLanding != null)
		{
			if (!MP.IsInMultiplayer || dmsPendingLifterLanding.owner == MP.PlayerName)
			{
				presented.Remove((dmsPendingLifterLanding.id, dmsPendingLifterLanding.revision));
			}
			else
			{
				DeadMansSwitchII.SyncLifterClaim(dmsPendingLifterLanding.id, dmsPendingLifterLanding.revision, dmsPendingLifterLanding.owner, MP.PlayerName);
			}
		}
	}

	private void Present(DmsPendingLifterLanding record)
	{
		int id = record.id;
		int revision = record.revision;
		string owner = record.owner;
		bool submitted = false;
		try
		{
			if (record.map == null || !Find.Maps.Contains(record.map))
			{
				Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(DeadMansSwitchII.LifterText("Regenerate"), () =>
				{
					Submit(record.fallback, fallback: true);
				}));
				return;
			}
			Current.Game.CurrentMap = record.map;
			CameraJumper.TryHideWorld();
			CameraJumper.TryJump(record.fallback, record.map);
			Find.Targeter.BeginTargeting(TargetingParameters.ForCell(), (LocalTargetInfo x) =>
			{
				Submit(x.Cell, fallback: false);
			}, (LocalTargetInfo x) =>
			{
				ThingDef thingDef = record.pods.FirstOrDefault()?.sentTransporterDef;
				if (CurrentRecord() && thingDef != null)
				{
					GhostDrawer.DrawGhostThing(x.Cell, Rot4.North, thingDef, thingDef.graphic, DeadMansSwitchII.LifterCellValid(x.Cell, record.map, record.Size) ? Designator_Place.CanPlaceColor : Designator_Place.CannotPlaceColor, AltitudeLayer.Blueprint);
				}
			}, (LocalTargetInfo x) => CurrentRecord() && DeadMansSwitchII.LifterCellValid(x.Cell, record.map, record.Size), null, () =>
			{
				Submit(record.fallback, fallback: true);
			}, CompLaunchable.TargeterMouseAttachment);
		}
		catch (Exception value)
		{
			Log.Error($"MP Compat: DMS lifter presentation failed; reopen the pending landing letter to retry: {value}");
		}
		bool CurrentRecord()
		{
			if (Current.Game == game && pending.Contains(record) && record.revision == revision)
			{
				return record.owner == owner;
			}
			return false;
		}
		void Submit(IntVec3 cell, bool fallback)
		{
			if (!submitted && CurrentRecord())
			{
				submitted = true;
				DeadMansSwitchII.SyncLifterLand(id, revision, owner, cell, fallback);
			}
		}
	}
}
