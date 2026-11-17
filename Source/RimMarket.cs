using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Multiplayer.Compat;

[MpCompatFor("forti.rimmarket")]
public class RimMarket
{
	internal sealed class MarketScope : IDisposable
	{
		internal readonly Faction Owner;

		internal readonly Map Map;

		private readonly object manager;

		internal readonly MarketScope previous;

		private readonly IDisposable factionScope;

		internal readonly Dictionary<string, (IList all, IList view, HashSet<object> selected)> views = new Dictionary<string, (IList, IList, HashSet<object>)>();

		private readonly RimMarketAccount account;

		private readonly RimMarketAccount restoreAccount;

		private readonly PatchingUtilities.TimeSnapshot? readClock;

		private readonly bool readOnly;

		private bool enteredRead;

		private bool disposed;

		internal MarketScope(Faction owner, Map map, bool read = false, bool worldRead = false)
		{
			Owner = owner;
			Map = map;
			manager = Manager;
			previous = Active;
			readOnly = read || MP.InInterface || (previous?.readOnly ?? false);
			if (read)
			{
				readClock = ((worldRead || map == null) ? PatchingUtilities.TimeSnapshot.GetAndSetFromWorld() : PatchingUtilities.TimeSnapshot.GetAndSetFromMap(map));
			}
			try
			{
				RimMarketState state = State;
				if (!readOnly)
				{
					state.EnsurePrimary(owner, manager);
				}
				RimMarketAccount primary = state.Primary;
				if (previous == null)
				{
					restoreAccount = (readOnly ? new RimMarketAccount(primary?.owner ?? RimMarketState.PrimaryOwner(owner), primary: true) : primary);
					restoreAccount.Capture(manager);
				}
				else
				{
					restoreAccount = previous.account;
					if (!previous.readOnly)
					{
						restoreAccount.Capture(manager);
					}
					previous.UnmountViews();
				}
				if (readOnly)
				{
					readDepth++;
					enteredRead = true;
				}
				account = ((previous?.Owner == owner) ? restoreAccount : ((previous == null && restoreAccount.owner == owner) ? restoreAccount : state.Account(owner, !readOnly)));
				account.Apply(manager);
				factionScope = CommandAuthorityBridge.EnterFaction(owner);
				string[] sharedLists = SharedLists;
				foreach (string text in sharedLists)
				{
					IList list = List(manager, text);
					IList list2 = (IList)Activator.CreateInstance(list.GetType());
					HashSet<object> hashSet = new HashSet<object>();
					foreach (object item in list)
					{
						if (Belongs(text, item))
						{
							list2.Add(item);
							hashSet.Add(item);
						}
					}
					views[text] = (list, list2, hashSet);
					Fields[text].SetValue(manager, list2);
				}
				Active = this;
			}
			catch
			{
				try
				{
					foreach (KeyValuePair<string, (IList, IList, HashSet<object>)> view in views)
					{
						Fields[view.Key].SetValue(manager, view.Value.Item1);
					}
					restoreAccount?.Apply(manager);
					previous?.ResumeViews();
				}
				finally
				{
					ExitContext();
				}
				throw;
			}
		}

		public void Dispose()
		{
			if (disposed)
			{
				return;
			}
			disposed = true;
			try
			{
				if (!readOnly)
				{
					account.Capture(manager);
				}
				UnmountViews();
				restoreAccount.Apply(manager);
				previous?.ResumeViews();
			}
			finally
			{
				Active = previous;
				ExitContext();
			}
		}

		private void ExitContext()
		{
			if (enteredRead)
			{
				readDepth--;
			}
			try
			{
				factionScope?.Dispose();
			}
			finally
			{
				readClock?.Set();
			}
		}

		private bool Belongs(string name, object item)
		{
			bool flag = RecordOwner(item) == Owner;
			if (flag)
			{
				bool flag2 = Map == null;
				if (!flag2)
				{
					bool flag3;
					switch (name)
					{
					case "routes":
					case "orders":
					case "awayStalls":
						flag3 = true;
						break;
					default:
						flag3 = false;
						break;
					}
					flag2 = flag3;
				}
				flag = flag2 || RecordMap(item) == Map;
			}
			return flag;
		}

		private void UnmountViews()
		{
			foreach (KeyValuePair<string, (IList, IList, HashSet<object>)> view in views)
			{
				var (list, list2, hashSet) = view.Value;
				if (!readOnly)
				{
					for (int num = list.Count - 1; num >= 0; num--)
					{
						if (hashSet.Contains(list[num]) && !list2.Contains(list[num]))
						{
							list.RemoveAt(num);
						}
					}
					foreach (object item in list2)
					{
						if (!list.Contains(item))
						{
							list.Add(item);
						}
						RememberOwner(item, Owner);
					}
				}
				Fields[view.Key].SetValue(manager, list);
			}
		}

		private void ResumeViews()
		{
			foreach (KeyValuePair<string, (IList, IList, HashSet<object>)> pair in views)
			{
				(IList, IList, HashSet<object>) value = pair.Value;
				IList item = value.Item1;
				IList item2 = value.Item2;
				HashSet<object> item3 = value.Item3;
				object[] array = (from object item5 in item
					where Belongs(pair.Key, item5)
					select item5).ToArray();
				object[] array2;
				if (!item2.Cast<object>().SequenceEqual(array))
				{
					item2.Clear();
					array2 = array;
					foreach (object value2 in array2)
					{
						item2.Add(value2);
					}
				}
				item3.Clear();
				array2 = array;
				foreach (object item4 in array2)
				{
					item3.Add(item4);
				}
				Fields[pair.Key].SetValue(manager, item2);
			}
		}
	}

	private sealed class MarketRadioDialog : Dialog_NodeTree
	{
		internal readonly Pawn pawn;

		internal readonly Faction faction;

		private readonly Map map;

		private readonly Game game = Current.Game;

		private readonly int epoch = localEpoch;

		internal MarketRadioDialog(Pawn pawn, Faction faction, Map map)
			: base(MarketRadioRoot(pawn, faction), delayInteractivity: false, radioMode: false, "RimMarket.DealsTitle".Translate())
		{
			this.pawn = pawn;
			this.faction = faction;
			this.map = map;
			forcePause = false;
			doCloseX = true;
			closeOnCancel = true;
		}

		public override void DoWindowContents(Rect inRect)
		{
			if (Current.Game != game || localEpoch != epoch || !CanOpenRadioMarket(pawn, faction, map))
			{
				Close(doCloseSound: false);
				return;
			}
			using (SessionServices.EnterLocalOnly())
			{
				using (new MarketScope(pawn.Faction, map, read: true, worldRead: true))
				{
					MarketRadioDialog drawingMarketRadio = RimMarket.drawingMarketRadio;
					ref bool reference = ref syncNodeTreeOpen();
					bool flag = reference;
					RimMarket.drawingMarketRadio = this;
					reference = false;
					try
					{
						base.DoWindowContents(inRect);
					}
					finally
					{
						reference = flag;
						RimMarket.drawingMarketRadio = drawingMarketRadio;
					}
				}
			}
		}
	}

	internal sealed class SettingsSnapshot : IExposable
	{
		internal float[] floats = new float[FloatSettings.Length];

		internal int[] ints = new int[IntSettings.Length];

		internal bool[] bools = new bool[BoolSettings.Length];

		internal bool Valid
		{
			get
			{
				if (floats?.Length == FloatSettings.Length && ints?.Length == IntSettings.Length && bools?.Length == BoolSettings.Length && floats.All((float f) => float.IsFinite(f) && f >= 0f && f <= 100000f) && ints.All((int i) => i >= 0 && i <= 1000000) && ints[3] <= 2 && ints[6] <= 3)
				{
					return ints[9] <= 2;
				}
				return false;
			}
		}

		internal bool Same(SettingsSnapshot other)
		{
			if (floats.SequenceEqual(other.floats) && ints.SequenceEqual(other.ints))
			{
				return bools.SequenceEqual(other.bools);
			}
			return false;
		}

		public void ExposeData()
		{
			for (int i = 0; i < floats.Length; i++)
			{
				Scribe_Values.Look(ref floats[i], FloatSettings[i], 0f, forceSave: true);
			}
			for (int j = 0; j < ints.Length; j++)
			{
				Scribe_Values.Look(ref ints[j], IntSettings[j], (j == 9) ? 1 : 0, forceSave: true);
			}
			for (int k = 0; k < bools.Length; k++)
			{
				Scribe_Values.Look(ref bools[k], BoolSettings[k], defaultValue: false, forceSave: true);
			}
		}
	}

	private static class BattlePresentationPatches
	{
		internal static void Before(Caravan caravan, out Caravan __state)
		{
			__state = battleCaravan;
			if (Enabled)
			{
				battleCaravan = caravan;
			}
		}

		internal static Exception After(Exception __exception, Caravan __state)
		{
			battleCaravan = __state;
			return __exception;
		}
	}

	private sealed class WindowOwner
	{
		internal Map map;

		internal Faction owner;

		internal Faction actor;

		internal Game game;

		internal HashSet<ThingDef> paintedOrders;
	}

	internal static bool Installed;

	internal static Type ManagerType;

	internal static readonly Dictionary<string, FieldInfo> Fields = new Dictionary<string, FieldInfo>();

	private static readonly Dictionary<string, Type> Types = new Dictionary<string, Type>();

	private static readonly Dictionary<(Type, string), MethodInfo> Methods = new Dictionary<(Type, string), MethodInfo>();

	[ThreadStatic]
	internal static MarketScope Active;

	[ThreadStatic]
	internal static Map TickMap;

	[ThreadStatic]
	private static bool dispatchingMap;

	[ThreadStatic]
	private static int readDepth;

	private static FieldInfo multiplayerGame;

	private static FieldInfo worldOwnerField;

	private static FieldInfo tickingMapField;

	private static MethodInfo mapTime;

	private static MethodInfo mapPre;

	private static MethodInfo mapPost;

	private static MethodInfo worldPre;

	private static MethodInfo worldPost;

	private static MethodInfo ownerReadTicks;

	private static Func<Faction> realLocalFaction;

	private static readonly string[] SharedLists = new string[8] { "visits", "thefts", "auctions", "routes", "orders", "awayStalls", "fairs", "leaving" };

	private const float MarketRadioFooter = 38f;

	[ThreadStatic]
	private static MarketRadioDialog drawingMarketRadio;

	private static AccessTools.FieldRef<Dialog_Negotiation, Pawn> radioNegotiator;

	private static AccessTools.FieldRef<Dialog_Negotiation, ICommunicable> radioTarget;

	private static AccessTools.FieldRef<bool> syncNodeTreeOpen;

	private const string SettingsKey = "forti.rimmarket.settings";

	private const int ThiefReactionIndex = 9;

	private static readonly string[] FloatSettings = new string[10] { "bargainEffect", "colonyShopperDays", "extraCaravans", "feePercent", "haggleBonus", "marketDays", "routeOpenFactor", "routeTollFactor", "sharePercent", "thiefMtbDays" };

	private static readonly string[] IntSettings = new string[10] { "fairCooldownDays", "fairCost", "feeCap", "feeMode", "guardsAtStall", "innPrice", "marketDifficulty", "maxGuards", "maxMerchants", "thiefReaction" };

	private static readonly string[] BoolSettings = new string[20]
	{
		"awayStalls", "colonistsBrowse", "fairEnabled", "feeInGoods", "guestsShop", "haggleEnabled", "hegemonyGoodwill", "hegemonyPrices", "hegemonyStock", "hegemonyTerritory",
		"marketSaturation", "reputationEffects", "shopkeepers", "shoppersVisit", "slaveAuctions", "specialOrders", "thievesEnabled", "tradeRoutes", "turnAwayIfBroke", "waiveFeeAfterToll"
	};

	[ThreadStatic]
	private static Caravan battleCaravan;

	private static Func<bool> initializingMarketCaches;

	private static Action pauseSimulation;

	private static ConditionalWeakTable<object, WindowOwner> windows = new ConditionalWeakTable<object, WindowOwner>();

	private static int localEpoch;

	internal static object Manager => Current.Game?.components?.FirstOrDefault((GameComponent c) => ManagerType?.IsInstanceOfType(c) ?? false);

	internal static RimMarketState State => Current.Game?.GetComponent<RimMarketState>();

	internal static bool Enabled
	{
		get
		{
			if (Installed && MP.IsInMultiplayer && Current.Game != null)
			{
				return Manager != null;
			}
			return false;
		}
	}

	private static bool Intercept
	{
		get
		{
			if (Enabled && MP.InInterface)
			{
				return !MP.IsExecutingSyncCommand;
			}
			return false;
		}
	}

	private static Faction LocalOwner
	{
		get
		{
			Faction faction = realLocalFaction?.Invoke() ?? Faction.OfPlayer;
			if (faction == null || !faction.IsPlayer)
			{
				return null;
			}
			return faction;
		}
	}

	private static object Settings => PatchingUtilities.Get(T("RimMarketMod"), "Settings");

	private static ThingComp Comp(Thing thing, string type)
	{
		return (thing as ThingWithComps)?.AllComps.FirstOrDefault((ThingComp c) => T(type).IsInstanceOfType(c));
	}

	private static bool ColorValid(Color c)
	{
		if (float.IsFinite(c.r) && float.IsFinite(c.g) && float.IsFinite(c.b) && float.IsFinite(c.a))
		{
			float r = c.r;
			if (r >= 0f && r <= 1f)
			{
				r = c.g;
				if (r >= 0f && r <= 1f)
				{
					r = c.b;
					if (r >= 0f && r <= 1f)
					{
						r = c.a;
						if (r >= 0f)
						{
							return r <= 1f;
						}
						return false;
					}
				}
			}
		}
		return false;
	}

