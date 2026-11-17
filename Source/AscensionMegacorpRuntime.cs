using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

internal static class AscensionMegacorpRuntime
{
	private sealed class TraderScope
	{
		internal PatchingUtilities.TimeSnapshot? Clock;

		internal Map Map;

		internal Map Previous;

		internal bool Pushed;
	}

	private sealed class CurrencyPreview
	{
		internal Thing Thing;
	}

	private static Func<bool> initializingCaches;

	private static FieldInfo tickingMap;

	[ThreadStatic]
	private static Map traderMap;

	private static Action<Map, Faction, bool> pushFaction;

	private static Func<Map, Faction> popFaction;

	private static readonly ConditionalWeakTable<Game, CurrencyPreview> CurrencyPreviews = new ConditionalWeakTable<Game, CurrencyPreview>();

	private static bool InitializingCaches => (initializingCaches ?? (initializingCaches = (Func<bool>)AccessTools.PropertyGetter(AccessTools.TypeByName("Multiplayer.Client.LoadPatch"), "IsInitializingCaches").CreateDelegate(typeof(Func<bool>))))();

	private static Type T(string name)
	{
		return AccessTools.TypeByName("USAC." + name) ?? throw new TypeLoadException("USAC." + name);
	}

	private static object Get(object obj, string name)
	{
		object? obj2 = AccessTools.Field(obj.GetType(), name)?.GetValue(obj);
		if (obj2 == null)
		{
			PropertyInfo propertyInfo = AccessTools.Property(obj.GetType(), name);
			if ((object)propertyInfo == null)
			{
				return null;
			}
			obj2 = propertyInfo.GetValue(obj);
		}
		return obj2;
	}

	private static bool IsOwnerTick(Map map)
	{
		return (tickingMap ?? (tickingMap = AccessTools.Field(AccessTools.TypeByName("Multiplayer.Client.AsyncTimeComp"), "tickingMap"))).GetValue(null) == map;
	}

	private static void Patch(MethodBase method, string prefix = null, string postfix = null, string transpiler = null, string finalizer = null)
	{
		if (method == null)
		{
			throw new MissingMethodException("Ascension 1.7 runtime target missing");
		}
		MpCompat.harmony.Patch(method, Hook(prefix), Hook(postfix), Hook(transpiler), Hook(finalizer));
		static HarmonyMethod Hook(string name)
		{
			if (name != null)
			{
				return new HarmonyMethod(typeof(AscensionMegacorpRuntime), name);
			}
			return null;
		}
	}

