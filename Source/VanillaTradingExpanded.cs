using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Xml;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;
using Verse.AI;

namespace Multiplayer.Compat;

[MpCompatFor("VanillaExpanded.VanillaTradingExpanded")]
public class VanillaTradingExpanded
{
	private sealed class TradingWorldClock : IDisposable
	{
		private readonly PatchingUtilities.TimeSnapshot? previous;

		internal TradingWorldClock()
		{
			if (MP.IsInMultiplayer && PatchingUtilities.IsAsyncTime && Current.Game != null)
			{
				previous = PatchingUtilities.TimeSnapshot.GetAndSetFromWorld();
			}
		}

		public void Dispose()
		{
			previous?.Set();
		}
	}

	internal sealed class TradingSettingsSnapshot : IExposable
	{
		internal float[] Floats = (float[])TradingFloatDefaults.Clone();

		internal int[] Ints = new int[3] { 30, 30, 10 };

		internal bool[] Bools = new bool[3];

		public void ExposeData()
		{
			for (int i = 0; i < Floats.Length; i++)
			{
				Scribe_Values.Look(ref Floats[i], TradingFloatSettings[i], TradingFloatDefaults[i]);
			}
			for (int j = 0; j < Ints.Length; j++)
			{
				Scribe_Values.Look(ref Ints[j], TradingIntSettings[j], (j == 2) ? 10 : 30);
			}
			for (int k = 0; k < Bools.Length; k++)
			{
				Scribe_Values.Look(ref Bools[k], TradingBoolSettings[k], defaultValue: false);
			}
		}

		internal bool SameAs(TradingSettingsSnapshot other)
		{
			if (other != null && Floats.SequenceEqual(other.Floats) && Ints.SequenceEqual(other.Ints))
			{
				return Bools.SequenceEqual(other.Bools);
			}
			return false;
		}
	}

	private sealed class TradingCaravanScope : IDisposable
	{
		private readonly Map previous;

		private readonly IDisposable simulation;

		private bool disposed;

		internal TradingCaravanScope(Map map)
		{
			previous = (Map)tradingTickingMap.GetValue(null);
			simulation = SimulationMapScope(map);
			try
			{
				tradingTickingMap.SetValue(null, map);
			}
			catch
			{
				simulation.Dispose();
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
				simulation.Dispose();
			}
			finally
			{
				tradingTickingMap.SetValue(null, previous);
			}
		}
	}

	private sealed class TradingSimulationScope(object owner, MethodInfo post) : IDisposable
	{
		private bool disposed;

		public void Dispose()
		{
			if (!disposed)
			{
				disposed = true;
				post.Invoke(owner, null);
			}
		}
	}

	private sealed class EmptyTradingScope : IDisposable
	{
		internal static readonly EmptyTradingScope Instance = new EmptyTradingScope();

		public void Dispose()
		{
		}
	}

	private sealed class LocalTransaction
	{
		internal Game game;

		internal int ticket;

		internal bool submitted;
	}

	private sealed class LocalTerminalJob
	{
		internal System.WeakReference<Game> game;

		internal int loadId;

		internal bool issuedBySelf;
	}

	private sealed class ReadUiState : IDisposable
	{
		private readonly IDisposable clock = WorldTime();

		private readonly bool vanilla = (bool)PatchingUtilities.Get(TypeOf("StatWorker_GetBaseValueFor_Patch"), "outputOnlyVanilla");