	private static void SyncObject(Thing thing, int action, bool on, int value)
	{
		if (!Enabled || !OwnedBuilding(thing))
		{
			return;
		}
		using (Simulation(thing.Map))
		{
			using (new MarketScope(thing.Faction, thing.Map))
			{
				switch (action)
				{
				case 0:
					if (thing.GetType() == T("Buildings.Building_MarketMarker"))
					{
						PatchingUtilities.Call(thing, "SetOpen", on);
					}
					break;
				case 1:
					if (thing.GetType() == T("Buildings.Building_MarketMarker") && value >= 0 && value <= 2)
					{
						PatchingUtilities.Set(thing, "feeMode", value);
					}
					break;
				case 2:
					if (thing.GetType() == T("Buildings.Building_MarketMarker"))
					{
						PatchingUtilities.Set(thing, "markerHidden", on);
						thing.DirtyMapMesh(thing.Map);
					}
					break;
				case 3:
					if (thing.GetType() == T("Buildings.Building_MarketSpot"))
					{
						PatchingUtilities.Set(thing, "spotHidden", on);
						thing.DirtyMapMesh(thing.Map);
					}
					break;
				case 4:
					if (thing.GetType() == T("Buildings.Building_MarketStall"))
					{
						PatchingUtilities.Call(thing, "SetColonyStall", on);
					}
					break;
				case 5:
				{
					ThingComp thingComp2 = Comp(thing, "Buildings.CompMarketStall");
					if (thingComp2 != null)
					{
						PatchingUtilities.Call(thingComp2, "SwapSide");
					}
					break;
				}
				case 6:
				{
					ThingComp thingComp = Comp(thing, "Inn.CompInnBed");
					if (thingComp != null)
					{
						PatchingUtilities.Call(thingComp, "SetInn", on);
					}
					break;
				}
				case 8:
					if (thing.GetType() == T("Buildings.Building_MarketMarker") && ((AcceptanceReport)PatchingUtilities.Call(T("Fair.FairUtility"), "CanAnnounce", thing)).Accepted)
					{
						PatchingUtilities.Call(T("Fair.FairUtility"), "Announce", thing);
					}
					break;
				case 9:
					if (thing.GetType() == T("Buildings.Building_MarketMarker"))
					{
						PatchingUtilities.Call(Manager, "CloseMarket", thing);
					}
					break;
				case 7:
					break;
				}
			}
		}
	}

	private static void SyncRoof(Thing stall, int shape, int pattern, Color canvas, Color stripe)
	{
		bool flag = !Enabled || !OwnedBuilding(stall) || stall.GetType() != T("Buildings.Building_MarketStall");
		if (!flag)
		{
			bool flag2 = ((shape < 1 || shape > 22) ? true : false);
			flag = flag2;
		}
		bool flag3 = flag;
		if (!flag3)
		{
			bool flag2 = ((pattern < 0 || pattern > 12) ? true : false);
			flag3 = flag2;
		}
		if (flag3 || !ColorValid(canvas) || !ColorValid(stripe))
		{
			return;
		}
		using (Simulation(stall.Map))
		{
			using (new MarketScope(stall.Faction, stall.Map))
			{
				PatchingUtilities.Call(stall, "SetRoof", shape, pattern, canvas, stripe);
			}
		}
	}

	private static void SyncOrder(Thing stall, ThingDef def, int count, float price, int min, int max, bool refill, int reserve)
	{
		bool flag = !Enabled || !OwnedBuilding(stall) || stall.GetType() != T("Buildings.Building_MarketStall") || def == null;
		if (!flag)
		{
			bool flag2 = ((count < 0 || count > 1000000) ? true : false);
			flag = flag2;
		}
		bool flag3 = flag || !float.IsFinite(price);
		if (!flag3)
		{
			bool flag2 = ((price < 0.5f || price > 3f) ? true : false);
			flag3 = flag2;
		}
		bool flag4 = flag3 || min < 0 || max > 6 || min > max;
		if (!flag4)
		{
			bool flag2 = ((reserve < 0 || reserve > 99999) ? true : false);
			flag4 = flag2;
		}
		if (flag4 || !(bool)PatchingUtilities.Get(stall, "ColonyStall"))
		{
			return;
		}
		using (Simulation(stall.Map))
		{
			using (new MarketScope(stall.Faction, stall.Map))
			{
				object obj = PatchingUtilities.Call(stall, "OrderFor", def);
				if (obj != null)
				{
					price = (float)Field(obj, "priceFactor");
					QualityRange qualityRange = (QualityRange)Field(obj, "quality");
					min = (int)qualityRange.min;
					max = (int)qualityRange.max;
					refill = (bool)Field(obj, "refill");
					reserve = Number(obj, "reserve");
				}
				PatchingUtilities.Call(stall, "SetOrder", def, count, price, new QualityRange((QualityCategory)min, (QualityCategory)max), refill, reserve);
			}
		}
	}

	private static void SyncPaint(Thing marker, IntVec3[] cells, bool add)
	{
		if (!Enabled || !OwnedBuilding(marker) || cells == null || cells.Length > 8192)
		{
			return;
		}
		ThingComp thingComp = Comp(marker, "Buildings.CompMarketQuarter");
		if (thingComp == null)
		{
			return;
		}
		using (Simulation(marker.Map))
		{
			using (new MarketScope(marker.Faction, marker.Map))
			{
				Designator designator = (Designator)Activator.CreateInstance(T("Buildings.Designator_MarketArea"), thingComp, add);
				List<IntVec3> list = (from c in cells.Distinct()
					where c.InBounds(marker.Map) && designator.CanDesignateCell(c).Accepted
					orderby marker.Map.cellIndices.CellToIndex(c)
					select c).ToList();
				if (list.Count > 0)
				{
					PatchingUtilities.Call(thingComp, "Paint", list, add);
				}
			}
		}
	}

	private static void SyncStallField(Thing stall, ThingDef def, int action, float price, int min, int max, bool refill, int reserve)
	{
		bool flag = !Enabled || !OwnedBuilding(stall) || stall.GetType() != T("Buildings.Building_MarketStall") || def == null;
		if (!flag)
		{
			bool flag2 = ((action < 0 || action > 3) ? true : false);
			flag = flag2;
		}
		bool flag3 = flag || !float.IsFinite(price);
		if (!flag3)
		{
			bool flag2 = ((price < 0.5f || price > 3f) ? true : false);
			flag3 = flag2;
		}
		bool flag4 = flag3 || min < 0 || max > 6 || min > max;
		if (!flag4)
		{
			bool flag2 = ((reserve < 0 || reserve > 99999) ? true : false);
			flag4 = flag2;
		}
		if (flag4 || !(bool)PatchingUtilities.Get(stall, "ColonyStall"))
		{
			return;
		}
		using (Simulation(stall.Map))
		{
			using (new MarketScope(stall.Faction, stall.Map))
			{
				object obj = PatchingUtilities.Call(stall, "OrderFor", def);
				if (obj != null)
				{
					switch (action)
					{
					case 0:
						PatchingUtilities.Set(obj, "priceFactor", price);
						return;
					case 1:
						PatchingUtilities.Set(obj, "refill", refill);
						return;
					case 2:
						PatchingUtilities.Set(obj, "reserve", reserve);
						return;
					}
					PatchingUtilities.Call(stall, "SetOrder", def, Number(obj, "toSell"), (float)Field(obj, "priceFactor"), new QualityRange((QualityCategory)min, (QualityCategory)max), (bool)Field(obj, "refill"), Number(obj, "reserve"));
				}
			}
		}
	}

	private static void SyncPolicy(Faction owner, Faction npc, int action, int tier, int price, bool invited)
	{
		if (!Enabled || owner == null || !owner.IsPlayer || !CommandAuthorityBridge.CanActForFaction(owner))
		{
			return;
		}
		using (Simulation(null))
		{
			using (new MarketScope(owner, null))
			{
				if (action == 0 && tier >= 0 && tier < 3 && price >= -1 && price <= 500)
				{
					PatchingUtilities.Call(Manager, "SetInnPrice", tier, price);
				}
				else if (action == 1 && npc != null && !npc.IsPlayer)
				{
					PatchingUtilities.Call(Manager, "SetFairInvited", npc, invited);
				}
			}
		}
	}

	private static void SyncDecision(Faction owner, Map map, int action, int id, int expected, int value, Pawn negotiator)
	{
		if (action == 0)
		{
			if (!Enabled || negotiator?.Faction != owner || negotiator.Map != map || negotiator.Dead || negotiator.Downed || !negotiator.Spawned || !CommandAuthorityBridge.CanActForPawn(negotiator))
			{
				return;
			}
			object obj = GlobalList("visits").Cast<object>().FirstOrDefault((object v) => Number(v, "id") == id);
			Faction faction = RecordOwner(obj);
			if (obj == null || faction == null || !faction.IsPlayer || RecordMap(obj) != map || (bool)Field(obj, "haggleUsed") || (bool)Field(obj, "closed"))
			{
				return;
			}
			using (Simulation(map))
			{
				using (new MarketScope(faction, map))
				{
					using (CommandAuthorityBridge.EnterFaction(negotiator.Faction))
					{
						PatchingUtilities.Call(Manager, "TryHaggle", obj, negotiator);
						return;
					}
				}
			}
		}
		if (!Enabled || owner == null || !owner.IsPlayer || !CommandAuthorityBridge.CanActForFaction(owner))
		{
			return;
		}
		bool flag = (uint)action <= 1u;
		using (Simulation(flag ? map : null))
		{
			flag = (uint)action <= 1u;
			using (new MarketScope(owner, flag ? map : null))
			{
				switch (action)
				{
				case 1:
				{
					object obj2 = List(Manager, "auctions").Cast<object>().FirstOrDefault((object a) => Number(a, "visitId") == id);
					if (obj2 != null)
					{
						Pawn obj3 = Field(obj2, "lot") as Pawn;
						if (obj3 != null && obj3.thingIDNumber == expected && Number(obj2, "price") == value)
						{
							PatchingUtilities.Call(Manager, "PlayerBid", obj2);
						}
					}
					return;
				}
				case 2:
				case 3:
				case 4:
					flag = true;
					break;
				default:
					flag = false;
					break;
				}
				if (flag)
				{
					if (State.Account(owner).noticeRevision != expected)
					{
						return;
					}
					object value2 = Fields["notice"].GetValue(Manager);
					if (value2 == null)
					{
						return;
					}
					Map map2 = RecordMap(value2);
					if (action == 2)
					{
						PatchingUtilities.Call(T("Orders.WantedUtility"), "Withdraw");
					}
					else
					{
						if (map2 == null || map2 != map)
						{
							return;
						}
						object obj4 = ((IList)Field(value2, "offers")).Cast<object>().FirstOrDefault((object o) => Number(o, "id") == id);
						if (obj4 == null || Number(obj4, "expiresTick") <= ClockTicks(null))
						{
							return;
						}
						if (action == 3)
						{
							if (Field(obj4, "faction") is Faction { defeated: false } faction2 && Field(obj4, "settlement") is Settlement { Spawned: not false, Destroyed: false } settlement && settlement.Faction == faction2 && PatchingUtilities.Call(T("Orders.SpecialOrderUtility"), "KindFor", faction2, map, null) != null)
							{
								PatchingUtilities.Call(T("Orders.WantedUtility"), "Accept", obj4, map);
							}
						}
						else if (value >= 0 && value < 3 && ValidMarketNegotiator(negotiator, map, owner) && !(bool)Field(obj4, "haggled"))
						{
							PatchingUtilities.Call(T("Orders.WantedUtility"), "Haggle", obj4, map, negotiator, value);
						}
					}
				}
				else if (action == 5)
				{
					object obj5 = List(Manager, "routes").Cast<object>().FirstOrDefault((object r) => Number(r, "id") == id);
					if (obj5 != null)
					{
						PatchingUtilities.Call(T("Routes.TradeRouteUtility"), "Close", obj5, null);
					}
				}
			}
		}
	}

	private static void SyncWanted(Map map, ThingDef[] defs, int[] counts)
	{
		Faction faction = MapOwner(map);
		bool flag = !Enabled || faction == null || !CommandAuthorityBridge.CanActForFaction(faction) || defs == null || counts == null;
		if (!flag)
		{
			int num = defs.Length;
			bool flag2 = ((num < 1 || num > 10) ? true : false);
			flag = flag2;
		}
		if (flag || counts.Length != defs.Length || defs.Any((ThingDef d) => d == null) || defs.Distinct().Count() != defs.Length || counts.Any((int c) => (c < 1 || c > 100000) ? true : false))
		{
			return;
		}
		using (Simulation(null))
		{
			using (new MarketScope(faction, null))
			{
				if (PatchingUtilities.Call(T("Orders.WantedUtility"), "CannotPost", map) != null)
				{
					return;
				}
				IList candidates = (IList)PatchingUtilities.Call(T("Orders.SpecialOrderUtility"), "Candidates", null, null);
				if (!defs.Any((ThingDef d) => !candidates.Contains(d)) && !(defs.Select((ThingDef d, int i) => (double)(float)PatchingUtilities.Call(T("Orders.SpecialOrderUtility"), "UnitValue", d) * (double)counts[i]).Sum() > 25000.0))
				{
					List<ThingDefCountClass> list = defs.Select((ThingDef d, int i) => new ThingDefCountClass(d, counts[i])).ToList();
					PatchingUtilities.Call(T("Orders.WantedUtility"), "Post", map, list);
				}
			}
		}
	}

	private static void SyncPurchase(Map map, Faction npc, Settlement settlement, TraderKindDef kind, int days, bool escort, string familyKey, bool large, Pawn negotiator)
	{
		Faction faction = MapOwner(map);
		if (!Enabled || faction == null || map.Disposed || !Find.Maps.Contains(map) || !ValidMarketNegotiator(negotiator, map, faction) || npc == null || npc.IsPlayer || npc.defeated || settlement == null || !settlement.Spawned || settlement.Destroyed || settlement.Faction != npc || !(bool)PatchingUtilities.Get(Settings, (familyKey == null) ? "tradeRoutes" : "specialOrders"))
		{
			return;
		}
		using (Simulation(null))
		{
			using (new MarketScope(faction, null))
			{
				if (familyKey == null && !CanOpenRoute(map, npc, settlement, kind, days, negotiator))
				{
					return;
				}
				object[] array = new object[4] { settlement.Tile, map.Tile, null, 0 };
				if (!(bool)M("Routes.TradeRouteUtility", "TryPath").Invoke(null, array))
				{
					return;
				}
				int num = ((IList)array[2]).Count - 1;
				if (familyKey == null)
				{
					int num2 = (int)PatchingUtilities.Call(T("Routes.TradeRouteUtility"), "OpenPrice", num);
					int num3 = (int)PatchingUtilities.Call(T("Routes.TradeRouteUtility"), "Toll", num, escort);
					if ((bool)PatchingUtilities.Call(T("Auction.AuctionUtility"), "TakeColonySilver", map, num2))
					{
						PatchingUtilities.Call(T("Routes.TradeRouteUtility"), "Open", npc, settlement, map, kind, days, escort, num3);
					}
					return;
				}
				object obj = PatchingUtilities.Call(T("Orders.SpecialOrderUtility"), "FamilyByKey", familyKey);
				if (obj != null && PatchingUtilities.Call(T("Orders.SpecialOrderUtility"), "CannotOrder", Manager, npc, map) == null && ((IList)PatchingUtilities.Call(T("Orders.SpecialOrderUtility"), "Candidates", obj, npc)).Count != 0)
				{
					int num4 = (int)PatchingUtilities.Call(T("Orders.SpecialOrderUtility"), "FamilyPrice", num, large);
					if ((bool)PatchingUtilities.Call(T("Auction.AuctionUtility"), "TakeColonySilver", map, num4))
					{
						PatchingUtilities.Call(T("Orders.SpecialOrderUtility"), "Place", npc, settlement, map, obj, large, null, num4);
					}
				}
			}
		}
	}