	internal static void Install()
	{
		PatchingUtilities.SetupAsyncTime();
		MethodInfo clock = AccessTools.PropertyGetter(typeof(TickManager), "TicksGame");
		string[] array = new string[13]
		{
			"DebtContract", "DebtContractManager", "DebtEvaluator", "DebtTransactionLogger", "GameComponent_USACDebt", "GameComponent_USACServices", "GameComponent_USACTrader", "GameComponent_USACHostilityReset", "Endings.GameComponent_DebtTransfer", "Alert_USACDebtRepayment",
			"Alert_USACSiegeCountdown", "InternalUI.Page_Assets", "InternalUI.Page_Services"
		};
		for (int i = 0; i < array.Length; i++)
		{
			Type type = T(array[i]);
			foreach (MethodBase item in AccessTools.GetDeclaredMethods(type).Cast<MethodBase>().Concat(type.GetConstructors(AccessTools.all)))
			{
				if (item.GetMethodBody() != null && PatchProcessor.GetOriginalInstructions(item).Any((CodeInstruction code) => code.Calls(clock)))
				{
					Patch(item, null, null, "WorldClock");
				}
			}
		}
		array = new string[5] { "GameComponent_USACDebt", "GameComponent_USACServices", "GameComponent_USACTrader", "GameComponent_USACHostilityReset", "Endings.GameComponent_DebtTransfer" };
		for (int i = 0; i < array.Length; i++)
		{
			Patch(AccessTools.Method(T(array[i]), "GameComponentTick"), "SimulationTick");
		}
		Patch(AccessTools.Method(T("GameComponent_USACServices"), "CheckAutoRenewals"), "AutoRenewWorld");
		Patch(AccessTools.Method(typeof(Map), "MapPostTick"), null, "AutoRenewMap");
		Patch(AccessTools.Method(T("GameComponent_USACServices"), "SpawnScheduledTrader"), "ScheduledTraderBefore", null, "TraderMapTranspiler", "OwnerClockAfter");
		Patch(AccessTools.Method(T("GameComponent_USACTrader"), "TryTriggerVisit"), "TraderBefore", null, "TraderMapTranspiler", "OwnerClockAfter");
		array = new string[3] { "SpawnWastewaterFleck", "SprayWastewater", "TrySpawnPeripheralDustEffect" };
		foreach (string name in array)
		{
			Patch(AccessTools.Method(T("Building_HeavyMiningRig"), name), "VisualBefore", null, null, "VisualAfter");
		}
		Patch(AccessTools.Method(T("USACTradeSummary"), "Refresh"), "VisualBefore", null, null, "VisualAfter");
		Patch(AccessTools.PropertyGetter(T("Tradeable_USACCurrency"), "Placeholder"), "CurrencyPlaceholder");
		Type type2 = T("Building_HeavyMiningRig");
		FieldInfo dust = AccessTools.Field(type2, "nextDustEffectTick");
		FieldInfo spray = AccessTools.Field(type2, "sprayEndTick");
		PatchingUtilities.RegisterTimestampFields(type2, (object obj, int offset) =>
		{
			if ((int)dust.GetValue(obj) > 0)
			{
				dust.SetValue(obj, (int)dust.GetValue(obj) + offset);
			}
			if ((int)spray.GetValue(obj) > 0)
			{
				spray.SetValue(obj, (int)spray.GetValue(obj) + offset);
			}
		});
		Type type3 = T("Projectile_MICLIC_Towed");
		FieldInfo landing = AccessTools.Field(type3, "landingTick");
		PatchingUtilities.RegisterTimestampFields(type3, (object obj, int offset) =>
		{
			if ((bool)Get(obj, "isLanded"))
			{
				landing.SetValue(obj, (int)landing.GetValue(obj) + offset);
			}
		});
	}

	private static bool SimulationTick()
	{
		if (MP.IsInMultiplayer)
		{
			return !InitializingCaches;
		}
		return true;
	}

	private static int WorldTicks()
	{
		if (!MP.IsInMultiplayer)
		{
			return Find.TickManager.TicksGame;
		}
		return FortifiedFeaturesFramework.WorldTicks();
	}

