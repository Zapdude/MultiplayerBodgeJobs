using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("AOBA.USAC.Core")]
public class AscensionMegacorp
{
	private sealed class Scope(object owner) : IDisposable
	{
		private bool disposed;

		public void Dispose()
		{
			if (!disposed)
			{
				disposed = true;
				mapPost.Invoke(owner, null);
			}
		}
	}

	private sealed class AssetsSnapshot
	{
		internal object previousPage;

		internal bool locked;

		internal int repays;

		internal int next;

		internal readonly List<(object contract, int fails, bool site)> contracts = new List<(object, int, bool)>();
	}

	private sealed class ServiceSnapshot
	{
		internal object previous;

		internal readonly List<(Thing rig, bool auto)> rigs = new List<(Thing, bool)>();
	}

	private sealed class PaymentOptions
	{
		internal bool? previousEnabled;

		internal bool? previousPriority;

		internal Tradeable currency;

		internal int currencyCount;
	}

	private sealed class LocalPlacement
	{
		internal int id = -1;
	}

	[ThreadStatic]
	private static Map loanMap;

	[ThreadStatic]
	private static Map paymentMap;

	[ThreadStatic]
	private static object assetsPage;

	[ThreadStatic]
	private static string deliveryIssuer;

	private static MethodInfo mapTime;

	private static MethodInfo mapPre;

	private static MethodInfo mapPost;

	[ThreadStatic]
	private static object servicesPage;

	private static Type tradeSessionType;

	[ThreadStatic]
	private static bool? enabledBonds;

	[ThreadStatic]
	private static bool? priorityBonds;

	private static readonly ConditionalWeakTable<object, PaymentOptions> localTradeOptions = new ConditionalWeakTable<object, PaymentOptions>();

	private static Type deliveryManagerType;

	private static readonly ConditionalWeakTable<Game, LocalPlacement> placements = new ConditionalWeakTable<Game, LocalPlacement>();

	internal static bool Intercept
	{
		get
		{
			if (MP.IsInMultiplayer && MP.InInterface)
			{
				return !MP.IsExecutingSyncCommand;
			}
			return false;
		}
	}

	internal static object Debt => Get(T("GameComponent_USACDebt"), "Instance");

	internal static object Services => Current.Game?.components.FirstOrDefault((GameComponent c) => c.GetType() == T("GameComponent_USACServices"));

	internal static AscensionMultiplayerState State => Current.Game?.GetComponent<AscensionMultiplayerState>();

	private static object ActiveTrade => Get(tradeSessionType, "current");

	private static object TradeWorld => Get(AccessTools.TypeByName("Multiplayer.Client.Multiplayer"), "WorldComp");

	private static IList Trades => Get(TradeWorld, "trading") as IList;

	internal static bool DeliveryInstalled => deliveryManagerType != null;

	private static object DeliveryManager => Get(deliveryManagerType, "Instance");

	private static IList Deliveries => Get(DeliveryManager, "PendingDeliveries") as IList;

	internal static Type T(string name)
	{
		return AccessTools.TypeByName("USAC." + name) ?? throw new TypeLoadException("USAC." + name);
	}

	internal static object Get(object obj, string name)
	{
		if (obj == null)
		{
			return null;
		}
		Type type = (obj as Type) ?? obj.GetType();
		object obj2 = ((obj is Type) ? null : obj);
		MemberInfo memberInfo = (MemberInfo)(((object)AccessTools.Field(type, name)) ?? ((object)AccessTools.Property(type, name)));
		if (!(memberInfo is FieldInfo fieldInfo))
		{
			if (memberInfo is PropertyInfo propertyInfo)
			{
				return propertyInfo.GetValue(obj2);
			}
			throw new MissingMemberException(type.FullName, name);
		}
		return fieldInfo.GetValue(obj2);
	}

	internal static void Set(object obj, string name, object value)
	{
		Type type = (obj as Type) ?? obj.GetType();
		object obj2 = ((obj is Type) ? null : obj);
		FieldInfo fieldInfo = AccessTools.Field(type, name);
		if ((object)fieldInfo != null)
		{
			fieldInfo.SetValue(obj2, value);
		}
		else
		{
			(AccessTools.Property(type, name) ?? throw new MissingMemberException(type.FullName, name)).SetValue(obj2, value);
		}
	}

	internal static object Call(object obj, string name, params object[] args)
	{
		Type type = (obj as Type) ?? obj.GetType();
		MethodInfo[] array = (from m in AccessTools.GetDeclaredMethods(type)
			where m.Name == name && m.GetParameters().Length == args.Length && m.GetParameters().Select((ParameterInfo p, int i) => (args[i] != null) ? p.ParameterType.IsInstanceOfType(args[i]) : (!p.ParameterType.IsValueType)).All((bool b) => b)
			select m).ToArray();
		if (array.Length != 1)
		{
			throw new AmbiguousMatchException(type.FullName + "." + name);
		}
		return array[0].Invoke((obj is Type) ? null : obj, args);
	}

	internal static void Patch(MethodBase method, string prefix = null, string postfix = null, string transpiler = null, string finalizer = null)
	{
		if (method == null)
		{
			throw new MissingMethodException("USAC 1.7 patch target missing");
		}
		MpCompat.harmony.Patch(method, Hook(prefix), Hook(postfix), Hook(transpiler), Hook(finalizer));
		static HarmonyMethod Hook(string name)
		{
			if (name != null)
			{
				return new HarmonyMethod(typeof(AscensionMegacorp), name);
			}
			return null;
		}
	}

	private static MethodInfo Method(string type, string name)
	{
		return AccessTools.DeclaredMethod(T(type), name) ?? throw new MissingMethodException("USAC." + type, name);
	}

	private static bool OwnMap(Map map)
	{
		if (map != null && Find.Maps.Contains(map) && map.IsPlayerHome)
		{
			return map.ParentFaction == Faction.OfPlayer;
		}
		return false;
	}