	private static bool ValidMarketNegotiator(Pawn pawn, Map map, Faction owner)
	{
		if (pawn != null && pawn.Spawned && !pawn.Destroyed && !pawn.Dead && !pawn.Downed && !pawn.InMentalState && pawn.Map == map && pawn.Faction == owner && CommandAuthorityBridge.CanActForPawn(pawn))
		{
			Pawn_SkillTracker skills = pawn.skills;
			if (skills == null)
			{
				return true;
			}
			return skills.GetSkill(SkillDefOf.Social)?.TotallyDisabled != true;
		}
		return false;
	}

	private static bool CanOpenRoute(Map map, Faction npc, Settlement settlement, TraderKindDef kind, int days, Pawn negotiator)
	{
		Type target = T("Routes.TradeRouteUtility");
		bool flag = ((days == 7 || days == 14) ? true : false);
		if (flag && kind != null && PatchingUtilities.Call(target, "RouteFrom", Manager, settlement) == null && PatchingUtilities.Call(target, "CannotOpen", Manager, npc, map) == null)
		{
			return ((IList)PatchingUtilities.Call(target, "KindsFor", npc, map, negotiator)).Contains(kind);
		}
		return false;
	}

	private static void SyncAway(Caravan caravan, int action, Settlement settlement, ThingDef def, bool keep)
	{
		if (!Enabled || caravan == null || !caravan.Spawned || caravan.Destroyed)
		{
			return;
		}
		Faction faction = caravan.Faction;
		if (faction == null || !faction.IsPlayer || !CommandAuthorityBridge.CanActForFaction(caravan.Faction))
		{
			return;
		}
		using (Simulation(null))
		{
			using (new MarketScope(caravan.Faction, null))
			{
				object obj = PatchingUtilities.Call(Manager, "AwayStallFor", caravan);
				if (action == 0 && obj == null && settlement != null && settlement.Spawned && !settlement.Destroyed)
				{
					PatchingUtilities.Call(Manager, "BeginAwayStall", caravan, settlement);
				}
				else if (action == 1 && obj != null)
				{
					PatchingUtilities.Call(Manager, "EndAwayStall", obj, 1);
				}
				else
				{
					if (action != 2 || obj == null || def == null)
					{
						return;
					}
					List<ThingDef> list = (List<ThingDef>)Field(obj, "keep");
					if (keep)
					{
						if (!list.Contains(def))
						{
							list.Add(def);
						}
					}
					else
					{
						list.Remove(def);
					}
				}
			}
		}
	}

	private static void SyncTackle(Pawn guard, Pawn thief)
	{
		if (!Enabled || guard?.Map == null || thief?.Map != guard.Map || guard.Dead || thief.Dead || !CommandAuthorityBridge.CanActForPawn(guard))
		{
			return;
		}
		using (Simulation(guard.Map))
		{
			using (new MarketScope(guard.Faction, guard.Map))
			{
				PatchingUtilities.Call(T("Thieves.ThiefUtility"), "TryTackle", guard, thief);
			}
		}
	}

	public RimMarket(ModContentPack mod)
	{
		ManagerType = T("Market.MarketManager");
		foreach (FieldInfo declaredField in AccessTools.GetDeclaredFields(ManagerType))
		{
			Fields[declaredField.Name] = declaredField;
		}
		CommandAuthorityBridge.Install();
		SessionServices.Install();
		PatchingUtilities.SetupAsyncTime();
		InstallClock();
		InstallSettings();
		InstallUI();
		InstallSimulation();
		SessionServices.RegisterLifecycle("forti.rimmarket.ui", null, ResetLocal);
		Installed = true;
	}

	internal static Type T(string name)
	{
		if (!Types.TryGetValue(name, out var value))
		{
			Dictionary<string, Type> types = Types;
			Type type = AccessTools.TypeByName("RimMarket." + name) ?? throw new TypeLoadException("RimMarket." + name);
			value = type;
			types[name] = type;
		}
		return value;
	}

	private static MethodInfo M(string type, string method)
	{
		return M(T(type), method);
	}

	private static MethodInfo M(Type type, string method)
	{
		if (!Methods.TryGetValue((type, method), out var value))
		{
			Dictionary<(Type, string), MethodInfo> methods = Methods;
			(Type, string) key = (type, method);
			MethodInfo methodInfo = AccessTools.DeclaredMethod(type, method) ?? throw new MissingMethodException(type.FullName, method);
			value = methodInfo;
			methods[key] = methodInfo;
		}
		return value;
	}

	private static void Patch(string type, string method, string prefix = null, string postfix = null, string finalizer = null, string transpiler = null)
	{
		Patch(M(type, method), prefix, postfix, finalizer, transpiler);
	}

	private static void Patch(MethodBase target, string prefix = null, string postfix = null, string finalizer = null, string transpiler = null)
	{
		MpCompat.harmony.Patch(target, Hook(prefix), Hook(postfix), finalizer: Hook(finalizer), transpiler: Hook(transpiler));
		static HarmonyMethod Hook(string name)
		{
			if (name != null)
			{
				return new HarmonyMethod(typeof(RimMarket), name);
			}
			return null;
		}
	}

	internal static object Field(object instance, string name)
	{
		if (instance != null)
		{
			return AccessTools.Field(instance.GetType(), name)?.GetValue(instance);
		}
		return null;
	}

	internal static int Number(object instance, string name)
	{
		object obj = Field(instance, name);
		if (obj is int)
		{
			return (int)obj;
		}
		return 0;
	}

	internal static IList List(object manager, string name)
	{
		return (IList)Fields[name].GetValue(manager);
	}

	internal static Faction MapOwner(Map map)
	{
		Faction faction = map?.Parent?.Faction;
		if (faction == null || !faction.IsPlayer)
		{
			return null;
		}
		return faction;
	}

	internal static Map RecordMap(object value)
	{
		if (value is Map result)
		{
			return result;
		}
		if (value is Thing thing)
		{
			return thing.MapHeld;
		}
		if (value is ThingComp { parent: var parent })
		{
			return parent?.MapHeld;
		}
		if (value is Lord lord)
		{
			return lord.Map;
		}
		if (value is MapParent mapParent)
		{
			return mapParent.Map;
		}
		if (value == null)
		{
			return null;
		}
		object obj = Field(value, "map") as Map;
		if (obj == null)
		{
			obj = (Field(value, "home") as MapParent)?.Map;
			if (obj == null)
			{
				obj = (Field(value, "block") as Thing)?.MapHeld;
				if (obj == null)
				{
					obj = (Field(value, "stall") as ThingComp)?.parent?.MapHeld;
					if (obj == null)
					{
						IEnumerable obj2 = Field(value, "pawns") as IEnumerable;
						obj = ((obj2 != null) ? (from p in obj2.Cast<object>().OfType<Pawn>()
							select p.MapHeld).FirstOrDefault((Map m) => m != null) : null);
					}
				}
			}
		}
		if (obj == null)
		{
			string text = RecordKey(value);
			if (text != null)
			{
				RimMarketState state = State;
				if (state != null && state.recordMaps.TryGetValue(text, out var value2))
				{
					obj = value2;
					goto IL_0185;
				}
			}
			return null;
		}
		goto IL_0185;
		IL_0185:
		return (Map)obj;
	}

	internal static Faction RecordOwner(object value)
	{
		if (value is Caravan caravan)
		{
			return caravan.Faction;
		}
		if (value is Thing { Faction: { IsPlayer: not false } } thing)
		{
			return thing.Faction;
		}
		if (value is ThingComp { parent: { Faction: var faction2 } } thingComp && faction2?.IsPlayer == true)
		{
			return thingComp.parent.Faction;
		}
		if (value == null)
		{
			return null;
		}
		if (value.GetType() == T("Routes.TradeConvoy"))
		{
			int order = Number(value, "orderId");
			int route = Number(value, "routeId");
			object obj = GlobalList((order > 0) ? "orders" : "routes").Cast<object>().FirstOrDefault((object r) => Number(r, "id") == ((order > 0) ? order : route));
			if (obj != null)
			{
				return RememberOwner(value, RecordOwner(obj));
			}
			return RememberedOwner(value);
		}
		if (value.GetType() == T("Routes.ConvoyBattle"))
		{
			return RememberOwner(value, RecordOwner(Field(value, "convoy"))) ?? RememberedOwner(value);
		}
		if (Field(value, "caravan") is Caravan caravan2)
		{
			return RememberOwner(value, caravan2.Faction);
		}
		if (Field(value, "stall") is ThingComp { parent: { Faction: var faction3 } } thingComp2 && faction3?.IsPlayer == true)
		{
			return RememberOwner(value, thingComp2.parent.Faction);
		}
		if (Field(value, "block") is Thing { Faction: { IsPlayer: not false } } thing2)
		{
			return RememberOwner(value, thing2.Faction);
		}
		if (Field(value, "home") is MapParent { Faction: { IsPlayer: not false } } mapParent)
		{
			return RememberOwner(value, mapParent.Faction);
		}
		if (Field(value, "marker") is Thing { Faction: { IsPlayer: not false } } thing3)
		{
			return RememberOwner(value, thing3.Faction);
		}
		if (value.GetType() == T("Thieves.MarketTheft") && Number(value, "visitId") > 0)
		{
			object obj2 = GlobalList("visits").Cast<object>().FirstOrDefault((object v) => Number(v, "id") == Number(value, "visitId"));
			if (obj2 != null)
			{
				Faction faction7 = RecordOwner(obj2);
				if (faction7 != null && faction7.IsPlayer)
				{
					return RememberOwner(value, faction7);
				}
			}
		}
		return RememberedOwner(value) ?? RememberOwner(value, MapOwner(RecordMap(value)));
	}

	private static string RecordKey(object value)
	{
		if (value == null)
		{
			return null;
		}
		string fullName = value.GetType().FullName;
		if (fullName == null || !fullName.StartsWith("RimMarket.", StringComparison.Ordinal))
		{
			return null;
		}
		int num = ((value is WorldObject worldObject) ? worldObject.ID : Number(value, "id"));
		if (num <= 0)
		{
			num = Number(value, "visitId");
		}
		if (num <= 0)
		{
			num = (Field(value, "caravan") as Caravan)?.ID ?? (Field(value, "marker") as Thing)?.thingIDNumber ?? (Field(value, "thief") as Pawn)?.thingIDNumber ?? 0;
		}
		if (num <= 0)
		{
			IEnumerable obj = Field(value, "pawns") as IEnumerable;
			num = ((obj == null) ? ((int?)null) : obj.Cast<object>().OfType<Pawn>().FirstOrDefault()?.thingIDNumber).GetValueOrDefault();
		}
		if (num <= 0)
		{
			return null;
		}
		return fullName + "/" + num;
	}

	private static Faction RememberedOwner(object value)
	{
		string text = RecordKey(value);
		if (text != null)
		{
			RimMarketState state = State;
			if (state != null && state.recordOwners.TryGetValue(text, out var value2))
			{
				return value2;
			}
		}
		return null;
	}

	private static IEnumerable<string> RecordKeys(object value)
	{
		if (value?.GetType() == T("Market.LeavingGroup") && Field(value, "pawns") is IEnumerable source)
		{
			foreach (Pawn item in source.Cast<object>().OfType<Pawn>())
			{
				yield return value.GetType().FullName + "/" + item.thingIDNumber;
			}
		}
		else
		{
			string text = RecordKey(value);
			if (text != null)
			{
				yield return text;
			}
		}
	}

	private static Faction RememberOwner(object value, Faction owner)
	{
		if (owner != null && owner.IsPlayer && readDepth == 0 && !MP.InInterface && State != null)
		{
			Map map = RecordMap(value);
			foreach (string item in RecordKeys(value))
			{
				State.recordOwners[item] = owner;
				if (map != null && Find.Maps.Contains(map))
				{
					State.recordMaps[item] = map;
				}
			}
		}
		return owner;
	}

	internal static IList GlobalList(string name)
	{
		MarketScope marketScope = Active;
		while (marketScope?.previous != null)
		{
			marketScope = marketScope.previous;
		}
		if (marketScope == null || !marketScope.views.TryGetValue(name, out (IList, IList, HashSet<object>) value))
		{
			return List(Manager, name);
		}
		return value.Item1;
	}

	private static Faction UnscopedOwner()
	{
		if (MP.InInterface)
		{
			return LocalOwner;
		}
		if (MP.IsExecutingSyncCommand)
		{
			Faction ofPlayer = Faction.OfPlayer;
			if (ofPlayer != null && ofPlayer.IsPlayer)
			{
				return ofPlayer;
			}
		}
		Faction? faction = State?.Primary?.owner;
		if (faction == null)
		{
			FactionManager factionManager = Find.FactionManager;
			if (factionManager == null)
			{
				return null;
			}
			faction = (from f in factionManager.AllFactionsListForReading
				where f.IsPlayer
				orderby f.loadID
				select f).FirstOrDefault();
		}
		return faction;
	}

	private static Faction InferOwner(object instance, object[] args, Map map)
	{
		object[] array = args ?? Array.Empty<object>();
		foreach (object obj in array)
		{
			Faction faction = RecordOwner(obj);
			if (faction != null && faction.IsPlayer && !(obj is Pawn))
			{
				return faction;
			}
		}
		return RecordOwner(instance) ?? Active?.Owner ?? MapOwner(map) ?? UnscopedOwner();
	}

	private static void BeforeNative(object __instance, object[] __args, MethodBase __originalMethod, out MarketScope __state)
	{
		__state = null;
		if (!Enabled)
		{
			return;
		}
		Map map = (__args ?? Array.Empty<object>()).Select(RecordMap).FirstOrDefault((Map m) => m != null) ?? RecordMap(__instance) ?? Active?.Map ?? (tickingMapField.GetValue(null) as Map);
		Faction faction = InferOwner(__instance, __args, map);
		if (__originalMethod.DeclaringType == ManagerType && __originalMethod.Name == "Notify_Traded")
		{
			object obj = __args.FirstOrDefault();
			Pawn trader = obj as Pawn;
			if (trader != null)
			{
				object obj2 = GlobalList("visits").Cast<object>().FirstOrDefault((object v) => Field(v, "trader") == trader);
				if (obj2 != null)
				{
					faction = RecordOwner(obj2) ?? faction;
				}
				goto IL_01b6;
			}
		}
		bool flag = __originalMethod.DeclaringType == ManagerType;
		if (flag)
		{
			string name = __originalMethod.Name;
			bool flag2 = ((name == "Notify_ThiefCaught" || name == "Notify_CulpritCaptured") ? true : false);
			flag = flag2;
		}
		if (flag)
		{
			HashSet<Pawn> pawns = (__args ?? Array.Empty<object>()).OfType<Pawn>().ToHashSet();
			object obj3 = GlobalList("thefts").Cast<object>().FirstOrDefault((object t) => Field(t, "thief") is Pawn item && pawns.Contains(item));
			if (obj3 != null)
			{
				faction = RecordOwner(obj3) ?? faction;
			}
		}
		goto IL_01b6;
		IL_01b6:
		if (faction != null && (Active == null || Active.Owner != faction))
		{
			__state = new MarketScope(faction, map);
		}
	}

