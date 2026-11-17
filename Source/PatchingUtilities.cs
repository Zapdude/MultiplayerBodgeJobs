using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Compat;

public static class PatchingUtilities
{
	internal sealed class SimulationScope : IDisposable
	{
		private readonly object owner;

		private readonly MethodInfo post;

		private readonly FieldInfo tickingMap;

		private readonly Map map;

		private readonly Map previous;

		private bool disposed;

		internal SimulationScope(object owner, MethodInfo pre, MethodInfo post, Map map, FieldInfo tickingMap, string missingOwnerMessage)
		{
			this.owner = owner ?? throw new InvalidOperationException(missingOwnerMessage);
			this.post = post;
			this.map = map;
			this.tickingMap = tickingMap;
			previous = (Map)tickingMap.GetValue(null);
			pre.Invoke(owner, null);
			try
			{
				if (map != null)
				{
					tickingMap.SetValue(null, map);
				}
			}
			catch
			{
				post.Invoke(owner, null);
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
				post.Invoke(owner, null);
			}
			finally
			{
				if (map != null)
				{
					tickingMap.SetValue(null, previous);
				}
			}
		}
	}

	internal sealed class EmptySimulationScope : IDisposable
	{
		internal static readonly EmptySimulationScope Instance = new EmptySimulationScope();

		public void Dispose()
		{
		}
	}

	public class RandRedirector : System.Random
	{
		private static RandRedirector instance;

		public static RandRedirector Instance => instance ?? (instance = new RandRedirector());

		public RandRedirector()
		{
		}

		public RandRedirector(int seed)
			: base(seed)
		{
		}

		public override int Next()
		{
			return Rand.Range(0, int.MaxValue);
		}

		public override int Next(int maxValue)
		{
			return Rand.Range(0, maxValue);
		}

		public override int Next(int minValue, int maxValue)
		{
			return Rand.Range(minValue, maxValue);
		}

		public override void NextBytes(byte[] buffer)
		{
			for (int i = 0; i < buffer.Length; i++)
			{
				buffer[i] = (byte)Rand.RangeInclusive(0, 255);
			}
		}

		public override double NextDouble()
		{
			return Rand.Range(0f, 1f);
		}

		protected override double Sample()
		{
			return NextDouble();
		}
	}

	private class CurrentMapPatchHelper
	{
		private enum CurrentMapUserType
		{
			Thing,
			ThingComp,
			Hediff,
			HediffComp,
			GameCondition,
			MapComponent,
			IncidentParms,
			UnsupportedType
		}

		private static readonly MethodInfo TargetMethodFind = AccessTools.PropertyGetter(typeof(Find), "CurrentMap");

		private static readonly MethodInfo TargetMethodGame = AccessTools.PropertyGetter(typeof(Game), "CurrentMap");

		private readonly CurrentMapUserType currentType;

		private readonly MethodBase baseMethod;

		private readonly IReadOnlyList<CodeInstruction> instructions;

		public bool IsSupported => currentType != CurrentMapUserType.UnsupportedType;

		public bool IsPatched { get; private set; }

		public CurrentMapPatchHelper(MethodBase baseMethod)
		{
			this.baseMethod = baseMethod ?? throw new ArgumentNullException("baseMethod");
			currentType = GetMapUserForMethod(this.baseMethod, out var earlyInstr);
			instructions = PrepareInstructions(earlyInstr, currentType);
		}

		public IEnumerable<CodeInstruction> ProcessCurrentInstruction(CodeInstruction ci)
		{
			CurrentMapUserType currentMapUserType = currentType;
			bool flag = ((currentMapUserType >= CurrentMapUserType.UnsupportedType || currentMapUserType < CurrentMapUserType.Thing) ? true : false);
			if (flag || ci.opcode != OpCodes.Call || !(ci.operand is MethodInfo methodInfo))
			{
				yield break;
			}
			bool first = true;
			if (methodInfo == TargetMethodGame)
			{
				IsPatched = true;
				first = false;
				ci.opcode = OpCodes.Pop;
				ci.operand = null;
			}
			else if (methodInfo != TargetMethodFind)
			{
				yield break;
			}
			foreach (CodeInstruction instruction in instructions)
			{
				if (first)
				{
					IsPatched = true;
					first = false;
					ci.opcode = instruction.opcode;
					ci.operand = instruction.operand;
				}
				else
				{
					yield return new CodeInstruction(instruction.opcode, instruction.operand);
				}
			}
		}