	private static object Contract(string id)
	{
		IList obj = Get(Debt, "ActiveContracts") as IList;
		if (obj == null)
		{
			return null;
		}
		return obj.Cast<object>().FirstOrDefault((object c) => (string)Get(c, "ContractId") == id);
	}

	public AscensionMegacorp(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(Install);
	}

	private static void Install()
	{
		PatchingUtilities.SetupAsyncTime();
		mapTime = AccessTools.DeclaredMethod("Multiplayer.Client.Extensions:AsyncTime");
		mapPre = AccessTools.DeclaredMethod("Multiplayer.Client.AsyncTimeComp:PreContext");
		mapPost = AccessTools.DeclaredMethod("Multiplayer.Client.AsyncTimeComp:PostContext");
		string[] array = new string[12]
		{
			"ApplyLoan", "PayDebt", "Negotiate", "CallTrader", "LeaseRig", "RenewMech", "SetAutoRenew", "SetRigAuto", "ExecuteTrade", "ConfirmDelivery",
			"ClaimDelivery", "AbortDeliveries"
		};
		foreach (string methodOrPropertyName in array)
		{
			MP.RegisterSyncMethod(typeof(AscensionMegacorp), methodOrPropertyName).SetVersion(13);
		}
		Patch(Method("GameComponent_USACDebt", "ApplyForLoan"), "LoanPrefix");
		Patch(Method("GameComponent_USACDebt", "GetRichestPlayerHomeMap"), "LoanMapPrefix");
		Patch(Method("GameComponent_USACDebt", "GetBondCountOnMap"), "BondCountUiPrefix");
		array = new string[2] { "TryPayInterest", "TryPayPrincipal" };
		foreach (string name in array)
		{
			Patch(Method("DebtContract", name), "PaymentPrefix", null, null, "PaymentAfter");
		}
		array = new string[2] { "GetBondCountNearBeacons", "GetBondCountOnMap" };
		foreach (string name2 in array)
		{
			Patch(Method("DebtBondOperations", name2), "BondCountPrefix");
		}
		array = new string[2] { "ConsumeBondsNearBeacons", "ConsumeBonds" };
		foreach (string name3 in array)
		{
			Patch(Method("DebtBondOperations", name3), "ConsumeBondsPrefix");
		}
		Patch(Method("InternalUI.Page_Assets", "Draw"), "AssetsBefore", null, null, "AssetsAfter");
		Patch(Method("InternalUI.Page_Services", "Draw"), "ServicesBefore", null, null, "ServicesAfter");
		Patch(Method("InternalUI.Page_Services", "StartLease"), "StartLeasePrefix");
		Patch(Method("InternalUI.Page_Services", "CallTrader"), "CallTraderPrefix");
		Patch(Method("InternalUI.Page_Services", "GetCachedMechnitors"), null, "FilterMechnitors");
		Patch(Method("InternalUI.PortalUIUtility", "DrawTacticalButton"), null, "ServiceButton");
		array = new string[2] { "PreOpen", "PostClose" };
		foreach (string name4 in array)
		{
			Patch(Method("Dialog_USACPortal", name4), "LocalPortalLifecycle");
		}
		foreach (ConstructorInfo declaredConstructor in AccessTools.GetDeclaredConstructors(T("DebtContract")))
		{
			if (declaredConstructor.GetParameters().Length != 0)
			{
				Patch(declaredConstructor, null, "UniqueContractIdentity");
			}
		}
		InstallTrading();
		InstallDelivery();
		InstallDebtDialogs();
		InstallGizmos();
		AscensionMegacorpRuntime.Install();
	}

	private static IDisposable MapScope(Map map)
	{
		object obj = mapTime.Invoke(null, new object[1] { map });
		if (obj == null)
		{
			throw new InvalidOperationException("USAC action requires an initialized map owner");
		}
		mapPre.Invoke(obj, null);
		return new Scope(obj);
	}

	private static bool LocalPortalLifecycle()
	{
		return !MP.IsInMultiplayer;
	}

	private static bool LoanPrefix(object[] __args)
	{
		if (!Intercept)
		{
			return true;
		}
		float interest = ((assetsPage == null) ? ((float)__args[3]) : ((float)Get(assetsPage, "_targetInterest")));
		ApplyLoan(Find.CurrentMap, Convert.ToInt32(__args[0]), (float)__args[1], (float)__args[2], interest, Convert.ToInt32(__args[4]));
		return false;
	}

	private static bool LoanMapPrefix(ref Map __result)
	{
		if (!MP.IsInMultiplayer || loanMap == null)
		{
			return true;
		}
		__result = loanMap;
		return false;
	}

	private static void ApplyLoan(Map map, int type, float amount, float growth, float interest, int mode)
	{
		bool flag = !OwnMap(map) || Debt == null || type != 0;
		if (!flag)
		{
			bool flag2 = ((mode < 0 || mode > 1) ? true : false);
			flag = flag2;
		}
		if (flag || !float.IsFinite(amount) || amount < 1000f || amount > 1E+09f || amount % 1000f != 0f || !float.IsFinite(growth) || growth < 0f || growth > 0.3f || !float.IsFinite(interest) || interest < 0.01f || interest > 0.5f)
		{
			return;
		}
		object obj = Get(T("Endings.GameComponent_DebtTransfer"), "Instance");
		if (obj != null && Convert.ToInt32(Get(obj, "Phase")) != 0)
		{
			return;
		}
		object obj2 = Call(Debt, "EvaluateLoan", interest, growth, Enum.ToObject(T("DebtGrowthMode"), mode));
		if (!(bool)Get(obj2, "IsAvailable") || amount > (float)Get(obj2, "MaxAmount"))
		{
			return;
		}
		Map map2 = loanMap;
		loanMap = map;
		try
		{
			Call(Debt, "ApplyForLoan", Enum.ToObject(T("DebtType"), type), amount, (float)Get(obj2, "GrowthRate"), (float)Get(obj2, "InterestRate"), Enum.ToObject(T("DebtGrowthMode"), mode));
		}
		finally
		{
			loanMap = map2;
		}
	}

