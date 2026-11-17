using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("shunter.bettertradersguild")]
public class BetterTradersGuild
{
	private sealed class GuildDefeatFrame
	{
		internal Settlement Settlement;

		internal object NativeState;

		internal GuildDefeatFrame Parent;

		internal int Id;

		internal bool WasDefeating;
	}

	private sealed class GuildBoundSnapshot
	{
		internal List<BetterTradersGuildBoundMemory> Rows;
	}

	internal sealed class GuildSettingsSnapshot : IExposable
	{
		internal bool[] Bools = (bool[])GuildBoolDefaults.Clone();

		internal int[] Ints = (int[])GuildIntDefaults.Clone();

		internal float[] Floats = (float[])GuildFloatDefaults.Clone();

		public void ExposeData()
		{
			for (int i = 0; i < Bools.Length; i++)
			{
				Scribe_Values.Look(ref Bools[i], GuildBoolSettings[i], GuildBoolDefaults[i]);
			}
			for (int j = 0; j < Ints.Length; j++)
			{
				Scribe_Values.Look(ref Ints[j], GuildIntSettings[j], GuildIntDefaults[j]);
			}
			for (int k = 0; k < Floats.Length; k++)
			{
				Scribe_Values.Look(ref Floats[k], GuildFloatSettings[k], GuildFloatDefaults[k]);
			}
		}

		internal bool SameAs(GuildSettingsSnapshot other)
		{
			if (other != null && Bools.SequenceEqual(other.Bools) && Ints.SequenceEqual(other.Ints))
			{
				return Floats.SequenceEqual(other.Floats);
			}
			return false;
		}
	}

	private sealed class GuildStockState
	{
		internal IDisposable Scope;

		internal HashSet<int> Regenerating;

		internal Dictionary<int, int> Alignments;

		internal int Id;

		internal int PreviousAlignment;

		internal bool WasRegenerating;

		internal bool HadAlignment;
	}

	private sealed class GuildFactionSimulationScope(IDisposable scope, Map map) : IDisposable
	{
		private bool disposed;

		public void Dispose()
		{
			if (disposed)
			{
				return;
			}
			disposed = true;
			try
			{
				guildPopFaction.Invoke(null, new object[1] { map });
			}
			finally
			{
				scope.Dispose();
			}
		}
	}

	private const string Ns = "BetterTradersGuild.";

	private static readonly ConcurrentDictionary<string, Type> Types = new ConcurrentDictionary<string, Type>();

	private static readonly ConcurrentDictionary<(Type, string), MemberInfo> Members = new ConcurrentDictionary<(Type, string), MemberInfo>();

	private static bool guildWdDefeatIntegration;

	private static MethodInfo guildNativeDefeatPrefix;

	private static Type guildNativeDefeatState;

	private static HashSet<int> guildDefeatingSettlements;

	[ThreadStatic]
	private static GuildDefeatFrame guildDefeatFrame;

	private static FieldInfo guildBoundMemory;

	private static Type guildBoundValue;

	private static FieldInfo guildBoundLaunchable;

	private static FieldInfo guildBoundTick;

	private static readonly ConditionalWeakTable<object, GuildBoundSnapshot> guildBoundSnapshots = new ConditionalWeakTable<object, GuildBoundSnapshot>();

	private static readonly string[] GuildBoolSettings = new string[6] { "useSpacerTierDefenders", "useEntrenchedDefenders", "resupplyTriggersRaid", "scaleDefendersToThreatLevel", "useCustomLayouts", "enableCargoVault" };

	private static readonly string[] GuildIntSettings = new string[5] { "resupplyMealsPerDefender", "lifeSupportUnitPowerOutput", "traderRotationIntervalDays", "cargoVaultMaxStacks", "cargoVaultMaxWealth" };

	private static readonly string[] GuildFloatSettings = new string[5] { "threatPointsMultiplier", "sentryDronePresence", "securityDefeatFraction", "tradeRequestQuestWeight", "smugglersDenQuestWeight" };

	private static readonly bool[] GuildBoolDefaults = new bool[6] { true, true, true, false, true, true };

	private static readonly int[] GuildIntDefaults = new int[5] { 2, 3200, 30, 200, 250000 };

	private static readonly float[] GuildFloatDefaults = new float[5] { 1f, 0.25f, 0.8f, 1f, 0.8f };

	private static readonly FieldInfo GuildPowerConsumption = AccessTools.Field(typeof(CompProperties_Power), "basePowerConsumption");

	private static Func<object> guildGame;

	private static AccessTools.FieldRef<object, object> guildWorld;

	private static Func<object, int> guildOwnerTicks;

	private static FieldInfo guildTickingMap;

	private static FieldInfo guildRegenerating;

	private static FieldInfo guildPendingAlignment;

	private static MethodInfo guildMapGetter;

	private static MethodInfo guildMapPre;

	private static MethodInfo guildMapPost;

	private static MethodInfo guildWorldPre;

	private static MethodInfo guildWorldPost;

	private static MethodInfo guildPushFaction;

	private static MethodInfo guildPopFaction;

	private static Func<Settlement, bool> isGuildSettlement;

	private static AccessTools.FieldRef<object, Dictionary<int, string>> guildCachedKinds;

	private static AccessTools.FieldRef<object, Dictionary<int, int>> guildCachedExpirations;

	private static bool Intercept
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

	private static object GuildMod
	{
		get
		{
			Type type = AccessTools.TypeByName("BetterTradersGuild.BetterTradersGuildMod_ModClass");
			if (!(type == null))
			{
				return LoadedModManager.ModHandles.FirstOrDefault((Mod mod) => mod.GetType() == type);
			}
			return null;
		}
	}

	private static object GuildSettings => Get(GuildMod, "settings");

