using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Outposts;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Multiplayer.Compat;

[MpCompatFor("OskarPotocki.VanillaFactionsExpanded.Core")]
internal class VanillaExpandedFrameworkReferenced
{
	public VanillaExpandedFrameworkReferenced(ModContentPack mod)
	{
		CommandAuthorityBridge.Install();
		MpCompat.harmony.Patch(AccessTools.Method(typeof(Dialog_CreateCamp), "DoOutpostDisplay"), new HarmonyMethod(typeof(VanillaExpandedFrameworkReferenced), "PreDoOutpostDisplay"));
		MP.RegisterSyncMethod(typeof(VanillaExpandedFrameworkReferenced), "SyncedCreateOutpost");
		MpCompat.harmony.Patch(AccessTools.Method(typeof(Dialog_TakeItems), "DoBottomButtons"), new HarmonyMethod(typeof(VanillaExpandedFrameworkReferenced), "PreTakeItemsDoBottomButtons"));
		MP.RegisterSyncMethod(typeof(VanillaExpandedFrameworkReferenced), "SyncedTakeItems");
		MpCompat.harmony.Patch(AccessTools.Method(typeof(Dialog_GiveItems), "DoBottomButtons"), new HarmonyMethod(typeof(VanillaExpandedFrameworkReferenced), "PreGiveItemsDoBottomButtons"));
		MP.RegisterSyncMethod(typeof(VanillaExpandedFrameworkReferenced), "SyncedGiveItems");
		MpCompat.RegisterLambdaMethod(typeof(Outpost), "GetGizmos", 0, 1, 9, 10, 11, 12).Reverse().Take(4)
			.SetDebugOnly();
		MpCompat.RegisterLambdaDelegate(typeof(Outpost), "GetGizmos", 8);
		MethodInfo lambda = MpMethodUtil.GetLambda(typeof(Outpost), "GetGizmos", MethodType.Normal, null, 4);
		AccessTools.FieldRef<object, Outpost> outpostsInnerClassThisField = AccessTools.FieldRefAccess<Outpost>(lambda.DeclaringType, "<>4__this");
		MP.RegisterSyncDelegate(typeof(Outpost), lambda.DeclaringType.Name, lambda.Name).TransformField("p", Serializer.New((Pawn p, object instance, object[] _) => (o: outpostsInnerClassThisField(instance), pawnId: p.thingIDNumber), ((Outpost o, int pawnId) tuple) => tuple.o.AllPawns.FirstOrDefault((Pawn p) => p.thingIDNumber == tuple.pawnId)));
		MP.RegisterSyncWorker<ResultOption>(SyncResultOption);
		MpCompat.RegisterLambdaDelegate(typeof(Outpost), "GetCaravanGizmos", 2);
		MpCompat.RegisterLambdaDelegate("Outposts.Outpost_ChooseResult", "GetGizmos", 2);
		MP.RegisterSyncWorker<WITab_Outpost_Gear>(SyncWITabOutpostGear);
		MethodInfo original = AccessTools.DeclaredMethod(typeof(WITab_Outpost_Gear), "TryEquipDraggedItem");
		MP.RegisterSyncMethod(MpMethodUtil.MethodOf(new Action<WITab_Outpost_Gear, int>(SyncedTryEquipDraggedItem))).SetContext(SyncContext.WorldSelected);
		MpCompat.harmony.Patch(original, new HarmonyMethod(MpMethodUtil.MethodOf(new Func<WITab_Outpost_Gear, Pawn, bool>(PreTryEquipDraggedItem))), null, null, new HarmonyMethod(MpMethodUtil.MethodOf(ClearDraggedItem)));
		original = AccessTools.DeclaredMethod(typeof(WITab_Outpost_Gear), "TryEquipDraggedItem_Equipment");
		MP.RegisterSyncMethod(original).SetContext(SyncContext.WorldSelected).TransformArgument(0, Serializer.New((Pawn p) => p.thingIDNumber, (int id) =>
		{
			//IL_0017: Unknown result type (might be due to invalid IL or missing references)
			return ((Outpost)Find.WorldSelector.SingleSelectedObject).AllPawns.FirstOrDefault((Pawn p) => p.thingIDNumber == id);
		}))
			.TransformArgument(1, Serializer.SimpleReader(() => (ThingWithComps)null));
		MpCompat.harmony.Patch(original, new HarmonyMethod(MpMethodUtil.MethodOf(SetupArgumentFromDraggedItem)), null, null, new HarmonyMethod(MpMethodUtil.MethodOf(ClearDraggedItem)));
		original = AccessTools.DeclaredMethod(typeof(WITab_Outpost_Gear), "MoveDraggedItemToInventory");
		MP.RegisterSyncMethod(original).SetContext(SyncContext.WorldSelected);
		MpCompat.harmony.Patch(original, null, null, null, new HarmonyMethod(MpMethodUtil.MethodOf(ClearDraggedItem)));
	}