	private static Exception AfterNative(Exception __exception, MarketScope __state)
	{
		__state?.Dispose();
		return __exception;
	}

	private static bool OwnedBuilding(Thing thing)
	{
		if (thing != null && thing.Spawned && !thing.Destroyed && Find.Maps.Contains(thing.Map))
		{
			Faction faction = thing.Faction;
			if (faction != null && faction.IsPlayer)
			{
				return CommandAuthorityBridge.CanActForFaction(thing.Faction);
			}
		}
		return false;
	}

	private static void InstallClock()
	{
		Type type = AccessTools.TypeByName("Multiplayer.Client.AsyncTimeComp") ?? throw new TypeLoadException("AsyncTimeComp");
		Type type2 = AccessTools.TypeByName("Multiplayer.Client.AsyncTime.AsyncWorldTimeComp") ?? throw new TypeLoadException("AsyncWorldTimeComp");
		multiplayerGame = AccessTools.DeclaredField("Multiplayer.Client.Multiplayer:game");
		worldOwnerField = AccessTools.DeclaredField("Multiplayer.Client.MultiplayerGame:asyncWorldTimeComp");
		tickingMapField = AccessTools.DeclaredField(type, "tickingMap");
		mapTime = AccessTools.DeclaredMethod("Multiplayer.Client.Extensions:AsyncTime");
		mapPre = M(type, "PreContext");
		mapPost = M(type, "PostContext");
		worldPre = M(type2, "PreContextForCallback");
		worldPost = M(type2, "PostContext");
		ownerReadTicks = AccessTools.DeclaredMethod("Multiplayer.Client.SimulationTimeContext:ReadTicks") ?? throw new MissingMethodException("SimulationTimeContext.ReadTicks");
		realLocalFaction = (Func<Faction>)(AccessTools.PropertyGetter("Multiplayer.Client.Multiplayer:RealPlayerFaction") ?? throw new MissingMethodException("Multiplayer.RealPlayerFaction")).CreateDelegate(typeof(Func<Faction>));
		if (multiplayerGame == null || worldOwnerField == null || tickingMapField == null || mapTime == null)
		{
			throw new MissingMemberException("RimMarket requires current Core simulation contexts");
		}
	}

	private static object ClockOwner(Map map)
	{
		if (map == null)
		{
			return worldOwnerField.GetValue(multiplayerGame.GetValue(null));
		}
		return mapTime.Invoke(null, new object[1] { map });
	}

	internal static int ClockTicks(Map map)
	{
		if (MP.IsInMultiplayer)
		{
			return (int)ownerReadTicks.Invoke(null, new object[1] { ClockOwner(map) });
		}
		return Find.TickManager.TicksGame;
	}

	private static IDisposable Simulation(Map map)
	{
		if (MP.IsInMultiplayer)
		{
			return new PatchingUtilities.SimulationScope(ClockOwner(map), (map == null) ? worldPre : mapPre, (map == null) ? worldPost : mapPost, map, tickingMapField, "RimMarket action has no live simulation owner");
		}
		return PatchingUtilities.EmptySimulationScope.Instance;
	}

	private static void InstallRadio()
	{
		radioNegotiator = AccessTools.FieldRefAccess<Dialog_Negotiation, Pawn>("negotiator");
		radioTarget = AccessTools.FieldRefAccess<Dialog_Negotiation, ICommunicable>("commTarget");
		syncNodeTreeOpen = AccessTools.StaticFieldRefAccess<bool>(AccessTools.DeclaredField("Multiplayer.Client.SyncUtil:isDialogNodeTreeOpen"));
		Patch("HarmonyPatches.Patch_FactionDialogRoutes", "Postfix", "UseNativeRadioOptions");
		Patch(AccessTools.DeclaredMethod(typeof(Dialog_Negotiation), "DoWindowContents"), "ReserveRadioFooter", "DrawRadioFooter");
		Patch(AccessTools.DeclaredMethod(typeof(FactionDialogMaker), "FactionDialogFor", new Type[2]
		{
			typeof(Pawn),
			typeof(Faction)
		}), "ReturnToMarketRadioRoot");
	}

	private static bool UseNativeRadioOptions()
	{
		if (Installed)
		{
			return !MP.IsInMultiplayer;
		}
		return true;
	}

	private static bool CanOpenRadioMarket(Pawn pawn, Faction faction, Map map)
	{
		if (Intercept && map != null && !map.Disposed && Find.Maps.Contains(map))
		{
			Faction faction2 = MapOwner(map);
			if (faction2 != null && faction2 == LocalOwner && ValidMarketNegotiator(pawn, map, faction2) && faction != null && !faction.IsPlayer && !faction.defeated)
			{
				FactionDef def = faction.def;
				if (def != null && !def.permanentEnemy && def.humanlikeFaction)
				{
					List<TraderKindDef> caravanTraderKinds = faction.def.caravanTraderKinds;
					if (caravanTraderKinds != null && caravanTraderKinds.Count > 0)
					{
						if (!(bool)PatchingUtilities.Get(Settings, "tradeRoutes"))
						{
							return (bool)PatchingUtilities.Get(Settings, "specialOrders");
						}
						return true;
					}
				}
			}
		}
		return false;
	}

	private static void ReserveRadioFooter(Dialog_Negotiation __instance, ref Rect inRect, out Rect? __state)
	{
		__state = null;
		if (Installed && MP.IsInMultiplayer)
		{
			Pawn pawn = radioNegotiator(__instance);
			if (CanOpenRadioMarket(pawn, radioTarget(__instance)?.GetFaction(), pawn?.Map))
			{
				__state = new Rect(inRect.x, inRect.yMax - 38f + 4f, inRect.width, 34f);
				inRect.height -= 38f;
			}
		}
	}

	private static void DrawRadioFooter(Dialog_Negotiation __instance, Rect? __state)
	{
		if (!__state.HasValue)
		{
			return;
		}
		Rect valueOrDefault = __state.GetValueOrDefault();
		if (!Widgets.ButtonText(valueOrDefault, "RimMarket.DealsTitle".Translate()))
		{
			return;
		}
		Pawn pawn = radioNegotiator(__instance);
		Faction faction = radioTarget(__instance)?.GetFaction();
		Map map = pawn?.Map;
		if (!CanOpenRadioMarket(pawn, faction, map))
		{
			return;
		}
		using (SessionServices.EnterLocalOnly())
		{
			using (new MarketScope(pawn.Faction, map, read: true, worldRead: true))
			{
				Find.WindowStack.Add(new MarketRadioDialog(pawn, faction, map));
			}
		}
	}

	private static DiaNode MarketRadioRoot(Pawn pawn, Faction faction)
	{
		DiaNode diaNode = new DiaNode(faction.Name);
		if ((bool)PatchingUtilities.Get(Settings, "tradeRoutes") && PatchingUtilities.Call(T("Routes.TradeRouteDialog"), "RootOption", pawn, faction) is DiaOption item)
		{
			diaNode.options.Add(item);
		}
		if ((bool)PatchingUtilities.Get(Settings, "specialOrders") && PatchingUtilities.Call(T("Orders.SpecialOrderDialog"), "RootOption", pawn, faction) is DiaOption item2)
		{
			diaNode.options.Add(item2);
		}
		diaNode.options.Add(new DiaOption("CloseButton".Translate())
		{
			resolveTree = true
		});
		return diaNode;
	}

	private static bool ReturnToMarketRadioRoot(Pawn negotiator, Faction faction, ref DiaNode __result)
	{
		MarketRadioDialog marketRadioDialog = drawingMarketRadio;
		if (marketRadioDialog == null || marketRadioDialog.pawn != negotiator || marketRadioDialog.faction != faction)
		{
			return true;
		}
		__result = MarketRadioRoot(negotiator, faction);
		return false;
	}

	private static void InstallSettings()
	{
		if (!(from f in AccessTools.GetDeclaredFields(T("RimMarketSettings"))
			where !f.IsStatic && f.IsPublic
			select f.Name).Except(new _003C_003Ez__ReadOnlySingleElementList<string>("showMessages")).OrderBy((string n) => n, StringComparer.Ordinal).SequenceEqual(FloatSettings.Concat(IntSettings).Concat(BoolSettings).OrderBy((string n) => n, StringComparer.Ordinal)))
		{
			throw new InvalidOperationException("RimMarket simulation settings schema changed");
		}
		SessionServices.RegisterSettings("forti.rimmarket.settings", typeof(SettingsSnapshot), () => CaptureSettings(Settings), () => CaptureSettings(Activator.CreateInstance(T("RimMarketSettings"))), (IExposable value) =>
		{
			ApplySettings((SettingsSnapshot)value);
		}, (IExposable value) => value is SettingsSnapshot settingsSnapshot && settingsSnapshot.Valid, () => (ModSettings)Settings, "RimMarket");
		MP.RegisterSyncMethod(typeof(RimMarket), "SyncSettings").SetHostOnly().SetVersion(33);
		Patch("RimMarketMod", "DoSettingsWindowContents", "SettingsBefore", null, "SettingsAfter");
		string[] array = new string[2] { "ChoiceRow", "FeeModeRow" };
		foreach (string method in array)
		{
			Patch("RimMarketMod", method, null, null, null, "SettingsMenuTranspiler");
		}
	}

	private static SettingsSnapshot CaptureSettings(object settings)
	{
		SettingsSnapshot settingsSnapshot = new SettingsSnapshot();
		for (int i = 0; i < settingsSnapshot.floats.Length; i++)
		{
			settingsSnapshot.floats[i] = (float)PatchingUtilities.Get(settings, FloatSettings[i]);
		}
		for (int j = 0; j < settingsSnapshot.ints.Length; j++)
		{
			settingsSnapshot.ints[j] = (int)PatchingUtilities.Get(settings, IntSettings[j]);
		}
		for (int k = 0; k < settingsSnapshot.bools.Length; k++)
		{
			settingsSnapshot.bools[k] = (bool)PatchingUtilities.Get(settings, BoolSettings[k]);
		}
		return settingsSnapshot;
	}

	private static void ApplySettings(SettingsSnapshot snapshot)
	{
		for (int i = 0; i < snapshot.floats.Length; i++)
		{
			PatchingUtilities.Set(Settings, FloatSettings[i], snapshot.floats[i]);
		}
		for (int j = 0; j < snapshot.ints.Length; j++)
		{
			PatchingUtilities.Set(Settings, IntSettings[j], snapshot.ints[j]);
		}
		for (int k = 0; k < snapshot.bools.Length; k++)
		{
			PatchingUtilities.Set(Settings, BoolSettings[k], snapshot.bools[k]);
		}
	}

	internal static IExposable RoomSettings(Game game)
	{
		return SessionServices.SettingsForGame("forti.rimmarket.settings", game);
	}

	private static void SettingsBefore(out SettingsSnapshot __state)
	{
		__state = (MP.IsInMultiplayer ? CaptureSettings(Settings) : null);
	}

	private static Exception SettingsAfter(Exception __exception, SettingsSnapshot __state)
	{
		if (__state == null)
		{
			return __exception;
		}
		SettingsSnapshot settingsSnapshot = CaptureSettings(Settings);
		ApplySettings(__state);
		if (__exception == null && MP.IsHosting && MP.InInterface && !settingsSnapshot.Same(__state))
		{
			SyncSettings(settingsSnapshot.floats, settingsSnapshot.ints, settingsSnapshot.bools);
		}
		return __exception;
	}