	internal static bool HasGuildSettings => GuildSettings != null;

	public BetterTradersGuild(ModContentPack mod)
	{
		PatchingUtilities.SetupAsyncTime();
		InstallSettings();
		InstallCommands();
		InstallSimulation();
		InstallPersistence();
		InstallIntegrations();
	}

	private static Type TypeOf(string name)
	{
		return Types.GetOrAdd(name, (string value) => AccessTools.TypeByName("BetterTradersGuild." + value) ?? throw new TypeLoadException("BetterTradersGuild." + value));
	}

	private static MemberInfo Member(Type type, string name)
	{
		return Members.GetOrAdd((type, name), ((Type, string) key) => (MemberInfo)(((object)AccessTools.Field(key.Item1, key.Item2)) ?? ((object)(AccessTools.Property(key.Item1, key.Item2) ?? throw new MissingMemberException(key.Item1.FullName, key.Item2)))));
	}

	private static object Get(object target, string name)
	{
		if (target == null)
		{
			return null;
		}
		Type type = (target as Type) ?? target.GetType();
		object obj = ((target is Type) ? null : target);
		MemberInfo memberInfo = Member(type, name);
		if (!(memberInfo is FieldInfo fieldInfo))
		{
			if (memberInfo is PropertyInfo propertyInfo)
			{
				return propertyInfo.GetValue(obj);
			}
			throw new MissingMemberException(type.FullName, name);
		}
		return fieldInfo.GetValue(obj);
	}

	private static void Set(object target, string name, object value)
	{
		Type type = (target as Type) ?? target.GetType();
		object obj = ((target is Type) ? null : target);
		MemberInfo memberInfo = Member(type, name);
		if (!(memberInfo is FieldInfo fieldInfo))
		{
			if (memberInfo is PropertyInfo propertyInfo)
			{
				propertyInfo.SetValue(obj, value);
			}
		}
		else
		{
			fieldInfo.SetValue(obj, value);
		}
	}

	private static object Call(object target, string name, params object[] args)
	{
		Type type = (target as Type) ?? target.GetType();
		List<MethodInfo> list = new List<MethodInfo>();
		Type type2 = type;
		while (type2 != null)
		{
			list.AddRange(AccessTools.GetDeclaredMethods(type2));
			type2 = type2.BaseType;
		}
		MethodInfo[] array = (from method in list
			group method by method.Name + "(" + string.Join(",", from p in method.GetParameters()
				select p.ParameterType.FullName) + ")" into @group
			select @group.First() into method
			where method.Name == name && method.GetParameters().Length == args.Length && method.GetParameters().Select((ParameterInfo parameter, int index) =>
			{
				if (args[index] != null)
				{
					return parameter.ParameterType.IsInstanceOfType(args[index]);
				}
				return !parameter.ParameterType.IsValueType || Nullable.GetUnderlyingType(parameter.ParameterType) != null;
			}).All((bool value) => value)
			select method).ToArray();
		if (array.Length != 1)
		{
			throw new AmbiguousMatchException($"Expected one {type.FullName}.{name} for {args.Length} arguments, found {array.Length}");
		}
		return array[0].Invoke((target is Type) ? null : target, args);
	}

	private static void Patch(string type, string method, string prefix = null, string postfix = null, string transpiler = null, string finalizer = null)
	{
		MethodInfo[] array = (from value in AccessTools.GetDeclaredMethods(TypeOf(type))
			where value.Name == method
			select value).ToArray();
		if (array.Length != 1)
		{
			throw new AmbiguousMatchException($"Expected one {"BetterTradersGuild."}{type}.{method}; found {array.Length}");
		}
		MpCompat.harmony.Patch(array[0], Hook(prefix), Hook(postfix), Hook(transpiler), Hook(finalizer));
		static HarmonyMethod Hook(string name)
		{
			if (name != null)
			{
				return new HarmonyMethod(typeof(BetterTradersGuild), name);
			}
			return null;
		}
	}

	private static void InstallCommands()
	{
		MP.RegisterSyncMethod(typeof(BetterTradersGuild), "SyncForceClaim");
		MP.RegisterSyncMethod(typeof(BetterTradersGuild), "SyncEmpireRelation");
		MP.RegisterSyncMethod(typeof(BetterTradersGuild), "SyncGuildTrade");
		MpCompat.harmony.Patch(MpMethodUtil.GetLambda(TypeOf("Comps.CompForceClaimable"), "CompGetGizmosExtra"), new HarmonyMethod(typeof(BetterTradersGuild), "ForceClaimPrefix"));
		for (int i = 0; i < 2; i++)
		{
			MpCompat.harmony.Patch(MpMethodUtil.GetLambda(TypeOf("GameComponents.GameComponent_EmpireRelationFix"), "ShowFixDialog", MethodType.Normal, null, i), new HarmonyMethod(typeof(BetterTradersGuild), (i == 0) ? "EmpireNeutralPrefix" : "EmpireHostilePrefix"));
		}
		Patch("TradersGuildHelper", "OpenTradeDialog", "GuildOpenTradePrefix");
		MpCompat.harmony.Patch(MpMethodUtil.GetLambda(TypeOf("Patches.SettlementPatches.SettlementGetCaravanGizmos"), "Postfix", MethodType.Normal, null, 1), new HarmonyMethod(typeof(BetterTradersGuild), "GuildTradeRoutePrefix"));
	}

	private static bool ForceClaimPrefix(ThingComp __instance)
	{
		if (!Intercept)
		{
			return true;
		}
		SyncForceClaim(__instance.parent);
		return false;
	}

