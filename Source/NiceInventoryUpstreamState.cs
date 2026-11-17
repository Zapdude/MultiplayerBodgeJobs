using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

public sealed class NiceInventoryUpstreamState(Game game) : GameComponent
{
	public sealed class Request : IExposable
	{
		public Pawn Pawn;

		public ApparelLayerDef Layer;

		public void ExposeData()
		{
			Scribe_References.Look(ref Pawn, "targetPawn");
			Scribe_Defs.Look(ref Layer, "layer");
		}
	}

	private Dictionary<Bill_ProductionWithUft, Request> requests = new Dictionary<Bill_ProductionWithUft, Request>();

	private List<Bill_ProductionWithUft> billKeys;

	private List<Request> requestValues;

	internal bool LoadedRequestsNode;

	private static bool Active(Bill_ProductionWithUft bill)
	{
		if (bill?.billStack != null && !bill.DeletedOrDereferenced)
		{
			return bill.billStack.Bills.Contains(bill);
		}
		return false;
	}

	public override void ExposeData()
	{
		base.ExposeData();
		if (!NiceInventoryTab.OriginalRelease)
		{
			return;
		}
		if (Scribe.mode == LoadSaveMode.Saving)
		{
			Dictionary<Bill_ProductionWithUft, Request> dict = new Dictionary<Bill_ProductionWithUft, Request>();
			foreach (DictionaryEntry authorRequest in NiceInventoryTab.AuthorRequests)
			{
				Bill_ProductionWithUft bill_ProductionWithUft = authorRequest.Key as Bill_ProductionWithUft;
				object value = authorRequest.Value;
				Pawn pawn = ((value == null) ? null : (AccessTools.Field(value.GetType(), "TargetPawn").GetValue(value) as Pawn));
				ApparelLayerDef apparelLayerDef = ((value == null) ? null : (AccessTools.Field(value.GetType(), "Layer").GetValue(value) as ApparelLayerDef));
				if (Active(bill_ProductionWithUft) && pawn != null && apparelLayerDef != null)
				{
					dict[bill_ProductionWithUft] = new Request
					{
						Pawn = pawn,
						Layer = apparelLayerDef
					};
				}
			}
			Scribe_Collections.Look(ref dict, "niceInventoryWearRequests", LookMode.Reference, LookMode.Deep);
			return;
		}
		if (Scribe.mode == LoadSaveMode.LoadingVars)
		{
			LoadedRequestsNode = Scribe.loader.curXmlParent?["niceInventoryWearRequests"] != null;
		}
		Scribe_Collections.Look(ref requests, "niceInventoryWearRequests", LookMode.Reference, LookMode.Deep, ref billKeys, ref requestValues);
		if (Scribe.mode != LoadSaveMode.PostLoadInit)
		{
			return;
		}
		IDictionary authorRequests = NiceInventoryTab.AuthorRequests;
		authorRequests.Clear();
		foreach (KeyValuePair<Bill_ProductionWithUft, Request> item in requests ?? new Dictionary<Bill_ProductionWithUft, Request>())
		{
			if (Active(item.Key) && item.Value?.Pawn != null && item.Value.Layer != null)
			{
				authorRequests[item.Key] = NiceInventoryTab.AuthorRequest(item.Value.Pawn, item.Value.Layer);
			}
		}
	}
}
