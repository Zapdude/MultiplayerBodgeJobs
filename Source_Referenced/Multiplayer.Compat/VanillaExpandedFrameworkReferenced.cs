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
		MpCompat.harmony.Patch(AccessTools.Method(typeof(Dialog_CreateCamp), "DoOutpostDisplay"), new HarmonyMethod(typeof(VanillaExpandedFrameworkReferenced), "PreDoOutpostDisplay"));
		MP.RegisterSyncMethod(typeof(VanillaExpandedFrameworkReferenced), "SyncedCreateOutpost");
		MpCompat.harmony.Patch(AccessTools.Method(typeof(Dialog_TakeItems), "DoBottomButtons"), new HarmonyMethod(typeof(VanillaExpandedFrameworkReferenced), "PreTakeItemsDoBottomButtons"));
		MP.RegisterSyncMethod(typeof(VanillaExpandedFrameworkReferenced), "SyncedTakeItems");
		MpCompat.harmony.Patch(AccessTools.Method(typeof(Dialog_GiveItems), "DoBottomButtons"), new HarmonyMethod(typeof(VanillaExpandedFrameworkReferenced), "PreGiveItemsDoBottomButtons"));
		MP.RegisterSyncMethod(typeof(VanillaExpandedFrameworkReferenced), "SyncedGiveItems");
		Extensions.SetDebugOnly(MpCompat.RegisterLambdaMethod(typeof(Outpost), "GetGizmos", new int[7] { 0, 1, 8, 9, 10, 11, 12 }).Reverse().Take(4));
		MpCompat.RegisterLambdaDelegate(typeof(Outpost), "GetGizmos", new int[1] { 8 });
		MethodInfo lambda = MpMethodUtil.GetLambda(typeof(Outpost), "GetGizmos", MethodType.Normal, (Type[])null, 4);
		AccessTools.FieldRef<object, Outpost> outpostsInnerClassThisField = AccessTools.FieldRefAccess<Outpost>(lambda.DeclaringType, "<>4__this");
		MP.RegisterSyncDelegate(typeof(Outpost), lambda.DeclaringType.Name, lambda.Name).TransformField("p", Serializer.New((Pawn p, object instance, object[] _) => (o: outpostsInnerClassThisField(instance), pawnId: p.thingIDNumber), ((Outpost o, int pawnId) tuple) => tuple.o.AllPawns.FirstOrDefault((Pawn p) => p.thingIDNumber == tuple.pawnId)));
		MP.RegisterSyncWorker<ResultOption>(SyncResultOption);
		MpCompat.RegisterLambdaDelegate(typeof(Outpost), "GetCaravanGizmos", new int[1] { 2 });
		MpCompat.RegisterLambdaDelegate("Outposts.Outpost_ChooseResult", "GetGizmos", new int[1] { 2 });
		MP.RegisterSyncWorker<WITab_Outpost_Gear>(SyncWITabOutpostGear);
		MethodInfo original = AccessTools.DeclaredMethod(typeof(WITab_Outpost_Gear), "TryEquipDraggedItem");
		MP.RegisterSyncMethod(MpMethodUtil.MethodOf((Delegate)new Action<WITab_Outpost_Gear, int>(SyncedTryEquipDraggedItem))).SetContext(SyncContext.WorldSelected);
		MpCompat.harmony.Patch(original, new HarmonyMethod(MpMethodUtil.MethodOf((Delegate)new Func<WITab_Outpost_Gear, Pawn, bool>(PreTryEquipDraggedItem))), null, null, new HarmonyMethod(MpMethodUtil.MethodOf((Delegate)new ClearDraggedItemDelegate(ClearDraggedItem))));
		original = AccessTools.DeclaredMethod(typeof(WITab_Outpost_Gear), "TryEquipDraggedItem_Equipment");
		MP.RegisterSyncMethod(original).SetContext(SyncContext.WorldSelected).TransformArgument(0, Serializer.New((Pawn p) => p.thingIDNumber, (int id) => ((Outpost)Find.WorldSelector.SingleSelectedObject).AllPawns.FirstOrDefault((Pawn p) => p.thingIDNumber == id)))
			.TransformArgument(1, Serializer.SimpleReader(() => (ThingWithComps)null));
		MpCompat.harmony.Patch(original, new HarmonyMethod(MpMethodUtil.MethodOf((Delegate)new SetupDraggedItemDelegate(SetupArgumentFromDraggedItem))), null, null, new HarmonyMethod(MpMethodUtil.MethodOf((Delegate)new ClearDraggedItemDelegate(ClearDraggedItem))));
		original = AccessTools.DeclaredMethod(typeof(WITab_Outpost_Gear), "MoveDraggedItemToInventory");
		MP.RegisterSyncMethod(original).SetContext(SyncContext.WorldSelected);
		MpCompat.harmony.Patch(original, null, null, null, new HarmonyMethod(MpMethodUtil.MethodOf((Delegate)new ClearDraggedItemDelegate(ClearDraggedItem))));
	}

	private static void SyncResultOption(SyncWorker sync, ref ResultOption option)
	{
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
				__instance.Close();
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
		Outpost outpost = (Outpost)WorldObjectMaker.MakeWorldObject(outpostDef);
		outpost.Name = NameGenerator.GenerateName(creator.Faction.def.settlementNameMaker, from o in Find.WorldObjects.AllWorldObjects.OfType<Outpost>()
			select o.Name);
		outpost.Tile = creator.Tile;
		outpost.SetFaction(creator.Faction);
		Find.WorldObjects.Add(outpost);
		foreach (Pawn item in creator.PawnsListForReading.ListFullCopy())
		{
			outpost.AddPawn(item);
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
			__instance.Close();
		}
		if (Widgets.ButtonText(new Rect(0f, rect2.y, __instance.BottomButtonSize.x, __instance.BottomButtonSize.y), "CancelButton".Translate()))
		{
			__instance.Close();
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
		Dialog_TakeItems dialog_TakeItems = new Dialog_TakeItems(outpost, caravan);
		dialog_TakeItems.CalculateAndRecacheTransferables();
		foreach (TransferableOneWay transferable in dialog_TakeItems.transferables)
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
			__instance.Close();
		}
		if (Widgets.ButtonText(new Rect(0f, rect2.y, __instance.BottomButtonSize.x, __instance.BottomButtonSize.y), "CancelButton".Translate()))
		{
			__instance.Close();
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
		Dialog_GiveItems dialog_GiveItems = new Dialog_GiveItems(outpost, caravan);
		dialog_GiveItems.CalculateAndRecacheTransferables();
		foreach (TransferableOneWay transferable in dialog_GiveItems.transferables)
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

	private static void SyncWITabOutpostGear(SyncWorker sync, ref WITab_Outpost_Gear tab)
	{
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

	// The donor delegates were compiler-generated (<>A{00000001} and <>F{00000001}); these are
	// their equivalent signatures.
	private delegate void ClearDraggedItemDelegate(ref Thing draggedItem);

	private delegate bool SetupDraggedItemDelegate(ref ThingWithComps eq, Thing draggedItem);
}