	private static IEnumerable<CodeInstruction> SettingsMenuTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		ConstructorInfo constructor = AccessTools.Constructor(typeof(FloatMenu), new Type[1] { typeof(List<FloatMenuOption>) });
		int replaced = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.opcode == OpCodes.Newobj && object.Equals(instruction.operand, constructor))
			{
				instruction.opcode = OpCodes.Call;
				instruction.operand = AccessTools.DeclaredMethod(typeof(RimMarket), "SettingsMenu");
				replaced++;
			}
			yield return instruction;
		}
		if (replaced != 1)
		{
			throw new InvalidOperationException("RimMarket settings menu contract changed");
		}
	}

	private static FloatMenu SettingsMenu(List<FloatMenuOption> options)
	{
		if (!MP.IsInMultiplayer || Current.Game == null)
		{
			return new FloatMenu(options);
		}
		Game game = Current.Game;
		int epoch = localEpoch;
		foreach (FloatMenuOption option in options)
		{
			Action native = option.action;
			if (native == null)
			{
				continue;
			}
			option.action = () =>
			{
				if (Current.Game == game && localEpoch == epoch && MP.IsHosting && Intercept)
				{
					SettingsSnapshot settingsSnapshot = CaptureSettings(Settings);
					SettingsSnapshot settingsSnapshot2;
					try
					{
						native();
						settingsSnapshot2 = CaptureSettings(Settings);
					}
					finally
					{
						ApplySettings(settingsSnapshot);
					}
					if (!settingsSnapshot2.Same(settingsSnapshot))
					{
						SyncSettings(settingsSnapshot2.floats, settingsSnapshot2.ints, settingsSnapshot2.bools);
					}
				}
			};
		}
		return new FloatMenu(options);
	}

	private static void SyncSettings(float[] floats, int[] ints, bool[] bools)
	{
		SettingsSnapshot settingsSnapshot = new SettingsSnapshot
		{
			floats = floats,
			ints = ints,
			bools = bools
		};
		if (Current.Game != null && settingsSnapshot.Valid)
		{
			SessionServices.ApplySettings(RoomSettings(Current.Game), settingsSnapshot);
		}
	}

	private static void InstallSimulation()
	{
		initializingMarketCaches = (Func<bool>)(AccessTools.PropertyGetter(AccessTools.TypeByName("Multiplayer.Client.LoadPatch"), "IsInitializingCaches") ?? throw new MissingMethodException("Multiplayer.Client.LoadPatch.IsInitializingCaches")).CreateDelegate(typeof(Func<bool>));
		pauseSimulation = (Action)(AccessTools.DeclaredMethod("Multiplayer.Client.AsyncTime.ReceiveLetterPause:SimPause") ?? throw new MissingMethodException("ReceiveLetterPause.SimPause")).CreateDelegate(typeof(Action));
		Patch("Market.MarketManager", "GameComponentTick", "WorldTick");
		Patch(AccessTools.DeclaredMethod(typeof(Map), "MapPostTick"), null, "MapTick");
		string[] array = new string[44]
		{
			"TryBeginVisit", "EndVisit", "CloseMarket", "OfferGuide", "Notify_Traded", "PayFee", "TryHaggle", "PlayerBid", "Notify_ThiefCaught", "Notify_CulpritCaptured",
			"ResolveEscaped", "ResolveDropped", "BeginAwayStall", "EndAwayStall", "RecordColonySale", "RecordGuestSale", "AddFees", "AddTakings", "Record", "LedgerFor",
			"OwnWeight", "OpinionOf", "HoldsGrudge", "RefusalChance", "get_Reputation", "get_Journal", "get_Ledgers", "get_Notice", "set_Notice", "get_GuestBook",
			"get_TotalFees", "get_TotalGuestSales", "get_TotalColonySales", "get_ThievesCaught", "get_ThievesEscaped", "get_ThievesDeterred", "get_RecentTakings", "get_LastFairTick", "set_LastFairTick", "InnPrice",
			"SetInnPrice", "AddReview", "FairInvited", "SetFairInvited"
		};
		foreach (string method in array)
		{
			Patch("Market.MarketManager", method, "BeforeNative", null, "AfterNative");
		}
		Dictionary<string, string[]> dictionary = new Dictionary<string, string[]>();
		dictionary["Market.ColonyStalls"] = new string[1] { "Sell" };
		dictionary["Inn.InnUtility"] = new string[4] { "Charge", "PriceFor", "FindBed", "CheckOut" };
		dictionary["Thieves.ThiefUtility"] = new string[3] { "TrySnatch", "TryTackle", "TrySendThief" };
		dictionary["Routes.TradeRouteUtility"] = new string[6] { "Open", "Close", "Depart", "TryArrive", "ResolveAmbush", "LoseConvoy" };
		dictionary["Routes.ConvoyBattleUtility"] = new string[6] { "Arrive", "Start", "Check", "Win", "Lose", "Notify_MapRemoved" };
		dictionary["Orders.SpecialOrderUtility"] = new string[3] { "Place", "Cancel", "TryArrive" };
		dictionary["Orders.WantedUtility"] = new string[3] { "Post", "Haggle", "Accept" };
		dictionary["Fair.FairUtility"] = new string[7] { "Announce", "CanAnnounce", "Tick", "Open", "Close", "Judge", "Cancel" };
		dictionary["Reputation.ReputationUtility"] = new string[1] { "RefusesMarket" };
		dictionary["Routes.TradeConvoy"] = new string[1] { "Tick" };
		foreach (KeyValuePair<string, string[]> item in dictionary)
		{
			array = item.Value;
			foreach (string method2 in array)
			{
				Patch(item.Key, method2, "BeforeNative", null, "AfterNative");
			}
		}
		array = new string[3] { "ReputationTick", "ColonyShoppers", "ThiefArrivals" };
		foreach (string method3 in array)
		{
			Patch("Market.MarketManager", method3, null, null, null, "ScopedMaps");
		}
		Patch("Inn.InnUtility", "Tidy", null, null, null, "ScopedMaps");
		Patch("Inn.InnBeds", "On", null, "InnBedsForOwner");
		Patch("Inn.JobGiver_SleepAtInn", "TryGiveJob", null, null, null, "InnRetryTranspiler");
		Patch("Market.MarketManager", "InnPaidUntil", null, "InnPaidOnMap");
		Patch("Reputation.MarketReputation", "Decay", "ReputationDecay");
		Patch("Market.MarketManager", "InnTick", "InnTick");
		Patch("Market.MarketManager", "SetInnPaid", "InnPaid");
		Patch("Market.MarketManager", "get_AnyFair", "AnyFairForOwner");
		Patch("Fair.FairUtility", "Tick", "CompletePendingFairJudge");
		Patch("Fair.FairUtility", "StallsOf", null, "SortFairStalls");
		Patch("Market.MarketManager", "PurgeStaleWaiting", "PurgeWaiting");
		Patch("Orders.SpecialOrderUtility", "TryArrive", null, "OrderArrived");
		Patch("Orders.SpecialOrderUtility", "Cancel", null, null, null, "OrderRefundMapTranspiler");
		Patch("Market.MarketManager", "ReactToThief", null, null, null, "ThiefReactionTranspiler");
		array = new string[3] { "AnyOpen", "FirstStocked", "CustomersAround" };
		foreach (string method4 in array)
		{
			Patch("Market.ColonyStalls", method4, null, null, null, "ColonyStallCacheTranspiler");
		}
		Patch("Routes.ConvoyBattleUtility", "Check", "BattleCheck");
		Patch("Routes.ConvoyBattleUtility", "Start", null, "BattleCreated");
		Patch("Routes.ConvoyBattleUtility", "Start", null, null, null, "BattleCreationTranspiler");
		MpCompat.harmony.Patch(M("Routes.ConvoyBattleUtility", "Start"), new HarmonyMethod(typeof(BattlePresentationPatches), "Before"), null, null, new HarmonyMethod(typeof(BattlePresentationPatches), "After"));
		Patch("Routes.TradeConvoy", "Setup", null, "ConvoyCreated");
		Patch("Auction.AuctionUtility", "ColonySilverThings", null, "SortTreasurySilver");
		(string, string)[] array2 = new (string, string)[8]
		{
			("Auction.AuctionUtility", "TakeColonySilver"),
			("Market.MarketUtility", "TakeAndPlacePayment"),
			("Orders.SpecialOrderUtility", "HandOver"),
			("Orders.SpecialOrderUtility", "Refund"),
			("Orders.SpecialOrderUtility", "Deliver"),
			("Orders.WantedUtility", "SettleBarter"),
			("Routes.ConvoyBattleUtility", "EnterNearCamp"),
			("Routes.ConvoyBattleUtility", "BuildCamp")
		};
		for (int i = 0; i < array2.Length; i++)
		{
			(string, string) tuple = array2[i];
			Patch(tuple.Item1, tuple.Item2, "PhysicalBefore", null, "SaleWorldAfter");
		}
		array = new string[3] { "Demand", "Record", "Forget" };
		foreach (string method5 in array)
		{
			Patch("Market.MarketDemand", method5, null, null, null, "WorldTicksTranspiler");
		}
		Patch("Market.MarketManager", "DemandFor", null, null, null, "WorldTicksTranspiler");
		Patch("Market.MarketManager", "get_RecentTakings", null, null, null, "WorldTicksTranspiler");
		Patch("Market.MarketManager", "AddTakings", null, null, null, "WorldTicksTranspiler");
		array2 = new (string, string)[5]
		{
			("Reputation.ReputationUtility", "MakeLogEntry"),
			("UI.Window_MarketReputation", "JournalHead"),
			("Inn.InnUtility", "RecordStay"),
			("UI.Dialog_InnRegister", "DrawBook"),
			("UI.Window_MarketDeals", "WantedRows")
		};
		for (int i = 0; i < array2.Length; i++)
		{
			(string, string) tuple2 = array2[i];
			Patch(tuple2.Item1, tuple2.Item2, null, null, null, "WorldTicksTranspiler");
		}
		array = new string[6] { "Open", "Depart", "RoutesTick", "TryArrive", "StartAmbush", "ResolveAmbush" };
		foreach (string method6 in array)
		{
			Patch("Routes.TradeRouteUtility", method6, null, null, null, "WorldTicksTranspiler");
		}
		array = new string[3] { "Place", "Depart", "TryArrive" };
		foreach (string method7 in array)
		{
			Patch("Orders.SpecialOrderUtility", method7, null, null, null, "WorldTicksTranspiler");
		}
		Patch("Orders.WantedUtility", "get_Now", null, null, null, "WorldTicksTranspiler");
		array = new string[4] { "BeginAwayStall", "EndAwayStall", "AwayTick", "AwayCooldownLeft" };
		foreach (string method8 in array)
		{
			Patch("Market.MarketManager", method8, null, null, null, "WorldTicksTranspiler");
		}
		array2 = new (string, string)[4]
		{
			("Away.AwayStall", "get_TicksLeft"),
			("Away.AwayStall", "get_EventActive"),
			("Routes.TradeRouteDialog", "RouteStatus"),
			("Orders.SpecialOrderDialog", "OrderStatus")
		};
		for (int i = 0; i < array2.Length; i++)
		{
			(string, string) tuple3 = array2[i];
			Patch(tuple3.Item1, tuple3.Item2, null, null, null, "WorldTicksTranspiler");
		}
		Patch("Market.StallVisit", "get_TicksLeft", "ReadVisitBefore", null, "ReadWorldAfter");
		Patch("Fair.FairUtility", "get_Now", "FairNow");
		Patch("Debug.DebugActions_RimMarket", "FairStartNow", null, null, null, "WorldTicksTranspiler");
		array = new string[2] { "StartTickFor", "DateOf" };
		foreach (string method9 in array)
		{
			Patch("Fair.FairUtility", method9, "ReadWorldBefore", null, "ReadWorldAfter");
		}
		Patch("Compat.VteBridge", "Notify_PlayerSold", "SaleWorldBefore", null, "SaleWorldAfter");
		array = new string[4] { "Opening", "Closing", "Petals", "Fire" };
		foreach (string method10 in array)
		{
			PatchingUtilities.PatchPushPopRand(M("Fair.MapComponent_MarketShow", method10));
		}
	}

	private static bool WorldTick()
	{
		if (!Enabled)
		{
			return true;
		}
		if (initializingMarketCaches())
		{
			return false;
		}
		int num = ClockTicks(null);
		if (num % 250 != 0)
		{
			return false;
		}
		State.EnsureAccounts(Manager);
		if (num % 60000 == 0)
		{
			PruneMetadata();
		}
		Faction[] array = (from f in Find.FactionManager.AllFactionsListForReading
			where f.IsPlayer
			orderby f.loadID
			select f).ToArray();
		for (int num2 = 0; num2 < array.Length; num2++)
		{
			using (new MarketScope(array[num2], null))
			{
				if (num % 2500 == 0)
				{
					PatchingUtilities.Call(Manager, "ReputationTick");
					if (List(Manager, "routes").Count > 0)
					{
						PatchingUtilities.Call(T("Routes.TradeRouteUtility"), "RoutesTick", Manager);
					}
					if (List(Manager, "orders").Count > 0)
					{
						TickOrders(local: false);
					}
					if (Fields["notice"].GetValue(Manager) != null)
					{
						PatchingUtilities.Call(T("Orders.WantedUtility"), "Tick", Manager);
					}
				}
				if (num % 250 == 0 && List(Manager, "awayStalls").Count > 0)
				{
					PatchingUtilities.Call(Manager, "AwayTick");
				}
			}
		}
		return false;
	}

	private static void MapTick(Map __instance)
	{
		if (!Enabled || dispatchingMap || initializingMarketCaches())
		{
			return;
		}
		int ticksGame = Find.TickManager.TicksGame;
		if (ticksGame % 120 != 0 && ticksGame % 250 != 0)
		{
			return;
		}
		Map map = __instance;
		if (ticksGame % 250 != 0 && map.Parent?.GetType() != T("Routes.ConvoyBattle"))
		{
			return;
		}
		Map tickMap = TickMap;
		TickMap = map;
		dispatchingMap = true;
		try
		{
			HashSet<Faction> hashSet = new HashSet<Faction>();
			Faction faction = MapOwner(map);
			if (faction != null)
			{
				hashSet.Add(faction);
			}
			foreach (Building item in map.listerBuildings.allBuildingsColonist)
			{
				Faction faction2 = item.Faction;
				if (faction2 != null && faction2.IsPlayer)
				{
					string? text = item.GetType().Namespace;
					if (text != null && text.StartsWith("RimMarket.", StringComparison.Ordinal))
					{
						hashSet.Add(item.Faction);
					}
				}
			}
			foreach (object item2 in from object v in GlobalList("visits")
				where RecordMap(v) == map
				select v)
			{
				Faction faction3 = RecordOwner(item2);
				if (faction3 != null && faction3.IsPlayer)
				{
					hashSet.Add(faction3);
				}
			}
			if (map.Parent?.GetType() == T("Routes.ConvoyBattle"))
			{
				Faction faction4 = RecordOwner(map.Parent);
				if (faction4 != null && faction4.IsPlayer)
				{
					using (new MarketScope(faction4, map))
					{
						if (ticksGame % 120 == 0)
						{
							PatchingUtilities.Call(T("Routes.ConvoyBattleUtility"), "Check", map.Parent);
						}
					}
				}
			}
			if (ticksGame % 250 != 0)
			{
				return;
			}
			foreach (Faction item3 in hashSet.OrderBy((Faction f) => f.loadID))
			{
				using (new MarketScope(item3, map))
				{
					if (ticksGame % 2500 == 0)
					{
						PatchingUtilities.Call(Manager, "ReputationTick");
						PatchingUtilities.Call(Manager, "InnTick");
						TickOrders(local: true);
						if ((bool)PatchingUtilities.Get(PatchingUtilities.Get(T("RimMarketMod"), "Settings"), "shoppersVisit"))
						{
							PatchingUtilities.Call(Manager, "ColonyShoppers");
						}
						if (List(Manager, "leaving").Count > 0)
						{
							PatchingUtilities.Call(T("Market.Stragglers"), "Tick", Fields["leaving"].GetValue(Manager));
						}
					}
					if (List(Manager, "fairs").Count > 0)
					{
						PatchingUtilities.Call(Manager, "FairTick");
					}
					if (List(Manager, "visits").Count > 0 || List(Manager, "thefts").Count > 0 || List(Manager, "auctions").Count > 0)
					{
						PatchingUtilities.Call(Manager, "Patrol");
					}
				}
			}
		}
		finally
		{
			TickMap = tickMap;
			dispatchingMap = false;
		}
	}

	private static List<Map> MapsForScope()
	{
		if (Enabled)
		{
			if (TickMap == null)
			{
				return new List<Map>();
			}
			return new List<Map> { TickMap };
		}
		return Find.Maps;
	}

	private static IEnumerable<CodeInstruction> ScopedMaps(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo original = AccessTools.PropertyGetter(typeof(Find), "Maps");
		foreach (CodeInstruction instruction in instructions)
		{
			yield return instruction.Calls(original) ? new CodeInstruction(instruction)
			{
				opcode = OpCodes.Call,
				operand = AccessTools.DeclaredMethod(typeof(RimMarket), "MapsForScope")
			} : instruction;
		}
	}

	private static int WorldTicks(TickManager manager)
	{
		if (!Enabled)
		{
			return manager.TicksGame;
		}
		return ClockTicks(null);
	}

	private static IEnumerable<CodeInstruction> OrderRefundMapTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		return ReplaceMarketCall(instructions, AccessTools.PropertyGetter(typeof(Find), "AnyPlayerHomeMap"), "OrderRefundMap");
	}

	private static Map OrderRefundMap()
	{
		if (!Enabled)
		{
			return Find.AnyPlayerHomeMap;
		}
		Faction owner = Active?.Owner;
		if (owner != null)
		{
			return (from map in Find.Maps
				where !map.Disposed && MapOwner(map) == owner && map.IsPlayerHome
				orderby map.uniqueID
				select map).FirstOrDefault();
		}
		return null;
	}

	private static IEnumerable<CodeInstruction> ThiefReactionTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		return ReplaceMarketCall(instructions, AccessTools.DeclaredMethod(typeof(TickManager), "Pause"), "ThiefPause");
	}

	private static void ThiefPause(TickManager manager)
	{
		if (Enabled)
		{
			pauseSimulation();
		}
		else
		{
			manager.Pause();
		}
	}

	private static Map ColonyStallCachedMap(Map map)
	{
		if (!Installed || !MP.IsInMultiplayer)
		{
			return map;
		}
		return null;
	}

	private static IEnumerable<CodeInstruction> InnRetryTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		return ReplaceMarketCall(instructions, AccessTools.DeclaredMethod(typeof(Dictionary<int, int>), "TryGetValue"), "TryReadInnRetry");
	}

	private static bool TryReadInnRetry(Dictionary<int, int> retryByPawn, int pawnId, out int retryTick)
	{
		retryTick = 0;
		if (!Installed || !MP.IsInMultiplayer)
		{
			return retryByPawn.TryGetValue(pawnId, out retryTick);
		}
		return false;
	}

	private static IEnumerable<CodeInstruction> ColonyStallCacheTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
	{
		string fieldName = __originalMethod.Name switch
		{
			"AnyOpen" => "openMap", 
			"FirstStocked" => "stockedMap", 
			"CustomersAround" => "customerMap", 
			_ => throw new MissingMethodException(__originalMethod.Name), 
		};
		FieldInfo cache = AccessTools.DeclaredField(__originalMethod.DeclaringType, fieldName);
		int replaced = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			yield return instruction;
			if (instruction.LoadsField(cache))
			{
				yield return CodeInstruction.Call(typeof(RimMarket), "ColonyStallCachedMap");
				replaced++;
			}
		}
		if (replaced != 1)
		{
			throw new InvalidOperationException($"RimMarket colony stall cache contract changed: {fieldName} ({replaced})");
		}
	}

	private static IEnumerable<CodeInstruction> ReplaceMarketCall(IEnumerable<CodeInstruction> instructions, MethodInfo original, string replacement)
	{
		int replaced = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(original))
			{
				instruction.opcode = OpCodes.Call;
				instruction.operand = AccessTools.DeclaredMethod(typeof(RimMarket), replacement);
				replaced++;
			}
			yield return instruction;
		}
		if (replaced != 1)
		{
			throw new InvalidOperationException($"RimMarket call contract changed: {replacement} ({replaced})");
		}
	}

	private static IEnumerable<CodeInstruction> WorldTicksTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo original = AccessTools.PropertyGetter(typeof(TickManager), "TicksGame");
		foreach (CodeInstruction instruction in instructions)
		{
			yield return instruction.Calls(original) ? new CodeInstruction(instruction)
			{
				opcode = OpCodes.Call,
				operand = AccessTools.DeclaredMethod(typeof(RimMarket), "WorldTicks")
			} : instruction;
		}
	}

	private static bool ReputationDecay(ref int now)
	{
		if (!Enabled)
		{
			return true;
		}
		if (dispatchingMap)
		{
			return false;
		}
		now = ClockTicks(null);
		return true;
	}

	private static bool AnyFairForOwner(ref bool __result)
	{
		if (!Enabled)
		{
			return true;
		}
		Faction owner = Active?.Owner ?? UnscopedOwner();
		__result = GlobalList("fairs").Cast<object>().Any((object f) => RecordOwner(f) == owner);
		return false;
	}

	private static void CompletePendingFairJudge(object mgr, object fair)
	{
		if (Enabled && TickMap == RecordMap(fair) && Number(fair, "state") == 1 && !(bool)Field(fair, "judged") && ClockTicks(null) >= Number(fair, "endTick"))
		{
			PatchingUtilities.Call(T("Fair.FairUtility"), "Judge", mgr, fair);
		}
	}

	private static bool BattleCheck(object battle)
	{
		if (Enabled)
		{
			if (TickMap != null)
			{
				return RecordMap(battle) == TickMap;
			}
			return false;
		}
		return true;
	}

	private static void ConvoyCreated(object __instance)
	{
		if (Enabled)
		{
			RememberOwner(__instance, RecordOwner(__instance));
		}
	}

	private static void BattleCreated(object convoy)
	{
		if (Enabled)
		{
			RememberOwner(Field(convoy, "battle"), RecordOwner(convoy));
		}
	}

	private static Thing SpawnBattleThing(Thing thing, IntVec3 cell, Map map, Rot4 rotation, WipeMode mode, bool respawning, bool forbidLeavings)
	{
		if (!Enabled)
		{
			return GenSpawn.Spawn(thing, cell, map, rotation, mode, respawning, forbidLeavings);
		}
		Faction owner = Active?.Owner ?? MapOwner(map);
		using (Simulation(map))
		{
			using (CommandAuthorityBridge.EnterFaction(owner))
			{
				return GenSpawn.Spawn(thing, cell, map, rotation, mode, respawning, forbidLeavings);
			}
		}
	}

	private static Lord CreateBattleLord(Faction faction, LordJob job, Map map, IEnumerable<Pawn> pawns)
	{
		if (!Enabled)
		{
			return LordMaker.MakeNewLord(faction, job, map, pawns);
		}
		Faction owner = Active?.Owner ?? MapOwner(map);
		using (Simulation(map))
		{
			using (CommandAuthorityBridge.EnterFaction(owner))
			{
				return LordMaker.MakeNewLord(faction, job, map, pawns);
			}
		}
	}

	private static void BattleJump(IntVec3 cell, Map map, CameraJumper.MovementMode mode)
	{
		if (!Enabled || MP.IsExecutingSyncCommandIssuedBySelf || (battleCaravan != null && State.caravanPlayers.TryGetValue(battleCaravan.ID, out var value) && value == SessionServices.LocalPlayer()))
		{
			CameraJumper.TryJump(cell, map, mode);
		}
	}

	private static IEnumerable<CodeInstruction> BattleCreationTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		int spawn = 0;
		int lord = 0;
		int jump = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.operand is MethodInfo methodInfo)
			{
				string text = null;
				if (methodInfo.DeclaringType == typeof(GenSpawn) && methodInfo.Name == "Spawn" && methodInfo.GetParameters().Length == 7)
				{
					text = "SpawnBattleThing";
					spawn++;
				}
				else if (methodInfo.DeclaringType == typeof(LordMaker) && methodInfo.Name == "MakeNewLord")
				{
					text = "CreateBattleLord";
					lord++;
				}
				else if (methodInfo.DeclaringType == typeof(CameraJumper) && methodInfo.Name == "TryJump" && methodInfo.GetParameters().Length == 3)
				{
					text = "BattleJump";
					jump++;
				}
				if (text != null)
				{
					yield return new CodeInstruction(instruction)
					{
						opcode = OpCodes.Call,
						operand = AccessTools.DeclaredMethod(typeof(RimMarket), text)
					};
					continue;
				}
			}
			yield return instruction;
		}
		if (spawn != 2 || lord != 2 || jump != 1)
		{
			throw new InvalidOperationException($"RimMarket aid-map creation contract changed: spawn={spawn}, lord={lord}, jump={jump}");
		}
	}

	private static void PhysicalBefore(object[] __args, out IDisposable __state)
	{
		Map map = __args.OfType<Map>().FirstOrDefault() ?? __args.Select(RecordMap).FirstOrDefault((Map m) => m != null);
		__state = ((Enabled && map != null) ? Simulation(map) : null);
	}

	private static void OrderArrived(object convoy)
	{
		if (!Enabled || Number(convoy, "orderId") <= 0)
		{
			return;
		}
		object obj = List(Manager, "orders").Cast<object>().FirstOrDefault((object o) => Number(o, "id") == Number(convoy, "orderId"));
		if (obj != null && (bool)Field(obj, "delivered"))
		{
			Map map = RecordMap(obj);
			if (map != null)
			{
				State.orderArrivalTicks.TryAdd(Number(obj, "id"), ClockTicks(map));
			}
		}
	}

	private static void TickOrders(bool local)
	{
		IList list = List(Manager, "orders");
		IList list2 = (IList)Activator.CreateInstance(list.GetType());
		Dictionary<object, int> dictionary = new Dictionary<object, int>();
		foreach (object item in list)
		{
			bool flag = (bool)Field(item, "delivered");
			bool num;
			if (!local)
			{
				if (!flag)
				{
					goto IL_0081;
				}
				num = RecordMap(item) != null;
			}
			else
			{
				if (!flag)
				{
					continue;
				}
				num = RecordMap(item) != TickMap;
			}
			if (num)
			{
				continue;
			}
			goto IL_0081;
			IL_0081:
			list2.Add(item);
			if (local)
			{
				int key = Number(item, "id");
				if (!State.orderArrivalTicks.TryGetValue(key, out var value))
				{
					value = ClockTicks(TickMap) - (ClockTicks(null) - Number(item, "arrivedTick"));
					State.orderArrivalTicks[key] = value;
				}
				dictionary[item] = Number(item, "arrivedTick");
				PatchingUtilities.Set(item, "arrivedTick", value);
			}
		}
		if (list2.Count == 0)
		{
			return;
		}
		Fields["orders"].SetValue(Manager, list2);
		try
		{
			PatchingUtilities.Call(T("Orders.SpecialOrderUtility"), "OrdersTick", Manager);
		}
		finally
		{
			foreach (KeyValuePair<object, int> item2 in dictionary)
			{
				PatchingUtilities.Set(item2.Key, "arrivedTick", item2.Value);
			}
			object[] array = list.Cast<object>().ToArray();
			foreach (object obj in array)
			{
				bool num2;
				if (!local)
				{
					if (!(bool)Field(obj, "delivered"))
					{
						goto IL_0226;
					}
					num2 = RecordMap(obj) == null;
				}
				else
				{
					if (!(bool)Field(obj, "delivered"))
					{
						continue;
					}
					num2 = RecordMap(obj) == TickMap;
				}
				if (!num2)
				{
					continue;
				}
				goto IL_0226;
				IL_0226:
				if (!list2.Contains(obj))
				{
					list.Remove(obj);
					State.orderArrivalTicks.Remove(Number(obj, "id"));
				}
			}
			foreach (object item3 in list2)
			{
				if (!list.Contains(item3))
				{
					list.Add(item3);
				}
			}
			Fields["orders"].SetValue(Manager, list);
		}
	}

	private static bool PurgeWaiting(int now)
	{
		if (!Enabled)
		{
			return true;
		}
		if (TickMap == null)
		{
			return false;
		}
		Dictionary<int, int> dictionary = (Dictionary<int, int>)Fields["waitingSince"].GetValue(Manager);
		Dictionary<int, string> dictionary2 = (Dictionary<int, string>)Fields["waitingReason"].GetValue(Manager);
		HashSet<int> hashSet = TickMap.lordManager.lords.Select((Lord l) => l.loadID).ToHashSet();
		KeyValuePair<int, int>[] array = dictionary.ToArray();
		for (int num = 0; num < array.Length; num++)
		{
			KeyValuePair<int, int> keyValuePair = array[num];
			if (hashSet.Contains(keyValuePair.Key) && now - keyValuePair.Value > 600)
			{
				dictionary.Remove(keyValuePair.Key);
				dictionary2.Remove(keyValuePair.Key);
			}
		}
		return false;
	}

	private static void PruneMetadata()
	{
		HashSet<string> keys = new HashSet<string>();
		HashSet<int> orderIds = new HashSet<int>();
		string[] sharedLists = SharedLists;
		foreach (string text in sharedLists)
		{
			foreach (object item in GlobalList(text))
			{
				foreach (string item2 in RecordKeys(item))
				{
					keys.Add(item2);
				}
				if (text == "orders")
				{
					orderIds.Add(Number(item, "id"));
				}
			}
		}
		foreach (WorldObject allWorldObject in Find.WorldObjects.AllWorldObjects)
		{
			string text2 = RecordKey(allWorldObject);
			if (text2 != null)
			{
				keys.Add(text2);
			}
		}
		sharedLists = State.recordOwners.Keys.Where((string k) => !keys.Contains(k)).ToArray();
		foreach (string key in sharedLists)
		{
			State.recordOwners.Remove(key);
		}
		sharedLists = State.recordMaps.Keys.Where((string k) => !keys.Contains(k) || !Find.Maps.Contains(State.recordMaps[k])).ToArray();
		foreach (string key2 in sharedLists)
		{
			State.recordMaps.Remove(key2);
		}
		int[] array = State.orderArrivalTicks.Keys.Where((int id) => !orderIds.Contains(id)).ToArray();
		foreach (int key3 in array)
		{
			State.orderArrivalTicks.Remove(key3);
		}
		HashSet<int> caravans = Find.WorldObjects.Caravans.Select((Caravan c) => c.ID).ToHashSet();
		array = State.caravanPlayers.Keys.Where((int id) => !caravans.Contains(id)).ToArray();
		foreach (int key4 in array)
		{
			State.caravanPlayers.Remove(key4);
		}
	}

	private static void InnPaid(Pawn p)
	{
		if (Enabled && p?.MapHeld != null)
		{
			State.innMaps[p.thingIDNumber] = p.MapHeld;
		}
	}

	private static void InnPaidOnMap(Pawn p, ref int __result)
	{
		if (Enabled && __result != 0 && p != null && State.innMaps.TryGetValue(p.thingIDNumber, out var value) && p.MapHeld != value)
		{
			__result = 0;
		}
	}

	private static bool InnTick()
	{
		if (!Enabled)
		{
			return true;
		}
		if (TickMap == null)
		{
			return false;
		}
		Dictionary<int, int> dictionary = (Dictionary<int, int>)Fields["innPaid"].GetValue(Manager);
		int ticksGame = Find.TickManager.TicksGame;
		KeyValuePair<int, int>[] array = dictionary.ToArray();
		for (int i = 0; i < array.Length; i++)
		{
			KeyValuePair<int, int> pair = array[i];
			if (!State.innMaps.TryGetValue(pair.Key, out var value))
			{
				value = Find.Maps.FirstOrDefault((Map m) => m.mapPawns.AllPawnsSpawned.Any((Pawn p) => p.thingIDNumber == pair.Key));
				if (value != null)
				{
					State.innMaps[pair.Key] = value;
				}
			}
			if (value == TickMap && pair.Value <= ticksGame)
			{
				dictionary.Remove(pair.Key);
				State.innMaps.Remove(pair.Key);
			}
		}
		PatchingUtilities.Call(T("Inn.InnUtility"), "Tidy");
		return false;
	}

	private static void InnBedsForOwner(ref List<Building_Bed> __result)
	{
		if (!Installed || !MP.IsInMultiplayer)
		{
			return;
		}
		List<Building_Bed> list = __result;
		if (list != null && list.Count > 0)
		{
			Faction owner = Active?.Owner;
			__result = (from b in __result
				where b != null && (owner == null || b.Faction == owner)
				orderby b.thingIDNumber
				select b).ToList();
		}
	}

	private static void SortFairStalls(IList __result)
	{
		if (Enabled && __result != null && __result.Count > 1)
		{
			ThingComp[] array = (from ThingComp stall in __result
				orderby stall.parent.thingIDNumber
				select stall).ToArray();
			for (int num = 0; num < array.Length; num++)
			{
				__result[num] = array[num];
			}
		}
	}

	private static void SortTreasurySilver(ref IEnumerable<Thing> __result)
	{
		if (Enabled && !MP.InInterface && __result != null)
		{
			__result = __result.OrderBy((Thing silver) => silver.thingIDNumber);
		}
	}

	private static bool FairNow(ref int __result)
	{
		if (!Enabled)
		{
			return true;
		}
		__result = ClockTicks(null);
		return false;
	}

	private static void ReadWorldBefore(out PatchingUtilities.TimeSnapshot? __state)
	{
		__state = (Enabled ? PatchingUtilities.TimeSnapshot.GetAndSetFromWorld() : ((PatchingUtilities.TimeSnapshot?)null));
	}

	private static void ReadVisitBefore(object __instance, out PatchingUtilities.TimeSnapshot? __state)
	{
		PatchingUtilities.TimeSnapshot? timeSnapshot;
		if (Enabled && MP.InInterface)
		{
			Map map = RecordMap(__instance);
			if (map != null)
			{
				timeSnapshot = PatchingUtilities.TimeSnapshot.GetAndSetFromMap(map);
				goto IL_002a;
			}
		}
		timeSnapshot = null;
		goto IL_002a;
		IL_002a:
		__state = timeSnapshot;
	}

	private static Exception ReadWorldAfter(Exception __exception, PatchingUtilities.TimeSnapshot? __state)
	{
		__state?.Set();
		return __exception;
	}

	private static void SaleWorldBefore(out IDisposable __state)
	{
		__state = (Enabled ? Simulation(null) : null);
	}

	private static Exception SaleWorldAfter(Exception __exception, IDisposable __state)
	{
		__state?.Dispose();
		return __exception;
	}

	private static void ResetLocal()
	{
		localEpoch++;
		windows = new ConditionalWeakTable<object, WindowOwner>();
		Active = null;
		TickMap = null;
	}

	private static void InstallUI()
	{
		string[] array = new string[11]
		{
			"SyncObject", "SyncRoof", "SyncOrder", "SyncPaint", "SyncDecision", "SyncWanted", "SyncPurchase", "SyncAway", "SyncPolicy", "SyncTackle",
			"SyncStallField"
		};
		foreach (string methodOrPropertyName in array)
		{
			MP.RegisterSyncMethod(typeof(RimMarket), methodOrPropertyName).SetVersion(33);
		}
		(string, string)[] array2 = new (string, string)[17]
		{
			("Buildings.Building_MarketMarker", "SetOpen"),
			("Buildings.Building_MarketStall", "SetColonyStall"),
			("Buildings.CompMarketStall", "SwapSide"),
			("Inn.CompInnBed", "SetInn"),
			("Fair.FairUtility", "Announce"),
			("Market.MarketManager", "CloseMarket"),
			("Market.MarketManager", "TryHaggle"),
			("Market.MarketManager", "PlayerBid"),
			("Market.MarketManager", "SetInnPrice"),
			("Market.MarketManager", "SetFairInvited"),
			("Market.MarketManager", "BeginAwayStall"),
			("Market.MarketManager", "EndAwayStall"),
			("Orders.WantedUtility", "Withdraw"),
			("Orders.WantedUtility", "Haggle"),
			("Orders.WantedUtility", "Accept"),
			("Routes.TradeRouteUtility", "Close"),
			("Thieves.ThiefUtility", "TryTackle")
		};
		for (int i = 0; i < array2.Length; i++)
		{
			(string, string) tuple = array2[i];
			Patch(tuple.Item1, tuple.Item2, "BeforeUiMutation");
		}
		Patch("Buildings.Building_MarketStall", "SetRoof", "RoofBefore");
		Patch("Buildings.Building_MarketStall", "SetOrder", "OrderBefore");
		Patch("Buildings.CompMarketQuarter", "Paint", "PaintBefore");
		Patch("Orders.WantedUtility", "Post", "WantedPostBefore", "WantedPostAfter");
		Patch("Orders.Dialog_WantedBoard", "DoWindowContents", null, null, null, "WantedWithdrawTranspiler");
		array = new string[4] { "SetRefill", "SetReserve", "SetPrice", "SetQuality" };
		foreach (string method in array)
		{
			Patch("UI.Dialog_ColonyStall", method, "StallRowBefore");
		}
		Patch("UI.Dialog_ColonyStall", "PaintAll", "OrderDragBefore");
		Patch("UI.Dialog_ColonyStall", "DoWindowContents", null, "ResetOrderDrag");
		Patch("UI.Dialog_AwayStallGoods", "DrawRow", "AwayRowBefore", null, "AwayRowAfter");
		PatchingUtilities.PatchPushPopRand(M("UI.Dialog_StallAwning", "DrawButtons"));
		Patch("UI.Dialog_StallAwning", "ApplyToAll", "PaintAllBefore");
		array = new string[2] { "Buildings.Building_MarketMarker", "Buildings.Building_MarketSpot" };
		for (int i = 0; i < array.Length; i++)
		{
			foreach (MethodInfo item in from m in T(array[i]).GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.NonPublic)
				where m.Name.StartsWith("<GetGizmos>", StringComparison.Ordinal) && m.ReturnType == typeof(void)
				select m)
			{
				MethodBody? methodBody = item.GetMethodBody();
				if (methodBody != null)
				{
					byte[]? iLAsByteArray = methodBody.GetILAsByteArray();
					if (((iLAsByteArray != null) ? new bool?(iLAsByteArray.Contains((byte)OpCodes.Stfld.Value)) : ((bool?)null)) == true)
					{
						Patch(item, "DirectGizmoBefore");
					}
				}
			}
		}
		Patch("Routes.TradeRouteDialog", "CadenceOption", null, "RoutePurchaseOption");
		Patch("Orders.SpecialOrderDialog", "FamilyOption", null, "CategoryPurchaseOption");
		InstallRadio();
		array = new string[9] { "UI.Dialog_ColonyStall", "UI.Dialog_StallAwning", "UI.Dialog_AwayStallGoods", "UI.Dialog_FairGuests", "UI.Dialog_InnRegister", "UI.Window_MarketDeals", "UI.Window_MarketReputation", "Orders.Dialog_WantedBoard", "Orders.Dialog_WantedNotice" };
		foreach (string text in array)
		{
			foreach (ConstructorInfo item2 in from c in AccessTools.GetDeclaredConstructors(T(text))
				where !c.IsStatic
				select c)
			{
				Patch(item2, "WindowConstructed");
			}
			Patch(text, "DoWindowContents", "WindowBefore", null, "AfterNative");
		}
		array = new string[2] { "Refresh", "DoWindowContents" };
		foreach (string method2 in array)
		{
			Patch("UI.Dialog_InnRegister", method2, null, null, null, "WindowMapTranspiler");
		}
		array = new string[4] { "UI.Alert_CaravanNeedsStall", "UI.Alert_MarketThief", "UI.Alert_WantedOffers", "UI.Alert_GreatFair" };
		for (int i = 0; i < array.Length; i++)
		{
			Patch(array[i], "GetReport", "AlertBefore", null, "AfterNative");
		}
		Patch("UI.Alert_CaravanNeedsStall", "GetReport", null, null, null, "AlertMapsTranspiler");
		Patch("Market.MarketManager", "WaitingForStall", "ReadWaitingBefore", null, "ReadWorldAfter");
		MP.RegisterSyncWorker<object>(AreaWorker, T("Buildings.Designator_MarketArea"));
		MP.RegisterSyncWorker(delegate(SyncWorker sync, ref object value)
		{
			value = PatchingUtilities.Get(T("Orders.WantedComm"), "Instance");
		}, T("Orders.WantedComm"));
		Patch(AccessTools.DeclaredMethod(typeof(Pawn_JobTracker), "TryTakeOrderedJob"), "RecordWantedJob");
		Patch("Orders.WantedComm", "TryOpenComms", "WantedWindowBefore");
		Patch(AccessTools.DeclaredMethod(typeof(Caravan_PathFollower), "StartPath"), "RecordArrivalPlayer");
		Patch(AccessTools.DeclaredMethod(typeof(WindowStack), "Add"), "MarketWindowBefore");
	}

	private static void WindowConstructed(object __instance, object[] __args)
	{
		if (MP.IsInMultiplayer && Current.Game != null)
		{
			Map map = __args.Select(RecordMap).FirstOrDefault((Map m) => m != null) ?? Active?.Map ?? Find.CurrentMap;
			Faction localOwner = LocalOwner;
			Faction owner = __args.Select(RecordOwner).FirstOrDefault((Faction f) => f?.IsPlayer ?? false) ?? Active?.Owner ?? localOwner;
			windows.Remove(__instance);
			windows.Add(__instance, new WindowOwner
			{
				map = map,
				owner = owner,
				actor = localOwner,
				game = Current.Game
			});
		}
	}

	private static bool WindowBefore(object __instance, out MarketScope __state)
	{
		__state = null;
		if (!Enabled)
		{
			return true;
		}
		if (!windows.TryGetValue(__instance, out var value))
		{
			(__instance as Window)?.Close(doCloseSound: false);
			return false;
		}
		if (value != null && (value.game != Current.Game || value.actor != LocalOwner || (value.map != null && !Find.Maps.Contains(value.map))))
		{
			(__instance as Window)?.Close(doCloseSound: false);
			return false;
		}
		if (value?.game == Current.Game)
		{
			Faction owner = value.owner;
			if (owner != null && owner.IsPlayer)
			{
				__state = new MarketScope(value.owner, value.map, read: true, __instance.GetType().Namespace == "RimMarket.Orders");
			}
		}
		return true;
	}

	private static Map WindowMap(Window window)
	{
		if (!windows.TryGetValue(window, out var value) || value.game != Current.Game)
		{
			return Find.CurrentMap;
		}
		return value.map;
	}

	private static void AlertBefore(out MarketScope __state)
	{
		object obj;
		if (Enabled)
		{
			Faction localOwner = LocalOwner;
			if (localOwner != null)
			{
				obj = new MarketScope(localOwner, null, read: true, worldRead: true);
				goto IL_001d;
			}
		}
		obj = null;
		goto IL_001d;
		IL_001d:
		__state = (MarketScope)obj;
	}

	private static List<Map> AlertMaps()
	{
		if (!Enabled)
		{
			return Find.Maps;
		}
		return Find.Maps.Where((Map m) => MapOwner(m) == LocalOwner).ToList();
	}

	private static IEnumerable<CodeInstruction> AlertMapsTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo getter = AccessTools.PropertyGetter(typeof(Find), "Maps");
		foreach (CodeInstruction instruction in instructions)
		{
			yield return instruction.Calls(getter) ? new CodeInstruction(instruction)
			{
				opcode = OpCodes.Call,
				operand = AccessTools.DeclaredMethod(typeof(RimMarket), "AlertMaps")
			} : instruction;
		}
	}

	private static void ReadWaitingBefore(Lord lord, out PatchingUtilities.TimeSnapshot? __state)
	{
		__state = ((Enabled && MP.InInterface && lord?.Map != null) ? PatchingUtilities.TimeSnapshot.GetAndSetFromMap(lord.Map) : ((PatchingUtilities.TimeSnapshot?)null));
	}

	private static IEnumerable<CodeInstruction> WindowMapTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo getter = AccessTools.PropertyGetter(typeof(Find), "CurrentMap");
		foreach (CodeInstruction instruction in instructions)
		{
			if (!instruction.Calls(getter))
			{
				yield return instruction;
				continue;
			}
			CodeInstruction codeInstruction = new CodeInstruction(OpCodes.Ldarg_0);
			codeInstruction.MoveLabelsFrom(instruction);
			codeInstruction.MoveBlocksFrom(instruction);
			yield return codeInstruction;
			yield return CodeInstruction.Call(typeof(RimMarket), "WindowMap");
		}
	}

	private static void AreaWorker(SyncWorker sync, ref object value)
	{
		Thing obj = ((!sync.isWriting) ? null : (Field(value, "quarter") as ThingComp)?.parent);
		bool obj2 = sync.isWriting && (bool)Field(value, "add");
		sync.Bind(ref obj);
		sync.Bind(ref obj2);
		if (sync.isWriting)
		{
			return;
		}
		object? obj3;
		if (obj is ThingWithComps { AllComps: var allComps })
		{
			ThingComp thingComp = allComps.FirstOrDefault((ThingComp c) => T("Buildings.CompMarketQuarter").IsInstanceOfType(c));
			if (thingComp != null)
			{
				obj3 = Activator.CreateInstance(T("Buildings.Designator_MarketArea"), thingComp, obj2);
				goto IL_00bc;
			}
		}
		obj3 = null;
		goto IL_00bc;
		IL_00bc:
		value = obj3;
	}

	private static bool BeforeUiMutation(object __instance, MethodBase __originalMethod, object[] __args)
	{
		if (!Intercept)
		{
			return true;
		}
		Faction faction = Active?.Owner ?? LocalOwner;
		Map map = __args.Select(RecordMap).FirstOrDefault((Map m) => m != null) ?? Active?.Map;
		string name = __originalMethod.Name;
		bool flag;
		switch (name)
		{
		case "SetOpen":
		case "SetColonyStall":
		case "SetInn":
		case "SwapSide":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (flag)
		{
			Thing thing = (__instance as Thing) ?? (__instance as ThingComp)?.parent;
			int action = name switch
			{
				"SetInn" => 6, 
				"SetColonyStall" => 4, 
				"SetOpen" => 0, 
				_ => 5, 
			};
			object obj = A(0);
			bool flag2 = default;
			int num;
			if (obj is bool)
			{
				flag2 = (bool)obj;
				num = 1;
			}
			else
			{
				num = 0;
			}
			SyncObject(thing, action, (byte)((uint)num & (flag2 ? 1u : 0u)) != 0, 0);
		}
		else if ((name == "Announce" || name == "CloseMarket") ? true : false)
		{
			SyncObject(A(0) as Thing, (name == "Announce") ? 8 : 9, on: false, 0);
		}
		else
		{
			switch (name)
			{
			case "TryHaggle":
				SyncDecision((A(1) as Pawn)?.Faction, map, 0, Number(A(0), "id"), 0, 0, A(1) as Pawn);
				break;
			case "PlayerBid":
				SyncDecision(faction, map, 1, Number(A(0), "visitId"), (Field(A(0), "lot") as Pawn)?.thingIDNumber ?? (-1), Number(A(0), "price"), null);
				break;
			case "SetInnPrice":
				SyncPolicy(faction, null, 0, (int)A(0), (int)A(1), invited: false);
				break;
			case "SetFairInvited":
				SyncPolicy(faction, A(0) as Faction, 1, 0, 0, (bool)A(1));
				break;
			case "Withdraw":
				SyncDecision(faction, null, 2, 0, State.Account(faction).noticeRevision, 0, null);
				break;
			case "Haggle":
			case "Accept":
				flag = true;
				goto IL_030d;
			default:
				{
					flag = false;
					goto IL_030d;
				}
				IL_030d:
				if (flag)
				{
					if (IsCurrentWantedOffer(faction, A(0)))
					{
						SyncDecision(faction, map, (name == "Accept") ? 3 : 4, Number(A(0), "id"), State.Account(faction).noticeRevision, (name == "Haggle") ? ((int)A(3)) : 0, (name == "Haggle") ? (A(2) as Pawn) : null);
					}
					break;
				}
				switch (name)
				{
				case "Close":
					SyncDecision(RecordOwner(A(0)) ?? faction, RecordMap(A(0)), 5, Number(A(0), "id"), 0, 0, null);
					break;
				case "BeginAwayStall":
					SyncAway(A(0) as Caravan, 0, A(1) as Settlement, null, keep: false);
					break;
				case "EndAwayStall":
					SyncAway(Field(A(0), "caravan") as Caravan, 1, null, null, keep: false);
					break;
				case "TryTackle":
					SyncTackle(A(0) as Pawn, A(1) as Pawn);
					break;
				}
				break;
			}
		}
		return false;
		object A(int i)
		{
			if (i >= __args.Length)
			{
				return null;
			}
			return __args[i];
		}
	}

	private static bool RoofBefore(Thing __instance, int shape, int pattern, Color canvas, Color stripe)
	{
		if (!Intercept)
		{
			return true;
		}
		SyncRoof(__instance, shape, pattern, canvas, stripe);
		return false;
	}

	private static bool OrderBefore(Thing __instance, ThingDef def, int toSell, float priceFactor, QualityRange quality, bool refill, int reserve)
	{
		if (!Intercept)
		{
			return true;
		}
		SyncOrder(__instance, def, toSell, priceFactor, (int)quality.min, (int)quality.max, refill, reserve);
		return false;
	}

	private static bool PaintBefore(ThingComp __instance, List<IntVec3> cells, bool add)
	{
		if (Enabled && !OwnedBuilding(__instance.parent))
		{
			return false;
		}
		if (!Intercept)
		{
			return true;
		}
		SyncPaint(__instance.parent, cells?.ToArray(), add);
		return false;
	}

	private static bool StallRowBefore(object __instance, object row, object[] __args, MethodBase __originalMethod)
	{
		if (!Intercept)
		{
			return true;
		}
		string text = __originalMethod.Name switch
		{
			"SetPrice" => "price", 
			"SetQuality" => "quality", 
			"SetRefill" => "refill", 
			_ => "reserve", 
		};
		object obj = __args[1];
		if (text == "price")
		{
			obj = Mathf.Clamp(Mathf.Round((float)obj / 0.05f) * 0.05f, 0.5f, 3f);
		}
		if (text == "reserve")
		{
			obj = Mathf.Clamp((int)obj, 0, 99999);
		}
		PatchingUtilities.Set(row, text, obj);
		if (text == "quality")
		{
			PatchingUtilities.Call(__instance, "Recount", row);
		}
		Thing thing = Field(__instance, "stall") as Thing;
		ThingDef thingDef = (ThingDef)Field(row, "def");
		if (PatchingUtilities.Call(thing, "OrderFor", thingDef) != null)
		{
			QualityRange qualityRange = (QualityRange)Field(row, "quality");
			SyncStallField(thing, thingDef, text switch
			{
				"reserve" => 2, 
				"refill" => 1, 
				"price" => 0, 
				_ => 3, 
			}, (float)Field(row, "price"), (int)qualityRange.min, (int)qualityRange.max, (bool)Field(row, "refill"), Number(row, "reserve"));
		}
		return false;
	}

	private static bool OrderDragBefore(object __instance, object row, int toSell, int max)
	{
		if (!Intercept)
		{
			return true;
		}
		if (!windows.TryGetValue(__instance, out var value) || value.game != Current.Game || value.actor != LocalOwner)
		{
			return false;
		}
		bool num;
		if (!(bool)Field(__instance, "paintAllOn"))
		{
			num = toSell > 0;
		}
		else
		{
			if (max <= 0)
			{
				goto IL_0089;
			}
			num = toSell < max;
		}
		if (num)
		{
			WindowOwner windowOwner = value;
			return (windowOwner.paintedOrders ?? (windowOwner.paintedOrders = new HashSet<ThingDef>())).Add((ThingDef)Field(row, "def"));
		}
		goto IL_0089;
		IL_0089:
		return false;
	}

	private static void ResetOrderDrag(object __instance)
	{
		if (Intercept && windows.TryGetValue(__instance, out var value) && !(bool)Field(__instance, "paintingAll"))
		{
			value.paintedOrders?.Clear();
		}
	}

	private static void AwayRowBefore(object __instance, out List<ThingDef> __state)
	{
		__state = (Intercept ? new List<ThingDef>((List<ThingDef>)Field(Field(__instance, "stall"), "keep")) : null);
	}

	private static Exception AwayRowAfter(object __instance, List<ThingDef> __state, Exception __exception)
	{
		if (__state == null)
		{
			return __exception;
		}
		object instance = Field(__instance, "stall");
		List<ThingDef> list = (List<ThingDef>)Field(instance, "keep");
		(ThingDef, bool)[] array = (from d in list.Except(__state)
			select (d: d, true)).Concat(from d in __state.Except(list)
			select (d: d, false)).ToArray();
		list.Clear();
		list.AddRange(__state);
		if (__exception == null)
		{
			(ThingDef, bool)[] array2 = array;
			for (int num = 0; num < array2.Length; num++)
			{
				var (def, keep) = array2[num];
				SyncAway(Field(instance, "caravan") as Caravan, 2, null, def, keep);
			}
		}
		return __exception;
	}

	private static bool PaintAllBefore(object __instance, ref int __result)
	{
		if (!Intercept)
		{
			return true;
		}
		Thing source = Field(__instance, "stall") as Thing;
		if (source?.Map == null)
		{
			return false;
		}
		Color canvas = (Color)PatchingUtilities.Get(source, "CanvasColor");
		Color stripe = (Color)PatchingUtilities.Get(source, "StripeColor");
		foreach (Thing item in from t in source.Map.listerThings.AllThings
			where t.GetType() == source.GetType() && t.Faction == source.Faction
			orderby t.thingIDNumber
			select t)
		{
			if (item != source)
			{
				SyncRoof(item, (int)PatchingUtilities.Get(source, "RoofShape"), (int)PatchingUtilities.Get(source, "RoofPattern"), canvas, stripe);
				__result++;
			}
		}
		return false;
	}

	private static bool DirectGizmoBefore(Thing __instance, MethodBase __originalMethod)
	{
		if (!Intercept)
		{
			return true;
		}
		_ = __instance.GetType().Name;
		byte[] bytes = __originalMethod.GetMethodBody().GetILAsByteArray();
		FieldInfo[] source = (from f in AccessTools.GetDeclaredFields(__instance.GetType()).Where((FieldInfo f) =>
			{
				if (!f.IsStatic)
				{
					Type fieldType = f.FieldType;
					if ((object)fieldType != null)
					{
						if (!(fieldType == typeof(bool)))
						{
							return fieldType == typeof(int);
						}
						return true;
					}
				}
				return false;
			})
			where ContainsFieldToken(bytes, f.MetadataToken)
			select f).ToArray();
		if (source.Any((FieldInfo f) => f.Name == "open"))
		{
			SyncObject(__instance, 0, !(bool)Field(__instance, "open"), 0);
		}
		else if (source.Any((FieldInfo f) => f.Name == "feeMode"))
		{
			SyncObject(__instance, 1, on: false, (int)PatchingUtilities.Call(__instance.GetType(), "NextFeeMode", Number(__instance, "feeMode")));
		}
		else if (source.Any((FieldInfo f) => f.Name == "markerHidden"))
		{
			SyncObject(__instance, 2, !(bool)Field(__instance, "markerHidden"), 0);
		}
		else
		{
			if (!source.Any((FieldInfo f) => f.Name == "spotHidden"))
			{
				return true;
			}
			SyncObject(__instance, 3, !(bool)Field(__instance, "spotHidden"), 0);
		}
		return false;
	}

	private static bool ContainsFieldToken(byte[] bytes, int token)
	{
		byte[] bytes2 = BitConverter.GetBytes(token);
		for (int i = 0; i + 4 < bytes.Length; i++)
		{
			if (bytes[i] == (byte)OpCodes.Stfld.Value && bytes.Skip(i + 1).Take(4).SequenceEqual(bytes2))
			{
				return true;
			}
		}
		return false;
	}

	private static bool WantedPostBefore(Map map, List<ThingDefCountClass> goods, ref int __result)
	{
		if (!Intercept)
		{
			return true;
		}
		SyncWanted(map, goods?.Select((ThingDefCountClass g) => g.thingDef).ToArray(), goods?.Select((ThingDefCountClass g) => g.count).ToArray());
		__result = 1;
		return false;
	}

	private static void WantedPostAfter(int __result)
	{
		if (Enabled && !Intercept && __result > 0)
		{
			Faction faction = Active?.Owner;
			if (faction != null)
			{
				State.Account(faction).noticeRevision++;
			}
		}
	}

	private static object CurrentWantedNotice(Faction owner)
	{
		if (owner == null)
		{
			return null;
		}
		if (Active != null)
		{
			if (Active.Owner != owner)
			{
				return State.Account(owner).Value("notice");
			}
			return Fields["notice"].GetValue(Manager);
		}
		if (State.Primary?.owner != owner)
		{
			return State.Account(owner).Value("notice");
		}
		return Fields["notice"].GetValue(Manager);
	}

	private static bool IsCurrentWantedOffer(Faction owner, object offer)
	{
		if (offer != null && Field(CurrentWantedNotice(owner), "offers") is IList source)
		{
			return source.Cast<object>().Any((object current) => current == offer);
		}
		return false;
	}

	private static IEnumerable<CodeInstruction> WantedWithdrawTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		return ReplaceMarketCall(instructions, AccessTools.DeclaredMethod(typeof(Dialog_MessageBox), "CreateConfirmation", new Type[5]
		{
			typeof(TaggedString),
			typeof(Action),
			typeof(bool),
			typeof(string),
			typeof(WindowLayer)
		}), "WantedWithdrawConfirmation");
	}

	private static Dialog_MessageBox WantedWithdrawConfirmation(TaggedString text, Action confirmed, bool destructive, string title, WindowLayer layer)
	{
		if (!Intercept)
		{
			return Dialog_MessageBox.CreateConfirmation(text, confirmed, destructive, title, layer);
		}
		Faction owner = Active?.Owner ?? LocalOwner;
		object notice = CurrentWantedNotice(owner);
		Game game = Current.Game;
		int epoch = localEpoch;
		return Dialog_MessageBox.CreateConfirmation(text, () =>
		{
			if (Intercept && game == Current.Game && epoch == localEpoch && LocalOwner == owner && notice != null && notice == CurrentWantedNotice(owner))
			{
				confirmed();
			}
		}, destructive, title, layer);
	}

	private static void RoutePurchaseOption(DiaOption __result, Pawn negotiator, Faction faction, Settlement s, TraderKindDef kind, int days, bool escort)
	{
		BindPurchaseOption(__result, negotiator, faction, s, kind, days, escort, null, large: false);
	}

	private static void CategoryPurchaseOption(DiaOption __result, Pawn negotiator, Faction faction, Settlement s, object fam, bool large)
	{
		BindPurchaseOption(__result, negotiator, faction, s, null, 0, escort: false, (string)Field(fam, "key"), large);
	}

	private static void BindPurchaseOption(DiaOption option, Pawn negotiator, Faction faction, Settlement settlement, TraderKindDef kind, int days, bool escort, string familyKey, bool large)
	{
		if (!Intercept || option == null || option.action == null)
		{
			return;
		}
		Map map = negotiator.Map;
		Game game = Current.Game;
		int epoch = localEpoch;
		option.action = () =>
		{
			if (Current.Game == game && localEpoch == epoch && Intercept)
			{
				SyncPurchase(map, faction, settlement, kind, days, escort, familyKey, large, negotiator);
			}
		};
	}

	private static void RecordWantedJob(Job job)
	{
		if (Enabled && MP.IsExecutingSyncCommand && !(job?.def?.driverClass != T("Orders.JobDriver_PostWantedNotice")))
		{
			State.jobPlayers[job.loadID] = SessionServices.ExecutingPlayer();
		}
	}

	private static bool WantedWindowBefore(Pawn negotiator)
	{
		if (!Enabled || MP.InInterface)
		{
			return true;
		}
		int key = negotiator?.CurJob?.loadID ?? (-1);
		bool flag = State.jobPlayers.TryGetValue(key, out var value);
		State.jobPlayers.Remove(key);
		if (flag)
		{
			return value == SessionServices.LocalPlayer();
		}
		return false;
	}

	private static void RecordArrivalPlayer(object __instance, object[] __args)
	{
		if (Enabled && MP.IsExecutingSyncCommand && __args.Any((object a) => a?.GetType() == T("Away.CaravanArrivalAction_HoldStall") || a?.GetType() == T("Routes.CaravanArrivalAction_HelpConvoy")) && Field(__instance, "caravan") is Caravan caravan)
		{
			State.caravanPlayers[caravan.ID] = SessionServices.ExecutingPlayer();
		}
	}

	private static bool MarketWindowBefore(Window window)
	{
		if (!Enabled || window?.GetType() != T("UI.Dialog_AwayStallGoods") || MP.InInterface)
		{
			return true;
		}
		if (MP.IsExecutingSyncCommand)
		{
			return MP.IsExecutingSyncCommandIssuedBySelf;
		}
		if (Field(Field(window, "stall"), "caravan") is Caravan caravan && State.caravanPlayers.TryGetValue(caravan.ID, out var value))
		{
			return value == SessionServices.LocalPlayer();
		}
		return false;
	}
}