	private static void SyncForceClaim(Thing thing)
	{
		Faction ofPlayer = Faction.OfPlayer;
		if (!(thing is ThingWithComps { Spawned: not false, Destroyed: false } thingWithComps) || ofPlayer == null || thingWithComps.Faction == ofPlayer || !thingWithComps.AllComps.Any(TypeOf("Comps.CompForceClaimable").IsInstanceOfType))
		{
			return;
		}
		using (MapFactionScope(thingWithComps.Map, ofPlayer))
		{
			thingWithComps.SetFaction(ofPlayer);
		}
	}

	private static bool EmpireNeutralPrefix(object __instance)
	{
		return EmpireChoicePrefix(__instance, neutral: true);
	}

	private static bool EmpireHostilePrefix(object __instance)
	{
		return EmpireChoicePrefix(__instance, neutral: false);
	}

	private static bool EmpireChoicePrefix(object closure, bool neutral)
	{
		if (!Intercept)
		{
			return true;
		}
		SyncEmpireRelation(Get(closure, "empire") as Faction, neutral);
		return false;
	}

	private static void SyncEmpireRelation(Faction empire, bool neutral)
	{
		Faction ofPlayer = Faction.OfPlayer;
		GameComponent gameComponent = Current.Game?.components.FirstOrDefault(TypeOf("GameComponents.GameComponent_EmpireRelationFix").IsInstanceOfType);
		if (gameComponent == null || (bool)Get(gameComponent, "empireRelationFixed") || ofPlayer?.def?.defName != "BTG_IndependentTraders" || empire?.def?.defName != "Empire" || !Find.FactionManager.AllFactions.Contains(empire))
		{
			return;
		}
		using (WorldFactionScope(ofPlayer))
		{
			if (neutral && empire.RelationKindWith(ofPlayer) == FactionRelationKind.Hostile)
			{
				empire.TryAffectGoodwillWith(ofPlayer, -empire.GoodwillWith(ofPlayer), canSendMessage: false, canSendHostilityLetter: false);
			}
			Set(gameComponent, "empireRelationFixed", true);
		}
	}