	private static IEnumerable<CodeInstruction> WorldClock(IEnumerable<CodeInstruction> instructions)
	{
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(AccessTools.PropertyGetter(typeof(TickManager), "TicksGame")))
			{
				yield return new CodeInstruction(OpCodes.Pop).WithLabels(instruction.labels).WithBlocks(instruction.blocks);
				yield return CodeInstruction.Call(typeof(AscensionMegacorpRuntime), "WorldTicks");
			}
			else
			{
				yield return instruction;
			}
		}
	}

	private static void RenewFor(object services, Map map)
	{
		HashSet<Pawn> hashSet = (HashSet<Pawn>)Get(services, "autoRenewPawns");
		HediffDef namedSilentFail = DefDatabase<HediffDef>.GetNamedSilentFail("USAC_TempMechlinkTrigger");
		if (namedSilentFail == null)
		{
			return;
		}
		Pawn[] array = hashSet.OrderBy((Pawn p) => p?.thingIDNumber ?? (-1)).ToArray();
		foreach (Pawn pawn in array)
		{
			if (pawn == null || pawn.Dead || pawn.Destroyed)
			{
				hashSet.Remove(pawn);
			}
			else
			{
				if (pawn.MapHeld != map)
				{
					continue;
				}
				Hediff hediff = pawn.health?.hediffSet.GetFirstHediffOfDef(namedSilentFail);
				if (hediff == null)
				{
					hashSet.Remove(pawn);
					continue;
				}
				HediffComp_Disappears hediffComp_Disappears = hediff.TryGetComp<HediffComp_Disappears>();
				if (hediffComp_Disappears != null && hediffComp_Disappears.ticksToDisappear < 2550)
				{
					AccessTools.Method(services.GetType(), "TryRenew").Invoke(services, new object[2] { pawn, hediffComp_Disappears });
				}
			}
		}
	}

	private static bool AutoRenewWorld(object __instance)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (!InitializingCaches)
		{
			RenewFor(__instance, null);
		}
		return false;
	}

	private static void AutoRenewMap(Map __instance)
	{
		if (MP.IsInMultiplayer && !InitializingCaches && IsOwnerTick(__instance) && Find.TickManager.TicksGame % 2500 == 0)
		{
			GameComponent gameComponent = Current.Game?.components.FirstOrDefault((GameComponent c) => c.GetType() == T("GameComponent_USACServices"));
			if (gameComponent != null)
			{
				RenewFor(gameComponent, __instance);
			}
		}
	}

	private static void EnterTrader(Map map, out TraderScope state)
	{
		state = null;
		if (!MP.IsInMultiplayer)
		{
			return;
		}
		state = new TraderScope
		{
			Map = map,
			Previous = traderMap
		};
		traderMap = map;
		if (AccessTools.Field(AccessTools.TypeByName("Multiplayer.Client.Multiplayer"), "game").GetValue(null) != null && map != null && map.ParentFaction?.IsPlayer == true)
		{
			Type type = AccessTools.TypeByName("Multiplayer.Client.Factions.FactionExtensions");
			if (pushFaction == null)
			{
				pushFaction = AccessTools.Method(type, "PushFaction", new Type[3]
				{
					typeof(Map),
					typeof(Faction),
					typeof(bool)
				}).CreateDelegate<Action<Map, Faction, bool>>();
			}
			if (popFaction == null)
			{
				popFaction = AccessTools.Method(type, "PopFaction", new Type[1] { typeof(Map) }).CreateDelegate<Func<Map, Faction>>();
			}
			pushFaction(map, map.ParentFaction, arg3: true);
			state.Pushed = true;
		}
		state.Clock = PatchingUtilities.TimeSnapshot.GetAndSetFromMap(map);
	}

	private static void ScheduledTraderBefore(object __instance, out TraderScope __state)
	{
		int id = (int)Get(__instance, "traderTargetMapId");
		EnterTrader(Find.Maps.FirstOrDefault((Map m) => m.uniqueID == id) ?? Find.AnyPlayerHomeMap, out __state);
	}

	private static void TraderBefore(out TraderScope __state)
	{
		EnterTrader(Find.AnyPlayerHomeMap, out __state);
	}

	private static Map TraderMap()
	{
		if (!MP.IsInMultiplayer)
		{
			return Find.AnyPlayerHomeMap;
		}
		return traderMap;
	}

	private static IEnumerable<CodeInstruction> TraderMapTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		int count = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(AccessTools.PropertyGetter(typeof(Find), "AnyPlayerHomeMap")))
			{
				count++;
				yield return CodeInstruction.Call(typeof(AscensionMegacorpRuntime), "TraderMap").WithLabels(instruction.labels).WithBlocks(instruction.blocks);
			}
			else
			{
				yield return instruction;
			}
		}
		if (count != 1)
		{
			throw new InvalidOperationException($"USAC trader map IL changed: {count}");
		}
	}

	private static Exception OwnerClockAfter(Exception __exception, TraderScope __state)
	{
		if (__state != null)
		{
			try
			{
				__state.Clock?.Set();
			}
			finally
			{
				try
				{
					if (__state.Pushed)
					{
						popFaction(__state.Map);
					}
				}
				finally
				{
					traderMap = __state.Previous;
				}
			}
		}
		return __exception;
	}

	private static void VisualBefore(out bool __state)
	{
		__state = MP.IsInMultiplayer;
		if (__state)
		{
			Rand.PushState();
		}
	}

	private static Exception VisualAfter(Exception __exception, bool __state)
	{
		if (__state)
		{
			Rand.PopState();
		}
		return __exception;
	}

	private static bool CurrencyPlaceholder(ref Thing __result)
	{
		if (!MP.IsInMultiplayer || Current.Game == null)
		{
			return true;
		}
		CurrencyPreview value = CurrencyPreviews.GetValue(Current.Game, (Game _) => new CurrencyPreview());
		CurrencyPreview currencyPreview = value;
		if (currencyPreview.Thing == null)
		{
			currencyPreview.Thing = new Thing
			{
				def = DefDatabase<ThingDef>.GetNamed("USAC_Bond"),
				thingIDNumber = -1,
				stackCount = 1
			};
		}
		__result = value.Thing;
		return false;
	}
}
