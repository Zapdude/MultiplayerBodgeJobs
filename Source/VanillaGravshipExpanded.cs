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
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("vanillaexpanded.gravship")]
public class VanillaGravshipExpanded
{
	private sealed class LandingObstacles
	{
		internal Gravship Ship;

		internal Map Map;

		internal int MapId;

		internal int EngineId;

		internal IntVec3 Position;

		internal List<Thing> Things;
	}

	private delegate bool OrderedPlacement(WorldComponent_GravshipController controller, Gravship ship, Map map, IntVec3 root, out bool managed);

	private delegate bool CurrentLanding(Gravship ship, Map map, IntVec3 root, out bool managed);

	private static readonly AccessTools.FieldRef<WorldComponent_GravshipController, Map> landingMap = AccessTools.FieldRefAccess<WorldComponent_GravshipController, Map>("map");

	private static readonly AccessTools.FieldRef<WorldComponent_GravshipController, Gravship> landingGravship = AccessTools.FieldRefAccess<WorldComponent_GravshipController, Gravship>("gravship");

	private static readonly ConditionalWeakTable<Game, LandingObstacles> obstacles = new ConditionalWeakTable<Game, LandingObstacles>();

	private static Func<IntVec3, Map, IEnumerable<Thing>> blockingInCell;

	private static OrderedPlacement orderedPlacement;

	private static CurrentLanding currentLanding;

	private static FieldInfo authorBlockingThings;

	[ThreadStatic]
	private static WorldComponent_GravshipController authorController;

	public VanillaGravshipExpanded(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(() =>
		{
			Type type = mod.assemblies.loadedAssemblies.Find((Assembly assembly) => assembly.GetName().Name == "VanillaGravshipExpanded")?.GetType("VanillaGravshipExpanded.WorldComponent_GravshipController_LandingEnded_Patch", throwOnError: true) ?? throw new InvalidOperationException("VGE landing lifecycle patch was not loaded");
			MethodInfo original = AccessTools.DeclaredMethod(type, "Prefix") ?? throw new MissingMethodException("VGE landing Prefix");
			MethodInfo original2 = AccessTools.DeclaredMethod(type, "Postfix") ?? throw new MissingMethodException("VGE landing Postfix");
			MethodInfo original3 = AccessTools.DeclaredMethod(type, "ApplyCrashlanding") ?? throw new MissingMethodException("VGE ApplyCrashlanding");
			MethodInfo original4 = AccessTools.DeclaredMethod(typeof(WorldComponent_GravshipController), "PlaceGravship") ?? throw new MissingMethodException("Native PlaceGravship");
			MethodInfo original5 = AccessTools.DeclaredMethod(typeof(WorldComponent_GravshipController), "LandingEnded") ?? throw new MissingMethodException("Native LandingEnded");
			Type? type2 = type.Assembly.GetType("VanillaGravshipExpanded.GravshipMapGenUtility", throwOnError: true);
			blockingInCell = (Func<IntVec3, Map, IEnumerable<Thing>>)(AccessTools.DeclaredMethod(type2, "GetBlockingThingsInCell") ?? throw new MissingMethodException("VGE GetBlockingThingsInCell")).CreateDelegate(typeof(Func<IntVec3, Map, IEnumerable<Thing>>));
			authorBlockingThings = AccessTools.DeclaredField(type2, "BlockingThings");
			if (authorBlockingThings?.FieldType != typeof(HashSet<Thing>))
			{
				throw new MissingFieldException("VGE BlockingThings contract changed");
			}
			Type type3 = AccessTools.TypeByName("Multiplayer.Client.Patches.GravshipFlightPreparation");
			MethodInfo methodInfo = AccessTools.DeclaredMethod(type3, "IsOrderedLandingPlacement", new Type[5]
			{
				typeof(WorldComponent_GravshipController),
				typeof(Gravship),
				typeof(Map),
				typeof(IntVec3),
				typeof(bool).MakeByRefType()
			}) ?? throw new MissingMethodException("Multiplayer GravshipFlightPreparation.IsOrderedLandingPlacement");
			MethodInfo methodInfo2 = AccessTools.DeclaredMethod(type3, "MatchesCurrentLanding", new Type[4]
			{
				typeof(Gravship),
				typeof(Map),
				typeof(IntVec3),
				typeof(bool).MakeByRefType()
			}) ?? throw new MissingMethodException("Multiplayer GravshipFlightPreparation.MatchesCurrentLanding");
			orderedPlacement = (OrderedPlacement)methodInfo.CreateDelegate(typeof(OrderedPlacement));
			currentLanding = (CurrentLanding)methodInfo2.CreateDelegate(typeof(CurrentLanding));
			VanillaGravshipMaintenanceEffects.Install(type.Assembly);
			VanillaGravshipPreview.Install("VanillaGravshipExpanded.LandingStructure");
			MpCompat.harmony.Patch(original, new HarmonyMethod(typeof(VanillaGravshipExpanded), "BeforeLandingPrefix"), null, null, new HarmonyMethod(typeof(VanillaGravshipExpanded), "AfterLandingPrefix"));
			MpCompat.harmony.Patch(original2, new HarmonyMethod(typeof(VanillaGravshipExpanded), "BeforeLandingPostfix"));
			MpCompat.harmony.Patch(original4, new HarmonyMethod(typeof(VanillaGravshipExpanded), "CaptureLandingObstacles")
			{
				priority = 800
			});
			MpCompat.harmony.Patch(original3, null, null, new HarmonyMethod(typeof(VanillaGravshipExpanded), "StableCrashEnumeration"));
			MpCompat.harmony.Patch(original5, null, new HarmonyMethod(typeof(VanillaGravshipExpanded), "AfterNativeLandingEnd")
			{
				priority = 0
			});
		});
	}