	private static bool GuildOpenTradePrefix(Caravan caravan, Settlement settlement)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (Intercept)
		{
			if (Call(TypeOf("TradersGuildHelper"), "FindNegotiator", caravan, settlement) != null)
			{
				CameraJumper.TryJumpAndSelect(caravan);
			}
			SyncGuildTrade(caravan, settlement, travel: false);
		}
		else
		{
			using (WorldFactionScope(caravan?.Faction ?? Faction.OfPlayer))
			{
				OpenGuildTradeNow(caravan, settlement);
			}
		}
		return false;
	}

	private static bool GuildTradeRoutePrefix(object __instance)
	{
		if (!Intercept)
		{
			return true;
		}
		SyncGuildTrade(Get(__instance, "caravan") as Caravan, Get(__instance, "__instance") as Settlement, travel: true);
		return false;
	}

	private static void SyncGuildTrade(Caravan caravan, Settlement settlement, bool travel)
	{
		Faction ofPlayer = Faction.OfPlayer;
		if (caravan == null || settlement == null || caravan.Destroyed || settlement.Destroyed || caravan.Faction != ofPlayer || !isGuildSettlement(settlement))
		{
			return;
		}
		using (WorldFactionScope(ofPlayer))
		{
			if ((bool)Call(TypeOf("TradersGuildHelper"), "CanPeacefullyVisit", settlement.Faction))
			{
				if (travel && caravan.Tile != settlement.Tile)
				{
					caravan.pather.StartPath(settlement.Tile, new CaravanArrivalAction_Trade(settlement), repathImmediately: true);
				}
				else if (!(caravan.Tile != settlement.Tile) && Call(TypeOf("TradersGuildHelper"), "GetTradeBlockedReason", caravan, settlement) == null)
				{
					OpenGuildTradeNow(caravan, settlement);
				}
			}
		}
	}

	private static void OpenGuildTradeNow(Caravan caravan, Settlement settlement)
	{
		if (Call(TypeOf("TradersGuildHelper"), "FindNegotiator", caravan, settlement) is Pawn playerNegotiator)
		{
			Find.WindowStack.Add(new Dialog_Trade(playerNegotiator, settlement));
		}
	}

	private static void InstallIntegrations()
	{
		guildWdDefeatIntegration = AccessTools.TypeByName("TSA_WorldDomination.Patch_InterceptDefeat") != null;
		if (guildWdDefeatIntegration)
		{
			Type type = TypeOf("Patches.SettlementPatches.SettlementDefeatUtilityCheckDefeated");
			guildNativeDefeatState = AccessTools.Inner(type, "DefeatState") ?? throw new TypeLoadException(type.FullName + "+DefeatState");
			guildNativeDefeatPrefix = AccessTools.DeclaredMethod(type, "Prefix", new Type[2]
			{
				typeof(Settlement),
				guildNativeDefeatState.MakeByRefType()
			}) ?? throw new MissingMethodException(type.FullName, "Prefix");
			guildDefeatingSettlements = (Get(type, "settlementsBeingDefeated") as HashSet<int>) ?? throw new InvalidOperationException("BTG defeat guard is not HashSet<int>");
			MethodInfo original = AccessTools.DeclaredMethod(typeof(SettlementDefeatUtility), "CheckDefeated", new Type[1] { typeof(Settlement) }) ?? throw new MissingMethodException(typeof(SettlementDefeatUtility).FullName, "CheckDefeated");
			MpCompat.harmony.Patch(original, new HarmonyMethod(typeof(BetterTradersGuild), "GuildDefeatCapturePrefix")
			{
				priority = 800
			}, null, null, new HarmonyMethod(typeof(BetterTradersGuild), "GuildDefeatCaptureFinalizer"));
			Patch("Patches.SettlementPatches.SettlementDefeatUtilityCheckDefeated", "Postfix", null, null, "GuildDefeatStateTranspiler");
		}
	}

	private static void GuildDefeatCapturePrefix(Settlement factionBase, out GuildDefeatFrame __state)
	{
		__state = null;
		if (MP.IsInMultiplayer && guildWdDefeatIntegration && factionBase != null && isGuildSettlement(factionBase))
		{
			GuildDefeatFrame guildDefeatFrame = (BetterTradersGuild.guildDefeatFrame = (__state = new GuildDefeatFrame
			{
				Settlement = factionBase,
				Id = factionBase.ID,
				Parent = BetterTradersGuild.guildDefeatFrame,
				WasDefeating = guildDefeatingSettlements.Contains(factionBase.ID)
			}));
			object[] array = new object[2] { factionBase, null };
			guildNativeDefeatPrefix.Invoke(null, array);
			guildDefeatFrame.NativeState = array[1];
		}
	}

	private static object GuildDefeatStateForPostfix(Settlement settlement, object originalState)
	{
		if (originalState != null || !MP.IsInMultiplayer)
		{
			return originalState;
		}
		for (GuildDefeatFrame parent = guildDefeatFrame; parent != null; parent = parent.Parent)
		{
			if (parent.Settlement == settlement)
			{
				return parent.NativeState;
			}
		}
		return null;
	}

	private static IEnumerable<CodeInstruction> GuildDefeatStateTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		yield return new CodeInstruction(OpCodes.Ldarg_0);
		yield return new CodeInstruction(OpCodes.Ldarg_1);
		yield return new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(BetterTradersGuild), "GuildDefeatStateForPostfix"));
		yield return new CodeInstruction(OpCodes.Castclass, guildNativeDefeatState);
		yield return new CodeInstruction(OpCodes.Starg_S, (byte)1);
		foreach (CodeInstruction instruction in instructions)
		{
			yield return instruction;
		}
	}

	private static void GuildDefeatCaptureFinalizer(GuildDefeatFrame __state)
	{
		if (__state == null)
		{
			return;
		}
		try
		{
			if (__state.WasDefeating)
			{
				guildDefeatingSettlements.Add(__state.Id);
			}
			else
			{
				guildDefeatingSettlements.Remove(__state.Id);
			}
		}
		finally
		{
			guildDefeatFrame = __state.Parent;
		}
	}

	private static void InstallPersistence()
	{
		guildBoundMemory = AccessTools.DeclaredField(TypeOf("LordJobs.EscapeGraceTracker"), "lastBound") ?? throw new MissingFieldException("EscapeGraceTracker", "lastBound");
		guildBoundValue = guildBoundMemory.FieldType.GetGenericArguments()[1];
		guildBoundLaunchable = AccessTools.DeclaredField(guildBoundValue, "launchable") ?? throw new MissingFieldException(guildBoundValue.FullName, "launchable");
		guildBoundTick = AccessTools.DeclaredField(guildBoundValue, "tick") ?? throw new MissingFieldException(guildBoundValue.FullName, "tick");
		Patch("LordJobs.EscapeGraceTracker", "ExposeData", null, "GuildBoundExposePostfix");
	}

	private static void GuildBoundExposePostfix(object __instance)
	{
		if (Scribe.mode != LoadSaveMode.Saving && !MP.IsInMultiplayer)
		{
			return;
		}
		GuildBoundSnapshot value = guildBoundSnapshots.GetValue(__instance, (object _) => new GuildBoundSnapshot());
		IDictionary dictionary = (IDictionary)guildBoundMemory.GetValue(__instance);
		if (Scribe.mode == LoadSaveMode.Saving)
		{
			value.Rows = new List<BetterTradersGuildBoundMemory>();
			foreach (DictionaryEntry item in dictionary)
			{
				if (item.Key is Pawn { Destroyed: false } pawn && guildBoundLaunchable.GetValue(item.Value) is Thing { Destroyed: false } thing)
				{
					value.Rows.Add(new BetterTradersGuildBoundMemory
					{
						Pawn = pawn,
						Launchable = thing,
						Tick = (int)guildBoundTick.GetValue(item.Value)
					});
				}
			}
			value.Rows = value.Rows.OrderBy((BetterTradersGuildBoundMemory x) => x.Pawn.thingIDNumber).ToList();
		}
		Scribe_Collections.Look(ref value.Rows, "mpBoundLaunchables", LookMode.Deep);
		if (Scribe.mode == LoadSaveMode.PostLoadInit)
		{
			dictionary.Clear();
			if (value.Rows != null)
			{
				foreach (BetterTradersGuildBoundMemory row in value.Rows)
				{
					if (row.Pawn != null && !row.Pawn.Destroyed && row.Launchable != null && !row.Launchable.Destroyed)
					{
						object obj = Activator.CreateInstance(guildBoundValue);
						guildBoundLaunchable.SetValue(obj, row.Launchable);
						guildBoundTick.SetValue(obj, row.Tick);
						dictionary[row.Pawn] = obj;
					}
				}
			}
			guildBoundSnapshots.Remove(__instance);
		}
		else if (Scribe.mode == LoadSaveMode.Saving)
		{
			guildBoundSnapshots.Remove(__instance);
		}
	}

	private static void InstallSettings()
	{
		Type type = TypeOf("BetterTradersGuildSettings");
		(string[], Type)[] array = new (string[], Type)[3]
		{
			(GuildBoolSettings, typeof(bool)),
			(GuildIntSettings, typeof(int)),
			(GuildFloatSettings, typeof(float))
		};
		for (int i = 0; i < array.Length; i++)
		{
			(string[], Type) tuple = array[i];
			string[] item = tuple.Item1;
			foreach (string text in item)
			{
				if (AccessTools.Field(type, text)?.FieldType != tuple.Item2)
				{
					throw new MissingFieldException(type.FullName, text);
				}
			}
		}
		MP.RegisterSyncMethod(typeof(BetterTradersGuild), "SyncGuildSettings");
		Patch("BetterTradersGuildMod_ModClass", "DoSettingsWindowContents", "GuildSettingsBeforeDraw", null, null, "GuildSettingsAfterDraw");
		Patch("BetterTradersGuildMod_ModClass", "WriteSettings", null, null, "GuildWriteSettingsTranspiler");
	}

	internal static GuildSettingsSnapshot CaptureGuildSettings()
	{
		object guildSettings = GuildSettings;
		if (guildSettings == null)
		{
			return null;
		}
		GuildSettingsSnapshot guildSettingsSnapshot = new GuildSettingsSnapshot();
		for (int i = 0; i < guildSettingsSnapshot.Bools.Length; i++)
		{
			guildSettingsSnapshot.Bools[i] = (bool)Get(guildSettings, GuildBoolSettings[i]);
		}
		for (int j = 0; j < guildSettingsSnapshot.Ints.Length; j++)
		{
			guildSettingsSnapshot.Ints[j] = (int)Get(guildSettings, GuildIntSettings[j]);
		}
		for (int k = 0; k < guildSettingsSnapshot.Floats.Length; k++)
		{
			guildSettingsSnapshot.Floats[k] = (float)Get(guildSettings, GuildFloatSettings[k]);
		}
		return guildSettingsSnapshot;
	}

	internal static void ApplyGuildSettings(GuildSettingsSnapshot value, bool effects, bool rescaleExpirations)
	{
		object guildSettings = GuildSettings;
		if (guildSettings == null || value == null)
		{
			return;
		}
		int num = (int)Get(guildSettings, "traderRotationIntervalDays");
		for (int i = 0; i < value.Bools.Length; i++)
		{
			Set(guildSettings, GuildBoolSettings[i], value.Bools[i]);
		}
		for (int j = 0; j < value.Ints.Length; j++)
		{
			Set(guildSettings, GuildIntSettings[j], value.Ints[j]);
		}
		for (int k = 0; k < value.Floats.Length; k++)
		{
			Set(guildSettings, GuildFloatSettings[k], value.Floats[k]);
		}
		if (!effects)
		{
			return;
		}
		checked
		{
			using (WorldScope())
			{
				Call(TypeOf("BetterTradersGuildMod"), "ApplyQuestWeightSettings");
				if (rescaleExpirations && num != value.Ints[2])
				{
					object obj = Call(TypeOf("WorldComponents.TradersGuildWorldComponent"), "GetComponent");
					if (obj != null)
					{
						Call(obj, "ScaleExpirationsForIntervalChange", num * 60000, value.Ints[2] * 60000);
					}
				}
				Set(GuildMod, "previousRotationInterval", value.Ints[2]);
				RefreshGuildPower(ApplyGuildPowerDef(value.Ints[1]));
			}
		}
	}

	internal static void RestoreGuildSettings(GuildSettingsSnapshot value)
	{
		if (value != null && GuildSettings != null)
		{
			ApplyGuildSettings(value, effects: false, rescaleExpirations: false);
			Call(TypeOf("BetterTradersGuildMod"), "ApplyQuestWeightSettings");
			Set(GuildMod, "previousRotationInterval", value.Ints[2]);
			ApplyGuildPowerDef(value.Ints[1]);
		}
	}

	internal static void RefreshGuildPowerAfterLoad()
	{
		GuildSettingsSnapshot guildSettingsSnapshot = CaptureGuildSettings();
		if (guildSettingsSnapshot != null)
		{
			RefreshGuildPower(ApplyGuildPowerDef(guildSettingsSnapshot.Ints[1]));
		}
	}

	private static ThingDef ApplyGuildPowerDef(int output)
	{
		ThingDef namedSilentFail = DefDatabase<ThingDef>.GetNamedSilentFail("LifeSupportUnit");
		CompProperties_Power compProperties_Power = namedSilentFail?.GetCompProperties<CompProperties_Power>();
		if (compProperties_Power == null)
		{
			return null;
		}
		if (GuildPowerConsumption == null)
		{
			throw new MissingFieldException(typeof(CompProperties_Power).FullName, "basePowerConsumption");
		}
		GuildPowerConsumption.SetValue(compProperties_Power, (float)(-output));
		return namedSilentFail;
	}

	private static void RefreshGuildPower(ThingDef def)
	{
		if (def == null || Current.ProgramState != ProgramState.Playing)
		{
			return;
		}
		foreach (Map map in Find.Maps)
		{
			if (map.listerThings == null)
			{
				continue;
			}
			using (MapScope(map))
			{
				Thing[] array = map.listerThings.ThingsOfDef(def).ToArray();
				for (int i = 0; i < array.Length; i++)
				{
					array[i].TryGetComp<CompPowerPlant>()?.UpdateDesiredPowerOutput();
				}
			}
		}
	}

	private static void GuildSettingsBeforeDraw(out GuildSettingsSnapshot __state)
	{
		__state = (MP.IsInMultiplayer ? CaptureGuildSettings() : null);
	}

	private static Exception GuildSettingsAfterDraw(Exception __exception, GuildSettingsSnapshot __state)
	{
		if (__state == null)
		{
			return __exception;
		}
		GuildSettingsSnapshot guildSettingsSnapshot = CaptureGuildSettings();
		ApplyGuildSettings(__state, effects: false, rescaleExpirations: false);
		if (__exception == null && Intercept && guildSettingsSnapshot != null && !guildSettingsSnapshot.SameAs(__state))
		{
			SyncGuildSettings(guildSettingsSnapshot.Bools, guildSettingsSnapshot.Ints, guildSettingsSnapshot.Floats);
		}
		return __exception;
	}

	private static void SyncGuildSettings(bool[] bools, int[] ints, float[] floats)
	{
		bool flag = bools == null || bools.Length != 6 || ints == null || ints.Length != 5 || floats == null || floats.Length != 5 || floats.Any((float x) => !float.IsFinite(x));
		if (!flag)
		{
			int num = ints[0];
			bool flag2 = ((num < 0 || num > 10) ? true : false);
			flag = flag2;
		}
		bool flag3 = flag;
		if (!flag3)
		{
			int num = ints[1];
			bool flag2 = ((num < 0 || num > 5000) ? true : false);
			flag3 = flag2;
		}
		bool flag4 = flag3;
		if (!flag4)
		{
			int num = ints[2];
			bool flag2 = ((num < 5 || num > 60) ? true : false);
			flag4 = flag2;
		}
		bool flag5 = flag4;
		if (!flag5)
		{
			int num = ints[3];
			bool flag2 = ((num < 0 || num > 355) ? true : false);
			flag5 = flag2;
		}
		bool flag6 = flag5;
		if (!flag6)
		{
			int num = ints[4];
			bool flag2 = ((num < 0 || num > 505000) ? true : false);
			flag6 = flag2;
		}
		bool flag7 = flag6;
		if (!flag7)
		{
			float num2 = floats[0];
			bool flag2 = ((num2 < 0.5f || num2 > 3f) ? true : false);
			flag7 = flag2;
		}
		bool flag8 = flag7;
		if (!flag8)
		{
			float num2 = floats[1];
			bool flag2 = ((num2 < 0f || num2 > 2f) ? true : false);
			flag8 = flag2;
		}
		bool flag9 = flag8;
		if (!flag9)
		{
			float num2 = floats[2];
			bool flag2 = ((num2 < 0.5f || num2 > 1f) ? true : false);
			flag9 = flag2;
		}
		bool flag10 = flag9;
		if (!flag10)
		{
			float num2 = floats[3];
			bool flag2 = ((num2 < 0f || num2 > 3f) ? true : false);
			flag10 = flag2;
		}
		bool flag11 = flag10;
		if (!flag11)
		{
			float num2 = floats[4];
			bool flag2 = ((num2 < 0f || num2 > 3f) ? true : false);
			flag11 = flag2;
		}
		if (!flag11)
		{
			GuildSettingsSnapshot guildSettingsSnapshot = new GuildSettingsSnapshot
			{
				Bools = bools,
				Ints = ints,
				Floats = floats
			};
			if (!guildSettingsSnapshot.SameAs(CaptureGuildSettings()))
			{
				ApplyGuildSettings(guildSettingsSnapshot, effects: true, rescaleExpirations: true);
			}
		}
	}

	private static void LocalSettingsPower()
	{
		if (!MP.IsInMultiplayer)
		{
			Call(TypeOf("BetterTradersGuildMod"), "ApplyLifeSupportUnitPowerSetting");
		}
	}

	private static void LocalSettingsQuests()
	{
		if (!MP.IsInMultiplayer)
		{
			Call(TypeOf("BetterTradersGuildMod"), "ApplyQuestWeightSettings");
		}
	}

	private static void LocalSettingsScale(object component, int oldTicks, int newTicks)
	{
		if (!MP.IsInMultiplayer)
		{
			Call(component, "ScaleExpirationsForIntervalChange", oldTicks, newTicks);
		}
	}

	private static IEnumerable<CodeInstruction> GuildWriteSettingsTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		int power = 0;
		int quests = 0;
		int scale = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			string text = null;
			if (instruction.operand is MethodInfo methodInfo)
			{
				if (methodInfo.DeclaringType == TypeOf("BetterTradersGuildMod") && methodInfo.Name == "ApplyLifeSupportUnitPowerSetting")
				{
					text = "LocalSettingsPower";
					power++;
				}
				else if (methodInfo.DeclaringType == TypeOf("BetterTradersGuildMod") && methodInfo.Name == "ApplyQuestWeightSettings")
				{
					text = "LocalSettingsQuests";
					quests++;
				}
				else if (methodInfo.DeclaringType == TypeOf("WorldComponents.TradersGuildWorldComponent") && methodInfo.Name == "ScaleExpirationsForIntervalChange")
				{
					text = "LocalSettingsScale";
					scale++;
				}
			}
			yield return (text == null) ? instruction : new CodeInstruction(instruction)
			{
				opcode = OpCodes.Call,
				operand = AccessTools.DeclaredMethod(typeof(BetterTradersGuild), text)
			};
		}
		if (power != 1 || quests != 1 || scale != 1)
		{
			throw new InvalidOperationException($"BTG settings WriteSettings changed: power={power}, quests={quests}, scale={scale}");
		}
	}

	private static void InstallSimulation()
	{
		InitializeGuildScopes();
		isGuildSettlement = AccessTools.MethodDelegate<Func<Settlement, bool>>(AccessTools.DeclaredMethod(TypeOf("TradersGuildHelper"), "IsTradersGuildSettlement") ?? throw new MissingMethodException("TradersGuildHelper", "IsTradersGuildSettlement"));
		Type type = TypeOf("WorldComponents.TradersGuildWorldComponent");
		guildCachedKinds = AccessTools.FieldRefAccess<Dictionary<int, string>>(type, "cachedTraderKinds");
		guildCachedExpirations = AccessTools.FieldRefAccess<Dictionary<int, int>>(type, "cacheExpirationTicks");
		Patch("WorldComponents.TradersGuildWorldComponent", "CacheTraderKind", "GuildCacheWritePrefix");
		Patch("WorldComponents.TradersGuildWorldComponent", "TryGetCachedTraderKind", "GuildCacheReadPrefix", null, "GuildWorldClockTranspiler");
		string[] array = new string[3] { "GetVirtualLastStockTicks", "GetEffectiveLastStockTicks", "ShouldRegenerateNow" };
		foreach (string method in array)
		{
			Patch("Helpers.TradersGuildTraderRotation", method, null, null, "GuildWorldClockTranspiler");
		}
		Patch("Patches.SettlementPatches.SettlementGetInspectString", "Postfix", null, null, "GuildWorldClockTranspiler");
		guildRegenerating = AccessTools.DeclaredField(TypeOf("Patches.SettlementPatches.SettlementTraderTrackerRegenerateStock"), "regeneratingSettlements") ?? throw new MissingFieldException("SettlementTraderTrackerRegenerateStock", "regeneratingSettlements");
		guildPendingAlignment = AccessTools.DeclaredField(TypeOf("Patches.SettlementPatches.SettlementTraderTrackerRegenerateStockAlignment"), "pendingAlignments") ?? throw new MissingFieldException("SettlementTraderTrackerRegenerateStockAlignment", "pendingAlignments");
		MethodInfo original = AccessTools.DeclaredMethod(typeof(Settlement_TraderTracker), "RegenerateStock") ?? throw new MissingMethodException(typeof(Settlement_TraderTracker).FullName, "RegenerateStock");
		MpCompat.harmony.Patch(original, new HarmonyMethod(typeof(BetterTradersGuild), "GuildStockPrefix")
		{
			priority = 800
		}, null, null, new HarmonyMethod(typeof(BetterTradersGuild), "GuildStockFinalizer"));
		Patch("Comps.CompPromoteOnHack", "Promote", "GuildPromotionPrefix", null, null, "GuildScopeFinalizer");
	}

	private static bool GuildCacheWritePrefix()
	{
		return !Intercept;
	}

	private static bool GuildCacheReadPrefix(object __instance, int settlementId, ref TraderKindDef traderKind, ref bool __result)
	{
		if (!Intercept)
		{
			return true;
		}
		traderKind = null;
		__result = false;
		Dictionary<int, string> dictionary = guildCachedKinds(__instance);
		Dictionary<int, int> dictionary2 = guildCachedExpirations(__instance);
		if (dictionary != null && dictionary.TryGetValue(settlementId, out var value) && (dictionary2 == null || !dictionary2.TryGetValue(settlementId, out var value2) || ReadWorldTicks() < value2))
		{
			traderKind = DefDatabase<TraderKindDef>.GetNamedSilentFail(value);
			__result = traderKind != null;
		}
		return false;
	}

	private static int GuildWorldClock(TickManager manager)
	{
		if (!MP.IsInMultiplayer)
		{
			return manager.TicksGame;
		}
		return ReadWorldTicks();
	}

	private static IEnumerable<CodeInstruction> GuildWorldClockTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo original = AccessTools.PropertyGetter(typeof(TickManager), "TicksGame");
		MethodInfo replacement = AccessTools.DeclaredMethod(typeof(BetterTradersGuild), "GuildWorldClock");
		int count = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(original))
			{
				instruction.opcode = OpCodes.Call;
				instruction.operand = replacement;
				count++;
			}
			yield return instruction;
		}
		if (count == 0)
		{
			throw new InvalidOperationException("BTG world clock target no longer reads game ticks");
		}
	}

	private static void GuildStockPrefix(Settlement_TraderTracker __instance, out GuildStockState __state)
	{
		__state = null;
		if (MP.IsInMultiplayer && __instance.settlement != null && isGuildSettlement(__instance.settlement))
		{
			GuildStockState guildStockState = new GuildStockState
			{
				Id = __instance.settlement.ID,
				Regenerating = ((ThreadLocal<HashSet<int>>)guildRegenerating.GetValue(null)).Value,
				Alignments = ((ThreadLocal<Dictionary<int, int>>)guildPendingAlignment.GetValue(null)).Value
			};
			guildStockState.WasRegenerating = guildStockState.Regenerating.Contains(guildStockState.Id);
			guildStockState.HadAlignment = guildStockState.Alignments.TryGetValue(guildStockState.Id, out guildStockState.PreviousAlignment);
			guildStockState.Scope = WorldScope();
			__state = guildStockState;
		}
	}

	private static Exception GuildStockFinalizer(Exception __exception, GuildStockState __state)
	{
		if (__state == null)
		{
			return __exception;
		}
		try
		{
			if (__state.WasRegenerating)
			{
				__state.Regenerating.Add(__state.Id);
			}
			else
			{
				__state.Regenerating.Remove(__state.Id);
			}
			if (__state.HadAlignment)
			{
				__state.Alignments[__state.Id] = __state.PreviousAlignment;
			}
			else
			{
				__state.Alignments.Remove(__state.Id);
			}
		}
		finally
		{
			__state.Scope.Dispose();
		}
		return __exception;
	}

	private static void GuildPromotionPrefix(ThingComp __instance, Faction taker, out IDisposable __state)
	{
		object obj;
		if (MP.IsInMultiplayer)
		{
			Map map = __instance.parent?.Map;
			if (map != null)
			{
				obj = MapFactionScope(map, taker);
				goto IL_0028;
			}
		}
		obj = null;
		goto IL_0028;
		IL_0028:
		__state = (IDisposable)obj;
	}

	private static Exception GuildScopeFinalizer(Exception __exception, IDisposable __state)
	{
		__state?.Dispose();
		return __exception;
	}

	private static void InitializeGuildScopes()
	{
		Type type = AccessTools.TypeByName("Multiplayer.Client.AsyncTimeComp") ?? throw new TypeLoadException("AsyncTimeComp");
		Type type2 = AccessTools.TypeByName("Multiplayer.Client.AsyncTime.AsyncWorldTimeComp") ?? throw new TypeLoadException("AsyncWorldTimeComp");
		FieldInfo field = AccessTools.DeclaredField("Multiplayer.Client.Multiplayer:game") ?? throw new MissingFieldException("Multiplayer:game");
		FieldInfo fieldInfo = AccessTools.DeclaredField("Multiplayer.Client.MultiplayerGame:asyncWorldTimeComp") ?? throw new MissingFieldException("MultiplayerGame:asyncWorldTimeComp");
		guildWorld = AccessTools.FieldRefAccess<object>(fieldInfo.DeclaringType, fieldInfo.Name);
		guildTickingMap = AccessTools.DeclaredField(type, "tickingMap") ?? throw new MissingFieldException(type.FullName, "tickingMap");
		guildMapGetter = AccessTools.DeclaredMethod("Multiplayer.Client.Extensions:AsyncTime") ?? throw new MissingMethodException("Extensions:AsyncTime");
		guildMapPre = AccessTools.DeclaredMethod(type, "PreContext") ?? throw new MissingMethodException(type.FullName, "PreContext");
		guildMapPost = AccessTools.DeclaredMethod(type, "PostContext") ?? throw new MissingMethodException(type.FullName, "PostContext");
		guildWorldPre = AccessTools.DeclaredMethod(type2, "PreContextForCallback") ?? throw new MissingMethodException(type2.FullName, "PreContextForCallback");
		guildWorldPost = AccessTools.DeclaredMethod(type2, "PostContext") ?? throw new MissingMethodException(type2.FullName, "PostContext");
		Type type3 = AccessTools.TypeByName("Multiplayer.Client.Factions.FactionExtensions") ?? throw new TypeLoadException("FactionExtensions");
		guildPushFaction = AccessTools.DeclaredMethod(type3, "PushFaction", new Type[3]
		{
			typeof(Map),
			typeof(Faction),
			typeof(bool)
		}) ?? throw new MissingMethodException(type3.FullName, "PushFaction");
		guildPopFaction = AccessTools.DeclaredMethod(type3, "PopFaction", new Type[1] { typeof(Map) }) ?? throw new MissingMethodException(type3.FullName, "PopFaction");
		DynamicMethod dynamicMethod = new DynamicMethod("BTG_GetMultiplayerGame", typeof(object), Type.EmptyTypes, typeof(BetterTradersGuild), skipVisibility: true);
		ILGenerator iLGenerator = dynamicMethod.GetILGenerator();
		iLGenerator.Emit(OpCodes.Ldsfld, field);
		iLGenerator.Emit(OpCodes.Ret);
		guildGame = (Func<object>)dynamicMethod.CreateDelegate(typeof(Func<object>));
		MethodInfo methodInfo = AccessTools.DeclaredMethod("Multiplayer.Client.SimulationTimeContext:ReadTicks") ?? throw new MissingMethodException("SimulationTimeContext:ReadTicks");
		DynamicMethod dynamicMethod2 = new DynamicMethod("BTG_ReadOwnerTicks", typeof(int), new Type[1] { typeof(object) }, typeof(BetterTradersGuild), skipVisibility: true);
		ILGenerator iLGenerator2 = dynamicMethod2.GetILGenerator();
		iLGenerator2.Emit(OpCodes.Ldarg_0);
		iLGenerator2.Emit(OpCodes.Castclass, methodInfo.GetParameters()[0].ParameterType);
		iLGenerator2.Emit(OpCodes.Call, methodInfo);
		iLGenerator2.Emit(OpCodes.Ret);
		guildOwnerTicks = (Func<object, int>)dynamicMethod2.CreateDelegate(typeof(Func<object, int>));
	}

	private static object GuildWorldOwner()
	{
		object obj = guildGame();
		return ((obj == null) ? null : guildWorld(obj)) ?? throw new InvalidOperationException("BTG needs an initialized MP world owner");
	}

	private static int ReadWorldTicks()
	{
		if (!MP.IsInMultiplayer)
		{
			return Find.TickManager.TicksGame;
		}
		return guildOwnerTicks(GuildWorldOwner());
	}

	private static IDisposable WorldScope()
	{
		if (!MP.IsInMultiplayer)
		{
			return PatchingUtilities.EmptySimulationScope.Instance;
		}
		return new PatchingUtilities.SimulationScope(GuildWorldOwner(), guildWorldPre, guildWorldPost, null, guildTickingMap, "Missing BTG MP simulation owner");
	}

	private static IDisposable MapScope(Map map)
	{
		if (map == null)
		{
			throw new ArgumentNullException("map");
		}
		if (!MP.IsInMultiplayer)
		{
			return PatchingUtilities.EmptySimulationScope.Instance;
		}
		return new PatchingUtilities.SimulationScope(guildMapGetter.Invoke(null, new object[1] { map }), guildMapPre, guildMapPost, map, guildTickingMap, "Missing BTG MP simulation owner");
	}

	private static IDisposable MapFactionScope(Map map, Faction faction)
	{
		IDisposable disposable = MapScope(map);
		if (!MP.IsInMultiplayer || faction == null)
		{
			return disposable;
		}
		try
		{
			guildPushFaction.Invoke(null, new object[3] { map, faction, true });
		}
		catch
		{
			disposable.Dispose();
			throw;
		}
		return new GuildFactionSimulationScope(disposable, map);
	}

	private static IDisposable WorldFactionScope(Faction faction)
	{
		IDisposable disposable = WorldScope();
		if (!MP.IsInMultiplayer || faction == null)
		{
			return disposable;
		}
		try
		{
			guildPushFaction.Invoke(null, new object[3] { null, faction, true });
		}
		catch
		{
			disposable.Dispose();
			throw;
		}
		return new GuildFactionSimulationScope(disposable, null);
	}
}