	private static void UniqueContractIdentity(object __instance)
	{
		if (!MP.IsInMultiplayer)
		{
			return;
		}
		string text = (string)Get(__instance, "ContractId");
		if (text != null)
		{
			int num = 0;
			string text2 = text;
			while (Contract(text2) != null)
			{
				text2 = text + "_" + ++num;
			}
			Set(__instance, "ContractId", text2);
		}
	}

	private static bool PaymentPrefix(object __instance, object[] __args, MethodBase __originalMethod, out Map __state)
	{
		__state = paymentMap;
		if (!Intercept)
		{
			if (MP.IsInMultiplayer)
			{
				paymentMap = __args[0] as Map;
			}
			return true;
		}
		PayDebt(Find.CurrentMap, (string)Get(__instance, "ContractId"), (__originalMethod.Name == "TryPayPrincipal") ? ((int)__args[1]) : 0);
		return false;
	}

	private static Exception PaymentAfter(Exception __exception, Map __state)
	{
		paymentMap = __state;
		return __exception;
	}

	private static void PayDebt(Map map, string id, int count)
	{
		object obj = Contract(id);
		if (!OwnMap(map) || obj == null || !(bool)Get(obj, "IsActive") || count < 0 || count > 1000000)
		{
			return;
		}
		if (count == 0)
		{
			if ((bool)Call(obj, "TryPayInterest", map))
			{
				State?.PendingRepayments.RemoveAll((AscensionRepayment p) => p.contractId == id);
			}
		}
		else
		{
			Call(obj, "TryPayPrincipal", map, count);
		}
	}

	private static bool BondCountUiPrefix(ref int __result)
	{
		if (!MP.IsInMultiplayer || (paymentMap == null && !Intercept))
		{
			return true;
		}
		__result = BondThings(paymentMap ?? Find.CurrentMap, beacon: false).Sum((Thing t) => t.stackCount);
		return false;
	}

	private static List<Thing> BondThings(Map map, bool beacon)
	{
		if (map == null)
		{
			return new List<Thing>();
		}
		ThingDef namedSilentFail = DefDatabase<ThingDef>.GetNamedSilentFail("USAC_Bond");
		if (namedSilentFail == null)
		{
			return new List<Thing>();
		}
		HashSet<IntVec3> cells = (beacon ? map.listerBuildings.allBuildingsColonist.OfType<Building_OrbitalTradeBeacon>().SelectMany((Building_OrbitalTradeBeacon b) => b.TradeableCells).ToHashSet() : null);
		return (from t in map.listerThings.ThingsOfDef(namedSilentFail)
			where t.Spawned && !t.Destroyed && t.stackCount > 0 && (cells == null || cells.Contains(t.Position))
			orderby t.thingIDNumber
			select t).ToList();
	}

