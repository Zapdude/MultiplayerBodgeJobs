using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

public sealed class RimDarkSchedulerComponent(Game game) : GameComponent
{
	private static readonly string[] Notifiers = new string[2] { "Core40k.GeneVariantAvailabilityNotifier", "Core40k.RankEligibilityNotifier" };

	private readonly List<Pawn>[] sweeps = new List<Pawn>[2];

	private readonly int[] cursors = new int[2];

	private List<RimDarkShieldEvent> shieldQueue;

	public override void ExposeData()
	{
		for (int i = 0; i < Notifiers.Length; i++)
		{
			Type type = AccessTools.TypeByName(Notifiers[i]);
			if (type == null)
			{
				continue;
			}
			FieldInfo fieldInfo = AccessTools.DeclaredField(type, "sweepPawns");
			FieldInfo fieldInfo2 = AccessTools.DeclaredField(type, "sweepIndex");
			if (fieldInfo == null || fieldInfo2 == null)
			{
				continue;
			}
			if (Scribe.mode == LoadSaveMode.Saving)
			{
				sweeps[i] = new List<Pawn>((List<Pawn>)fieldInfo.GetValue(null));
				cursors[i] = (int)fieldInfo2.GetValue(null);
			}
			Scribe_Collections.Look(ref sweeps[i], "mpRimDarkSweepPawns" + i, LookMode.Reference);
			Scribe_Values.Look(ref cursors[i], "mpRimDarkSweepIndex" + i, 0);
			if (Scribe.mode == LoadSaveMode.PostLoadInit && MP.IsInMultiplayer)
			{
				List<Pawn> list = (List<Pawn>)fieldInfo.GetValue(null);
				list.Clear();
				if (sweeps[i] != null)
				{
					list.AddRange(sweeps[i]);
				}
				fieldInfo2.SetValue(null, Math.Clamp(cursors[i], 0, list.Count));
			}
		}
		Type type2 = AccessTools.TypeByName("Core40k.ShieldBashUtility");
		if (type2 == null)
		{
			return;
		}
		FieldInfo fieldInfo3 = AccessTools.DeclaredField(type2, "pending");
		if (Scribe.mode == LoadSaveMode.Saving)
		{
			shieldQueue = ((IEnumerable)fieldInfo3.GetValue(null)).Cast<object>().Select(RimDarkShieldEvent.Capture).ToList();
		}
		Scribe_Collections.Look(ref shieldQueue, "mpRimDarkShieldQueue", LookMode.Deep);
		if (Scribe.mode != LoadSaveMode.PostLoadInit || !MP.IsInMultiplayer)
		{
			return;
		}
		IList list2 = (IList)fieldInfo3.GetValue(null);
		list2.Clear();
		if (shieldQueue == null)
		{
			return;
		}
		foreach (RimDarkShieldEvent item in shieldQueue)
		{
			if (item.caster != null && item.target != null)
			{
				list2.Add(item.Create(list2.GetType().GetGenericArguments()[0]));
			}
		}
	}
}