	private static bool BeforeLandingPrefix(WorldComponent_GravshipController __0, out WorldComponent_GravshipController __state)
	{
		__state = authorController;
		authorController = __0;
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (!MP.IsExecutingSyncCommand || __0 == null || landingMap(__0) == null || landingGravship(__0) == null)
		{
			return false;
		}
		if (IsManagedCrash(landingGravship(__0), landingMap(__0)))
		{
			RequireObstacles(landingGravship(__0), landingMap(__0));
		}
		return true;
	}

	private static void AfterLandingPrefix(WorldComponent_GravshipController __state)
	{
		authorController = __state;
	}

	private static bool BeforeLandingPostfix((Gravship gravship, Dictionary<LandingOutcomeDef, float> outcomes) __0)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (MP.IsExecutingSyncCommand && __0.gravship != null)
		{
			return __0.outcomes != null;
		}
		return false;
	}

	private static void CaptureLandingObstacles(WorldComponent_GravshipController __instance, Gravship gravship, IntVec3 root, Map map)
	{
		if (!MP.IsInMultiplayer)
		{
			return;
		}
		if (!orderedPlacement(__instance, gravship, map, root, out var managed))
		{
			if (!managed)
			{
				return;
			}
			throw new InvalidOperationException("VGE obstacle capture requires the ordered gravship placement");
		}
		if (Current.Game == null || landingMap(__instance) != map || map == null || map.Disposed || gravship?.Engine == null)
		{
			throw new InvalidOperationException("VGE obstacle capture has an invalid destination");
		}
		if (obstacles.TryGetValue(Current.Game, out var value))
		{
			if (value.Ship == gravship && value.Map == map && value.Position == root)
			{
				return;
			}
			throw new InvalidOperationException("Another VGE landing obstacle snapshot is still pending");
		}
		HashSet<Thing> hashSet = new HashSet<Thing>();
		foreach (IntVec3 item in from cell in gravship.Foundations.Keys
			select cell + root into cell
			orderby cell.z, cell.x
			select cell)
		{
			foreach (Thing item2 in blockingInCell(item, map))
			{
				hashSet.Add(item2);
			}
		}
		if (hashSet.Any((Thing thing) => thing == null || thing.thingIDNumber < 0))
		{
			throw new InvalidOperationException("VGE destination obstacle has no authoritative Thing ID");
		}
		obstacles.Add(Current.Game, new LandingObstacles
		{
			Ship = gravship,
			Map = map,
			MapId = map.uniqueID,
			EngineId = gravship.Engine.thingIDNumber,
			Position = root,
			Things = hashSet.OrderBy((Thing thing) => thing.thingIDNumber).ToList()
		});
	}

	private static LandingObstacles RequireObstacles(Gravship ship, Map map)
	{
		if (Current.Game == null || !MP.IsExecutingSyncCommand || !obstacles.TryGetValue(Current.Game, out var value) || value.Ship != ship || value.Map != map || !currentLanding(ship, map, value.Position, out var _) || map == null || map.Disposed || value.MapId != map.uniqueID || ship?.Engine == null || value.EngineId != ship.Engine.thingIDNumber || ship.Engine.Map != map || ship.Engine.Position != value.Position)
		{
			throw new InvalidOperationException("VGE landing has no matching synchronized destination obstacle snapshot");
		}
		return value;
	}

	private static bool IsManagedCrash(Gravship ship, Map map)
	{
		if (!MP.IsInMultiplayer || (authorController != null && authorController != Find.GravshipController))
		{
			return false;
		}
		IntVec3 root = ship?.Engine?.Position ?? IntVec3.Invalid;
		if (Current.Game != null && obstacles.TryGetValue(Current.Game, out var value) && value.Ship == ship)
		{
			root = value.Position;
		}
		currentLanding(ship, map, root, out var managed);
		return managed;
	}

	private static IEnumerable<Thing> CrashTargets(Gravship ship, Map map)
	{
		if (!IsManagedCrash(ship, map))
		{
			return (HashSet<Thing>)authorBlockingThings.GetValue(null);
		}
		return RequireObstacles(ship, map).Things;
	}

	private static void AfterNativeLandingEnd(WorldComponent_GravshipController __instance)
	{
		if (MP.IsInMultiplayer && MP.IsExecutingSyncCommand && Current.Game != null && __instance == Find.GravshipController && obstacles.TryGetValue(Current.Game, out var value) && currentLanding(value.Ship, value.Map, value.Position, out var _) && landingMap(__instance) == null && landingGravship(__instance) == null)
		{
			obstacles.Remove(Current.Game);
		}
	}

	private static IEnumerable<CodeInstruction> StableCrashEnumeration(IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase original)
	{
		Type nativeEnumerator = typeof(HashSet<Thing>.Enumerator);
		LocalVariableInfo[] array = original.GetMethodBody()?.LocalVariables.Where((LocalVariableInfo local) => local.LocalType == nativeEnumerator).ToArray();
		if (array == null || array.Length != 1)
		{
			throw new InvalidOperationException("Expected one VGE crash obstacle enumerator");
		}
		int oldIndex = array[0].LocalIndex;
		LocalBuilder ordered = generator.DeclareLocal(typeof(IEnumerator<Thing>));
		int field = 0;
		int iterator = 0;
		int current = 0;
		int next = 0;
		int addresses = 0;
		int stores = 0;
		int dispose = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.LoadsField(authorBlockingThings))
			{
				CodeInstruction codeInstruction = new CodeInstruction(OpCodes.Ldarg_0);
				codeInstruction.labels.AddRange(instruction.labels);
				codeInstruction.blocks.AddRange(instruction.blocks);
				yield return codeInstruction;
				yield return new CodeInstruction(OpCodes.Ldarg_1);
				yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(VanillaGravshipExpanded), "CrashTargets"));
				field++;
				continue;
			}
			if (instruction.Calls(AccessTools.Method(typeof(HashSet<Thing>), "GetEnumerator")))
			{
				instruction.operand = AccessTools.Method(typeof(IEnumerable<Thing>), "GetEnumerator");
				iterator++;
			}
			else if (instruction.Calls(AccessTools.PropertyGetter(nativeEnumerator, "Current")))
			{
				instruction.opcode = OpCodes.Callvirt;
				instruction.operand = AccessTools.PropertyGetter(typeof(IEnumerator<Thing>), "Current");
				current++;
			}
			else if (instruction.Calls(AccessTools.Method(nativeEnumerator, "MoveNext")))
			{
				instruction.opcode = OpCodes.Callvirt;
				instruction.operand = AccessTools.Method(typeof(IEnumerator), "MoveNext");
				next++;
			}
			else if (instruction.opcode == OpCodes.Constrained && object.Equals(instruction.operand, nativeEnumerator))
			{
				instruction.opcode = OpCodes.Nop;
				instruction.operand = null;
				dispose++;
			}
			else if (instruction.IsStloc() && instruction.LocalIndex() == oldIndex)
			{
				instruction.opcode = OpCodes.Stloc;
				instruction.operand = ordered;
				stores++;
			}
			else if ((instruction.opcode == OpCodes.Ldloca || instruction.opcode == OpCodes.Ldloca_S) && instruction.LocalIndex() == oldIndex)
			{
				instruction.opcode = OpCodes.Ldloc;
				instruction.operand = ordered;
				addresses++;
			}
			yield return instruction;
		}
		if (field != 1 || iterator != 1 || current != 1 || next != 1 || addresses != 3 || stores != 1 || dispose != 1)
		{
			throw new InvalidOperationException($"VGE crash traversal changed: {field}/{iterator}/{current}/{next}/{addresses}/{stores}/{dispose}");
		}
	}
}