	private static bool BondCountPrefix(Map map, MethodBase __originalMethod, ref int __result)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		__result = BondThings(map, __originalMethod.Name.Contains("NearBeacons")).Sum((Thing t) => t.stackCount);
		return false;
	}

	private static bool ConsumeBondsPrefix(Map map, int count, MethodBase __originalMethod)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		List<Thing> list = BondThings(map, __originalMethod.Name.Contains("NearBeacons"));
		int num = count;
		if (num < 0 || ((IEnumerable<Thing>)list).Sum((Func<Thing, long>)((Thing t) => t.stackCount)) < num)
		{
			return false;
		}
		foreach (Thing item in list)
		{
			int num2 = Math.Min(num, item.stackCount);
			if (num2 > 0)
			{
				item.SplitOff(num2).Destroy();
			}
			num -= num2;
			if (num == 0)
			{
				break;
			}
		}
		return false;
	}

	private static void AssetsBefore(object __instance, out AssetsSnapshot __state)
	{
		__state = null;
		if (!Intercept || Debt == null)
		{
			return;
		}
		__state = new AssetsSnapshot
		{
			previousPage = assetsPage,
			locked = (bool)Get(Debt, "IsSystemLocked"),
			repays = (int)Get(Debt, "RepayCountDuringLock"),
			next = (int)Get(Debt, "nextSiteBatchTick")
		};
		assetsPage = __instance;
		foreach (object item in (IList)Get(Debt, "ActiveContracts"))
		{
			__state.contracts.Add((item, (int)Get(item, "ConsecutiveCollectionFails"), (bool)Get(item, "IsInSiteMode")));
		}
	}

	private static Exception AssetsAfter(Exception __exception, AssetsSnapshot __state)
	{
		if (__state == null)
		{
			return __exception;
		}
		assetsPage = __state.previousPage;
		bool flag = __state.locked && !(bool)Get(Debt, "IsSystemLocked");
		Set(Debt, "IsSystemLocked", __state.locked);
		Set(Debt, "RepayCountDuringLock", __state.repays);
		Set(Debt, "nextSiteBatchTick", __state.next);
		foreach (var (obj, num, flag2) in __state.contracts)
		{
			Set(obj, "ConsecutiveCollectionFails", num);
			Set(obj, "IsInSiteMode", flag2);
		}
		if ((__exception == null) & flag)
		{
			Negotiate();
		}
		return __exception;
	}

	private static void Negotiate()
	{
		if (Debt == null || !(bool)Get(Debt, "IsSystemLocked") || (int)Get(Debt, "RepayCountDuringLock") < 2)
		{
			return;
		}
		Set(Debt, "IsSystemLocked", false);
		Set(Debt, "RepayCountDuringLock", 0);
		Set(Debt, "nextSiteBatchTick", -1);
		foreach (object item in (IList)Get(Debt, "ActiveContracts"))
		{
			Set(item, "ConsecutiveCollectionFails", 0);
			Set(item, "IsInSiteMode", false);
		}
	}

	private static void ServicesBefore(object __instance, out ServiceSnapshot __state)
	{
		__state = null;
		if (!Intercept)
		{
			return;
		}
		__state = new ServiceSnapshot
		{
			previous = servicesPage
		};
		servicesPage = __instance;
		foreach (Building item in from t in Find.Maps.SelectMany((Map m) => m.listerBuildings.allBuildingsColonist)
			where t.GetType() == T("Building_HeavyMiningRig")
			select t)
		{
			__state.rigs.Add((item, (bool)Get(item, "autoRenew")));
		}
	}

	private static Exception ServicesAfter(Exception __exception, ServiceSnapshot __state)
	{
		if (__state == null)
		{
			return __exception;
		}
		servicesPage = __state.previous;
		foreach (var rig in __state.rigs)
		{
			Thing item = rig.rig;
			bool item2 = rig.auto;
			bool flag = (bool)Get(item, "autoRenew");
			Set(item, "autoRenew", item2);
			if (__exception == null && flag != item2)
			{
				SetRigAuto(item, flag);
			}
		}
		return __exception;
	}

	private static void FilterMechnitors(ref List<Pawn> __result)
	{
		if (MP.IsInMultiplayer && Intercept && __result != null)
		{
			__result = __result.Where((Pawn p) => p?.Faction == Faction.OfPlayer && p.Spawned).ToList();
		}
	}

	private static void ServiceButton(string key, ref bool __result)
	{
		if (!__result || !Intercept || servicesPage == null || key == null)
		{
			return;
		}
		bool flag = key.StartsWith("renew_", StringComparison.Ordinal);
		bool flag2 = key.StartsWith("auto_", StringComparison.Ordinal);
		if ((!flag && !flag2) || !int.TryParse(key.Substring(flag ? 6 : 5), out var id))
		{
			return;
		}
		Pawn pawn = PawnsFinder.AllMaps_FreeColonistsSpawned.FirstOrDefault((Pawn p) => p.thingIDNumber == id);
		if (pawn != null)
		{
			if (flag)
			{
				RenewMech(pawn);
			}
			else
			{
				SetAutoRenew(pawn, !((IEnumerable)Get(Services, "autoRenewPawns")).Cast<Pawn>().Contains(pawn));
			}
		}
		__result = false;
	}

	private static bool StartLeasePrefix(object __instance, Window parent)
	{
		if (!Intercept)
		{
			return true;
		}
		Map map = Find.CurrentMap;
		int days = (int)Get(__instance, "leaseDays");
		bool auto = (bool)Get(__instance, "leaseAutoRenew");
		parent.Close();
		Find.Targeter.BeginTargeting(new TargetingParameters
		{
			canTargetLocations = true
		}, (LocalTargetInfo target) =>
		{
			LeaseRig(map, target.Cell, days, auto);
		}, null, (LocalTargetInfo target) => Find.CurrentMap == map && target.Cell.InBounds(map), null, null, null, playSoundOnAction: true, null, (LocalTargetInfo target) =>
		{
			if (Find.CurrentMap == map && target.Cell.InBounds(map))
			{
				GenDraw.DrawRadiusRing(target.Cell, 6.5f);
				map.deepResourceGrid.MarkForDraw();
			}
		});
		return false;
	}

	private static bool CallTraderPrefix()
	{
		if (!Intercept)
		{
			return true;
		}
		CallTrader(Find.CurrentMap);
		return false;
	}

	private static void CallTrader(Map map)
	{
		if (OwnMap(map) && Debt != null && Services != null && (int)Get(Services, "traderArrivalTick") <= 0 && BondThings(map, beacon: true).Sum((Thing t) => t.stackCount) >= 4)
		{
			Call(Debt, "ConsumeBondsNearBeacons", map, 4);
			Set(Services, "traderArrivalTick", FortifiedFeaturesFramework.WorldTicks() + 2500);
			Set(Services, "traderTargetMapId", map.uniqueID);
		}
	}

	private static void LeaseRig(Map map, IntVec3 cell, int days, bool auto)
	{
		bool flag = !OwnMap(map) || Debt == null;
		if (!flag)
		{
			bool flag2 = ((days < 1 || days > 15) ? true : false);
			flag = flag2;
		}
		if (flag || !cell.InBounds(map) || BondThings(map, beacon: true).Sum((Thing t) => t.stackCount) < days)
		{
			return;
		}
		ThingDef namedSilentFail = DefDatabase<ThingDef>.GetNamedSilentFail("USAC_MiningRigIncoming");
		if (namedSilentFail == null || GenAdj.OccupiedRect(cell, Rot4.North, namedSilentFail.size).Any((IntVec3 c) => !c.InBounds(map) || c.GetFirstBuilding(map) != null || (c.GetRoof(map)?.isThickRoof ?? false)))
		{
			return;
		}
		using (MapScope(map))
		{
			Call(Debt, "ConsumeBondsNearBeacons", map, days);
			Thing thing = ThingMaker.MakeThing(namedSilentFail);
			Call(thing, "SetLease", days * 60000, auto);
			Call(thing, "SetGuards", null, Faction.OfPlayer);
			GenSpawn.Spawn(thing, cell, map);
		}
	}

	private static HediffComp_Disappears Renewal(Pawn pawn)
	{
		if (pawn == null)
		{
			return null;
		}
		Pawn_HealthTracker health = pawn.health;
		if (health == null)
		{
			return null;
		}
		HediffSet hediffSet = health.hediffSet;
		if (hediffSet == null)
		{
			return null;
		}
		Hediff firstHediffOfDef = hediffSet.GetFirstHediffOfDef(DefDatabase<HediffDef>.GetNamedSilentFail("USAC_TempMechlinkTrigger"));
		if (firstHediffOfDef == null)
		{
			return null;
		}
		return firstHediffOfDef.TryGetComp<HediffComp_Disappears>();
	}

	private static void RenewMech(Pawn pawn)
	{
		if (pawn == null || !pawn.Spawned || pawn.Faction != Faction.OfPlayer || !OwnMap(pawn.Map))
		{
			return;
		}
		HediffComp_Disappears hediffComp_Disappears = Renewal(pawn);
		if (hediffComp_Disappears == null || Debt == null || BondThings(pawn.Map, beacon: true).Sum((Thing t) => t.stackCount) < 4 || hediffComp_Disappears.ticksToDisappear > 2145683647)
		{
			return;
		}
		using (MapScope(pawn.Map))
		{
			Call(Debt, "ConsumeBondsNearBeacons", pawn.Map, 4);
			hediffComp_Disappears.ticksToDisappear += 1800000;
		}
	}

	private static void SetAutoRenew(Pawn pawn, bool value)
	{
		if (pawn != null && pawn.Spawned && pawn.Faction == Faction.OfPlayer && Renewal(pawn) != null && Services != null)
		{
			Call(Get(Services, "autoRenewPawns"), value ? "Add" : "Remove", pawn);
		}
	}

	private static void SetRigAuto(Thing rig, bool value)
	{
		if (rig != null && rig.Spawned && !(rig.GetType() != T("Building_HeavyMiningRig")) && OwnMap(rig.Map))
		{
			Set(rig, "autoRenew", value);
		}
	}

	private static object Trade(int id)
	{
		IList trades = Trades;
		if (trades == null)
		{
			return null;
		}
		return trades.Cast<object>().FirstOrDefault((object t) => (int)Get(t, "SessionId") == id);
	}

	private static bool UsesUsacCurrency(object session)
	{
		if (session == null || !(Get(session, "trader") is ITrader { TraderKind: not null } trader))
		{
			return false;
		}
		return trader.TraderKind.modExtensions?.Any((DefModExtension e) => e.GetType() == T("ModExtension_CorpseBagTrader") && (bool)Get(e, "useCorpseBagCurrency")) ?? false;
	}

	private static void InstallTrading()
	{
		tradeSessionType = AccessTools.TypeByName("Multiplayer.Client.MpTradeSession") ?? throw new TypeLoadException("MpTradeSession");
		Patch(AccessTools.DeclaredMethod(tradeSessionType, "TryExecute"), "TradeExecutePrefix");
		Patch(AccessTools.DeclaredMethod(typeof(Dialog_Trade), "DoWindowContents"), "TradeDrawBefore", "TradeOptions", null, "TradeDrawAfter");
		Patch(Method("Patch_USACTradeWindow", "Prefix"), "KeepNativeTradeSession");
		Patch(Method("Tradeable_USACCurrency", "get_EnableBondsForPayment"), "EnabledBonds");
		Patch(Method("Tradeable_USACCurrency", "get_UseBondsForPayment"), "PriorityBonds");
		Patch(Method("Tradeable_USACCurrency", "GetHashCode"), "CurrencyHash");
	}

	private static bool KeepNativeTradeSession(ref bool __result)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		__result = true;
		return false;
	}

	private static bool CurrencyHash(ref int __result)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		__result = GenText.StableStringHash("USACCurrency");
		return false;
	}

	private static void TradeDrawBefore(out PaymentOptions __state)
	{
		__state = null;
		if (MP.IsInMultiplayer && UsesUsacCurrency(ActiveTrade))
		{
			__state = new PaymentOptions
			{
				previousEnabled = enabledBonds,
				previousPriority = priorityBonds
			};
			__state.currency = TradeSession.deal?.CurrencyTradeable;
			__state.currencyCount = __state.currency?.CountToTransfer ?? 0;
			PaymentOptions value = localTradeOptions.GetValue(ActiveTrade, (object _) => new PaymentOptions
			{
				previousEnabled = true,
				previousPriority = true
			});
			enabledBonds = value.previousEnabled ?? true;
			priorityBonds = value.previousPriority ?? true;
		}
	}

	private static void TradeOptions(Rect inRect)
	{
		if (MP.IsInMultiplayer && UsesUsacCurrency(ActiveTrade))
		{
			PaymentOptions value = localTradeOptions.GetValue(ActiveTrade, (object _) => new PaymentOptions
			{
				previousEnabled = true,
				previousPriority = true
			});
			bool checkOn = value.previousEnabled ?? true;
			bool checkOn2 = value.previousPriority ?? true;
			Widgets.CheckboxLabeled(new Rect(inRect.x + 10f, inRect.yMax - 100f, 240f, 24f), "USAC.Trade.EnableBonds".Translate(), ref checkOn);
			Widgets.CheckboxLabeled(new Rect(inRect.x + 260f, inRect.yMax - 100f, 240f, 24f), "USAC.Trade.PriorityBonds".Translate(), ref checkOn2, !checkOn);
			value.previousEnabled = checkOn;
			value.previousPriority = checkOn2;
		}
	}

	private static Exception TradeDrawAfter(Exception __exception, PaymentOptions __state)
	{
		if (__state != null)
		{
			if (__state.currency != null)
			{
				__state.currency.countToTransfer = __state.currencyCount;
			}
			enabledBonds = __state.previousEnabled;
			priorityBonds = __state.previousPriority;
		}
		return __exception;
	}

	private static bool EnabledBonds(ref bool __result)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		__result = enabledBonds ?? true;
		return false;
	}

	private static bool PriorityBonds(ref bool __result)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		__result = priorityBonds ?? true;
		return false;
	}

	private static bool TradeExecutePrefix(object __instance)
	{
		if (!Intercept || !UsesUsacCurrency(__instance))
		{
			return true;
		}
		PaymentOptions value = localTradeOptions.GetValue(__instance, (object _) => new PaymentOptions
		{
			previousEnabled = true,
			previousPriority = true
		});
		ExecuteTrade((int)Get(__instance, "SessionId"), value.previousEnabled ?? true, value.previousPriority ?? true, MP.PlayerName);
		return false;
	}

	private static void ExecuteTrade(int sessionId, bool enabled, bool priority, string issuer)
	{
		object obj = Trade(sessionId);
		if (obj == null || !UsesUsacCurrency(obj) || !(Get(obj, "playerNegotiator") is Pawn { Destroyed: false, Dead: false } pawn) || pawn.Faction != Faction.OfPlayer || issuer == null || issuer.Length > 200)
		{
			return;
		}
		bool? flag = enabledBonds;
		bool? flag2 = priorityBonds;
		string text = deliveryIssuer;
		enabledBonds = enabled;
		priorityBonds = priority;
		deliveryIssuer = issuer;
		try
		{
			Call(obj, "TryExecute");
		}
		finally
		{
			enabledBonds = flag;
			priorityBonds = flag2;
			deliveryIssuer = text;
		}
	}

	private static object Delivery(int id)
	{
		IList deliveries = Deliveries;
		if (deliveries == null)
		{
			return null;
		}
		return deliveries.Cast<object>().FirstOrDefault((object d) =>
		{
			Thing obj = Get(d, "thing") as Thing;
			return obj != null && obj.thingIDNumber == id;
		});
	}

	private static void InstallDelivery()
	{
		Patch(Method("USACDeliveryManager", "AddDelivery"), null, "RecordDelivery");
		Patch(Method("USACDeliveryManager", "GameComponentUpdate"), "DeliveryUpdate");
		Patch(Method("USACDeliveryManager", "StartPlacementProcess"), "DeliveryStart");
		Patch(Method("USACDeliveryManager", "ProcessNextDelivery"), "DeliveryStart");
		Patch(Method("USACDeliveryManager", "ConfirmPlacement"), "PlacementPrefix");
		Patch(Method("USACDeliveryManager", "AbortAllDeliveries"), "AbortPrefix");
		Patch(Method("USACDeliveryManager", "ResolveMap"), "DeliveryMap");
		Patch(Method("Designator_PlaceUSACDelivery", "HandleRotation"), "RotatePreview");
		Patch(Method("Dialog_USACPortal", "DoWindowContents"), null, "DeliveryControls");
		MP.RegisterSyncWorker<Designator>(DeliveryDesignator, T("Designator_PlaceUSACDelivery"));
		deliveryManagerType = T("USACDeliveryManager");
	}

	private static void RecordDelivery(Thing thing, Map map)
	{
		if (MP.IsInMultiplayer && thing != null && Delivery(thing.thingIDNumber) != null)
		{
			State?.SetOwner(thing.thingIDNumber, deliveryIssuer ?? "");
		}
	}

	private static bool DeliveryStart()
	{
		return !MP.IsInMultiplayer;
	}

	private static bool DeliveryMap(object delivery, ref Map __result)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		Map map = Get(delivery, "map") as Map;
		__result = ((map != null && Find.Maps.Contains(map)) ? map : null);
		return false;
	}

	private static bool DeliveryUpdate()
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (Current.ProgramState != ProgramState.Playing || Current.Game == null || Find.DesignatorManager == null)
		{
			return false;
		}
		LocalPlacement value = placements.GetValue(Current.Game, (Game _) => new LocalPlacement());
		IList deliveries = Deliveries;
		object obj = ((deliveries != null) ? deliveries.Cast<object>().FirstOrDefault((object d) => Get(d, "thing") is Thing { Destroyed: false } thing2 && Get(d, "map") is Map map2 && Find.Maps.Contains(map2) && map2.ParentFaction == Faction.OfPlayer && State?.Owner(thing2.thingIDNumber) == MP.PlayerName) : null);
		if (obj == null)
		{
			value.id = -1;
			return false;
		}
		Thing thing = (Thing)Get(obj, "thing");
		Designator selectedDesignator = Find.DesignatorManager.SelectedDesignator;
		if (value.id == thing.thingIDNumber && selectedDesignator?.GetType() == T("Designator_PlaceUSACDelivery") && Get(selectedDesignator, "thingToPlace") == thing)
		{
			return false;
		}
		Map map = (Map)Get(obj, "map");
		if (Find.CurrentMap != map)
		{
			Current.Game.CurrentMap = map;
		}
		value.id = thing.thingIDNumber;
		Find.DesignatorManager.Select((Designator)Activator.CreateInstance(T("Designator_PlaceUSACDelivery"), thing));
		return false;
	}

	private static void DeliveryDesignator(SyncWorker sync, ref Designator obj)
	{
		if (sync.isWriting)
		{
			sync.Write((Get(obj, "thingToPlace") as Thing)?.thingIDNumber ?? (-1));
			sync.Write((Rot4)Get(obj, "placementRot"));
			return;
		}
		int id = sync.Read<int>();
		Rot4 rot = sync.Read<Rot4>();
		Thing thing = Get(Delivery(id), "thing") as Thing;
		obj = ((thing != null && !thing.Destroyed) ? ((Designator)Activator.CreateInstance(T("Designator_PlaceUSACDelivery"), thing)) : null);
		if (obj != null)
		{
			Set(obj, "placementRot", rot);
		}
	}

	private static bool RotatePreview(object __instance, RotationDirection dir)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		Rot4 rot = (Rot4)Get(__instance, "placementRot");
		rot.Rotate(dir);
		Set(__instance, "placementRot", rot);
		return false;
	}

	private static bool PlacementPrefix(Thing thing, IntVec3 pos, Rot4 rot)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (thing != null && Get(Delivery(thing.thingIDNumber), "map") is Map map)
		{
			ConfirmDelivery(thing.thingIDNumber, map, pos, rot);
		}
		return false;
	}

	private static void ConfirmDelivery(int id, Map map, IntVec3 cell, Rot4 rot)
	{
		object obj = Delivery(id);
		Thing thing = Get(obj, "thing") as Thing;
		if (!OwnMap(map) || obj == null || Get(obj, "map") != map || thing == null || thing.Destroyed || !rot.IsValid || !cell.InBounds(map) || (bool)Get(obj, "confirmed") || !GenConstruct.CanPlaceBlueprintAt(thing.def, cell, rot, map).Accepted || GenAdj.OccupiedRect(cell, rot, thing.def.size).Any((IntVec3 c) =>
		{
			if (c.InBounds(map))
			{
				RoofDef roof = c.GetRoof(map);
				if (roof == null || !roof.isThickRoof)
				{
					return c.GetFirstBuilding(map) != null;
				}
			}
			return true;
		}))
		{
			return;
		}
		using (MapScope(map))
		{
			Set(obj, "targetPos", cell);
			Set(obj, "targetRot", rot);
			Set(obj, "confirmed", true);
			try
			{
				if (!(bool)Call(DeliveryManager, "DispatchOne", obj))
				{
					Set(obj, "confirmed", false);
					return;
				}
				Deliveries.Remove(obj);
				State?.RemoveOwner(id);
			}
			catch
			{
				Set(obj, "confirmed", false);
				throw;
			}
		}
	}

	private static void DeliveryControls(Rect inRect)
	{
		if (!Intercept || Deliveries == null || State == null)
		{
			return;
		}
		object[] array = (from object d in Deliveries
			where Get(d, "thing") is Thing { Destroyed: false } && Get(d, "map") is Map map && map.ParentFaction == Faction.OfPlayer && Find.Maps.Contains(map)
			select d).ToArray();
		if (array.Length != 0)
		{
			object obj = array[0];
			int thingIDNumber = ((Thing)Get(obj, "thing")).thingIDNumber;
			if (Widgets.ButtonText(new Rect(inRect.xMax - 305f, inRect.yMax - 33f, 190f, 28f), "USAC.Trade.SelectPlacement".Translate(((Thing)Get(obj, "thing")).LabelCap)))
			{
				ClaimDelivery(thingIDNumber, MP.PlayerName);
			}
			if (Widgets.ButtonText(new Rect(inRect.xMax - 110f, inRect.yMax - 33f, 100f, 28f), "Cancel".Translate()))
			{
				AbortDeliveries(thingIDNumber);
			}
		}
	}

	private static void ClaimDelivery(int id, string owner)
	{
		object obj = Delivery(id);
		if (owner != null && owner.Length <= 200 && Get(obj, "map") is Map map && OwnMap(map))
		{
			State?.SetOwner(id, owner);
		}
	}

	private static bool AbortPrefix()
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (Intercept && Deliveries != null)
		{
			object[] array = Deliveries.Cast<object>().ToArray();
			foreach (object obj in array)
			{
				if (Get(obj, "thing") is Thing thing && Get(obj, "map") is Map map && OwnMap(map))
				{
					AbortDeliveries(thing.thingIDNumber);
				}
			}
		}
		return false;
	}

	private static void AbortDeliveries(int id)
	{
		object obj = Delivery(id);
		if (!(Get(obj, "map") is Map map) || !OwnMap(map) || !(Get(obj, "thing") is Thing { Destroyed: false }))
		{
			return;
		}
		using (MapScope(map))
		{
			if ((bool)Call(T("USACDeliveryManager"), "TryFallbackDropPod", obj))
			{
				Deliveries.Remove(obj);
				State?.RemoveOwner(id);
			}
		}
	}

	private static void InstallDebtDialogs()
	{
		MP.RegisterSyncMethod(typeof(AscensionMegacorp), "ResolveRepayment").SetVersion(13);
		MP.RegisterSyncMethod(typeof(AscensionMegacorp), "ResolveTransfer").SetVersion(13);
		Patch(Method("DebtContractManager", "ShowRepaymentDialog"), "RepaymentDialog");
		Patch(Method("Endings.GameComponent_DebtTransfer", "ShowNegotiationDialog"), "TransferDialog");
	}

	private static bool RepaymentDialog(object contract, Map map)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (State == null || map == null || !(bool)Get(contract, "IsActive"))
		{
			return false;
		}
		State.SetRepayment((string)Get(contract, "ContractId"), (int)Get(contract, "NextCycleTick"), map);
		return false;
	}

	private static bool TransferDialog()
	{
		return !MP.IsInMultiplayer;
	}

	internal static void DebtDialogUpdate()
	{
		if (!MP.IsInMultiplayer || Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null || State == null || Find.WindowStack == null || Find.WindowStack.Windows.Any((Window w) => w is AscensionDebtChoice))
		{
			return;
		}
		AscensionRepayment pending = State.PendingRepayments.FirstOrDefault((AscensionRepayment p) => p.map == Find.CurrentMap && p.map.ParentFaction == Faction.OfPlayer);
		if (pending != null)
		{
			object obj = Contract(pending.contractId);
			if (obj != null)
			{
				TaggedString taggedString = "USAC.Debt.Dialog.Repayment.Text".Translate((string)Get(obj, "Label"), ((float)Get(obj, "AccruedInterest")).ToString("N0"), (int)Math.Ceiling((float)Get(obj, "AccruedInterest") / 1000f), ((float)Get(obj, "Principal")).ToString("N0"), (int)Get(obj, "MissedPayments"));
				Find.WindowStack.Add(new AscensionDebtChoice(taggedString, "USAC.Debt.Dialog.Repayment.Option.Pay".Translate(), "USAC.Debt.Dialog.Repayment.Option.Refuse".Translate(), () => State.PendingRepayments.Any((AscensionRepayment p) => p.contractId == pending.contractId && p.cycle == pending.cycle), (bool pay) =>
				{
					ResolveRepayment(pending.contractId, pending.cycle, pending.map, pay);
				}));
				return;
			}
		}
		object transfer = Get(T("Endings.GameComponent_DebtTransfer"), "Instance");
		if (transfer == null || Convert.ToInt32(Get(transfer, "Phase")) != 1 || !OwnMap(Find.CurrentMap))
		{
			return;
		}
		Map map = Find.CurrentMap;
		Faction faction = Get(transfer, "BuyerFaction") as Faction;
		if (faction != null)
		{
			int silver = (int)Get(transfer, "SilverBuyout");
			Find.WindowStack.Add(new AscensionDebtChoice("USAC.Ending.Transfer.NegotiationText".Translate(faction.Name, ((float)Get(transfer, "OriginalDebtAmount")).ToString("N0"), silver), "USAC.Ending.Transfer.Option.Pay".Translate(silver), "USAC.Ending.Transfer.Option.Refuse".Translate(15), () => Convert.ToInt32(Get(transfer, "Phase")) == 1, (bool pay) =>
			{
				ResolveTransfer(map, faction, silver, pay);
			}));
		}
	}

	private static void ResolveRepayment(string id, int cycle, Map map, bool pay)
	{
		AscensionRepayment ascensionRepayment = State?.PendingRepayments.FirstOrDefault((AscensionRepayment p) => p.contractId == id && p.cycle == cycle && p.map == map);
		object obj = Contract(id);
		if (ascensionRepayment != null && OwnMap(map) && obj != null && (bool)Get(obj, "IsActive"))
		{
			State.PendingRepayments.Remove(ascensionRepayment);
			if (!pay || !(bool)Call(obj, "TryPayInterest", map))
			{
				Call(Debt, "HandleFailedPayment", obj, map);
			}
		}
	}

	private static void ResolveTransfer(Map map, Faction faction, int price, bool pay)
	{
		object obj = Get(T("Endings.GameComponent_DebtTransfer"), "Instance");
		if (!OwnMap(map) || obj == null || Convert.ToInt32(Get(obj, "Phase")) != 1 || Get(obj, "BuyerFaction") != faction || (int)Get(obj, "SilverBuyout") != price)
		{
			return;
		}
		Map map2 = loanMap;
		loanMap = map;
		try
		{
			if (pay)
			{
				Call(T("Endings.GameComponent_DebtTransfer"), "TryBuyout", obj, price);
			}
			else
			{
				Call(T("Endings.GameComponent_DebtTransfer"), "StartCountdown", obj);
			}
		}
		finally
		{
			loanMap = map2;
		}
	}

	private static void InstallGizmos()
	{
		(string, string)[] array = new (string, string)[3]
		{
			("Building_Crate", "OpenCrate"),
			("Building_HeavyMiningRig", "EjectStoredMinerals"),
			("Building_USACCorpseStorage", "EjectContents")
		};
		for (int i = 0; i < array.Length; i++)
		{
			(string, string) tuple = array[i];
			MP.RegisterSyncMethod(Method(tuple.Item1, tuple.Item2)).SetVersion(13);
		}
		MP.RegisterSyncMethod(Method("Building_HeavyMiningRig", "DevMineAll")).SetVersion(13).SetDebugOnly();
		ISyncMethod[] array2 = MpCompat.RegisterLambdaMethod(T("Building_HeavyMiningRig"), "GetGizmos", 1);
		for (int i = 0; i < array2.Length; i++)
		{
			array2[i].SetVersion(13).SetDebugOnly();
		}
		array2 = MpCompat.RegisterLambdaMethod(T("CompMechReadiness"), "CompGetGizmosExtra", 1);
		for (int i = 0; i < array2.Length; i++)
		{
			array2[i].SetVersion(13);
		}
		MP.RegisterSyncMethod(typeof(AscensionMegacorp), "RegisterOrder").SetVersion(13);
		Patch(Method("Comp_USACMechOrderRegistration", "Execute"), "RegisterOrderPrefix");
	}

	private static bool RegisterOrderPrefix(ThingComp __instance, Pawn pawn)
	{
		if (!Intercept)
		{
			return true;
		}
		RegisterOrder(__instance, pawn, MP.PlayerName);
		return false;
	}

	private static void RegisterOrder(ThingComp order, Pawn pawn, string issuer)
	{
		if (order == null || order.parent?.Spawned != true || order.parent.Destroyed || pawn == null || !pawn.Spawned || pawn.Dead || order.GetType() != T("Comp_USACMechOrderRegistration") || pawn.Faction != Faction.OfPlayer || pawn.Map != order.parent.Map || !OwnMap(pawn.Map) || issuer == null || issuer.Length > 200 || !(Get(order.parent.def.modExtensions?.Find((DefModExtension e) => e.GetType() == T("ModExtension_MechOrder")), "mechKindDef") is PawnKindDef pawnKindDef) || DeliveryManager == null || Deliveries == null)
		{
			return;
		}
		int num = Mathf.CeilToInt(order.parent.def.GetStatValueAbstract(StatDefOf.MarketValue) * 0.2f);
		if (num < 0 || ((IEnumerable<Thing>)BondThings(pawn.Map, beacon: false)).Sum((Func<Thing, long>)((Thing t) => t.stackCount)) < num)
		{
			return;
		}
		string text = deliveryIssuer;
		deliveryIssuer = issuer;
		try
		{
			using (MapScope(pawn.Map))
			{
				int count = Deliveries.Count;
				Call(T("USAC_MechTradeUtility"), "DropMech", pawnKindDef, pawn);
				if (Deliveries.Count == count + 1)
				{
					Call(T("DebtBondOperations"), "ConsumeBonds", pawn.Map, num);
					order.parent.Destroy();
				}
			}
		}
		finally
		{
			deliveryIssuer = text;
		}
	}
}