	private static void SyncResultOption(SyncWorker sync, ref ResultOption option)
	{
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Expected Obj, but got Unknown
		if (sync.isWriting)
		{
			sync.Write(option.Thing);
			return;
		}
		option = new ResultOption
		{
			Thing = sync.Read<ThingDef>()
		};
	}

	private static bool PreDoOutpostDisplay(ref Rect inRect, WorldObjectDef outpostDef, Dialog_CreateCamp __instance)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		GameFont font = Text.Font;
		TextAnchor anchor = Text.Anchor;
		Text.Font = GameFont.Tiny;
		inRect.height = Text.CalcHeight(outpostDef.description, inRect.width - 90f) + 60f;
		Rect outerRect = inRect.LeftPartPixels(50f);
		Rect rect = inRect.RightPartPixels(inRect.width - 60f);
		Texture2D expandingIconTexture = outpostDef.ExpandingIconTexture;
		GUI.color = __instance.creator.Faction.Color;
		Widgets.DrawTextureFitted(outerRect, expandingIconTexture, 1f, new Vector2(expandingIconTexture.width, expandingIconTexture.height), new Rect(0f, 0f, 1f, 1f));
		GUI.color = Color.white;
		Text.Font = GameFont.Medium;
		Widgets.Label(rect.TopPartPixels(30f), outpostDef.label.CapitalizeFirst(outpostDef));
		Rect rect2 = rect.BottomPartPixels(30f).LeftPartPixels(100f);
		Rect rect3 = rect.BottomPartPixels(30f).RightPartPixels(rect.width - 120f);
		Text.Font = GameFont.Tiny;
		Widgets.Label(new Rect(rect.x, rect.y + 30f, rect.width, rect.height - 60f), outpostDef.description);
		Text.Font = GameFont.Small;
		Text.Anchor = TextAnchor.MiddleLeft;
		Widgets.Label(rect3, __instance.validity[outpostDef].First);
		Text.Font = font;
		Text.Anchor = anchor;
		if (Widgets.ButtonText(rect2, "Outposts.Dialog.Create".Translate()))
		{
			if (__instance.validity[outpostDef].First.NullOrEmpty())
			{
				SyncedCreateOutpost(outpostDef, __instance.creator);
				((Window)(object)__instance).Close(true);
				Find.WorldSelector.Deselect(__instance.creator);
			}
			else
			{
				Messages.Message(__instance.validity[outpostDef].First, MessageTypeDefOf.RejectInput, historical: false);
			}
		}
		TooltipHandler.TipRegion(inRect, __instance.validity[outpostDef].Second);
		return false;
	}

	private static void SyncedCreateOutpost(WorldObjectDef outpostDef, Caravan creator)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Expected Obj, but got Unknown
		Outpost val = (Outpost)WorldObjectMaker.MakeWorldObject(outpostDef);
		val.Name = NameGenerator.GenerateName(creator.Faction.def.settlementNameMaker, from o in Find.WorldObjects.AllWorldObjects.OfType<Outpost>()
			select o.Name);
		((WorldObject)(object)val).Tile = creator.Tile;
		((WorldObject)(object)val).SetFaction(creator.Faction);
		Find.WorldObjects.Add((WorldObject)(object)val);
		foreach (Pawn item in creator.PawnsListForReading.ListFullCopy())
		{
			val.AddPawn(item);
		}
	}

	private static bool PreTakeItemsDoBottomButtons(Rect rect, Dialog_TakeItems __instance)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		Rect rect2 = new Rect(rect.width - __instance.BottomButtonSize.x, rect.height - 40f, __instance.BottomButtonSize.x, __instance.BottomButtonSize.y);
		if (Widgets.ButtonText(rect2, "Outposts.Take".Translate()))
		{
			Dictionary<ThingDef, int> itemsToTransfer = __instance.transferables.Where((TransferableOneWay x) => x.HasAnyThing && x.CountToTransfer > 0).ToDictionary((TransferableOneWay x) => x.ThingDef, (TransferableOneWay x) => x.CountToTransfer);
			SyncedTakeItems(__instance.outpost, __instance.caravan, itemsToTransfer);
			((Window)(object)__instance).Close(true);
		}
		if (Widgets.ButtonText(new Rect(0f, rect2.y, __instance.BottomButtonSize.x, __instance.BottomButtonSize.y), "CancelButton".Translate()))
		{
			((Window)(object)__instance).Close(true);
		}
		if (Widgets.ButtonText(new Rect(rect.width / 2f - __instance.BottomButtonSize.x, rect2.y, __instance.BottomButtonSize.x, __instance.BottomButtonSize.y), "ResetButton".Translate()))
		{
			SoundDefOf.Tick_Low.PlayOneShotOnCamera();
			__instance.CalculateAndRecacheTransferables();
		}
		return false;
	}

	private static void SyncedTakeItems(Outpost outpost, Caravan caravan, Dictionary<ThingDef, int> itemsToTransfer)
	{
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		if (!CanExchange(outpost, caravan) || !ValidManifest(itemsToTransfer, outpost.Things))
		{
			return;
		}
		Dialog_TakeItems val = new Dialog_TakeItems(outpost, caravan);
		val.CalculateAndRecacheTransferables();
		foreach (TransferableOneWay transferable in val.transferables)
		{
			if (transferable.HasAnyThing && itemsToTransfer.TryGetValue(transferable.ThingDef, out var value))
			{
				transferable.ForceTo(value);
			}
			while (transferable.HasAnyThing && transferable.CountToTransfer > 0)
			{
				Thing thing = transferable.things.Pop();
				if (thing.stackCount <= transferable.CountToTransfer)
				{
					transferable.AdjustBy(-thing.stackCount);
					caravan.AddPawnOrItem(outpost.TakeItem(thing), addCarriedPawnToWorldPawnsIfAny: true);
				}
				else
				{
					caravan.AddPawnOrItem(thing.SplitOff(transferable.CountToTransfer), addCarriedPawnToWorldPawnsIfAny: true);
					transferable.AdjustTo(0);
					transferable.things.Add(thing);
				}
			}
		}
	}

	private static bool PreGiveItemsDoBottomButtons(Rect rect, Dialog_GiveItems __instance)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		Rect rect2 = new Rect(rect.width - __instance.BottomButtonSize.x, rect.height - 40f, __instance.BottomButtonSize.x, __instance.BottomButtonSize.y);
		if (Widgets.ButtonText(rect2, "Outposts.Give".Translate()))
		{
			Dictionary<ThingDef, int> itemsToTransfer = __instance.transferables.Where((TransferableOneWay x) => x.HasAnyThing && x.CountToTransfer > 0).ToDictionary((TransferableOneWay x) => x.ThingDef, (TransferableOneWay x) => x.CountToTransfer);
			SyncedGiveItems(__instance.outpost, __instance.caravan, itemsToTransfer);
			((Window)(object)__instance).Close(true);
		}
		if (Widgets.ButtonText(new Rect(0f, rect2.y, __instance.BottomButtonSize.x, __instance.BottomButtonSize.y), "CancelButton".Translate()))
		{
			((Window)(object)__instance).Close(true);
		}
		if (Widgets.ButtonText(new Rect(rect.width / 2f - __instance.BottomButtonSize.x, rect2.y, __instance.BottomButtonSize.x, __instance.BottomButtonSize.y), "ResetButton".Translate()))
		{
			SoundDefOf.Tick_Low.PlayOneShotOnCamera();
			__instance.CalculateAndRecacheTransferables();
		}
		return false;
	}

	private static void SyncedGiveItems(Outpost outpost, Caravan caravan, Dictionary<ThingDef, int> itemsToTransfer)
	{
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		if (!CanExchange(outpost, caravan) || !ValidManifest(itemsToTransfer, CaravanInventoryUtility.AllInventoryItems(caravan)))
		{
			return;
		}
		Dialog_GiveItems val = new Dialog_GiveItems(outpost, caravan);
		val.CalculateAndRecacheTransferables();
		foreach (TransferableOneWay transferable in val.transferables)
		{
			if (transferable.HasAnyThing && itemsToTransfer.TryGetValue(transferable.ThingDef, out var value))
			{
				transferable.ForceTo(value);
			}
			while (transferable.HasAnyThing && transferable.CountToTransfer > 0)
			{
				Thing thing = transferable.things.Pop();
				if (thing.stackCount <= transferable.CountToTransfer)
				{
					transferable.AdjustBy(-thing.stackCount);
					thing.holdingOwner?.Remove(thing);
					outpost.AddItem(thing);
				}
				else
				{
					outpost.AddItem(thing.SplitOff(transferable.CountToTransfer));
					transferable.AdjustTo(0);
					transferable.things.Add(thing);
				}
			}
		}
	}

	private static bool CanExchange(Outpost outpost, Caravan caravan)
	{
		if (outpost != null && caravan != null && !((WorldObject)(object)outpost).Destroyed && !caravan.Destroyed && ((WorldObject)(object)outpost).Spawned && caravan.Spawned && ((WorldObject)(object)outpost).Tile == caravan.Tile && CommandAuthorityBridge.CanActForFaction(((WorldObject)(object)outpost).Faction))
		{
			return CommandAuthorityBridge.CanActForFaction(caravan.Faction);
		}
		return false;
	}

	private static bool ValidManifest(Dictionary<ThingDef, int> requested, IEnumerable<Thing> available)
	{
		if (requested == null || requested.Count == 0 || requested.Any((KeyValuePair<ThingDef, int> pair) => pair.Key == null || pair.Value <= 0))
		{
			return false;
		}
		Dictionary<ThingDef, long> counts = (from thing in available
			where thing != null && !thing.Destroyed && thing.stackCount > 0
			group thing by thing.def).ToDictionary((IGrouping<ThingDef, Thing> group) => group.Key, (IGrouping<ThingDef, Thing> group) => ((IEnumerable<Thing>)group).Sum((Func<Thing, long>)((Thing thing) => thing.stackCount)));
		return requested.All((KeyValuePair<ThingDef, int> pair) => counts.TryGetValue(pair.Key, out var value) && pair.Value <= value);
	}

	private static void SyncWITabOutpostGear(SyncWorker sync, ref WITab_Outpost_Gear tab)
	{
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Expected Obj, but got Unknown
		if (sync.isWriting)
		{
			sync.Write(tab.draggedItem.thingIDNumber);
			return;
		}
		int id = sync.Read<int>();
		tab = new WITab_Outpost_Gear();
		tab.draggedItem = tab.SelOutpost.Things.FirstOrDefault((Thing t) => t.thingIDNumber == id);
	}

	private static void SyncedTryEquipDraggedItem(WITab_Outpost_Gear tab, int pawnId)
	{
		Pawn pawn = tab.SelOutpost.AllPawns.FirstOrDefault((Pawn p) => p.thingIDNumber == pawnId);
		if (pawn != null)
		{
			tab.TryEquipDraggedItem(pawn);
		}
	}

	private static bool PreTryEquipDraggedItem(WITab_Outpost_Gear __instance, Pawn p)
	{
		if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
		{
			return true;
		}
		if (p.apparel == null || !(__instance.draggedItem is Apparel))
		{
			return true;
		}
		SyncedTryEquipDraggedItem(__instance, p.thingIDNumber);
		return false;
	}

	private static bool SetupArgumentFromDraggedItem(ref ThingWithComps eq, Thing ___draggedItem)
	{
		if (!MP.IsInMultiplayer || !MP.IsExecutingSyncCommand)
		{
			return true;
		}
		if (!(___draggedItem is ThingWithComps thingWithComps))
		{
			return false;
		}
		eq = thingWithComps;
		return true;
	}

	private static void ClearDraggedItem(ref Thing ___draggedItem)
	{
		___draggedItem = null;
	}
}