		private static IReadOnlyList<CodeInstruction> PrepareInstructions(CodeInstruction earlyInstruction, CurrentMapUserType type)
		{
			List<CodeInstruction> list = new List<CodeInstruction>();
			if (earlyInstruction != null)
			{
				list.Add(earlyInstruction);
			}
			switch (type)
			{
			case CurrentMapUserType.Thing:
				list.Add(new CodeInstruction(OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(Thing), "Map")));
				break;
			case CurrentMapUserType.ThingComp:
				list.Add(new CodeInstruction(OpCodes.Ldfld, AccessTools.DeclaredField(typeof(ThingComp), "parent")));
				list.Add(new CodeInstruction(OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(Thing), "Map")));
				break;
			case CurrentMapUserType.Hediff:
				list.Add(new CodeInstruction(OpCodes.Ldfld, AccessTools.DeclaredField(typeof(Hediff), "pawn")));
				list.Add(new CodeInstruction(OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(Pawn), "Map")));
				break;
			case CurrentMapUserType.HediffComp:
				list.Add(new CodeInstruction(OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(HediffComp), "Pawn")));
				list.Add(new CodeInstruction(OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(Pawn), "Map")));
				break;
			case CurrentMapUserType.GameCondition:
				list.Add(new CodeInstruction(OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(GameCondition), "SingleMap")));
				break;
			case CurrentMapUserType.MapComponent:
				list.Add(new CodeInstruction(OpCodes.Ldfld, AccessTools.DeclaredField(typeof(MapComponent), "map")));
				break;
			case CurrentMapUserType.IncidentParms:
				list.Add(new CodeInstruction(OpCodes.Ldfld, AccessTools.DeclaredField(typeof(IncidentParms), "target")));
				list.Add(new CodeInstruction(OpCodes.Castclass, typeof(Map)));
				break;
			}
			return list.AsReadOnly();
		}

		private static CurrentMapUserType GetMapUserForMethod(MethodBase method, out CodeInstruction earlyInstr)
		{
			earlyInstr = null;
			if (method.DeclaringType != null && !method.IsStatic)
			{
				CurrentMapUserType mapUserForType = GetMapUserForType(method.DeclaringType);
				if (mapUserForType != CurrentMapUserType.UnsupportedType)
				{
					earlyInstr = new CodeInstruction(OpCodes.Ldarg_0);
					return mapUserForType;
				}
			}
			ParameterInfo[] parameters = method.GetParameters();
			for (int i = 0; i < parameters.Length; i++)
			{
				CurrentMapUserType mapUserForType2 = GetMapUserForType(parameters[i].ParameterType);
				if (mapUserForType2 != CurrentMapUserType.UnsupportedType)
				{
					earlyInstr = GetLdargForIndex(method, i);
					return mapUserForType2;
				}
			}
			return CurrentMapUserType.UnsupportedType;
		}

		private static CurrentMapUserType GetMapUserForType(Type type)
		{
			if (typeof(Thing).IsAssignableFrom(type))
			{
				return CurrentMapUserType.Thing;
			}
			if (typeof(ThingComp).IsAssignableFrom(type))
			{
				return CurrentMapUserType.ThingComp;
			}
			if (typeof(Hediff).IsAssignableFrom(type))
			{
				return CurrentMapUserType.Hediff;
			}
			if (typeof(HediffComp).IsAssignableFrom(type))
			{
				return CurrentMapUserType.HediffComp;
			}
			if (typeof(GameCondition).IsAssignableFrom(type))
			{
				return CurrentMapUserType.GameCondition;
			}
			if (typeof(MapComponent).IsAssignableFrom(type))
			{
				return CurrentMapUserType.MapComponent;
			}
			if (typeof(IncidentParms).IsAssignableFrom(type))
			{
				return CurrentMapUserType.IncidentParms;
			}
			return CurrentMapUserType.UnsupportedType;
		}

		private static CodeInstruction GetLdargForIndex(MethodBase method, int index)
		{
			if (index < 0)
			{
				return null;
			}
			if (!method.IsStatic)
			{
				index++;
			}
			if (index <= 255)
			{
				return index switch
				{
					0 => new CodeInstruction(OpCodes.Ldarg_0), 
					1 => new CodeInstruction(OpCodes.Ldarg_1), 
					2 => new CodeInstruction(OpCodes.Ldarg_2), 
					3 => new CodeInstruction(OpCodes.Ldarg_3), 
					_ => new CodeInstruction(OpCodes.Ldarg_S, index), 
				};
			}
			if (index <= 65535)
			{
				return new CodeInstruction(OpCodes.Ldarg, index);
			}
			return null;
		}
	}

	public delegate bool TryHandleGainMemory(Thought_Memory thought);

	private delegate void ReadClock(object owner, out int ticks, out TimeSpeed speed, out TimeSlower slower, out int gameStartAbsTick);

	private delegate bool BeginMapRead(Map map, out long token);

	private delegate bool BeginWorldRead(out long token);

	private delegate bool BeginPawnRead(Pawn pawn, out long token);

	public struct TimeSnapshot
	{
		private static readonly AccessTools.FieldRef<TickManager, int> TickField = AccessTools.FieldRefAccess<TickManager, int>("ticksGameInt");

		private static readonly AccessTools.FieldRef<TickManager, TimeSpeed> SpeedField = AccessTools.FieldRefAccess<TickManager, TimeSpeed>("curTimeSpeed");

		private static readonly AccessTools.FieldRef<TickManager, TimeSlower> SlowerField = AccessTools.FieldRefAccess<TickManager, TimeSlower>("slower");

		private static readonly AccessTools.FieldRef<TickManager, int> StartField = AccessTools.FieldRefAccess<TickManager, int>("gameStartAbsTick");

		public int ticks;

		public TimeSpeed speed;

		public TimeSlower slower;

		public int gameStartAbsTick;

		private long ownerReadToken;

		public void Set()
		{
			if (ownerReadToken != 0L)
			{
				endOwnerRead(ownerReadToken);
				return;
			}
			TickManager tickManager = Find.TickManager;
			TickField(tickManager) = ticks;
			SlowerField(tickManager) = slower;
			SpeedField(tickManager) = speed;
			StartField(tickManager) = gameStartAbsTick;
		}

		public static TimeSnapshot Current()
		{
			TickManager tickManager = Find.TickManager;
			return new TimeSnapshot
			{
				ticks = TickField(tickManager),
				speed = SpeedField(tickManager),
				slower = SlowerField(tickManager),
				gameStartAbsTick = StartField(tickManager)
			};
		}

		public static TimeSnapshot? GetAndSetFromMap(Map map)
		{
			if (map == null)
			{
				return null;
			}
			if (beginOwnerMapRead != null)
			{
				if (Verse.Current.Game?.tickManager == null)
				{
					return null;
				}
				TimeSnapshot value = Current();
				if (!beginOwnerMapRead(map, out var token))
				{
					return null;
				}
				value.ownerReadToken = token;
				return value;
			}
			if (!isAsyncTimeMapCompSuccessful)
			{
				return null;
			}
			TimeSnapshot value2 = Current();
			TickManager tickManager = Find.TickManager;
			object obj = getAsyncTimeCompForMapMethod(map);
			if (obj == null)
			{
				return null;
			}
			if (readAsyncClock != null)
			{
				readAsyncClock(obj, out var num, out var timeSpeed, out var timeSlower, out var num2);
				TimeSnapshot timeSnapshot = default;
				timeSnapshot.ticks = num;
				timeSnapshot.speed = timeSpeed;
				timeSnapshot.slower = timeSlower;
				timeSnapshot.gameStartAbsTick = num2;
				timeSnapshot.Set();
				return value2;
			}
			TickField(tickManager) = asyncTimeMapTicksField(obj);
			SlowerField(tickManager) = asyncTimeSlowerField(obj);
			SpeedField(tickManager) = asyncTimeTimeSpeedIntField(obj);
			int num3 = asyncTimeGameStartAbsTickField(obj);
			if (num3 != 0)
			{
				StartField(tickManager) = num3;
			}
			return value2;
		}

		public static TimeSnapshot? GetAndSetFromWorld()
		{
			if (beginOwnerWorldRead != null)
			{
				if (Verse.Current.Game?.tickManager == null)
				{
					return null;
				}
				TimeSnapshot value = Current();
				if (!beginOwnerWorldRead(out var token))
				{
					return null;
				}
				value.ownerReadToken = token;
				return value;
			}
			if (!isAsyncTimeWorldCompSuccessful)
			{
				return null;
			}
			object obj = multiplayerGameField();
			if (obj == null)
			{
				return null;
			}
			object obj2 = gameAsyncWorldTimeField(obj);
			if (obj2 == null)
			{
				return null;
			}
			TimeSnapshot value2 = Current();
			if (readAsyncClock != null)
			{
				readAsyncClock(obj2, out var num, out var timeSpeed, out var timeSlower, out var num2);
				TimeSnapshot timeSnapshot = default;
				timeSnapshot.ticks = num;
				timeSnapshot.speed = timeSpeed;
				timeSnapshot.slower = timeSlower;
				timeSnapshot.gameStartAbsTick = num2;
				timeSnapshot.Set();
				return value2;
			}
			TickManager tickManager = Find.TickManager;
			TickField(tickManager) = asyncWorldTimeTicksField(obj2);
			if (getWorldTicksAbsMethod != null)
			{
				StartField(tickManager) = getWorldTicksAbsMethod() - TickField(tickManager);
			}
			return value2;
		}

		public static TimeSnapshot? GetAndSetFromPawn(Pawn pawn)
		{
			if (pawn == null)
			{
				return null;
			}
			if (beginOwnerPawnRead != null)
			{
				if (Verse.Current.Game?.tickManager == null)
				{
					return null;
				}
				TimeSnapshot value = Current();
				if (!beginOwnerPawnRead(pawn, out var token))
				{
					return null;
				}
				value.ownerReadToken = token;
				return value;
			}
			Map map = ((getPawnClockMapMethod != null) ? getPawnClockMapMethod(pawn) : pawn.MapHeld);
			if (map == null)
			{
				return GetAndSetFromWorld();
			}
			return GetAndSetFromMap(map);
		}
	}

	private delegate ref int TimestampFieldGetter(IExposable obj);

	private static readonly ConcurrentDictionary<(Type, string), MemberInfo> memberCache = new ConcurrentDictionary<(Type, string), MemberInfo>();

	private static readonly ConstructorInfo SystemRandConstructor = typeof(System.Random).GetConstructor(Type.EmptyTypes);

	private static readonly ConstructorInfo SystemRandSeededConstructor = typeof(System.Random).GetConstructor(new Type[1] { typeof(int) });

	private static readonly ConstructorInfo RandRedirectorConstructor = typeof(RandRedirector).GetConstructor(Type.EmptyTypes);

	private static readonly ConstructorInfo RandRedirectorSeededConstructor = typeof(RandRedirector).GetConstructor(new Type[1] { typeof(int) });

	private static readonly MethodInfo UnityRandomRangeInt = AccessTools.Method(typeof(UnityEngine.Random), "Range", new Type[2]
	{
		typeof(int),
		typeof(int)
	});

	private static readonly MethodInfo UnityRandomRangeIntObsolete = AccessTools.Method(typeof(UnityEngine.Random), "RandomRange", new Type[2]
	{
		typeof(int),
		typeof(int)
	});

	private static readonly MethodInfo UnityRandomRangeFloat = AccessTools.Method(typeof(UnityEngine.Random), "Range", new Type[2]
	{
		typeof(float),
		typeof(float)
	});

	private static readonly MethodInfo UnityRandomRangeFloatObsolete = AccessTools.Method(typeof(UnityEngine.Random), "RandomRange", new Type[2]
	{
		typeof(float),
		typeof(float)
	});

	private static readonly MethodInfo UnityRandomValue = AccessTools.PropertyGetter(typeof(UnityEngine.Random), "value");

	private static readonly MethodInfo UnityInsideUnitCircle = AccessTools.PropertyGetter(typeof(UnityEngine.Random), "insideUnitCircle");

	private static readonly MethodInfo VerseRandomRangeInt = AccessTools.Method(typeof(Rand), "Range", new Type[2]
	{
		typeof(int),
		typeof(int)
	});

	private static readonly MethodInfo VerseRandomRangeFloat = AccessTools.Method(typeof(Rand), "Range", new Type[2]
	{
		typeof(float),
		typeof(float)
	});

	private static readonly MethodInfo VerseRandomValue = AccessTools.PropertyGetter(typeof(Rand), "Value");

	private static readonly MethodInfo VerseInsideUnitCircle = AccessTools.PropertyGetter(typeof(Rand), "InsideUnitCircle");

	private static List<TryHandleGainMemory> tryGainMemoryHandlers;

	private static bool patchedVanillaMethod = false;

	private static bool isAsyncTimeSetup = false;

	private static bool isAsyncTimeGameCompSuccessful = false;

	private static bool isAsyncTimeMapCompSuccessful = false;

	private static bool isAsyncTimeWorldCompSuccessful = false;

	private static AccessTools.FieldRef<object> multiplayerGameField;

	private static AccessTools.FieldRef<object, object> gameGameCompField;

	private static AccessTools.FieldRef<object, object> gameAsyncWorldTimeField;

	private static AccessTools.FieldRef<object, bool> gameCompAsyncTimeField;

	private static Func<Map, object> getAsyncTimeCompForMapMethod;

	private static AccessTools.FieldRef<object, int> asyncTimeMapTicksField;

	private static AccessTools.FieldRef<object, TimeSlower> asyncTimeSlowerField;

	private static AccessTools.FieldRef<object, TimeSpeed> asyncTimeTimeSpeedIntField;

	private static AccessTools.FieldRef<object, int> asyncTimeGameStartAbsTickField;

	private static AccessTools.FieldRef<object, int> asyncWorldTimeTicksField;

	private static Func<Pawn, Map> getPawnClockMapMethod;

	private static Func<int> getWorldTicksAbsMethod;

	private static ReadClock readAsyncClock;

	private static BeginMapRead beginOwnerMapRead;

	private static BeginWorldRead beginOwnerWorldRead;

	private static BeginPawnRead beginOwnerPawnRead;

	private static Action<long> endOwnerRead;

	private static bool isTimestampFixerInitialized = false;

	private static Type timestampFixerDelegateType;

	private static Type timestampFixerListType;

	private static AccessTools.FieldRef<IDictionary> timestampFieldsDictionaryField;

	private static Action<Type, Action<object, int>> registerTimestampFields;

	private static bool longEventMarkerPatchActive = false;

	public static bool IsAsyncTime
	{
		get
		{
			if (!isAsyncTimeGameCompSuccessful)
			{
				return false;
			}
			object obj = multiplayerGameField();
			if (obj != null)
			{
				object obj2 = gameGameCompField(obj);
				if (obj2 != null)
				{
					return gameCompAsyncTimeField(obj2);
				}
			}
			return false;
		}
	}

	public static bool AllowedToRunLongEvents { get; private set; } = true;

	private static MemberInfo Member(Type type, string name)
	{
		return memberCache.GetOrAdd((type, name), ((Type, string) key) => (MemberInfo)(((object)AccessTools.Field(key.Item1, key.Item2)) ?? ((object)(AccessTools.Property(key.Item1, key.Item2) ?? throw new MissingMemberException(key.Item1.FullName, key.Item2)))));
	}

	internal static object Get(object target, string name)
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

	internal static void Set(object target, string name, object value)
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

	internal static object Call(object target, string name, params object[] args)
	{
		Type type = (target as Type) ?? target.GetType();
		MethodInfo[] array = (from method in AccessTools.GetDeclaredMethods(type)
			where method.Name == name && method.GetParameters().Length == args.Length && method.GetParameters().Select((ParameterInfo parameter, int index) =>
			{
				if (args[index] != null)
				{
					return parameter.ParameterType.IsInstanceOfType(args[index]);
				}
				return !parameter.ParameterType.IsValueType || Nullable.GetUnderlyingType(parameter.ParameterType) != null;
			}).All((bool match) => match)
			select method).ToArray();
		if (array.Length != 1)
		{
			throw new AmbiguousMatchException($"Expected one {type.FullName}.{name} for {args.Length} arguments, found {array.Length}");
		}
		return array[0].Invoke((target is Type) ? null : target, args);
	}

	private static void FixRNGPre(out bool __state)
	{
		__state = false;
		Rand.PushState();
		__state = true;
	}

	private static Exception FixRNGFinalizer(Exception __exception, bool __state)
	{
		if (__state)
		{
			Rand.PopState();
		}
		return __exception;
	}

	public static void PatchSystemRand(IEnumerable<string> methods, bool patchPushPop = true)
	{
		foreach (string method in methods)
		{
			PatchSystemRand(method, patchPushPop);
		}
	}

	public static void PatchSystemRand(IEnumerable<MethodBase> methods, bool patchPushPop = true)
	{
		foreach (MethodBase method in methods)
		{
			PatchSystemRand(method, patchPushPop);
		}
	}

	public static void PatchSystemRand(string method, bool patchPushPop = true)
	{
		MethodInfo methodInfo = AccessTools.DeclaredMethod(method) ?? AccessTools.Method(method);
		if (methodInfo == null)
		{
			Log.Error("Trying to patch System.Random for method " + method + ", but the method does not exist.");
		}
		else
		{
			PatchSystemRand(methodInfo, patchPushPop);
		}
	}

	public static void PatchSystemRand(MethodBase method, bool patchPushPop = true)
	{
		if (method == null)
		{
			Log.Error("Trying to patch System.Random for null method.");
			return;
		}
		HarmonyMethod transpiler = new HarmonyMethod(typeof(PatchingUtilities), "FixRNG");
		if (patchPushPop)
		{
			PatchPushPopRand(method, transpiler);
		}
		else
		{
			MpCompat.harmony.Patch(method, null, null, transpiler);
		}
	}

	public static void PatchSystemRandCtor(string typeName, bool patchPushPop = true)
	{
		Type type = AccessTools.TypeByName(typeName);
		if (type == null)
		{
			Log.Error("Trying to patch System.Random in constructor of type " + typeName + ", but the type does not exist.");
			return;
		}
		ConstructorInfo constructorInfo = AccessTools.DeclaredConstructor(type) ?? AccessTools.Constructor(type);
		if (constructorInfo == null)
		{
			Log.Error("Trying to patch System.Random in constructor of type " + typeName + ", but the constructor could not be found.");
		}
		else
		{
			PatchSystemRand(constructorInfo, patchPushPop);
		}
	}

	public static void PatchSystemRandCtor(IEnumerable<string> typeNames, bool patchPushPop = true)
	{
		foreach (string typeName in typeNames)
		{
			Type type = AccessTools.TypeByName(typeName);
			PatchSystemRand(AccessTools.DeclaredConstructor(type) ?? AccessTools.Constructor(type), patchPushPop);
		}
	}

	public static void PatchPushPopRand(IEnumerable<string> methods, HarmonyMethod transpiler = null)
	{
		foreach (string method in methods)
		{
			PatchPushPopRand(method, transpiler);
		}
	}

	public static void PatchPushPopRand(IEnumerable<MethodBase> methods, HarmonyMethod transpiler = null)
	{
		foreach (MethodBase method in methods)
		{
			PatchPushPopRand(method, transpiler);
		}
	}

	public static void PatchPushPopRand(string method, HarmonyMethod transpiler = null)
	{
		MethodInfo methodInfo = AccessTools.DeclaredMethod(method) ?? AccessTools.Method(method);
		if (methodInfo == null)
		{
			Log.Error("Trying to patch Verse.Rand.Push/Pop for method " + method + ", but the method does not exist..");
		}
		else
		{
			PatchPushPopRand(methodInfo, transpiler);
		}
	}

	public static void PatchPushPopRand(MethodBase method, HarmonyMethod transpiler = null)
	{
		if (method == null)
		{
			Log.Error("Trying to patch Verse.Rand.Push/Pop for null method.");
			return;
		}
		Harmony harmony = MpCompat.harmony;
		HarmonyMethod prefix = new HarmonyMethod(typeof(PatchingUtilities), "FixRNGPre")
		{
			priority = 0
		};
		HarmonyMethod finalizer = new HarmonyMethod(typeof(PatchingUtilities), "FixRNGFinalizer")
		{
			priority = 800
		};
		harmony.Patch(method, prefix, null, transpiler, finalizer);
	}

	public static void PatchUnityRand(IEnumerable<string> methods, bool patchPushPop = true)
	{
		foreach (string method in methods)
		{
			PatchUnityRand(method, patchPushPop);
		}
	}

	public static void PatchUnityRand(IEnumerable<MethodBase> methods, bool patchPushPop = true)
	{
		foreach (MethodBase method in methods)
		{
			PatchUnityRand(method, patchPushPop);
		}
	}

	public static void PatchUnityRand(string method, bool patchPushPop = true)
	{
		MethodInfo methodInfo = AccessTools.DeclaredMethod(method) ?? AccessTools.Method(method);
		if (methodInfo == null)
		{
			Log.Error("Trying to patch UnityEngine.Random for method " + method + ", but the method does not exist.");
		}
		else
		{
			PatchUnityRand(methodInfo, patchPushPop);
		}
	}

	public static void PatchUnityRand(MethodBase method, bool patchPushPop = true)
	{
		if (method == null)
		{
			Log.Error("Trying to patch UnityEngine.Random for null method.");
			return;
		}
		HarmonyMethod transpiler = new HarmonyMethod(typeof(PatchingUtilities), "FixUnityRNG");
		if (patchPushPop)
		{
			PatchPushPopRand(method, transpiler);
		}
		else
		{
			MpCompat.harmony.Patch(method, null, null, transpiler);
		}
	}

	internal static IEnumerable<CodeInstruction> FixRNG(IEnumerable<CodeInstruction> instr, MethodBase original)
	{
		bool anythingPatched = false;
		foreach (CodeInstruction item in instr)
		{
			if (item.opcode == OpCodes.Newobj && item.operand is ConstructorInfo constructorInfo)
			{
				if (constructorInfo == SystemRandConstructor)
				{
					item.operand = RandRedirectorConstructor;
					anythingPatched = true;
				}
				else if (constructorInfo == SystemRandSeededConstructor)
				{
					item.operand = RandRedirectorSeededConstructor;
					anythingPatched = true;
				}
			}
			yield return item;
		}
		if (!anythingPatched)
		{
			Log.Warning("No System RNG was patched for method: " + (original?.FullDescription() ?? "(unknown method)"));
		}
	}

	internal static bool IsSupportedUnityRng(MethodInfo method)
	{
		if (!(method == UnityRandomRangeInt) && !(method == UnityRandomRangeIntObsolete) && !(method == UnityRandomRangeFloat) && !(method == UnityRandomRangeFloatObsolete) && !(method == UnityRandomValue))
		{
			return method == UnityInsideUnitCircle;
		}
		return true;
	}

	internal static IEnumerable<CodeInstruction> FixUnityRNG(IEnumerable<CodeInstruction> instr, MethodBase original)
	{
		bool anythingPatched = false;
		foreach (CodeInstruction item in instr)
		{
			if (item.opcode == OpCodes.Call && item.operand is MethodInfo methodInfo)
			{
				if (methodInfo == UnityRandomRangeInt || methodInfo == UnityRandomRangeIntObsolete)
				{
					item.operand = VerseRandomRangeInt;
					anythingPatched = true;
				}
				else if (methodInfo == UnityRandomRangeFloat || methodInfo == UnityRandomRangeFloatObsolete)
				{
					item.operand = VerseRandomRangeFloat;
					anythingPatched = true;
				}
				else if (methodInfo == UnityRandomValue)
				{
					item.operand = VerseRandomValue;
					anythingPatched = true;
				}
				else if (methodInfo == UnityInsideUnitCircle)
				{
					item.operand = VerseInsideUnitCircle;
					anythingPatched = true;
				}
			}
			yield return item;
		}
		if (!anythingPatched)
		{
			Log.Warning("No Unity RNG was patched for method: " + (original?.FullDescription() ?? "(unknown method)"));
		}
	}

	public static void PatchCancelInInterface(params string[] methodNames)
	{
		PatchCancelInInterface((IEnumerable<string>)methodNames);
	}

	public static void PatchCancelInInterface(IEnumerable<string> methodNames)
	{
		PatchCancelInInterface(from m in methodNames.Select((string m) =>
			{
				MethodInfo methodInfo = AccessTools.DeclaredMethod(m) ?? AccessTools.Method(m);
				if (methodInfo == null)
				{
					Log.Error("(PatchingUtilities) Could not find method " + m);
				}
				return methodInfo;
			})
			where m != null
			select m);
	}

	public static void PatchCancelInInterface(params MethodBase[] methods)
	{
		PatchCancelInInterface((IEnumerable<MethodBase>)methods);
	}

	public static void PatchCancelInInterface(IEnumerable<MethodBase> methods)
	{
		HarmonyMethod patch = new HarmonyMethod(typeof(PatchingUtilities), "CancelInInterface");
		foreach (MethodBase method in methods)
		{
			PatchCancelInInterfaceInternal(method, patch);
		}
	}

	public static void PatchCancelInInterfaceSetResultToTrue(params string[] methodNames)
	{
		PatchCancelInInterfaceSetResultToTrue((IEnumerable<string>)methodNames);
	}

	public static void PatchCancelInInterfaceSetResultToTrue(IEnumerable<string> methodNames)
	{
		PatchCancelInInterfaceSetResultToTrue(from m in methodNames.Select((string m) =>
			{
				MethodInfo methodInfo = AccessTools.DeclaredMethod(m) ?? AccessTools.Method(m);
				if (methodInfo == null)
				{
					Log.Error("(PatchingUtilities) Could not find method " + m);
				}
				return methodInfo;
			})
			where m != null
			select m);
	}

	public static void PatchCancelInInterfaceSetResultToTrue(params MethodBase[] methods)
	{
		PatchCancelInInterfaceSetResultToTrue((IEnumerable<MethodBase>)methods);
	}

	public static void PatchCancelInInterfaceSetResultToTrue(IEnumerable<MethodBase> methods)
	{
		HarmonyMethod patch = new HarmonyMethod(typeof(PatchingUtilities), "CancelInInterfaceSetResultToTrue");
		foreach (MethodBase method in methods)
		{
			PatchCancelInInterfaceInternal(method, patch);
		}
	}

	private static void PatchCancelInInterfaceInternal(MethodBase method, HarmonyMethod patch)
	{
		if (method == null)
		{
			Log.Error("Trying to patch cancel in interface on a null method.");
		}
		else
		{
			MpCompat.harmony.Patch(method, patch);
		}
	}

	private static bool CancelInInterface()
	{
		return !MP.InInterface;
	}

	private static bool CancelInInterfaceSetResultToTrue(ref bool __result)
	{
		if (!MP.InInterface)
		{
			return true;
		}
		__result = true;
		return false;
	}

	public static void ReplaceCurrentMapUsage(Type type, string methodName)
	{
		if (type == null)
		{
			Log.Error((methodName == null) ? "Trying to patch current map usage for null type and null or empty method name." : ("Trying to patch current map usage for null type (" + methodName + ")."));
			return;
		}
		if (methodName.NullOrEmpty())
		{
			Log.Error("Trying to patch current map usage for null or empty method name (" + type.FullName + ").");
			return;
		}
		MethodInfo methodInfo = AccessTools.DeclaredMethod(type, methodName) ?? AccessTools.Method(type, methodName);
		if (methodInfo != null)
		{
			ReplaceCurrentMapUsage(methodInfo);
			return;
		}
		string text = (type.Namespace.NullOrEmpty() ? methodName : (type.Name + ":" + methodName));
		Log.Warning("Trying to patch current map usage for null method (" + text + "). Was the method removed or renamed?");
	}

	public static void ReplaceCurrentMapUsage(string typeColonName, bool logIfNothingPatched = true, bool logIfMissingMethod = true)
	{
		if (typeColonName.NullOrEmpty())
		{
			Log.Error("Trying to patch current map usage for null or empty method name.");
			return;
		}
		MethodInfo methodInfo = AccessTools.DeclaredMethod(typeColonName) ?? AccessTools.Method(typeColonName);
		if (methodInfo != null)
		{
			ReplaceCurrentMapUsage(methodInfo, logIfNothingPatched, logIfMissingMethod);
		}
		else if (logIfMissingMethod)
		{
			Log.Warning("Trying to patch current map usage for null method (" + typeColonName + "). Was the method removed or renamed?");
		}
	}

	public static void ReplaceCurrentMapUsage(MethodBase method, bool logIfNothingPatched = true, bool logIfMissingMethod = true)
	{
		if (method != null)
		{
			HarmonyMethod transpiler = new HarmonyMethod(logIfNothingPatched ? MpMethodUtil.MethodOf(new Func<IEnumerable<CodeInstruction>, MethodBase, IEnumerable<CodeInstruction>>(ReplaceCurrentMapUsageTranspiler)) : MpMethodUtil.MethodOf(new Func<IEnumerable<CodeInstruction>, MethodBase, IEnumerable<CodeInstruction>>(ReplaceCurrentMapUsageNoLogTranspiler)));
			MpCompat.harmony.Patch(method, null, null, transpiler);
		}
		else if (logIfMissingMethod)
		{
			Log.Warning("Trying to patch current map usage for null method. Was the method removed or renamed?");
		}
	}

	private static IEnumerable<CodeInstruction> ReplaceCurrentMapUsageTranspiler(IEnumerable<CodeInstruction> instr, MethodBase baseMethod)
	{
		return ReplaceCurrentMapUsageTranspilerInternal(instr, baseMethod, logNoCurrentMapPatches: true);
	}

	private static IEnumerable<CodeInstruction> ReplaceCurrentMapUsageNoLogTranspiler(IEnumerable<CodeInstruction> instr, MethodBase baseMethod)
	{
		return ReplaceCurrentMapUsageTranspilerInternal(instr, baseMethod, logNoCurrentMapPatches: false);
	}

	private static IEnumerable<CodeInstruction> ReplaceCurrentMapUsageTranspilerInternal(IEnumerable<CodeInstruction> instr, MethodBase baseMethod, bool logNoCurrentMapPatches)
	{
		CurrentMapPatchHelper helper = new CurrentMapPatchHelper(baseMethod);
		foreach (CodeInstruction ci in instr)
		{
			yield return ci;
			foreach (CodeInstruction item in helper.ProcessCurrentInstruction(ci))
			{
				yield return item;
			}
		}
		string text = ((baseMethod.DeclaringType?.Namespace).NullOrEmpty() ? baseMethod.Name : (baseMethod.DeclaringType.Name + ":" + baseMethod.Name));
		if (!helper.IsSupported)
		{
			Log.Warning("Unsupported type, can't patch current map usage for " + text);
		}
		else if (logNoCurrentMapPatches && !helper.IsPatched)
		{
			Log.Warning("Failed patching current map usage for " + text);
		}
	}

	public static void PatchTryGainMemory(TryHandleGainMemory memoryGainHandler)
	{
		if (memoryGainHandler == null)
		{
			Log.Error("Trying to patch TryGainMemory, but delegate is null.");
			return;
		}
		if (tryGainMemoryHandlers == null)
		{
			tryGainMemoryHandlers = new List<TryHandleGainMemory>();
		}
		tryGainMemoryHandlers.Add(memoryGainHandler);
		if (!patchedVanillaMethod)
		{
			MethodInfo original = AccessTools.DeclaredMethod(typeof(MemoryThoughtHandler), "TryGainMemory", new Type[2]
			{
				typeof(Thought_Memory),
				typeof(Pawn)
			});
			MpCompat.harmony.Patch(original, null, new HarmonyMethod(typeof(PatchingUtilities), "PostTryGainMemory"));
			patchedVanillaMethod = true;
		}
	}

	private static void PostTryGainMemory(Thought_Memory newThought)
	{
		if (newThought.pawn != null)
		{
			for (int i = 0; i < tryGainMemoryHandlers.Count && !tryGainMemoryHandlers[i](newThought); i++)
			{
			}
		}
	}

	public static void SetupAsyncTime()
	{
		if (isAsyncTimeSetup)
		{
			return;
		}
		isAsyncTimeSetup = true;
		try
		{
			Type type = AccessTools.TypeByName("Multiplayer.Client.Compatibility.OwnerClock");
			if (type != null && (int)AccessTools.DeclaredField(type, "Version").GetRawConstantValue() >= 1)
			{
				BeginMapRead beginMapRead = (BeginMapRead)AccessTools.DeclaredMethod(type, "BeginMapRead").CreateDelegate(typeof(BeginMapRead));
				BeginWorldRead beginWorldRead = (BeginWorldRead)AccessTools.DeclaredMethod(type, "BeginWorldRead").CreateDelegate(typeof(BeginWorldRead));
				BeginPawnRead beginPawnRead = (BeginPawnRead)AccessTools.DeclaredMethod(type, "BeginPawnRead").CreateDelegate(typeof(BeginPawnRead));
				Action<long> action = (Action<long>)AccessTools.DeclaredMethod(type, "EndRead").CreateDelegate(typeof(Action<long>));
				beginOwnerMapRead = beginMapRead;
				beginOwnerWorldRead = beginWorldRead;
				beginOwnerPawnRead = beginPawnRead;
				endOwnerRead = action;
			}
		}
		catch (Exception value)
		{
			Log.Error($"Encountered an exception while binding Core owner clock facade:\n{value}");
		}
		try
		{
			multiplayerGameField = AccessTools.StaticFieldRefAccess<object>(AccessTools.DeclaredField("Multiplayer.Client.Multiplayer:game"));
		}
		catch (Exception value2)
		{
			Log.Error($"Encountered an exception while settings up core async time functionality:\n{value2}");
			return;
		}
		try
		{
			gameGameCompField = AccessTools.FieldRefAccess<object>("Multiplayer.Client.MultiplayerGame:gameComp");
			gameCompAsyncTimeField = AccessTools.FieldRefAccess<bool>("Multiplayer.Client.Comp.MultiplayerGameComp:asyncTime");
			isAsyncTimeGameCompSuccessful = true;
		}
		catch (Exception value3)
		{
			Log.Error($"Encountered an exception while settings up game async time:\n{value3}");
		}
		try
		{
			getAsyncTimeCompForMapMethod = AccessTools.DeclaredMethod("Multiplayer.Client.Extensions:AsyncTime").CreateDelegate<Func<Map, object>>();
			Type type2 = AccessTools.TypeByName("Multiplayer.Client.AsyncTimeComp");
			asyncTimeMapTicksField = AccessTools.FieldRefAccess<int>(type2, "mapTicks");
			asyncTimeSlowerField = AccessTools.FieldRefAccess<TimeSlower>(type2, "slower");
			asyncTimeTimeSpeedIntField = AccessTools.FieldRefAccess<TimeSpeed>(type2, "timeSpeedInt");
			asyncTimeGameStartAbsTickField = AccessTools.FieldRefAccess<int>(type2, "gameStartAbsTickMap");
			isAsyncTimeMapCompSuccessful = true;
		}
		catch (Exception value4)
		{
			Log.Error($"Encountered an exception while settings up map async time:\n{value4}");
		}
		try
		{
			gameAsyncWorldTimeField = AccessTools.FieldRefAccess<object>("Multiplayer.Client.MultiplayerGame:asyncWorldTimeComp");
			asyncWorldTimeTicksField = AccessTools.FieldRefAccess<int>("Multiplayer.Client.AsyncTime.AsyncWorldTimeComp:worldTicks");
			MethodInfo methodInfo = AccessTools.DeclaredMethod("Multiplayer.Client.AsyncTimeComp:GetWorldTicksAbs");
			if (methodInfo != null)
			{
				getWorldTicksAbsMethod = AccessTools.MethodDelegate<Func<int>>(methodInfo);
			}
			MethodInfo methodInfo2 = AccessTools.DeclaredMethod("Multiplayer.Client.Patches.TimestampFixer:GetPawnClockMap");
			if (methodInfo2 != null)
			{
				getPawnClockMapMethod = methodInfo2.CreateDelegate<Func<Pawn, Map>>();
			}
			isAsyncTimeWorldCompSuccessful = true;
		}
		catch (Exception value5)
		{
			Log.Error($"Encountered an exception while setting up world ability time:\n{value5}");
		}
		try
		{
			MethodInfo methodInfo3 = AccessTools.DeclaredMethod("Multiplayer.Client.SimulationTimeContext:ReadClock");
			if (!(methodInfo3 != null))
			{
				return;
			}
			ParameterInfo[] parameters = methodInfo3.GetParameters();
			Type[] array = new Type[5]
			{
				typeof(object),
				typeof(int).MakeByRefType(),
				typeof(TimeSpeed).MakeByRefType(),
				typeof(TimeSlower).MakeByRefType(),
				typeof(int).MakeByRefType()
			};
			if (!methodInfo3.IsStatic || methodInfo3.ReturnType != typeof(void) || parameters.Length != array.Length)
			{
				throw new MissingMethodException("Multiplayer shared clock reader signature changed");
			}
			for (int i = 1; i < parameters.Length; i++)
			{
				if (parameters[i].ParameterType != array[i])
				{
					throw new MissingMethodException("Multiplayer shared clock reader parameter changed");
				}
			}
			DynamicMethod dynamicMethod = new DynamicMethod("MpCompatReadClock", typeof(void), array, typeof(PatchingUtilities), skipVisibility: true);
			ILGenerator iLGenerator = dynamicMethod.GetILGenerator();
			iLGenerator.Emit(OpCodes.Ldarg_0);
			iLGenerator.Emit(OpCodes.Castclass, parameters[0].ParameterType);
			for (byte b = 1; b < array.Length; b++)
			{
				iLGenerator.Emit(OpCodes.Ldarg_S, b);
			}
			iLGenerator.Emit(OpCodes.Call, methodInfo3);
			iLGenerator.Emit(OpCodes.Ret);
			readAsyncClock = dynamicMethod.CreateDelegate<ReadClock>();
		}
		catch (Exception value6)
		{
			Log.Error($"Encountered an exception while setting up shared async clock reads:\n{value6}");
		}
	}

	public static void RegisterTimestampFields(Type type, Action<object, int> convert)
	{
		if (type == null)
		{
			throw new ArgumentNullException("type");
		}
		if (convert == null)
		{
			throw new ArgumentNullException("convert");
		}
		if (registerTimestampFields == null)
		{
			MethodInfo methodInfo = AccessTools.Method("Multiplayer.Client.Patches.TimestampFixer:RegisterTimestampFields", new Type[2]
			{
				typeof(Type),
				typeof(Action<object, int>)
			});
			if (methodInfo == null)
			{
				throw new MissingMethodException("Multiplayer.Client.Patches.TimestampFixer", "RegisterTimestampFields");
			}
			registerTimestampFields = (Action<Type, Action<object, int>>)methodInfo.CreateDelegate(typeof(Action<Type, Action<object, int>>));
		}
		registerTimestampFields(type, convert);
	}

	public static void RegisterTimestampFixer(Type type, MethodInfo timestampFixerMethod)
	{
		if (type == null || timestampFixerMethod == null)
		{
			Log.Error("Trying to register timestamp fixer failed - value null. Type=" + type.ToStringSafe() + ", Method=" + timestampFixerMethod.ToStringSafe());
			return;
		}
		InitializeTimestampFixer();
		if (registerTimestampFields != null)
		{
			try
			{
				TimestampFieldGetter getter = (TimestampFieldGetter)timestampFixerMethod.CreateDelegate(typeof(TimestampFieldGetter));
				registerTimestampFields(type, (object obj, int offset) =>
				{
					if (!(obj.GetType() != type))
					{
						getter((IExposable)obj) += offset;
					}
				});
				return;
			}
			catch (Exception value)
			{
				Log.Error($"Trying to initialize timestamp fixer failed, exception caught:\n{value}");
				return;
			}
		}
		if (timestampFixerDelegateType == null || timestampFixerListType == null || timestampFieldsDictionaryField == null)
		{
			return;
		}
		try
		{
			IDictionary dictionary = timestampFieldsDictionaryField();
			IList list = (IList)(dictionary.Contains(type) ? ((IList)dictionary[type]) : (dictionary[type] = (IList)Activator.CreateInstance(timestampFixerListType)));
			list.Add(Delegate.CreateDelegate(timestampFixerDelegateType, timestampFixerMethod));
		}
		catch (Exception value2)
		{
			Log.Error($"Trying to initialize timestamp fixer failed, exception caught:\n{value2}");
		}
	}

	public static void InitializeTimestampFixer()
	{
		if (isTimestampFixerInitialized)
		{
			return;
		}
		isTimestampFixerInitialized = true;
		try
		{
			Type type = AccessTools.TypeByName("Multiplayer.Client.Patches.TimestampFixer");
			if (type == null)
			{
				Log.Error("Trying to initialize timestamp fixer failed, could not find TimestampFixer type.");
				return;
			}
			MethodInfo methodInfo = AccessTools.Method(type, "RegisterTimestampFields", new Type[2]
			{
				typeof(Type),
				typeof(Action<object, int>)
			});
			if (methodInfo != null)
			{
				registerTimestampFields = (Action<Type, Action<object, int>>)methodInfo.CreateDelegate(typeof(Action<Type, Action<object, int>>));
				return;
			}
			Type type2 = AccessTools.Inner(type, "FieldGetter`1");
			if (type2 == null)
			{
				Log.Error("Trying to initialize timestamp fixer failed, could not find FieldGetter inner type.");
				return;
			}
			timestampFieldsDictionaryField = AccessTools.StaticFieldRefAccess<IDictionary>(AccessTools.DeclaredField(type, "timestampFields"));
			timestampFixerDelegateType = type2.MakeGenericType(typeof(IExposable));
			timestampFixerListType = typeof(List<>).MakeGenericType(timestampFixerDelegateType);
		}
		catch (Exception value)
		{
			Log.Error($"Trying to initialize timestamp fixer failed, exception caught:\n{value}");
			timestampFixerDelegateType = null;
			timestampFixerListType = null;
			timestampFieldsDictionaryField = null;
		}
	}

	public static void CancelCallIfLongEventsAreUnsafe(params string[] methodNames)
	{
		CancelCallIfLongEventsAreUnsafe((IEnumerable<string>)methodNames);
	}

	public static void CancelCallIfLongEventsAreUnsafe(IEnumerable<string> methodNames)
	{
		CancelCallIfLongEventsAreUnsafe(from m in methodNames.Select((string m) =>
			{
				MethodInfo methodInfo = AccessTools.DeclaredMethod(m) ?? AccessTools.Method(m);
				if (methodInfo == null)
				{
					Log.Error("(PatchingUtilities) Could not find method " + m);
				}
				return methodInfo;
			})
			where m != null
			select m);
	}

	public static void CancelCallIfLongEventsAreUnsafe(params MethodBase[] methods)
	{
		CancelCallIfLongEventsAreUnsafe((IEnumerable<MethodBase>)methods);
	}

	public static void CancelCallIfLongEventsAreUnsafe(IEnumerable<MethodBase> methods)
	{
		PatchLongEventMarkers();
		HarmonyMethod prefix = new HarmonyMethod(typeof(PatchingUtilities), "StopSecondHostCall");
		foreach (MethodBase method in methods)
		{
			if (method == null)
			{
				Log.Error("Trying to patch unsafe long event, but the method is null.");
			}
			else
			{
				MpCompat.harmony.Patch(method, prefix);
			}
		}
	}

	public static void PatchLongEventMarkers()
	{
		if (!longEventMarkerPatchActive)
		{
			longEventMarkerPatchActive = true;
			MpCompat.harmony.Patch(AccessTools.DeclaredMethod("Multiplayer.Client.SaveLoad:LoadInMainThread"), new HarmonyMethod(typeof(PatchingUtilities), "SetDisallowedToRun"), null, null, new HarmonyMethod(typeof(PatchingUtilities), "SetAllowedToRun"));
		}
	}

	private static void SetDisallowedToRun()
	{
		AllowedToRunLongEvents = false;
	}

	private static void SetAllowedToRun()
	{
		AllowedToRunLongEvents = true;
	}

	private static bool StopSecondHostCall()
	{
		return AllowedToRunLongEvents;
	}

	public static IEnumerable<CodeInstruction> ReplaceMethod(this IEnumerable<CodeInstruction> instr, MethodBase from, MethodBase to = null, MethodBase baseMethod = null, Func<CodeInstruction, IEnumerable<CodeInstruction>> extraInstructionsBefore = null, Func<CodeInstruction, IEnumerable<CodeInstruction>> extraInstructionsAfter = null, int expectedReplacements = -1, string targetText = null, string excludedText = null, Func<CodeInstruction, bool> targetInstruction = null, Func<CodeInstruction, bool> excludedInstruction = null)
	{
		if (instr == null)
		{
			throw new ArgumentNullException("instr");
		}
		if (from == null)
		{
			Log.Error("Call to ReplaceMethod is meaningless as the target method is null for method " + MethodName());
		}
		if (to == null && extraInstructionsBefore == null && extraInstructionsAfter == null)
		{
			Log.Error("Call to ReplaceMethod is meaningless as no useful arguments were provided for method " + MethodName());
		}
		if (targetText != null)
		{
			if (targetInstruction != null)
			{
				Log.Error($"Call to {"ReplaceMethod"} provides both {"targetText"} and {"targetInstruction"}, which are mutually exclusive, for method {MethodName()}");
			}
			targetInstruction = (CodeInstruction codeInstruction) => codeInstruction.opcode == OpCodes.Ldstr && codeInstruction.operand as string == targetText;
		}
		if (excludedText != null)
		{
			if (excludedInstruction != null)
			{
				Log.Error($"Call to {"ReplaceMethod"} provides both {"excludedText"} and {"excludedInstruction"}, which are mutually exclusive, for method {MethodName()}");
			}
			excludedInstruction = (CodeInstruction codeInstruction) => codeInstruction.opcode == OpCodes.Ldstr && codeInstruction.operand as string == excludedText;
		}
		bool isCorrectText = targetInstruction == null;
		bool skipNextCall = false;
		int replacedCount = 0;
		bool insertInstructionsAfter = false;
		foreach (CodeInstruction ci in instr)
		{
			if (excludedInstruction?.Invoke(ci) ?? false)
			{
				skipNextCall = true;
			}
			else if (targetInstruction?.Invoke(ci) ?? false)
			{
				isCorrectText = true;
			}
			else if (isCorrectText && ci.operand is MethodBase methodBase && methodBase == from)
			{
				isCorrectText = targetInstruction == null;
				if (skipNextCall)
				{
					skipNextCall = false;
				}
				else
				{
					if (to != null)
					{
						ci.opcode = (to.IsConstructor ? OpCodes.Newobj : OpCodes.Call);
						ci.operand = to;
					}
					if (extraInstructionsBefore != null)
					{
						foreach (CodeInstruction item in extraInstructionsBefore(ci))
						{
							yield return item;
						}
					}
					replacedCount++;
					insertInstructionsAfter = true;
				}
			}
			yield return ci;
			if (!insertInstructionsAfter || extraInstructionsAfter == null)
			{
				continue;
			}
			insertInstructionsAfter = false;
			foreach (CodeInstruction item2 in extraInstructionsAfter(ci))
			{
				yield return item2;
			}
		}
		if (replacedCount != expectedReplacements && expectedReplacements >= 0)
		{
			Log.Warning($"Patched incorrect number of {from.DeclaringType?.Name ?? "null"}.{from.Name} calls (patched {replacedCount}, expected {expectedReplacements}) for method {MethodName()}");
		}
		else if (replacedCount == 0 && expectedReplacements == -2)
		{
			Log.Warning($"No calls of {from.DeclaringType?.Name ?? "null"}.{from.Name} were patched for method {MethodName()}");
		}
		string MethodName()
		{
			if (baseMethod == null)
			{
				return "(unknown)";
			}
			if ((baseMethod.DeclaringType?.Namespace).NullOrEmpty())
			{
				return baseMethod.Name;
			}
			return baseMethod.DeclaringType.Name + ":" + baseMethod.Name;
		}
	}
}