		private readonly bool curves = (bool)PatchingUtilities.Get(TypeOf("SimpleCurveDrawer_Patch"), "modify");

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
				PatchingUtilities.Set(TypeOf("StatWorker_GetBaseValueFor_Patch"), "outputOnlyVanilla", vanilla);
				PatchingUtilities.Set(TypeOf("SimpleCurveDrawer_Patch"), "modify", curves);
			}
			finally
			{
				clock.Dispose();
			}
		}
	}

	private sealed class StockUiSnapshot
	{
		internal readonly List<(object company, bool follows)> values = new List<(object, bool)>();

		internal ReadUiState read;

		internal bool restored;
	}

	private sealed class ContractUiSnapshot
	{
		internal object window;

		internal object[] players;

		internal readonly List<(object contract, Map map, string key)> npc = new List<(object, Map, string)>();

		internal ReadUiState read;

		internal bool restored;
	}

	private const string Ns = "VanillaTradingExpanded.";

	private static FieldInfo managerInstance;

	private static Type recoveryTerminalType;

	private static MethodInfo recoveryFailureReason;

	private static readonly string[] TradingFloatSettings = new string[10] { "newsSpawnRate", "playerTransactionImpactMultiplier", "npcContractFulfilmentMultiplier", "playerContractFulfilmentMultiplier", "newsPriceImpactMultiplier", "amountOfItemsToFluctuate", "maximumMarketValueOfItemsInContracts", "maximumMarketValueOfItemsPerPlayerWealth", "minPriceFluctuation", "maxPriceFluctuation" };

	private static readonly string[] TradingIntSettings = new string[3] { "maxNPCContractCount", "maxCompanyCount", "maxMarkupOnNPCContract" };

	private static readonly string[] TradingBoolSettings = new string[3] { "contractBlackListCollectibles", "caravanLessContractItemPickup", "enablePriceFluctuationRestriction" };

	private static readonly float[] TradingFloatDefaults = new float[10] { 3f, 1f, 1f, 1f, 1f, 0.2f, 10000f, 0.01f, 0.95f, 2f };

	private const int TradingMarkupSeed = 1448363287;

	private static Action initializeTradingMarkup;

	private static FieldInfo tradingMarkupTable;

	private static object deterministicTradingMarkup;

	private static AccessTools.FieldRef<object, object> tradingPriceModifiers;

	private static Func<object, int> tradingOwnerTicks;

	private static Func<object> tradingGameRef;

	private static AccessTools.FieldRef<object, object> tradingWorldRef;

	private static AccessTools.FieldRef<object, int> tradingWorldAbsoluteBase;

	private static FieldInfo tradingMultiplayerGame;

	private static FieldInfo tradingWorldTime;

	private static FieldInfo tradingTickingMap;

	private static MethodInfo tradingMapTime;

	private static MethodInfo tradingMapPre;

	private static MethodInfo tradingMapPost;

	private static MethodInfo tradingWorldPre;

	private static MethodInfo tradingWorldPost;

	private static readonly ConditionalWeakTable<object, LocalTransaction> localTransactions = new ConditionalWeakTable<object, LocalTransaction>();

	private static readonly ConditionalWeakTable<Job, LocalTerminalJob> localTerminalJobs = new ConditionalWeakTable<Job, LocalTerminalJob>();

	private static Type terminalJobDriverType;

	private static Type paymentGainsType;

	private static Type paymentCostsType;

	[ThreadStatic]
	private static bool reopeningPayment;

	[ThreadStatic]
	private static ContractUiSnapshot activeContractUi;

	[ThreadStatic]
	private static string paymentOwner;

	private static object Manager => managerInstance?.GetValue(null);

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

	private static object TradingSettings => PatchingUtilities.Get(TypeOf("VanillaTradingExpandedMod"), "settings");

	internal static bool HasTradingSettings => AccessTools.TypeByName("VanillaTradingExpanded.VanillaTradingExpandedMod") != null;

	private static TradingPaymentState PaymentState => Current.Game?.GetComponent<TradingPaymentState>();

	public VanillaTradingExpanded(ModContentPack mod)
	{
		CommandAuthorityBridge.Install();
		managerInstance = AccessTools.DeclaredField(TypeOf("TradingManager"), "Instance") ?? throw new MissingFieldException("VanillaTradingExpanded.TradingManager", "Instance");
		PatchingUtilities.SetupAsyncTime();
		InstallSettings();
		InstallSimulation();
		InstallUI();
	}

	private static Type TypeOf(string name)
	{
		return AccessTools.TypeByName("VanillaTradingExpanded." + name) ?? throw new TypeLoadException("VanillaTradingExpanded." + name);
	}

	private static void Patch(string type, string method, string prefix = null, string postfix = null, string transpiler = null, string finalizer = null)
	{
		MethodInfo original = AccessTools.DeclaredMethod(TypeOf(type), method) ?? throw new MissingMethodException("VanillaTradingExpanded." + type, method);
		MpCompat.harmony.Patch(original, Hook(prefix), Hook(postfix), Hook(transpiler), Hook(finalizer));
		static HarmonyMethod Hook(string name)
		{
			if (name != null)
			{
				return new HarmonyMethod(typeof(VanillaTradingExpanded), name);
			}
			return null;
		}
	}

	private static IDisposable WorldTime()
	{
		return new TradingWorldClock();
	}

	private static void InstallPaymentRecovery()
	{
		MP.RegisterSyncMethod(typeof(VanillaTradingExpanded), "SyncRecoverPayment").SetVersion(3);
		recoveryTerminalType = TypeOf("Building_TradingTerminal");
		recoveryFailureReason = AccessTools.DeclaredMethod(recoveryTerminalType, "GetFailureReason", new Type[1] { typeof(Pawn) }) ?? throw new MissingMethodException(recoveryTerminalType.FullName, "GetFailureReason");
		Patch("Building_TradingTerminal", "GetFloatMenuOptions", null, "RecoveryOptionsPostfix");
	}

	private static bool RecoveryTerminalAvailable(Thing terminal, Pawn pawn)
	{
		if (terminal == null || !recoveryTerminalType.IsInstanceOfType(terminal) || terminal.Destroyed || !terminal.Spawned || pawn == null || pawn.Destroyed || !pawn.Spawned || pawn.Dead || pawn.Map != terminal.Map || !Find.Maps.Contains(terminal.Map) || !(bool)PatchingUtilities.Get(terminal, "CanUseTerminalNow"))
		{
			return false;
		}
		return recoveryFailureReason.Invoke(terminal, new object[1] { pawn }) == null;
	}

	private static bool RecoverablePayment(TradingPendingPayment record)
	{
		if (record != null)
		{
			if (record.kind == 1 || record.map != null)
			{
				if (record.map != null)
				{
					return Find.Maps.Contains(record.map);
				}
				return false;
			}
			return true;
		}
		return false;
	}

	private static void RecoveryOptionsPostfix(Thing __instance, Pawn __0, ref IEnumerable<FloatMenuOption> __result)
	{
		if (!Intercept || PaymentState == null || !RecoveryTerminalAvailable(__instance, __0))
		{
			return;
		}
		IEnumerable<FloatMenuOption> enumerable = __result;
		Game game = Current.Game;
		Thing terminal = __instance;
		Pawn pawn = __0;
		TradingPendingPayment[] array = PaymentState.pending.Where((TradingPendingPayment record) => RecoverablePayment(record) && !CanPresentPayment(record)).ToArray();
		if (array.Length == 0)
		{
			return;
		}
		bool chinese = LanguageDatabase.activeLanguage?.LegacyFolderName == "ChineseSimplified";
		__result = (enumerable ?? Enumerable.Empty<FloatMenuOption>()).Concat(array.Select((TradingPendingPayment record) =>
		{
			int id = record.id;
			string expectedOwner = record.owner;
			string value = ((!string.IsNullOrEmpty(expectedOwner)) ? expectedOwner : (chinese ? "未指定" : "Unassigned"));
			string value2 = record.gain.ToString(CultureInfo.CurrentCulture);
			string value3 = record.cost.ToString(CultureInfo.CurrentCulture);
			string label = (chinese ? $"接手待结算款项 #{id}（{value}；收入 {value2}，支出 {value3}）" : $"Take over pending payment #{id} ({value}; income {value2}, cost {value3})");
			return new FloatMenuOption(label, () =>
			{
				if (Current.Game == game && RecoveryTerminalAvailable(terminal, pawn))
				{
					string text = (chinese ? (label + "\n确认后由你选择账户继续结算。") : (label + "\nChoose the accounts and continue settlement after taking over."));
					Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(text, () =>
					{
						if (Current.Game == game)
						{
							SyncRecoverPayment(id, expectedOwner, MP.PlayerName, terminal, pawn);
						}
					}));
				}
			});
		}));
	}

	private static void SyncRecoverPayment(int id, string expectedOwner, string owner, Thing terminal, Pawn pawn)
	{
		if (MP.IsInMultiplayer && MP.IsExecutingSyncCommand && !string.IsNullOrWhiteSpace(owner) && owner.Length <= 128 && RecoveryTerminalAvailable(terminal, pawn))
		{
			try
			{
				XmlConvert.VerifyXmlChars(owner);
			}
			catch (XmlException)
			{
				return;
			}
			TradingPaymentState paymentState = PaymentState;
			TradingPendingPayment tradingPendingPayment = paymentState?.pending.FirstOrDefault((TradingPendingPayment record) => record.id == id);
			if (RecoverablePayment(tradingPendingPayment) && string.Equals(tradingPendingPayment.owner, expectedOwner, StringComparison.Ordinal) && !string.Equals(tradingPendingPayment.owner, owner, StringComparison.Ordinal))
			{
				ClosePaymentWindows(id);
				tradingPendingPayment.owner = owner;
				paymentState.RequestPresentation();
			}
		}
	}

	private static void InstallSettings()
	{
		MP.RegisterSyncMethod(typeof(VanillaTradingExpanded), "SyncTradingSettings");
		MP.RegisterSyncMethod(typeof(VanillaTradingExpanded), "SyncResetTradingPrices");
		Patch("VanillaTradingExpandedMod", "DoSettingsWindowContents", "TradingSettingsBeforeDraw", null, "TradingSettingsResetTranspiler", "TradingSettingsAfterDraw");
	}

	internal static TradingSettingsSnapshot CaptureTradingSettings()
	{
		object tradingSettings = TradingSettings;
		TradingSettingsSnapshot tradingSettingsSnapshot = new TradingSettingsSnapshot();
		for (int i = 0; i < tradingSettingsSnapshot.Floats.Length; i++)
		{
			tradingSettingsSnapshot.Floats[i] = (float)PatchingUtilities.Get(tradingSettings, TradingFloatSettings[i]);
		}
		for (int j = 0; j < tradingSettingsSnapshot.Ints.Length; j++)
		{
			tradingSettingsSnapshot.Ints[j] = (int)PatchingUtilities.Get(tradingSettings, TradingIntSettings[j]);
		}
		for (int k = 0; k < tradingSettingsSnapshot.Bools.Length; k++)
		{
			tradingSettingsSnapshot.Bools[k] = (bool)PatchingUtilities.Get(tradingSettings, TradingBoolSettings[k]);
		}
		return tradingSettingsSnapshot;
	}

	internal static void ApplyTradingSettings(TradingSettingsSnapshot value, bool rebuild)
	{
		if (value != null)
		{
			object tradingSettings = TradingSettings;
			for (int i = 0; i < value.Floats.Length; i++)
			{
				PatchingUtilities.Set(tradingSettings, TradingFloatSettings[i], value.Floats[i]);
			}
			for (int j = 0; j < value.Ints.Length; j++)
			{
				PatchingUtilities.Set(tradingSettings, TradingIntSettings[j], value.Ints[j]);
			}
			for (int k = 0; k < value.Bools.Length; k++)
			{
				PatchingUtilities.Set(tradingSettings, TradingBoolSettings[k], value.Bools[k]);
			}
			if (rebuild)
			{
				PatchingUtilities.Call(TypeOf("Utils"), "InitMarkupValues");
			}
		}
	}

	private static void TradingSettingsBeforeDraw(out TradingSettingsSnapshot __state)
	{
		__state = (MP.IsInMultiplayer ? CaptureTradingSettings() : null);
	}

	private static Exception TradingSettingsAfterDraw(Exception __exception, TradingSettingsSnapshot __state)
	{
		if (__state == null)
		{
			return __exception;
		}
		TradingSettingsSnapshot tradingSettingsSnapshot = CaptureTradingSettings();
		ApplyTradingSettings(__state, rebuild: false);
		if (__exception == null && !tradingSettingsSnapshot.SameAs(__state))
		{
			SyncTradingSettings(tradingSettingsSnapshot.Floats, tradingSettingsSnapshot.Ints, tradingSettingsSnapshot.Bools);
		}
		return __exception;
	}

	private static void SyncTradingSettings(float[] floats, int[] ints, bool[] bools)
	{
		if (floats?.Length == TradingFloatSettings.Length && ints?.Length == TradingIntSettings.Length && bools?.Length == TradingBoolSettings.Length && !floats.Any((float value) => !float.IsFinite(value) || value < 0f) && !ints.Any((int value) => value < 1) && ints[0] <= 200 && ints[1] <= 200 && ints[2] <= 100 && !(floats[0] > 100f) && !(floats[1] > 10f) && !(floats[2] > 10f) && !(floats[3] > 10f) && !(floats[4] > 10f) && !(floats[5] > 1f) && !(floats[6] > 1E+09f) && !(floats[7] > 1f) && !(floats[8] > 1f) && !(floats[9] > 3f))
		{
			ApplyTradingSettings(new TradingSettingsSnapshot
			{
				Floats = floats,
				Ints = ints,
				Bools = bools
			}, rebuild: true);
		}
	}

	private static void SyncResetTradingPrices()
	{
		if (Manager == null)
		{
			return;
		}
		using (WorldTime())
		{
			((IDictionary)PatchingUtilities.Get(Manager, "priceModifiers")).Clear();
			((IDictionary)PatchingUtilities.Get(Manager, "thingsAffectedBySoldPurchasedMarketValue")).Clear();
			((IDictionary)PatchingUtilities.Get(Manager, "priceHistoryRecorders")).Clear();
			PatchingUtilities.Call(Manager, "GenerateAllPriceRecorders");
		}
	}

	private static void SettingsClearDictionary(object dictionary)
	{
		if (!MP.IsInMultiplayer)
		{
			((IDictionary)dictionary).Clear();
		}
	}

	private static void SettingsGenerateRecorders(object manager)
	{
		if (MP.IsInMultiplayer)
		{
			SyncResetTradingPrices();
		}
		else
		{
			PatchingUtilities.Call(manager, "GenerateAllPriceRecorders");
		}
	}

	private static IEnumerable<CodeInstruction> TradingSettingsResetTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		int clears = 0;
		int generates = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.operand is MethodInfo { Name: "Clear", DeclaringType: var declaringType } methodInfo && (declaringType?.IsGenericType ?? false) && methodInfo.DeclaringType.GetGenericTypeDefinition() == typeof(Dictionary<, >))
			{
				yield return new CodeInstruction(instruction)
				{
					opcode = OpCodes.Call,
					operand = AccessTools.DeclaredMethod(typeof(VanillaTradingExpanded), "SettingsClearDictionary")
				};
				clears++;
			}
			else if (instruction.operand is MethodInfo methodInfo2 && methodInfo2.DeclaringType == TypeOf("TradingManager") && methodInfo2.Name == "GenerateAllPriceRecorders")
			{
				yield return new CodeInstruction(instruction)
				{
					opcode = OpCodes.Call,
					operand = AccessTools.DeclaredMethod(typeof(VanillaTradingExpanded), "SettingsGenerateRecorders")
				};
				generates++;
			}
			else
			{
				yield return instruction;
			}
		}
		if (clears != 3 || generates != 1)
		{
			throw new InvalidOperationException($"VTE settings reset changed: {clears} clears, {generates} recorder calls");
		}
	}

	private static void InstallSimulation()
	{
		Type type = TypeOf("Utils");
		initializeTradingMarkup = AccessTools.MethodDelegate<Action>(AccessTools.DeclaredMethod(type, "InitMarkupValues") ?? throw new MissingMethodException(type.FullName, "InitMarkupValues"));
		tradingMarkupTable = AccessTools.DeclaredField(type, "markupRange") ?? throw new MissingFieldException(type.FullName, "markupRange");
		tradingPriceModifiers = AccessTools.FieldRefAccess<object>(TypeOf("TradingManager"), "priceModifiers");
		Type type2 = AccessTools.TypeByName("Multiplayer.Client.AsyncTimeComp") ?? throw new TypeLoadException("Multiplayer.Client.AsyncTimeComp");
		Type type3 = AccessTools.TypeByName("Multiplayer.Client.AsyncTime.AsyncWorldTimeComp") ?? throw new TypeLoadException("Multiplayer.Client.AsyncTime.AsyncWorldTimeComp");
		tradingMultiplayerGame = AccessTools.DeclaredField("Multiplayer.Client.Multiplayer:game") ?? throw new MissingFieldException("Multiplayer.Client.Multiplayer", "game");
		tradingWorldTime = AccessTools.DeclaredField("Multiplayer.Client.MultiplayerGame:asyncWorldTimeComp") ?? throw new MissingFieldException("Multiplayer.Client.MultiplayerGame", "asyncWorldTimeComp");
		tradingMapTime = AccessTools.DeclaredMethod("Multiplayer.Client.Extensions:AsyncTime") ?? throw new MissingMethodException("Multiplayer.Client.Extensions", "AsyncTime");
		tradingMapPre = AccessTools.DeclaredMethod(type2, "PreContext") ?? throw new MissingMethodException(type2.FullName, "PreContext");
		tradingMapPost = AccessTools.DeclaredMethod(type2, "PostContext") ?? throw new MissingMethodException(type2.FullName, "PostContext");
		tradingTickingMap = AccessTools.DeclaredField(type2, "tickingMap") ?? throw new MissingFieldException(type2.FullName, "tickingMap");
		tradingWorldPre = AccessTools.DeclaredMethod(type3, "PreContextForCallback") ?? throw new MissingMethodException(type3.FullName, "PreContextForCallback");
		tradingWorldPost = AccessTools.DeclaredMethod(type3, "PostContext") ?? throw new MissingMethodException(type3.FullName, "PostContext");
		DynamicMethod dynamicMethod = new DynamicMethod("VTE_GetMultiplayerGame", typeof(object), Type.EmptyTypes, typeof(VanillaTradingExpanded), skipVisibility: true);
		ILGenerator iLGenerator = dynamicMethod.GetILGenerator();
		iLGenerator.Emit(OpCodes.Ldsfld, tradingMultiplayerGame);
		iLGenerator.Emit(OpCodes.Ret);
		tradingGameRef = (Func<object>)dynamicMethod.CreateDelegate(typeof(Func<object>));
		tradingWorldRef = AccessTools.FieldRefAccess<object>(tradingWorldTime.DeclaringType, "asyncWorldTimeComp");
		tradingWorldAbsoluteBase = AccessTools.FieldRefAccess<int>(type3, "gameStartAbsTickWorld");
		MethodInfo methodInfo = AccessTools.DeclaredMethod("Multiplayer.Client.SimulationTimeContext:ReadTicks") ?? throw new MissingMethodException("Multiplayer.Client.SimulationTimeContext", "ReadTicks");
		DynamicMethod dynamicMethod2 = new DynamicMethod("VTE_ReadOwnerTicks", typeof(int), new Type[1] { typeof(object) }, typeof(VanillaTradingExpanded), skipVisibility: true);
		ILGenerator iLGenerator2 = dynamicMethod2.GetILGenerator();
		iLGenerator2.Emit(OpCodes.Ldarg_0);
		iLGenerator2.Emit(OpCodes.Castclass, methodInfo.GetParameters()[0].ParameterType);
		iLGenerator2.Emit(OpCodes.Call, methodInfo);
		iLGenerator2.Emit(OpCodes.Ret);
		tradingOwnerTicks = (Func<object, int>)dynamicMethod2.CreateDelegate(typeof(Func<object, int>));
		Patch("Utils", "InitMarkupValues", "BeforeTradingMarkup", null, null, "AfterTradingMarkup");
		Patch("TradingManager", "Startup", "EnsureTradingMarkup");
		Patch("TradingManager", "TryGetModifiedPriceFor", "DoNotInitializeEconomyFromInterface");
		Patch("Bank", "TakeLoan", null, null, "UseWorldAbsoluteTradingClock");
		Patch("Loan", "get_DaysUntil", null, null, "UseWorldAbsoluteTradingClock");
		Patch("IncidentWorker_CaravanArriveForItems", "TrySpawnCaravanForContract", "BeforeTradingCaravan", null, null, "AfterTradingCaravan");
	}

	private static void BeforeTradingMarkup(out bool __state)
	{
		__state = false;
		if (MP.IsInMultiplayer)
		{
			Rand.PushState(1448363287);
			__state = true;
		}
	}

	private static void AfterTradingMarkup(bool __state, Exception __exception)
	{
		if (!__state)
		{
			deterministicTradingMarkup = null;
			return;
		}
		try
		{
			deterministicTradingMarkup = ((__exception == null) ? tradingMarkupTable.GetValue(null) : null);
		}
		finally
		{
			Rand.PopState();
		}
	}

	private static void EnsureTradingMarkup()
	{
		if (MP.IsInMultiplayer && (deterministicTradingMarkup == null || deterministicTradingMarkup != tradingMarkupTable.GetValue(null)))
		{
			initializeTradingMarkup();
		}
	}

	private static bool DoNotInitializeEconomyFromInterface(object __instance, ref float price, ref bool __result)
	{
		if (!Intercept || tradingPriceModifiers(__instance) != null)
		{
			return true;
		}
		price = -1f;
		__result = false;
		return false;
	}

	private static int TradingWorldAbs(TickManager manager)
	{
		if (!MP.IsInMultiplayer)
		{
			return manager.TicksAbs;
		}
		object obj = tradingGameRef();
		object obj2 = ((obj == null) ? null : tradingWorldRef(obj));
		if (obj2 == null)
		{
			throw new InvalidOperationException("VTE loan clock requires an initialized Multiplayer world");
		}
		return tradingOwnerTicks(obj2) + tradingWorldAbsoluteBase(obj2);
	}

	private static IEnumerable<CodeInstruction> UseWorldAbsoluteTradingClock(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo original = AccessTools.PropertyGetter(typeof(TickManager), "TicksAbs");
		MethodInfo replacement = AccessTools.DeclaredMethod(typeof(VanillaTradingExpanded), "TradingWorldAbs");
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
		if (count != 1)
		{
			throw new InvalidOperationException("VTE world absolute-clock contract changed: " + count);
		}
	}

	private static void WorldScope(Action action)
	{
		using (SimulationWorldScope())
		{
			action();
		}
	}

	private static void BeforeTradingCaravan(IncidentParms parms, out TradingCaravanScope __state)
	{
		__state = ((MP.IsInMultiplayer && parms?.target is Map map) ? new TradingCaravanScope(map) : null);
	}

	private static Exception AfterTradingCaravan(Exception __exception, TradingCaravanScope __state)
	{
		__state?.Dispose();
		return __exception;
	}

	private static IDisposable SimulationWorldScope()
	{
		return EnterTradingSimulation(null);
	}

	private static IDisposable SimulationMapScope(Map map)
	{
		if (map != null)
		{
			return EnterTradingSimulation(map);
		}
		throw new ArgumentNullException("map");
	}

	private static IDisposable EnterTradingSimulation(Map map)
	{
		if (!MP.IsInMultiplayer)
		{
			return EmptyTradingScope.Instance;
		}
		object obj2;
		if (map == null)
		{
			object obj = tradingGameRef();
			obj2 = ((obj == null) ? null : tradingWorldRef(obj));
		}
		else
		{
			obj2 = tradingMapTime.Invoke(null, new object[1] { map });
		}
		if (obj2 == null)
		{
			throw new InvalidOperationException("VTE economic action requires an initialized Multiplayer simulation owner");
		}
		MethodInfo methodInfo = ((map == null) ? tradingWorldPre : tradingMapPre);
		MethodInfo post = ((map == null) ? tradingWorldPost : tradingMapPost);
		methodInfo.Invoke(obj2, null);
		return new TradingSimulationScope(obj2, post);
	}

	private static IList UiList(string name)
	{
		return PatchingUtilities.Get(Manager, name) as IList;
	}

	private static object BankFor(Faction faction)
	{
		if (faction != null)
		{
			return (PatchingUtilities.Get(Manager, "banksByFaction") as IDictionary)?[faction];
		}
		return null;
	}

	private static object CompanyFor(int id)
	{
		IList list = UiList("companies");
		if (list == null)
		{
			return null;
		}
		return list.Cast<object>().FirstOrDefault((object c) => (int)PatchingUtilities.Get(c, "loadID") == id);
	}

	private static Faction BankFaction(object bank)
	{
		return PatchingUtilities.Get(bank, "parentFaction") as Faction;
	}

	private static int Number(object o, string field)
	{
		return (int)PatchingUtilities.Get(o, field);
	}

	private static void InstallUI()
	{
		string[] array = new string[8] { "SyncBank", "SyncTerminalBank", "SyncStock", "SyncFollow", "SyncSubmitContract", "SyncCancelContract", "SyncCompleteContract", "SyncResolvePayment" };
		foreach (string methodOrPropertyName in array)
		{
			MP.RegisterSyncMethod(typeof(VanillaTradingExpanded), methodOrPropertyName);
		}
		array = new string[3] { "SyncSubmitContractOwned", "SyncCompleteContractOwned", "SyncClaimPayment" };
		foreach (string methodOrPropertyName2 in array)
		{
			MP.RegisterSyncMethod(typeof(VanillaTradingExpanded), methodOrPropertyName2).SetVersion(1);
		}
		InstallPaymentRecovery();
		terminalJobDriverType = TypeOf("JobDriver_UseTradingTerminal");
		paymentGainsType = TypeOf("Window_PerformTransactionGains");
		paymentCostsType = TypeOf("Window_PerformTransactionCosts");
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(Pawn_JobTracker), "TryTakeOrderedJob"), new HarmonyMethod(typeof(VanillaTradingExpanded), "RecordTerminalJob")
		{
			priority = 0
		}, new HarmonyMethod(typeof(VanillaTradingExpanded), "TerminalJobAccepted"), null, new HarmonyMethod(typeof(VanillaTradingExpanded), "TerminalJobFinalizer"));
		array = new string[5] { "JobDriver_ContactBank", "JobDriver_OpenStockMarket", "JobDriver_OpenContracts", "JobDriver_ViewPrices", "JobDriver_ViewNews" };
		for (int i = 0; i < array.Length; i++)
		{
			Patch(array[i], "DoAction", "TerminalWindowPrefix", "TerminalWindowPostfix");
		}
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(WindowStack), "Add"), new HarmonyMethod(typeof(VanillaTradingExpanded), "PaymentWindowAddPrefix"));
		Patch("Bank", "DepositSilver", "DepositPrefix");
		Patch("Bank", "WithdrawSilver", "WithdrawPrefix");
		Patch("Bank", "TakeLoan", "TakeLoanPrefix");
		Patch("Bank", "TryRepayFromBalance", "RepayPrefix");
		MethodInfo lambda = MpMethodUtil.GetLambda(TypeOf("Building_TradingTerminal"), "GetBankOptions");
		MpCompat.harmony.Patch(lambda, new HarmonyMethod(typeof(VanillaTradingExpanded), "TerminalBankPrefix"));
		Patch("Window_StockMarket", "DoWindowContents", "StockUiPrefix", "StockUiPostfix", null, "StockUiFinalizer");
		Patch("Window_Contracts", "DoWindowContents", "ContractsUiPrefix", "ContractsUiPostfix", "ContractSubmitTranspiler", "ContractsUiFinalizer");
		Patch("TradingManager", "CompleteContract", "CompleteContractPrefix", null, "ContractWindowTranspiler");
		Patch("TradingManager", "ProcessContracts", "BeforeContractProcessing", "AfterContractProcessing");
		Patch("TradingManager", "GenerateRandomContract", null, "NewContractIdentity");
		Patch("TransactionProcess", "PerformTransaction", "TransactionPrefix");
		Patch("TransactionProcess", "PostClose", "TransactionClosePrefix");
		Patch("TransactionProcess", "PostCancel", "TransactionCancelPrefix");
		array = new string[2] { "Window_PerformTransactionGains", "Window_PerformTransactionCosts" };
		for (int i = 0; i < array.Length; i++)
		{
			ConstructorInfo original = AccessTools.GetDeclaredConstructors(TypeOf(array[i])).Single();
			MpCompat.harmony.Patch(original, null, new HarmonyMethod(typeof(VanillaTradingExpanded), "PaymentWindowConstructed"));
		}
		MpCompat.harmony.Patch(AccessTools.GetDeclaredConstructors(TypeOf("Window_Bank")).Single(), new HarmonyMethod(typeof(VanillaTradingExpanded), "BankWindowPrefix"));
		array = new string[3] { "Window_Bank", "Window_MarketPrices", "Window_News" };
		for (int i = 0; i < array.Length; i++)
		{
			Patch(array[i], "DoWindowContents", "ReadUiPrefix", "ReadUiPostfix", null, "ReadUiFinalizer");
		}
	}

	private static void RecordTerminalJob(Job job)
	{
		if (MP.IsInMultiplayer && MP.IsExecutingSyncCommand && !(job?.def?.driverClass == null) && terminalJobDriverType.IsAssignableFrom(job.def.driverClass))
		{
			LocalTerminalJob value = localTerminalJobs.GetValue(job, (Job _) => new LocalTerminalJob());
			value.game = new System.WeakReference<Game>(Current.Game);
			value.loadId = job.loadID;
			value.issuedBySelf = MP.IsExecutingSyncCommandIssuedBySelf;
		}
	}

	private static void TerminalJobAccepted(Job job, bool __result)
	{
		if (!__result && job != null)
		{
			localTerminalJobs.Remove(job);
		}
	}

	private static Exception TerminalJobFinalizer(Job job, Exception __exception)
	{
		if (__exception != null && job != null)
		{
			localTerminalJobs.Remove(job);
		}
		return __exception;
	}

	private static bool TerminalWindowPrefix(JobDriver __instance, out bool __state)
	{
		__state = MP.IsInMultiplayer && __instance.job != null && localTerminalJobs.TryGetValue(__instance.job, out var value) && value.game.TryGetTarget(out var target) && target == Current.Game && value.loadId == __instance.job.loadID && value.issuedBySelf;
		if (MP.IsInMultiplayer && __instance.job != null)
		{
			localTerminalJobs.Remove(__instance.job);
		}
		return !MP.IsInMultiplayer | __state;
	}

	private static void TerminalWindowPostfix(JobDriver __instance, bool __state)
	{
		if (__state)
		{
			bool flag;
			switch (__instance.GetType().Name)
			{
			case "JobDriver_ContactBank":
			case "JobDriver_OpenStockMarket":
			case "JobDriver_OpenContracts":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (flag)
			{
				PaymentState?.RequestUnownedPayments();
			}
		}
	}

	private static bool CanPresentPayment(TradingPendingPayment payment)
	{
		if (!string.IsNullOrEmpty(payment.owner))
		{
			return string.Equals(payment.owner, MP.PlayerName, StringComparison.Ordinal);
		}
		return false;
	}

	private static bool PaymentWindowAddPrefix(Window window)
	{
		if (!MP.IsInMultiplayer || window == null || (window.GetType() != paymentGainsType && window.GetType() != paymentCostsType))
		{
			return true;
		}
		object key = PatchingUtilities.Get(window, "transactionProcess");
		if (!localTransactions.TryGetValue(key, out var local) || local.game != Current.Game || local.ticket <= 0)
		{
			return true;
		}
		TradingPendingPayment tradingPendingPayment = PaymentState?.pending.FirstOrDefault((TradingPendingPayment record) => record.id == local.ticket);
		if (tradingPendingPayment != null)
		{
			return CanPresentPayment(tradingPendingPayment);
		}
		return false;
	}

	private static void ReadUiPrefix(out ReadUiState __state)
	{
		__state = (Intercept ? new ReadUiState() : null);
	}

	private static void ReadUiPostfix(ReadUiState __state)
	{
		__state?.Dispose();
	}

	private static Exception ReadUiFinalizer(Exception __exception, ReadUiState __state)
	{
		__state?.Dispose();
		return __exception;
	}

	private static Pawn BankNegotiator(object bank)
	{
		return Find.WindowStack?.Windows.Where((Window w) => w.GetType() == TypeOf("Window_Bank") && PatchingUtilities.Get(w, "bank") == bank).Select((Window w) => PatchingUtilities.Get(w, "negotiator") as Pawn).FirstOrDefault();
	}

	private static bool DepositPrefix(object __instance, int amountToDeposit)
	{
		if (!Intercept)
		{
			return true;
		}
		Pawn pawn = BankNegotiator(__instance);
		if (pawn != null)
		{
			SyncBank(BankFaction(__instance), pawn, 0, amountToDeposit, null);
		}
		return false;
	}

	private static bool WithdrawPrefix(object __instance, Pawn negotiator, int amountToWithwraw)
	{
		if (!Intercept)
		{
			return true;
		}
		SyncBank(BankFaction(__instance), negotiator, 1, amountToWithwraw, null);
		return false;
	}

	private static bool TakeLoanPrefix(object __instance, Pawn negotiator, object loanOption)
	{
		if (!Intercept)
		{
			return true;
		}
		IList list = PatchingUtilities.Get(PatchingUtilities.Get(__instance, "bankExtension"), "loanOptions") as IList;
		SyncBank(BankFaction(__instance), negotiator, 2, list?.IndexOf(loanOption) ?? (-1), null);
		return false;
	}

	private static bool RepayPrefix(object __instance, object loan)
	{
		if (!Intercept)
		{
			return true;
		}
		Pawn pawn = BankNegotiator(__instance);
		if (pawn != null)
		{
			SyncBank(BankFaction(__instance), pawn, 3, 0, LoanKey(loan));
		}
		return false;
	}

	private static string LoanKey(object loan)
	{
		InlineArray4<object> buffer = default;
		buffer[0] = Number(loan, "loanOptionId");
		buffer[1] = Number(loan, "repayDate");
		buffer[2] = Number(loan, "initialRepayAmount");
		buffer[3] = Number(loan, "mapTile");
		return string.Join("/", (ReadOnlySpan<object?>)buffer);
	}

	private static void SyncBank(Faction faction, Pawn pawn, int action, int value, string loanKey)
	{
		object obj = BankFor(faction);
		if (obj == null || pawn?.Map == null || pawn.Dead || !pawn.Spawned || !CommandAuthorityBridge.CanActForPawn(pawn))
		{
			return;
		}
		using (SimulationMapScope(pawn.Map))
		{
			using (CommandAuthorityBridge.EnterFaction(pawn.Faction))
			{
				if (action == 0 && value > 0)
				{
					Map map = pawn.Map;
					List<Thing> list = (from t in map.listerThings.ThingsOfDef(ThingDefOf.Silver)
						where !t.Destroyed && t.Spawned && !t.Position.Fogged(map) && (map.areaManager.Home[t.Position] || t.IsInAnyStorage()) && map.reachability.CanReach(pawn.Position, t, PathEndMode.Touch, TraverseMode.PassDoors, Danger.Deadly)
						orderby t.thingIDNumber
						select t).ToList();
					if (((IEnumerable<Thing>)list).Sum((Func<Thing, long>)((Thing t) => t.stackCount)) >= value && (long)Number(obj, "DepositAmount") + (long)value <= int.MaxValue)
					{
						PatchingUtilities.Call(obj, "DepositSilver", list, value);
					}
				}
				else if (action == 1 && value > 0 && Number(obj, "DepositAmount") >= value)
				{
					PatchingUtilities.Call(obj, "WithdrawSilver", pawn, value);
				}
				else
				{
					switch (action)
					{
					case 2:
					{
						if (!(PatchingUtilities.Get(PatchingUtilities.Get(obj, "bankExtension"), "loanOptions") is IList list2) || value < 0 || value >= list2.Count)
						{
							return;
						}
						object obj4 = list2[value];
						if (!(PatchingUtilities.Get(obj, "loans") is IList source) || source.Cast<object>().Any((object l) => Number(l, "loanOptionId") == value))
						{
							return;
						}
						int num4;
						int num5;
						long num6;
						using (WorldTime())
						{
							int num = (int)PatchingUtilities.Call(obj, "LastTimeLoanWasTaken", obj4);
							int num2 = (int)PatchingUtilities.Call(obj4, "GetRepayDateTicks");
							if ((num > -1 && (long)num + (long)num2 > Find.TickManager.TicksAbs) || (Number(obj, "DepositAmount") <= 0 && PatchingUtilities.Get(obj4, "fixedLoanAmount") == null))
							{
								return;
							}
							float num3 = ((float?)(PatchingUtilities.Get(obj4, "repayAmountPerDeposit") ?? PatchingUtilities.Get(obj4, "loanAmountPerDeposit"))) ?? 1f;
							num4 = (int)PatchingUtilities.Call(obj4, "GetLoanAmountFrom", obj, num3);
							num5 = (int)PatchingUtilities.Call(obj4, "GetRepayAmountFrom", obj);
							num6 = (long)Find.TickManager.TicksAbs + (long)num2;
						}
						if (num4 <= 0 || num5 < 0 || num6 > int.MaxValue)
						{
							return;
						}
						PatchingUtilities.Call(obj, "TakeLoan", pawn, num4, num5, (int)num6, obj4);
						break;
					}
					case 3:
					{
						IList obj2 = PatchingUtilities.Get(obj, "loans") as IList;
						object obj3 = ((obj2 != null) ? obj2.Cast<object>().SingleOrDefault((object l) => LoanKey(l) == loanKey) : null);
						if (obj3 != null)
						{
							PatchingUtilities.Call(obj, "TryRepayFromBalance", obj3);
						}
						break;
					}
					}
				}
				if (Find.WindowStack == null)
				{
					return;
				}
				foreach (Window item in Find.WindowStack.Windows.Where((Window w) => w.GetType() == TypeOf("Window_Bank")))
				{
					PatchingUtilities.Set(item, "cachedThings", null);
				}
			}
		}
	}

	private static object FindCaptured(object closure, Type type, int depth = 0)
	{
		if (closure == null || depth > 3)
		{
			return null;
		}
		if (type.IsInstanceOfType(closure))
		{
			return closure;
		}
		foreach (FieldInfo declaredField in AccessTools.GetDeclaredFields(closure.GetType()))
		{
			object value = declaredField.GetValue(closure);
			if (type.IsInstanceOfType(value))
			{
				return value;
			}
			if (declaredField.Name.Contains("locals"))
			{
				object obj = FindCaptured(value, type, depth + 1);
				if (obj != null)
				{
					return obj;
				}
			}
		}
		return null;
	}

	private static bool TerminalBankPrefix(object __instance)
	{
		if (!Intercept)
		{
			return true;
		}
		Thing thing = FindCaptured(__instance, TypeOf("Building_TradingTerminal")) as Thing;
		Pawn pawn = FindCaptured(__instance, typeof(Pawn)) as Pawn;
		object obj = (from f in AccessTools.GetDeclaredFields(__instance.GetType())
			select f.GetValue(__instance)).FirstOrDefault((object o) => o != null && o.GetType().IsGenericType && o.GetType().GetGenericTypeDefinition() == typeof(KeyValuePair<, >));
		Faction faction = ((obj == null) ? null : (PatchingUtilities.Get(obj, "Key") as Faction));
		if (thing != null && pawn != null && faction != null)
		{
			SyncTerminalBank(thing, pawn, faction);
		}
		return false;
	}

	private static void SyncTerminalBank(Thing terminal, Pawn pawn, Faction faction)
	{
		if (terminal != null && pawn?.Map != null && terminal.Map == pawn.Map && BankFor(faction) != null && !terminal.Destroyed && terminal.Spawned && !pawn.Dead && pawn.Spawned && CommandAuthorityBridge.CanActForPawn(pawn) && (bool)PatchingUtilities.Get(terminal, "CanUseTerminalNow"))
		{
			PatchingUtilities.Set(terminal, "currentVisitableFactionBank", faction);
			Job job = JobMaker.MakeJob((JobDef)PatchingUtilities.Get(TypeOf("VTE_DefOf"), "VTE_ContactBank"), terminal);
			job.count = faction.loadID;
			pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
		}
	}

	private static void BankWindowPrefix(Pawn negotiator, ref Faction bankFaction)
	{
		if (MP.IsInMultiplayer && negotiator?.CurJob != null && negotiator.CurJob.def == PatchingUtilities.Get(TypeOf("VTE_DefOf"), "VTE_ContactBank"))
		{
			Faction faction = Find.FactionManager.AllFactions.FirstOrDefault((Faction f) => f.loadID == negotiator.CurJob.count);
			if (BankFor(faction) != null)
			{
				bankFaction = faction;
			}
		}
	}

	private static void StockUiPrefix(out StockUiSnapshot __state)
	{
		__state = null;
		if (!Intercept)
		{
			return;
		}
		__state = new StockUiSnapshot
		{
			read = new ReadUiState()
		};
		foreach (object item in UiList("companies"))
		{
			__state.values.Add((item, (bool)PatchingUtilities.Get(item, "playerFollowsNews")));
		}
	}

	private static void RestoreStock(StockUiSnapshot state, bool send)
	{
		if (state == null || state.restored)
		{
			return;
		}
		state.restored = true;
		state.read?.Dispose();
		foreach (var value in state.values)
		{
			object item = value.company;
			bool item2 = value.follows;
			bool flag = (bool)PatchingUtilities.Get(item, "playerFollowsNews");
			PatchingUtilities.Set(item, "playerFollowsNews", item2);
			if (send && flag != item2)
			{
				SyncFollow(Number(item, "loadID"), flag);
			}
		}
	}

	private static void StockUiPostfix(StockUiSnapshot __state)
	{
		RestoreStock(__state, send: true);
	}

	private static Exception StockUiFinalizer(Exception __exception, StockUiSnapshot __state)
	{
		RestoreStock(__state, send: false);
		return __exception;
	}

	private static void SyncFollow(int companyId, bool follow)
	{
		object obj = CompanyFor(companyId);
		if (obj != null)
		{
			PatchingUtilities.Set(obj, "playerFollowsNews", follow);
		}
	}

	private static IDictionary TypedDictionary(string keyType)
	{
		return (IDictionary)Activator.CreateInstance(typeof(Dictionary<, >).MakeGenericType(TypeOf(keyType), typeof(int)));
	}

	private static IEnumerable<DictionaryEntry> Entries(IDictionary dictionary)
	{
		return from object k in dictionary.Keys
			select new DictionaryEntry(k, dictionary[k]);
	}

	private static (int[] ids, int[] amounts) ReadAllocation(object transaction, string field)
	{
		IEnumerable<DictionaryEntry> obj = ((!(PatchingUtilities.Get(transaction, field) is IDictionary dictionary)) ? null : Entries(dictionary));
		(int, int)[] source = ((obj != null) ? (from e in obj
			select (id: BankFaction(e.Key)?.loadID ?? (-1), amount: (int)e.Value) into e
			where e.amount != 0
			orderby e.id
			select e).ToArray() : null) ?? Array.Empty<(int, int)>();
		return (ids: source.Select(((int id, int amount) e) => e.id).ToArray(), amounts: source.Select(((int id, int amount) e) => e.amount).ToArray());
	}

	private static bool Allocation(int[] ids, int[] values, int total, bool spending, IDictionary gains, out IDictionary result)
	{
		result = TypedDictionary("Bank");
		if (ids == null || values == null || ids.Length != values.Length || ids.Length > 1000 || ids.Distinct().Count() != ids.Length)
		{
			return false;
		}
		long num = 0L;
		int i;
		for (i = 0; i < ids.Length; i++)
		{
			object obj = BankFor(Find.FactionManager.AllFactions.FirstOrDefault((Faction f) => f.loadID == ids[i]));
			if (obj == null || values[i] < 0)
			{
				return false;
			}
			long num2 = (long)Number(obj, "DepositAmount") + (long)((gains != null && gains.Contains(obj)) ? ((int)gains[obj]) : 0);
			if (!spending && (long)Number(obj, "DepositAmount") + (long)values[i] > int.MaxValue)
			{
				return false;
			}
			if (gains != null && gains.Contains(obj))
			{
				foreach (object item in ((IList)PatchingUtilities.Get(obj, "loans")).Cast<object>().Reverse())
				{
					if ((bool)PatchingUtilities.Get(item, "IsOverdue"))
					{
						num2 -= Math.Min(Math.Max(0L, num2), Number(item, "curRepayAmount"));
					}
				}
			}
			if (spending && values[i] > Math.Max(0L, num2))
			{
				return false;
			}
			result.Add(obj, values[i]);
			num += values[i];
		}
		return num == total;
	}

	private static bool TransactionPrefix(object __instance)
	{
		if (!Intercept)
		{
			return true;
		}
		LocalTransaction value = localTransactions.GetValue(__instance, (object _) => new LocalTransaction());
		if (value.submitted)
		{
			return false;
		}
		value.submitted = true;
		(int[], int[]) tuple = ReadAllocation(__instance, "amountToTransfer");
		(int[], int[]) tuple2 = ReadAllocation(__instance, "amountToSpend");
		if (value.ticket > 0 && value.game == Current.Game)
		{
			SyncResolvePayment(value.ticket, pay: true, tuple.Item1, tuple.Item2, tuple2.Item1, tuple2.Item2);
		}
		else
		{
			DictionaryEntry[] array = (from e in Entries((IDictionary)PatchingUtilities.Get(__instance, "companySharesToBuyOrSell"))
				where (int)e.Value != 0
				orderby Number(e.Key, "loadID")
				select e).ToArray();
			try
			{
				if (array.Length != 0)
				{
					SyncStock(array.Select((DictionaryEntry e) => Number(e.Key, "loadID")).ToArray(), array.Select((DictionaryEntry e) => (int)e.Value).ToArray(), tuple.Item1, tuple.Item2, tuple2.Item1, tuple2.Item2);
				}
			}
			finally
			{
				((IDictionary)PatchingUtilities.Get(__instance, "companySharesToBuyOrSell")).Clear();
				(PatchingUtilities.Get(__instance, "amountToTransfer") as IDictionary)?.Clear();
				(PatchingUtilities.Get(__instance, "amountToSpend") as IDictionary)?.Clear();
				PatchingUtilities.Set(__instance, "transactionGain", 0);
				PatchingUtilities.Set(__instance, "transactionCost", 0);
				PatchingUtilities.Set(__instance, "totalTransaction", 0);
				value.submitted = false;
			}
		}
		return false;
	}

	private static void SyncStock(int[] companies, int[] amounts, int[] gainBanks, int[] gains, int[] spendBanks, int[] spends)
	{
		if (companies == null || amounts == null || companies.Length != amounts.Length || companies.Length > 1000 || companies.Distinct().Count() != companies.Length)
		{
			return;
		}
		using (SimulationWorldScope())
		{
			IDictionary dictionary = TypedDictionary("Company");
			float num = 0f;
			float num2 = 0f;
			for (int i = 0; i < companies.Length; i++)
			{
				object obj = CompanyFor(companies[i]);
				if (obj == null || amounts[i] == 0 || amounts[i] < -1000000 || amounts[i] > 1000000 || (amounts[i] < 0 && -amounts[i] > ((IList)PatchingUtilities.Get(obj, "sharesHeldByPlayer")).Count))
				{
					return;
				}
				float num3 = (float)PatchingUtilities.Get(obj, "currentValue");
				if (!float.IsFinite(num3) || num3 < 0f)
				{
					return;
				}
				if (amounts[i] < 0)
				{
					num += (float)(-amounts[i]) * num3;
				}
				else
				{
					num2 += (float)amounts[i] * num3;
				}
				dictionary.Add(obj, amounts[i]);
			}
			if (float.IsFinite(num) && float.IsFinite(num2) && !(num > 2.1474836E+09f) && !(num2 > 2.1474836E+09f) && Allocation(gainBanks, gains, (int)num, spending: false, null, out var result) && Allocation(spendBanks, spends, (int)num2, spending: true, result, out var result2))
			{
				object? target = Activator.CreateInstance(TypeOf("TransactionProcess"));
				PatchingUtilities.Set(target, "companySharesToBuyOrSell", dictionary);
				PatchingUtilities.Set(target, "amountToTransfer", result);
				PatchingUtilities.Set(target, "amountToSpend", result2);
				PatchingUtilities.Call(target, "PerformTransaction");
			}
		}
	}

	private static string ContractKey(object contract, Map originalMap = null, bool overrideMap = false)
	{
		Map map = (overrideMap ? originalMap : (PatchingUtilities.Get(contract, "mapToTakeItems") as Map));
		InlineArray8<object> buffer = default;
		buffer[0] = DefName("item");
		buffer[1] = DefName("stuff");
		buffer[2] = Number(contract, "amount");
		buffer[3] = Number(contract, "reward");
		buffer[4] = ((float)PatchingUtilities.Get(contract, "rewardAsFloat")).ToString("R", CultureInfo.InvariantCulture);
		buffer[5] = Number(contract, "expiresInTicks");
		buffer[6] = Number(contract, "arrivesInTicks");
		buffer[7] = map?.uniqueID ?? (-1);
		return string.Join("|", (ReadOnlySpan<object?>)buffer);
		string DefName(string name)
		{
			return (PatchingUtilities.Get(contract, name) as Def)?.defName ?? "";
		}
	}

	private static void ContractsUiPrefix(object __instance, out ContractUiSnapshot __state)
	{
		__state = null;
		if (!Intercept)
		{
			return;
		}
		__state = new ContractUiSnapshot
		{
			window = __instance,
			players = UiList("playerSubmittedContracts").Cast<object>().ToArray()
		};
		__state.read = new ReadUiState();
		foreach (object item in UiList("npcSubmittedContracts"))
		{
			__state.npc.Add((item, PatchingUtilities.Get(item, "mapToTakeItems") as Map, ContractKey(item)));
		}
		activeContractUi = __state;
	}

	private static void RestoreContracts(ContractUiSnapshot state, bool send)
	{
		if (state == null || state.restored)
		{
			return;
		}
		state.restored = true;
		activeContractUi = null;
		state.read?.Dispose();
		IList list = UiList("playerSubmittedContracts");
		object[] second = list.Cast<object>().ToArray();
		list.Clear();
		object[] players = state.players;
		foreach (object value in players)
		{
			list.Add(value);
		}
		foreach (var (target, value2, _) in state.npc)
		{
			PatchingUtilities.Set(target, "mapToTakeItems", value2);
		}
		if (!send)
		{
			return;
		}
		foreach (object item in state.players.Except(second))
		{
			SyncCancelContract(ContractIdentity(item), ContractKey(item));
		}
	}

	private static IEnumerable<CodeInstruction> ContractSubmitTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		int count = 0;
		Type type = typeof(List<>).MakeGenericType(TypeOf("Contract"));
		MethodInfo add = AccessTools.DeclaredMethod(type, "Add");
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(add))
			{
				CodeInstruction codeInstruction = new CodeInstruction(OpCodes.Ldarg_0);
				codeInstruction.labels.AddRange(instruction.labels);
				instruction.labels.Clear();
				codeInstruction.blocks.AddRange(instruction.blocks);
				instruction.blocks.Clear();
				yield return codeInstruction;
				yield return new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(VanillaTradingExpanded), "SubmitContractFromUi"));
				count++;
			}
			else
			{
				yield return instruction;
			}
		}
		if (count != 1)
		{
			throw new InvalidOperationException("VTE submit contract expected one List<Contract>.Add, found " + count);
		}
	}

	private static void SubmitContractFromUi(object list, object contract, object window)
	{
		if (!Intercept)
		{
			((IList)list).Add(contract);
			return;
		}
		float num = (float)PatchingUtilities.Get(window, "fixedDurationInDays");
		if (float.IsFinite(num) && !(num < 0f) && !(num > 35791.395f))
		{
			SyncSubmitContractOwned(PatchingUtilities.Get(contract, "item") as ThingDef, PatchingUtilities.Get(contract, "stuff") as ThingDef, Number(contract, "amount"), (float)PatchingUtilities.Get(window, "fixedMarkupForPlayerContract"), (int)(60000f * num), MP.PlayerName);
		}
	}

	private static void ContractsUiPostfix(ContractUiSnapshot __state)
	{
		RestoreContracts(__state, send: true);
	}

	private static Exception ContractsUiFinalizer(Exception __exception, ContractUiSnapshot __state)
	{
		RestoreContracts(__state, send: false);
		return __exception;
	}

	private static bool CompleteContractPrefix(object contract)
	{
		if (!Intercept)
		{
			return true;
		}
		string key = (activeContractUi?.npc.FirstOrDefault(((object contract, Map map, string key) x) => x.contract == contract))?.key ?? ContractKey(contract);
		Map map = PatchingUtilities.Get(contract, "mapToTakeItems") as Map;
		SyncCompleteContractOwned(ContractIdentity(contract), key, map, MP.PlayerName);
		return false;
	}

	private static IEnumerable<CodeInstruction> ContractWindowTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo add = AccessTools.DeclaredMethod(typeof(WindowStack), "Add");
		MethodInfo replacement = AccessTools.DeclaredMethod(typeof(VanillaTradingExpanded), "AddContractWindow");
		int count = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(add))
			{
				instruction.opcode = OpCodes.Call;
				instruction.operand = replacement;
				count++;
			}
			yield return instruction;
		}
		if (count != 2)
		{
			throw new InvalidOperationException("VTE completion window contract changed: " + count);
		}
	}

	private static void AddContractWindow(WindowStack stack, Window window)
	{
		if (!MP.IsInMultiplayer || (!string.IsNullOrEmpty(paymentOwner) && string.Equals(paymentOwner, MP.PlayerName, StringComparison.Ordinal)))
		{
			stack.Add(window);
		}
	}

	private static void SyncCompleteContract(int contractId, string key, Map map)
	{
		SyncCompleteContractOwned(contractId, key, map, null);
	}

	private static void SyncCompleteContractOwned(int contractId, string key, Map map, string owner)
	{
		object obj = ContractByIdentity(contractId, key, "npcSubmittedContracts");
		if (obj == null || map == null || !Find.Maps.Contains(map) || Number(obj, "amount") <= 0)
		{
			return;
		}
		using (SimulationMapScope(map))
		{
			using (WorldTime())
			{
				if (Number(obj, "expiresInTicks") < Find.TickManager.TicksGame || ((IList)PatchingUtilities.Call(obj, "FoundItemsInMap", map)).Cast<Thing>().Sum((Func<Thing, long>)((Thing t) => t.stackCount)) < Number(obj, "amount"))
				{
					return;
				}
				PatchingUtilities.Set(obj, "mapToTakeItems", map);
				string text = paymentOwner;
				try
				{
					paymentOwner = owner;
					PatchingUtilities.Call(Manager, "CompleteContract", obj);
				}
				finally
				{
					paymentOwner = text;
				}
			}
		}
	}

	private static void SyncCancelContract(int contractId, string key)
	{
		object obj = ContractByIdentity(contractId, key);
		if (obj != null)
		{
			UiList("playerSubmittedContracts").Remove(obj);
			int[] array = (from p in PaymentState.pending
				where p.kind == 1 && p.contractId == contractId
				select p.id).ToArray();
			PaymentState.pending.RemoveAll((TradingPendingPayment p) => p.kind == 1 && p.contractId == contractId);
			int[] array2 = array;
			for (int num = 0; num < array2.Length; num++)
			{
				ClosePaymentWindows(array2[num]);
			}
			PaymentState.RequestPresentation();
		}
	}

	private static void SyncSubmitContract(ThingDef item, ThingDef stuff, int amount, float markup, int durationTicks)
	{
		SyncSubmitContractOwned(item, stuff, amount, markup, durationTicks, null);
	}

	private static void SyncSubmitContractOwned(ThingDef item, ThingDef stuff, int amount, float markup, int durationTicks, string owner)
	{
		if (item == null || amount <= 0 || amount > 1000000000 || !float.IsFinite(markup) || markup < 0f || durationTicks <= 0 || (item.MadeFromStuff && (stuff == null || !GenStuff.AllowedStuffsFor(item).Contains(stuff))) || (!item.MadeFromStuff && stuff != null))
		{
			return;
		}
		using (SimulationWorldScope())
		{
			long num = (long)Find.TickManager.TicksGame + (long)durationTicks;
			if (num <= int.MaxValue)
			{
				object obj = Activator.CreateInstance(TypeOf("Contract"));
				PatchingUtilities.Set(obj, "item", item);
				PatchingUtilities.Set(obj, "stuff", stuff);
				PatchingUtilities.Set(obj, "amount", amount);
				float num2 = (float)PatchingUtilities.Get(obj, "BaseMarketValue") * (markup / 100f);
				if (float.IsFinite(num2) && !(num2 < 0f) && !(num2 > 2.1474836E+09f))
				{
					PatchingUtilities.Set(obj, "reward", (int)num2);
					PatchingUtilities.Set(obj, "rewardAsFloat", num2);
					PatchingUtilities.Set(obj, "expiresInTicks", (int)num);
					RefreshContractIdentities(PaymentState);
					UiList("playerSubmittedContracts").Add(obj);
					PaymentState.contractOwners[obj] = owner;
					RefreshContractIdentities(PaymentState);
				}
			}
		}
	}

	private static void BeforeContractProcessing()
	{
		if (MP.IsInMultiplayer)
		{
			RefreshContractIdentities(PaymentState);
		}
	}

	private static void AfterContractProcessing()
	{
		if (!MP.IsInMultiplayer)
		{
			return;
		}
		TradingPaymentState paymentState = PaymentState;
		RefreshContractIdentities(paymentState);
		TradingPendingPayment[] array = paymentState.pending.Where((TradingPendingPayment p) => p.kind == 1).ToArray();
		foreach (TradingPendingPayment tradingPendingPayment in array)
		{
			object obj = ContractByIdentity(tradingPendingPayment.contractId, tradingPendingPayment.contractKey);
			if (obj == null || Number(obj, "expiresInTicks") < Find.TickManager.TicksGame)
			{
				paymentState.pending.Remove(tradingPendingPayment);
				ClosePaymentWindows(tradingPendingPayment.id);
			}
		}
	}

	private static int ContractIdentity(object contract)
	{
		TradingPaymentState paymentState = PaymentState;
		if (paymentState == null || !paymentState.liveContracts.TryGetValue(contract, out var value))
		{
			return -1;
		}
		return value;
	}

	private static void NewContractIdentity(object __result)
	{
		if (MP.IsInMultiplayer && __result != null && !Intercept && PaymentState != null && !PaymentState.liveContracts.ContainsKey(__result))
		{
			PaymentState.liveContracts.Add(__result, checked(PaymentState.nextContractId++));
		}
	}

	private static object ContractByIdentity(int id, string key, string listName = "playerSubmittedContracts")
	{
		if (id <= 0 || PaymentState == null)
		{
			return null;
		}
		object key2 = PaymentState.liveContracts.FirstOrDefault((KeyValuePair<object, int> p) => p.Value == id).Key;
		if (key2 == null || !UiList(listName).Contains(key2) || !(ContractKey(key2) == key))
		{
			return null;
		}
		return key2;
	}

	internal static void RefreshContractIdentities(TradingPaymentState state)
	{
		if (state == null || Manager == null || UiList("playerSubmittedContracts") == null || UiList("npcSubmittedContracts") == null)
		{
			return;
		}
		IList[] lists = new IList[2]
		{
			UiList("playerSubmittedContracts"),
			UiList("npcSubmittedContracts")
		};
		if (!state.identitiesBound)
		{
			state.liveContracts.Clear();
			state.contractOwners.Clear();
			foreach (TradingContractIdentity contract in state.contracts)
			{
				if (contract.list >= 0 && contract.list < lists.Length && contract.index >= 0 && contract.index < lists[contract.list].Count && ContractKey(lists[contract.list][contract.index]) == contract.key)
				{
					state.liveContracts[lists[contract.list][contract.index]] = contract.id;
					state.contractOwners[lists[contract.list][contract.index]] = contract.owner;
				}
			}
			state.identitiesBound = true;
		}
		state.contracts.Clear();
		for (int i = 0; i < lists.Length; i++)
		{
			for (int j = 0; j < lists[i].Count; j++)
			{
				object obj = lists[i][j];
				if (!state.liveContracts.ContainsKey(obj))
				{
					state.liveContracts.Add(obj, checked(state.nextContractId++));
				}
				state.contracts.Add(new TradingContractIdentity
				{
					id = state.liveContracts[obj],
					list = i,
					index = j,
					key = ContractKey(obj),
					owner = (state.contractOwners.TryGetValue(obj, out var value) ? value : null)
				});
			}
		}
		object[] array = state.liveContracts.Keys.Where((object c) => !lists.Any((IList list) => list.Contains(c))).ToArray();
		foreach (object key in array)
		{
			state.liveContracts.Remove(key);
			state.contractOwners.Remove(key);
		}
	}

	private static void PaymentWindowConstructed(object __instance, object parent)
	{
		if (!MP.IsInMultiplayer || reopeningPayment || Current.Game == null || Manager == null)
		{
			return;
		}
		LocalTransaction value = localTransactions.GetValue(parent, (object _) => new LocalTransaction());
		if ((value.game == Current.Game && value.ticket != 0) || (MP.InInterface && !MP.IsExecutingSyncCommand))
		{
			return;
		}
		int num = Number(parent, "transactionGain");
		int num2 = Number(parent, "transactionCost");
		Action action = PatchingUtilities.Get(parent, "postTransactionAction") as Action;
		object obj = ((action == null) ? null : FindCaptured(action.Target, TypeOf("Contract")));
		Map map = ((action == null) ? null : (FindCaptured(action.Target, typeof(Map)) as Map));
		if ((num2 > 0 && obj == null) || (num <= 0 && num2 <= 0))
		{
			return;
		}
		TradingPaymentState paymentState = PaymentState;
		RefreshContractIdentities(paymentState);
		string contractKey = ((obj == null) ? null : ContractKey(obj));
		int contractId = ((obj == null) ? (-1) : ContractIdentity(obj));
		TradingPendingPayment tradingPendingPayment = ((obj == null) ? null : paymentState.pending.FirstOrDefault((TradingPendingPayment r) => r.kind == 1 && r.contractId == contractId));
		if (tradingPendingPayment == null)
		{
			tradingPendingPayment = new TradingPendingPayment
			{
				id = checked(paymentState.nextId++),
				kind = ((obj != null) ? 1 : 0),
				gain = num,
				cost = num2,
				map = map,
				title = ((obj == null) ? "VTE.BankDepositsToPutContractReward" : "VTE.PayMoneyForContract"),
				contractKey = contractKey,
				contractId = contractId,
				owner = ((obj != null && paymentState.contractOwners.TryGetValue(obj, out var value2)) ? value2 : paymentOwner)
			};
			if (obj != null)
			{
				tradingPendingPayment.item = PatchingUtilities.Get(obj, "item") as ThingDef;
				tradingPendingPayment.stuff = PatchingUtilities.Get(obj, "stuff") as ThingDef;
				tradingPendingPayment.amount = Number(obj, "amount");
			}
			paymentState.pending.Add(tradingPendingPayment);
		}
		value.game = Current.Game;
		value.ticket = tradingPendingPayment.id;
	}

	internal static void ClaimUnownedPayments(TradingPaymentState state)
	{
		if (MP.IsInMultiplayer && Intercept && state == PaymentState)
		{
			TradingPendingPayment[] array = state.pending.Where((TradingPendingPayment record) => string.IsNullOrEmpty(record.owner)).ToArray();
			for (int num = 0; num < array.Length; num++)
			{
				SyncClaimPayment(array[num].id, MP.PlayerName);
			}
		}
	}

	private static void SyncClaimPayment(int id, string owner)
	{
		if (!string.IsNullOrEmpty(owner))
		{
			TradingPaymentState paymentState = PaymentState;
			TradingPendingPayment tradingPendingPayment = paymentState?.pending.FirstOrDefault((TradingPendingPayment record) => record.id == id);
			if (tradingPendingPayment != null && string.IsNullOrEmpty(tradingPendingPayment.owner))
			{
				tradingPendingPayment.owner = owner;
				paymentState.RequestPresentation();
			}
		}
	}

	private static bool TransactionCancelPrefix(object __instance)
	{
		return !SuppressPaymentClose(__instance, close: false);
	}

	private static bool TransactionClosePrefix(object __instance)
	{
		return !SuppressPaymentClose(__instance, close: true);
	}

	private static bool SuppressPaymentClose(object transaction, bool close)
	{
		if (!MP.IsInMultiplayer || !localTransactions.TryGetValue(transaction, out var local) || local.game != Current.Game || local.ticket <= 0)
		{
			return false;
		}
		if (close && Intercept && !local.submitted)
		{
			TradingPendingPayment tradingPendingPayment = PaymentState.pending.FirstOrDefault((TradingPendingPayment r) => r.id == local.ticket);
			if (tradingPendingPayment != null && tradingPendingPayment.kind == 1)
			{
				local.submitted = true;
				SyncResolvePayment(local.ticket, pay: false, Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>());
			}
			else
			{
				PaymentState.RequestPresentation();
			}
		}
		return true;
	}

	private static void SyncResolvePayment(int id, bool pay, int[] gainBanks, int[] gains, int[] spendBanks, int[] spends)
	{
		TradingPaymentState paymentState = PaymentState;
		TradingPendingPayment tradingPendingPayment = paymentState?.pending.FirstOrDefault((TradingPendingPayment r) => r.id == id);
		if (tradingPendingPayment == null)
		{
			return;
		}
		paymentState.RequestPresentation();
		using (SimulationWorldScope())
		{
			if (tradingPendingPayment.kind == 1)
			{
				object obj = ContractByIdentity(tradingPendingPayment.contractId, tradingPendingPayment.contractKey);
				if (obj == null || Number(obj, "expiresInTicks") < Find.TickManager.TicksGame)
				{
					paymentState.pending.Remove(tradingPendingPayment);
					ClosePaymentWindows(id);
					return;
				}
			}
			if (!pay && tradingPendingPayment.kind != 1)
			{
				return;
			}
			if (pay)
			{
				if (!Allocation(gainBanks, gains, tradingPendingPayment.gain, spending: false, null, out var result) || !Allocation(spendBanks, spends, tradingPendingPayment.cost, spending: true, result, out var result2) || (tradingPendingPayment.kind == 1 && (tradingPendingPayment.map == null || !Find.Maps.Contains(tradingPendingPayment.map))))
				{
					return;
				}
				paymentState.pending.Remove(tradingPendingPayment);
				foreach (DictionaryEntry item in result)
				{
					PatchingUtilities.Set(item.Key, "DepositAmount", Number(item.Key, "DepositAmount") + (int)item.Value);
				}
				foreach (DictionaryEntry item2 in result2)
				{
					PatchingUtilities.Set(item2.Key, "DepositAmount", Number(item2.Key, "DepositAmount") - (int)item2.Value);
				}
				if (tradingPendingPayment.kind == 1)
				{
					FulfillPayment(tradingPendingPayment);
				}
			}
			else
			{
				paymentState.pending.Remove(tradingPendingPayment);
			}
			if (tradingPendingPayment.kind == 1)
			{
				SyncCancelContract(tradingPendingPayment.contractId, tradingPendingPayment.contractKey);
			}
			ClosePaymentWindows(id);
		}
	}

	private static void ClosePaymentWindows(int id)
	{
		Window[] array = Find.WindowStack.Windows.ToArray();
		foreach (Window window in array)
		{
			if (!(window.GetType() != TypeOf("Window_PerformTransactionGains")) || !(window.GetType() != TypeOf("Window_PerformTransactionCosts")))
			{
				object key = PatchingUtilities.Get(window, "transactionProcess");
				if (localTransactions.TryGetValue(key, out var value) && value.game == Current.Game && value.ticket == id)
				{
					value.submitted = true;
					window.Close(doCloseSound: false);
				}
			}
		}
	}

	private static void FulfillPayment(TradingPendingPayment payment)
	{
		using (SimulationMapScope(payment.map))
		{
			object target = Activator.CreateInstance(TypeOf("Contract"));
			PatchingUtilities.Set(target, "item", payment.item);
			PatchingUtilities.Set(target, "stuff", payment.stuff);
			PatchingUtilities.Set(target, "amount", payment.amount);
			List<Thing> list = new List<Thing>();
			int num = payment.amount;
			while (num > 0)
			{
				Thing thing = (Thing)PatchingUtilities.Call(target, "MakeItem");
				if (thing == null || thing.stackCount <= 0)
				{
					throw new InvalidOperationException("VTE contract created no items");
				}
				num -= thing.stackCount;
				PatchingUtilities.Set(target, "amount", num);
				list.Add(thing);
			}
			DropPodUtility.DropThingsNear(DropCellFinder.TradeDropSpot(payment.map), payment.map, list);
			string text = string.Concat(list.Select((Thing t) => "x" + t.stackCount + " " + t.LabelNoCount + "\n")).TrimEndNewlines();
			Find.LetterStack.ReceiveLetter("VTE.ContractFullfilled".Translate(), "VTE.ContractFullfilledDesc".Translate() + text, LetterDefOf.PositiveEvent, new LookTargets(list));
		}
	}

	internal static void ReopenPayments(TradingPaymentState state)
	{
		if (!MP.IsInMultiplayer || Manager == null || Find.WindowStack == null || state != PaymentState)
		{
			return;
		}
		TradingPendingPayment[] array = state.pending.ToArray();
		foreach (TradingPendingPayment record in array)
		{
			if (CanPresentPayment(record) && !Find.WindowStack.Windows.Any((Window w) => (w.GetType() == TypeOf("Window_PerformTransactionGains") || w.GetType() == TypeOf("Window_PerformTransactionCosts")) && localTransactions.TryGetValue(PatchingUtilities.Get(w, "transactionProcess"), out var value2) && value2.game == Current.Game && value2.ticket == record.id))
			{
				object obj = Activator.CreateInstance(TypeOf("TransactionProcess"));
				PatchingUtilities.Set(obj, "transactionGain", record.gain);
				PatchingUtilities.Set(obj, "transactionCost", record.cost);
				LocalTransaction value = localTransactions.GetValue(obj, (object _) => new LocalTransaction());
				value.game = Current.Game;
				value.ticket = record.id;
				reopeningPayment = true;
				try
				{
					Type type = TypeOf((record.kind == 0) ? "Window_PerformTransactionGains" : "Window_PerformTransactionCosts");
					string text = ((record.kind == 0) ? record.title.Translate().ToString() : record.title.Translate(record.amount + " " + record.item?.label).ToString());
					object[] args = ((record.kind != 0) ? new object[2] { text, obj } : new object[3] { text, obj, true });
					Find.WindowStack.Add((Window)Activator.CreateInstance(type, args));
				}
				finally
				{
					reopeningPayment = false;
				}
			}
		}
	}
}
