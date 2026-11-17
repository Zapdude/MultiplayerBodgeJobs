using System;
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

namespace Multiplayer.Compat;

[MpCompatFor("Aoba.Exosuit.Framework")]
public class ExosuitFramework
{
	private sealed class FlightBayScope : IDisposable
	{
		internal readonly Thing PreviousBay = flightIssuingBay;

		internal readonly int PreviousIssuer = flightIssuer;

		private readonly IDisposable scope;

		internal FlightBayScope(Thing bay)
		{
			scope = new ModernOwnerScope(bay);
			flightIssuingBay = bay;
			flightIssuer = SessionServices.ExecutingPlayer();
		}

		public void Dispose()
		{
			try
			{
				flightIssuingBay = PreviousBay;
				flightIssuer = PreviousIssuer;
			}
			finally
			{
				scope.Dispose();
			}
		}
	}

	private sealed class ModernOwnerScope : IDisposable
	{
		private readonly object owner;

		private readonly IDisposable faction;

		internal ModernOwnerScope(Thing thing)
		{
			thing = ModernRoot(thing);
			Map map = thing?.MapHeld;
			if (map != null)
			{
				owner = modernMapOwner.Invoke(null, new object[1] { map });
				if (owner != null)
				{
					modernPre.Invoke(owner, null);
				}
			}
			try
			{
				if (thing?.Faction != null)
				{
					faction = CommandAuthorityBridge.EnterFaction(thing.Faction);
				}
			}
			catch
			{
				if (owner != null)
				{
					modernPost.Invoke(owner, null);
				}
				throw;
			}
		}

		public void Dispose()
		{
			try
			{
				faction?.Dispose();
			}
			finally
			{
				if (owner != null)
				{
					modernPost.Invoke(owner, null);
				}
			}
		}
	}

	private sealed class ModernUiLifetime
	{
		internal readonly Game Game = Current.Game;

		internal readonly object Session = modernSession.GetValue(null);

		internal readonly Faction Viewer = modernViewer();

		internal readonly object Tab;

		internal readonly Thing Bay;

		internal bool Valid
		{
			get
			{
				if (Current.Game == Game && modernSession.GetValue(null) == Session && modernViewer() == Viewer && ModernTabBay(Tab) == Bay)
				{
					return ModernCanAct(Bay);
				}
				return false;
			}
		}

		internal ModernUiLifetime(object tab)
		{
			Tab = tab;
			Bay = ModernTabBay(tab);
		}
	}

	private static Type flightFlyer;

	private static Type flightBay;

	private static MethodInfo flightJump;

	private static FieldInfo flightRangeStat;

	private static Func<bool> flightWarming;

	[ThreadStatic]
	private static Thing flightIssuingBay;

	[ThreadStatic]
	private static int flightIssuer;

	private static readonly ConditionalWeakTable<Game, HashSet<string>> flightPresented = new ConditionalWeakTable<Game, HashSet<string>>();

	private static Type modernBay;

	private static Type modernTurret;

	private static MethodInfo modernMapOwner;

	private static MethodInfo modernPre;

	private static MethodInfo modernPost;

	private static Func<Faction> modernViewer;

	private static Func<bool> modernTicking;

	private static Func<bool> modernSimEvent;

	private static FieldInfo modernSession;

	private static FieldInfo dummyPawn;

	private static FieldInfo turretLastAttack;

	private static FieldInfo turretLastSearch;

	private static readonly Dictionary<Type, FieldInfo[]> closureFields = new Dictionary<Type, FieldInfo[]>();

	private static readonly Dictionary<bool, (MethodInfo method, Type capture)> refGetInMethods = new Dictionary<bool, (MethodInfo, Type)>();

	private static readonly ConditionalWeakTable<Pawn, Thing> dummyBays = new ConditionalWeakTable<Pawn, Thing>();

	private static readonly ConditionalWeakTable<Thing, Thing> moduleBays = new ConditionalWeakTable<Thing, Thing>();

	private static readonly ConditionalWeakTable<Window, ModernUiLifetime> companionWindows = new ConditionalWeakTable<Window, ModernUiLifetime>();

	[ThreadStatic]
	private static ModernUiLifetime openingModernMenu;

	private static Type modernFuelComp;

	private static Type modernWeaponComp;

	private static Type modernWeaponType;

	private static Type modernForcedWeaponType;

	private static MethodInfo modernWeaponBareHands;

	private static MethodInfo modernWeaponWield;

	private static FieldInfo modernWeaponCompCapture;

	private static FieldInfo modernWeaponPawnCapture;

	internal static bool ModernFlightInstalled { get; private set; }

	private static ExosuitFlightComponent FlightState => Current.Game?.GetComponent<ExosuitFlightComponent>();

	public ExosuitFramework(ModContentPack mod)
	{
		if (AccessTools.TypeByName("Exosuit.Exosuit_Core") != null)
		{
			LongEventHandler.ExecuteWhenFinished(InstallModern);
			return;
		}
		LongEventHandler.ExecuteWhenFinished(LatePatch);
		MpCompat.RegisterLambdaMethod("WalkerGear.Building_EjectorBay", "GetGizmos", 0, 3, 8).SkipLast(1).SetContext(SyncContext.CurrentMap);
		MpCompat.RegisterLambdaMethod("WalkerGear.Building_MaintenanceBay", "GetGizmos", 0, 2);
		MpCompat.RegisterLambdaMethod("WalkerGear.ModuleComp_EmergencyEject", "CompGetWornGizmosExtra", 1);
		Type type = AccessTools.TypeByName("WalkerGear.Building_MaintenanceBay");
		MP.RegisterSyncMethod(type, "AddOrReplaceModule");
		MP.RegisterSyncMethod(type, "RemoveModules");
		MpCompat.RegisterLambdaDelegate(AccessTools.TypeByName("WalkerGear.FloatMenuMakerMap_MakeForFrame"), "AddHumanlikeOrders", 1);
	}

	private static void LatePatch()
	{
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod("WalkerGear.WalkerGear_Core:Eject"));
	}

	private static object FlightGet(object value, string name)
	{
		return AccessTools.Field(value.GetType(), name).GetValue(value);
	}

	private static void FlightSet(object value, string name, object field)
	{
		AccessTools.Field(value.GetType(), name).SetValue(value, field);
	}

	private static bool FlightBayBefore(Thing __instance, out IDisposable __state)
	{
		__state = null;
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (!ModernCanAct(__instance))
		{
			return false;
		}
		__state = new FlightBayScope(__instance);
		return true;
	}

	private static Exception FlightBayAfter(Exception __exception, IDisposable __state)
	{
		__state?.Dispose();
		return __exception;
	}

	private static Map FlightBayMap(Thing bay)
	{
		if (!MP.IsInMultiplayer)
		{
			return Find.CurrentMap;
		}
		return bay.Map;
	}

	private static IEnumerable<CodeInstruction> FlightBayMapIl(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo current = AccessTools.PropertyGetter(typeof(Find), "CurrentMap");
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(current))
			{
				yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
				yield return CodeInstruction.Call(typeof(ExosuitFramework), "FlightBayMap");
			}
			else
			{
				yield return instruction;
			}
		}
	}

	private static void FlightMade(Thing __result, Pawn pawn, Map destMap, bool isToMap)
	{
		if (MP.IsInMultiplayer && isToMap && __result != null && pawn != null && destMap != null)
		{
			ExosuitFlightComponent flightState = FlightState;
			ExosuitFlightRequest exosuitFlightRequest = new ExosuitFlightRequest
			{
				Id = flightState.NextId++,
				Pawn = pawn,
				Flyer = __result,
				Origin = destMap.Tile,
				Source = destMap,
				Phase = 0
			};
			object value = AccessTools.Property(flightFlyer, "Extension").GetValue(__result);
			int num = (int)AccessTools.Field(value.GetType(), "flightRange").GetValue(value);
			StatDef stat = flightRangeStat.GetValue(null) as StatDef;
			exosuitFlightRequest.Range = Math.Max(0, num + (int)pawn.GetStatValue(stat));
			SessionServices.SetOwner(exosuitFlightRequest.Ownership, (flightIssuingBay != null) ? flightIssuer : SessionServices.ExecutingPlayer());
			flightState.Requests.Add(exosuitFlightRequest);
		}
	}

	private static bool FlightWorldTransition(Thing __instance, Pawn pawn, ref bool __result)
	{
		if (!MP.IsInMultiplayer || !(bool)FlightGet(__instance, "isToMap"))
		{
			return true;
		}
		__result = true;
		ExosuitFlightRequest exosuitFlightRequest = FlightState?.Requests.FirstOrDefault((ExosuitFlightRequest r) => r.Flyer == __instance);
		Map map = FlightGet(__instance, "destMap") as Map;
		if (pawn == null || pawn.Destroyed || map == null || map.Disposed || !Find.Maps.Contains(map))
		{
			if (exosuitFlightRequest != null)
			{
				FlightState.Requests.Remove(exosuitFlightRequest);
			}
			return false;
		}
		if (pawn.Spawned)
		{
			pawn.DeSpawn();
		}
		GenSpawn.Spawn(pawn, new IntVec3(1, 0, 1).ClampInsideMap(map), map);
		CaravanMaker.MakeCaravan(new Pawn[1] { pawn }, pawn.Faction, map.Tile, addToWorldPawnsIfNotAlready: false);
		pawn.ExitMap(allowedToJoinOrCreateCaravan: false, Rot4.North);
		if (exosuitFlightRequest != null)
		{
			exosuitFlightRequest.Flyer = null;
			exosuitFlightRequest.Pawn = pawn;
			exosuitFlightRequest.Source = null;
			exosuitFlightRequest.Phase = 1;
		}
		return false;
	}

	private static void FlightScribePawn(ref Pawn pawn, string label, object[] args)
	{
		if (MP.IsInMultiplayer)
		{
			Scribe_References.Look(ref pawn, label);
		}
		else
		{
			Scribe_Deep.Look(ref pawn, label, args);
		}
	}

	private static IEnumerable<CodeInstruction> FlightPawnReferenceIl(IEnumerable<CodeInstruction> instructions)
	{
		int count = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.operand is MethodInfo methodInfo && methodInfo.DeclaringType == typeof(Scribe_Deep) && methodInfo.Name == "Look" && methodInfo.IsGenericMethod && methodInfo.GetGenericArguments()[0] == typeof(Pawn))
			{
				count++;
				yield return new CodeInstruction(instruction)
				{
					opcode = OpCodes.Call,
					operand = AccessTools.DeclaredMethod(typeof(ExosuitFramework), "FlightScribePawn")
				};
			}
			else
			{
				yield return instruction;
			}
		}
		if (count != 1)
		{
			throw new InvalidOperationException($"Exosuit stored pawn Scribe changed: {count}");
		}
	}

	private static bool FlightLegacyChoice(object __instance, int destinationTile, MethodBase __originalMethod)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		object obj = FlightGet(__instance, "pawnStored");
		Pawn pawn = obj as Pawn;
		if (pawn != null)
		{
			ExosuitFlightRequest exosuitFlightRequest = FlightState?.Requests.FirstOrDefault((ExosuitFlightRequest r) => r.Pawn == pawn);
			if (exosuitFlightRequest != null)
			{
				SyncFlightDestination(exosuitFlightRequest.Id, SessionServices.GetOwner(exosuitFlightRequest.Ownership), SessionServices.GetRevision(exosuitFlightRequest.Ownership), new PlanetTile(destinationTile), __originalMethod.Name == "TryLaunchToSiteOrSettleMent");
			}
		}
		return false;
	}

	private static bool FlightLive(ExosuitFlightRequest request)
	{
		if (request?.Pawn != null && !request.Pawn.Destroyed && !request.Pawn.Dead)
		{
			Faction faction = request.Pawn.Faction;
			if (faction != null && faction.IsPlayer)
			{
				Caravan caravan = request.Pawn.GetCaravan();
				if (caravan != null)
				{
					return !caravan.Destroyed;
				}
				return false;
			}
		}
		return false;
	}

	private static bool FlightAuthority(ExosuitFlightRequest request, int owner, int revision)
	{
		if (FlightLive(request) && CommandAuthorityBridge.CanActForPawn(request.Pawn))
		{
			return SessionServices.CanChoose(request.Ownership, owner, revision);
		}
		return false;
	}

	private static ExosuitFlightRequest FlightFind(int id)
	{
		return FlightState?.Requests.FirstOrDefault((ExosuitFlightRequest r) => r.Id == id);
	}

	private static void SyncFlightClaim(int id, int owner, int revision)
	{
		ExosuitFlightRequest exosuitFlightRequest = FlightFind(id);
		bool flag = FlightLive(exosuitFlightRequest);
		if (flag)
		{
			int phase = exosuitFlightRequest.Phase;
			bool flag2 = ((phase == 1 || phase == 3) ? true : false);
			flag = flag2;
		}
		if (flag && CommandAuthorityBridge.CanActForPawn(exosuitFlightRequest.Pawn))
		{
			SessionServices.TryClaim(exosuitFlightRequest.Ownership, owner, revision);
		}
	}

	private static void SyncFlightCancel(int id, int owner, int revision)
	{
		ExosuitFlightRequest exosuitFlightRequest = FlightFind(id);
		bool flag = FlightAuthority(exosuitFlightRequest, owner, revision);
		if (flag)
		{
			int phase = exosuitFlightRequest.Phase;
			bool flag2 = ((phase == 1 || (uint)(phase - 3) <= 1u) ? true : false);
			flag = flag2;
		}
		if (flag)
		{
			FlightState.Requests.Remove(exosuitFlightRequest);
		}
	}

	private static void SyncFlightDestination(int id, int owner, int revision, PlanetTile tile, bool enterMap)
	{
		ExosuitFlightRequest request = FlightFind(id);
		if (!FlightAuthority(request, owner, revision) || request.Phase != 1 || !tile.Valid || Find.World.Impassable(tile) || request.Pawn.GetCaravan().Tile != request.Origin || (request.Range > 0 && Find.WorldGrid.TraversalDistanceBetween(request.Origin, tile) > request.Range))
		{
			return;
		}
		if (!enterMap)
		{
			request.Pawn.GetCaravan().Tile = tile;
			FlightState.Requests.Remove(request);
			return;
		}
		MapParent target = Find.WorldObjects.AllWorldObjects.OfType<MapParent>().FirstOrDefault((MapParent p) =>
		{
			bool flag = p.Tile == tile;
			if (flag)
			{
				bool flag2 = ((p is Site || p is Settlement) ? true : false);
				flag = flag2;
			}
			return flag;
		});
		if (target == null || target.Destroyed)
		{
			return;
		}
		request.Destination = target;
		request.Phase = 2;
		request.Generated = !target.HasMap;
		if (target.HasMap)
		{
			request.TargetMap = target.Map;
			ExosuitFlightRequest exosuitFlightRequest = request;
			Map targetMap = request.TargetMap;
			exosuitFlightRequest.Phase = ((targetMap == null || targetMap.Disposed) ? 1 : 3);
			return;
		}
		Game game = Current.Game;
		LongEventHandler.QueueLongEvent(() =>
		{
			if (Current.Game != game || FlightFind(id) != request || !FlightLive(request) || request.Destination.Destroyed)
			{
				return;
			}
			try
			{
				using (CommandAuthorityBridge.EnterFaction(request.Pawn.Faction))
				{
					request.TargetMap = GetOrGenerateMapUtility.GetOrGenerateMap(tile, target.def);
					request.Phase = ((request.TargetMap == null) ? 1 : 3);
				}
			}
			catch
			{
				request.Phase = 1;
				throw;
			}
		}, "GeneratingMap".Translate(), doAsynchronously: true, null);
	}

	private static void SyncFlightLanding(int id, int owner, int revision, IntVec3 cell)
	{
		ExosuitFlightRequest exosuitFlightRequest = FlightFind(id);
		if (FlightAuthority(exosuitFlightRequest, owner, revision) && exosuitFlightRequest.Phase == 3 && exosuitFlightRequest.TargetMap != null && !exosuitFlightRequest.TargetMap.Disposed && Find.Maps.Contains(exosuitFlightRequest.TargetMap) && FlightCellValid(exosuitFlightRequest.TargetMap, cell))
		{
			exosuitFlightRequest.Cell = cell;
			exosuitFlightRequest.Phase = 4;
		}
	}

	private static bool FlightCellValid(Map map, IntVec3 cell)
	{
		if (cell.InBounds(map) && cell.Standable(map) && !cell.Roofed(map) && ThingDefOf.MeteoriteIncoming != null)
		{
			return Enumerable.Range(0, 4).All((int rotation) => GenAdj.OccupiedRect(cell, new Rot4(rotation), ThingDefOf.MeteoriteIncoming.size).Cells.All((IntVec3 c) => c.InBounds(map)));
		}
		return false;
	}

	private static void FlightMapTick(Map __instance)
	{
		if (!MP.IsInMultiplayer)
		{
			return;
		}
		Func<bool> func = flightWarming;
		if ((func != null && func()) || FlightState == null || AccessTools.Field(ModernType("Multiplayer.Client.AsyncTimeComp"), "tickingMap").GetValue(null) != __instance)
		{
			return;
		}
		ExosuitFlightRequest[] array = (from r in FlightState.Requests
			where r.Phase == 4 && r.TargetMap == __instance
			orderby r.Id
			select r).ToArray();
		foreach (ExosuitFlightRequest exosuitFlightRequest in array)
		{
			if (FlightLive(exosuitFlightRequest))
			{
				MapParent destination = exosuitFlightRequest.Destination;
				if (destination != null && !destination.Destroyed && FlightCellValid(__instance, exosuitFlightRequest.Cell))
				{
					using (CommandAuthorityBridge.EnterFaction(exosuitFlightRequest.Pawn.Faction))
					{
						Caravan caravan = exosuitFlightRequest.Pawn.GetCaravan();
						TaggedString letterText = "LetterTransportPodsLandedInEnemyBase".Translate(exosuitFlightRequest.Destination.Label).CapitalizeFirst();
						SettlementUtility.AffectRelationsOnAttacked(exosuitFlightRequest.Destination, ref letterText);
						if (exosuitFlightRequest.Generated)
						{
							GenStep_Fog.UnfogMapFromEdge(__instance);
							Find.TickManager.Notify_GeneratedPotentiallyHostileMap();
						}
						exosuitFlightRequest.Phase = 5;
						caravan.Tile = __instance.Tile;
						CaravanEnterMapUtility.Enter(caravan, __instance, CaravanEnterMode.Edge);
						if (!exosuitFlightRequest.Pawn.Spawned || exosuitFlightRequest.Pawn.Map != __instance)
						{
							break;
						}
						exosuitFlightRequest.Pawn.DeSpawn();
						SkyfallerMaker.SpawnSkyfaller(ThingDefOf.MeteoriteIncoming, exosuitFlightRequest.Pawn, exosuitFlightRequest.Cell, __instance);
						FlightState.Requests.Remove(exosuitFlightRequest);
					}
					continue;
				}
			}
			exosuitFlightRequest.Phase = 3;
		}
	}

	private static IEnumerable<Gizmo> FlightCaravanGizmos(IEnumerable<Gizmo> __result, Caravan __instance)
	{
		foreach (Gizmo item in __result)
		{
			yield return item;
		}
		if (!MP.IsInMultiplayer || __instance.Faction != modernViewer() || FlightState == null)
		{
			yield break;
		}
		ExosuitFlightRequest[] array = FlightState.Requests.Where((ExosuitFlightRequest r) =>
		{
			int phase2 = r.Phase;
			return ((phase2 == 1 || (uint)(phase2 - 3) <= 1u) ? true : false) && r.Pawn.GetCaravan() == __instance;
		}).ToArray();
		foreach (ExosuitFlightRequest request in array)
		{
			int owner = SessionServices.GetOwner(request.Ownership);
			int revision = SessionServices.GetRevision(request.Ownership);
			if (owner != SessionServices.LocalPlayer())
			{
				yield return new Command_Action
				{
					defaultLabel = "MpDmsClaimLanding".Translate(),
					defaultDesc = "MpDmsClaimLandingDesc".Translate(),
					action = () =>
					{
						SyncFlightClaim(request.Id, owner, revision);
					}
				};
				continue;
			}
			int phase = request.Phase;
			if ((phase == 1 || phase == 3) ? true : false)
			{
				yield return new Command_Action
				{
					defaultLabel = "MpExosuitFlightResume".Translate(),
					action = () =>
					{
						FlightPresent(request, resume: true);
					}
				};
			}
			yield return new Command_Action
			{
				defaultLabel = "Cancel".Translate(),
				action = () =>
				{
					SyncFlightCancel(request.Id, owner, revision);
				}
			};
		}
	}

	internal static void UpdateModernFlightUi()
	{
		if (ModernFlightInstalled && MP.IsInMultiplayer && MP.InInterface && FlightState != null)
		{
			ExosuitFlightRequest[] array = (from r in FlightState.Requests.Where((ExosuitFlightRequest r) =>
				{
					int phase = r.Phase;
					return (phase == 1 || phase == 3) ? true : false;
				})
				orderby r.Id
				select r).ToArray();
			for (int num = 0; num < array.Length; num++)
			{
				FlightPresent(array[num], resume: false);
			}
		}
	}

	internal static void TickModernFlight()
	{
		if (!ModernFlightInstalled || !MP.IsInMultiplayer)
		{
			return;
		}
		Func<bool> func = flightWarming;
		if ((func != null && func()) || FlightState == null)
		{
			return;
		}
		ExosuitFlightRequest[] array = FlightState.Requests.ToArray();
		foreach (ExosuitFlightRequest exosuitFlightRequest in array)
		{
			if (exosuitFlightRequest.Phase == 0)
			{
				if (exosuitFlightRequest.Flyer == null || exosuitFlightRequest.Flyer.Destroyed)
				{
					FlightState.Requests.Remove(exosuitFlightRequest);
				}
				continue;
			}
			if (!FlightLive(exosuitFlightRequest))
			{
				FlightState.Requests.Remove(exosuitFlightRequest);
				continue;
			}
			int phase = exosuitFlightRequest.Phase;
			if ((uint)(phase - 3) > 1u)
			{
				continue;
			}
			if (exosuitFlightRequest.TargetMap != null && !exosuitFlightRequest.TargetMap.Disposed && Find.Maps.Contains(exosuitFlightRequest.TargetMap))
			{
				MapParent destination = exosuitFlightRequest.Destination;
				if (destination != null && !destination.Destroyed)
				{
					continue;
				}
			}
			exosuitFlightRequest.TargetMap = null;
			exosuitFlightRequest.Destination = null;
			exosuitFlightRequest.Phase = 1;
			SessionServices.SetRevision(exosuitFlightRequest.Ownership, SessionServices.GetRevision(exosuitFlightRequest.Ownership) + 1);
		}
	}

	private static void FlightPresent(ExosuitFlightRequest request, bool resume)
	{
		if (!FlightLive(request) || request.Pawn.Faction != modernViewer() || SessionServices.GetOwner(request.Ownership) != SessionServices.LocalPlayer())
		{
			return;
		}
		int phase = request.Phase;
		int revision = SessionServices.GetRevision(request.Ownership);
		int owner = SessionServices.GetOwner(request.Ownership);
		string item = request.Id + ":" + phase + ":" + revision;
		if (!resume && !flightPresented.GetValue(Current.Game, (Game _) => new HashSet<string>()).Add(item))
		{
			return;
		}
		Game game = Current.Game;
		object session = modernSession.GetValue(null);
		Faction viewer = modernViewer();
		if (phase == 1)
		{
			CameraJumper.TryJump(new GlobalTargetInfo(request.Origin));
			Find.WorldTargeter.BeginTargeting((GlobalTargetInfo target) =>
			{
				if (!Valid() || !target.IsValid || Find.World.Impassable(target.Tile))
				{
					return false;
				}
				if (request.Range > 0 && Find.WorldGrid.TraversalDistanceBetween(request.Origin, target.Tile) > request.Range)
				{
					Messages.Message("TransportPodDestinationBeyondMaximumRange".Translate(), MessageTypeDefOf.RejectInput, historical: false);
					return false;
				}
				if (Find.WorldObjects.AllWorldObjects.OfType<MapParent>().FirstOrDefault((MapParent p) =>
				{
					bool flag = p.Tile == target.Tile;
					if (flag)
					{
						bool flag2 = ((p is Site || p is Settlement) ? true : false);
						flag = flag2;
					}
					return flag;
				}) == null)
				{
					SyncFlightDestination(request.Id, owner, revision, target.Tile, enterMap: false);
				}
				else
				{
					Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
					{
						new FloatMenuOption("WG_EjectToTile".Translate(), () =>
						{
							if (Valid())
							{
								SyncFlightDestination(request.Id, owner, revision, target.Tile, enterMap: false);
							}
						}),
						new FloatMenuOption("WG_EjectToBase".Translate(), () =>
						{
							if (Valid())
							{
								SyncFlightDestination(request.Id, owner, revision, target.Tile, enterMap: true);
							}
						})
					}));
				}
				return true;
			}, canTargetTiles: true, AccessTools.Field(flightFlyer, "TargeterMouseAttachment").GetValue(null) as Texture2D, closeWorldTabWhenFinished: false, () =>
			{
				if (Valid() && request.Range > 0)
				{
					GenDraw.DrawWorldRadiusRing(request.Origin, request.Range);
				}
			});
		}
		else
		{
			if (phase != 3 || request.TargetMap == null || request.TargetMap.Disposed || !Find.Maps.Contains(request.TargetMap))
			{
				return;
			}
			CameraJumper.TryJump(new GlobalTargetInfo(request.TargetMap.Center, request.TargetMap));
			Find.Targeter.BeginTargeting(TargetingParameters.ForCell(), (LocalTargetInfo target) =>
			{
				if (Valid())
				{
					SyncFlightLanding(request.Id, owner, revision, target.Cell);
				}
			});
		}
		bool Valid()
		{
			if (Current.Game == game && modernSession.GetValue(null) == session && modernViewer() == viewer && FlightFind(request.Id) == request && request.Phase == phase && SessionServices.Matches(request.Ownership, owner, revision) && SessionServices.LocalPlayer() == owner)
			{
				return FlightLive(request);
			}
			return false;
		}
	}

	private static Type ModernType(string name)
	{
		return AccessTools.TypeByName(name) ?? throw new TypeLoadException(name);
	}

	private static MethodInfo ModernMethod(Type type, string name)
	{
		return AccessTools.DeclaredMethod(type, name) ?? throw new MissingMethodException(type.FullName, name);
	}

	private static void ModernPatch(MethodBase method, string prefix = null, string postfix = null, string finalizer = null, string transpiler = null, int priority = 400)
	{
		MpCompat.harmony.Patch(method, (prefix == null) ? null : new HarmonyMethod(typeof(ExosuitFramework), prefix)
		{
			priority = priority
		}, (postfix == null) ? null : new HarmonyMethod(typeof(ExosuitFramework), postfix), finalizer: (finalizer == null) ? null : new HarmonyMethod(typeof(ExosuitFramework), finalizer), transpiler: (transpiler == null) ? null : new HarmonyMethod(typeof(ExosuitFramework), transpiler));
	}

	private static void InstallModern()
	{
		CommandAuthorityBridge.Install();
		PatchingUtilities.SetupAsyncTime();
		modernBay = ModernType("Exosuit.Building_MaintenanceBay");
		modernTurret = ModernType("Mechsuit.CompTurretGun");
		Type type = ModernType("Multiplayer.Client.Multiplayer");
		modernSession = AccessTools.Field(type, "session");
		modernViewer = (Func<Faction>)AccessTools.PropertyGetter(type, "RealPlayerFaction").CreateDelegate(typeof(Func<Faction>));
		modernTicking = (Func<bool>)AccessTools.PropertyGetter(type, "Ticking").CreateDelegate(typeof(Func<bool>));
		modernSimEvent = (Func<bool>)AccessTools.PropertyGetter(ModernType("Multiplayer.Client.Patches.SimLongEventContext"), "InSimulation").CreateDelegate(typeof(Func<bool>));
		modernMapOwner = ModernMethod(ModernType("Multiplayer.Client.Extensions"), "AsyncTime");
		Type type2 = ModernType("Multiplayer.Client.AsyncTimeComp");
		modernPre = ModernMethod(type2, "PreContext");
		modernPost = ModernMethod(type2, "PostContext");
		dummyPawn = AccessTools.Field(modernBay, "cachePawn");
		MethodInfo[] array = (from m in modernBay.Assembly.GetTypes().SelectMany(AccessTools.GetDeclaredMethods)
			where m.IsDefined(typeof(SyncMethodAttribute), inherit: false)
			select m).ToArray();
		foreach (MethodInfo methodInfo in array)
		{
			if (!methodInfo.IsStatic && methodInfo.DeclaringType.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
			{
				AddClosureModel(methodInfo.DeclaringType);
			}
			ModernPatch(methodInfo, "ModernCommandAllowed", null, null, null, 900);
			if (!methodInfo.Name.Contains("SyncGetIn", StringComparison.Ordinal))
			{
				continue;
			}
			ParameterInfo[] parameters = methodInfo.GetParameters();
			if (parameters != null && parameters.Length == 1 && parameters[0].ParameterType.IsByRef)
			{
				Type elementType = parameters[0].ParameterType.GetElementType();
				AddClosureModel(elementType);
				bool key = closureFields[elementType].Any((FieldInfo f) => f.FieldType == typeof(LocalTargetInfo));
				refGetInMethods.Add(key, (methodInfo, elementType));
				ModernPatch(methodInfo, "ModernRefGetIn", null, null, null, 1000);
			}
		}
		Type[] array2 = closureFields.Keys.Where((Type t) => !t.IsValueType).ToArray();
		foreach (Type targetType in array2)
		{
			MP.RegisterSyncWorker<object>(SyncModernClosure, targetType, isImplicit: false, shouldConstruct: true);
		}
		if (refGetInMethods.Count != 2)
		{
			throw new InvalidOperationException("Exosuit GetIn capture shapes changed");
		}
		MP.RegisterSyncMethod(typeof(ExosuitFramework), "SyncModernGetIn").SetVersion(30);
		ModernPatch(AccessTools.DeclaredMethod(typeof(ExosuitFramework), "SyncModernGetIn"), "ModernGetInAllowed", null, null, null, 900);
		InstallModernSerialization();
		string[] array3 = new string[6] { "RequestInstallModule", "RequestRemoveModule", "CancelPendingWork", "CancelPendingInstall", "CancelAllCoreWork", "CancelPendingRemove" };
		foreach (string name in array3)
		{
			MethodInfo method = ModernMethod(modernBay, name);
			MP.RegisterSyncMethod(method).SetVersion(30);
			ModernPatch(method, "ModernCommandAllowed", null, null, null, 900);
		}
		ModernPatch(ModernMethod(ModernType("Exosuit.CompAssignableToPawn_Parking"), "TryAssignPawn"), "ModernCommandAllowed", null, null, null, 900);
		ModernPatch(AccessTools.Method(typeof(CompAssignableToPawn), "TryUnassignPawn"), "ModernParkingUnassign", null, null, null, 900);
		MP.RegisterSyncMethod(ModernMethod(ModernType("Exosuit.CompFuelCell"), "UnloadFuelToGround")).SetVersion(30);
		ModernPatch(ModernMethod(ModernType("Exosuit.CompFuelCell"), "UnloadFuelToGround"), "ModernCommandAllowed", null, null, null, 900);
		array3 = new string[2] { "OrderAttack", "ClearForcedTarget" };
		foreach (string name2 in array3)
		{
			MethodInfo method2 = ModernMethod(modernTurret, name2);
			MP.RegisterSyncMethod(method2).SetVersion(30);
			ModernPatch(method2, "ModernCommandAllowed", null, null, null, 900);
		}
		FieldInfo fireAtWill = AccessTools.Field(modernTurret, "fireAtWill");
		MethodInfo[] source = (from m in AccessTools.GetDeclaredMethods(modernTurret)
			where m.Name.StartsWith("<GetGizmos>", StringComparison.Ordinal) && m.ReturnType == typeof(void) && m.GetParameters().Length == 0
			select m).ToArray();
		MethodInfo[] array4 = new MethodInfo[3]
		{
			source.Single((MethodInfo m) => PatchProcessor.GetOriginalInstructions(m).Any((CodeInstruction i) => i.StoresField(fireAtWill))),
			source.Single((MethodInfo m) => PatchProcessor.GetOriginalInstructions(m).Any((CodeInstruction i) => i.Calls(ModernMethod(modernTurret, "ClearForcedTarget")))),
			source.Single((MethodInfo m) => PatchProcessor.GetOriginalInstructions(m).Any((CodeInstruction i) => i.operand is MethodInfo methodInfo2 && methodInfo2.Name == "TryStartAsyncCast"))
		};
		for (int num2 = 0; num2 < array4.Length; num2++)
		{
			ISyncMethod syncMethod = MP.RegisterSyncMethod(array4[num2]).SetVersion(30);
			if (num2 == 2)
			{
				syncMethod.SetDebugOnly();
			}
			ModernPatch(array4[num2], "ModernCommandAllowed", null, null, null, 900);
		}
		MP.RegisterSyncWorker<Verb>(SyncModernVerb, ModernType("Mechsuit.AsyncShootVerb"), isImplicit: true);
		turretLastAttack = AccessTools.Field(modernTurret, "lastAttackTargetTick");
		turretLastSearch = AccessTools.Field(modernTurret, "lastFailedSearchTick");
		PatchingUtilities.RegisterTimestampFields(modernTurret, (object value, int offset) =>
		{
			turretLastAttack.SetValue(value, (int)turretLastAttack.GetValue(value) + offset);
			turretLastSearch.SetValue(value, (int)turretLastSearch.GetValue(value) + offset);
		});
		ModernPatch(ModernMethod(modernTurret, "PostExposeData"), null, "ModernTurretExpose");
		ModernPatch(ModernMethod(modernBay, "SpawnSetup"), null, "ModernBaySpawned");
		ModernPatch(AccessTools.PropertyGetter(modernBay, "Dummy"), "ModernOwnerBefore", "ModernDummyRead", "ModernOwnerAfter", "ModernDummyComponentsTranspiler");
		ModernPatch(ModernMethod(modernBay, "TryUpdateCache"), null, "ModernCacheUpdated");
		(string, string)[] array5 = new (string, string)[2]
		{
			("Exosuit.CompLaunchExhaust", "LaunchExhaust"),
			("Exosuit.CompProjectileFleckEmitter", "Emit")
		};
		for (int num = 0; num < array5.Length; num++)
		{
			(string, string) tuple = array5[num];
			ModernPatch(ModernMethod(ModernType(tuple.Item1), tuple.Item2), "ModernVisualBefore", null, "ModernVisualAfter");
		}
		MethodInfo finish = AccessTools.Method(typeof(LongEventHandler), "ExecuteWhenFinished");
		foreach (MethodInfo item in from m in AccessTools.GetDeclaredMethods(ModernType("Exosuit.Exosuit_Core"))
			where m.GetMethodBody() != null && PatchProcessor.GetOriginalInstructions(m).Any((CodeInstruction i) => i.Calls(finish))
			select m)
		{
			ModernPatch(item, null, null, null, "ModernDamageTranspiler");
		}
		array = new MethodInfo[3]
		{
			ModernMethod(ModernType("Exosuit.CompSuitModule_Ejectable"), "CheckDepletion"),
			ModernMethod(modernBay, "SetCacheDirty"),
			ModernMethod(modernBay, "ExposeData")
		};
		for (int num = 0; num < array.Length; num++)
		{
			ModernPatch(array[num], null, null, null, "ModernDamageTranspiler");
		}
		InstallModernUi();
		InstallModernWeaponCommands();
		InstallModernFlight();
	}

	private static void InstallModernFlight()
	{
		SessionServices.Install();
		flightFlyer = ModernType("Exosuit.WG_PawnFlyer");
		flightBay = ModernType("Exosuit.Building_EjectorBay");
		flightRangeStat = AccessTools.Field(ModernType("Exosuit.StatDefof"), "MF_FlightRange") ?? throw new MissingFieldException("Exosuit.StatDefof", "MF_FlightRange");
		flightWarming = (Func<bool>)AccessTools.PropertyGetter(ModernType("Multiplayer.Client.LoadPatch"), "IsInitializingCaches").CreateDelegate(typeof(Func<bool>));
		flightJump = AccessTools.DeclaredMethod(ModernType("Exosuit.WG_AbilityVerb_QuickJump"), "DoJump", new Type[6]
		{
			typeof(Pawn),
			typeof(Map),
			typeof(LocalTargetInfo),
			typeof(LocalTargetInfo),
			typeof(bool),
			typeof(bool)
		});
		MethodInfo[] array = (from m in AccessTools.GetDeclaredMethods(flightBay)
			where m.Name.StartsWith("<GetGizmos>", StringComparison.Ordinal) && m.ReturnType == typeof(void) && (m.GetParameters().Length == 0 || (from p in m.GetParameters()
				select p.ParameterType).SequenceEqual(new Type[1] { typeof(LocalTargetInfo) }))
			select m).ToArray();
		if (array.Length != 3)
		{
			throw new InvalidOperationException($"Exosuit ejector mutations changed: {array.Length}");
		}
		MethodInfo[] array2 = array;
		foreach (MethodInfo method in array2)
		{
			MP.RegisterSyncMethod(method).SetVersion(30);
			ModernPatch(method, "FlightBayBefore", null, "FlightBayAfter", "FlightBayMapIl", 900);
		}
		ModernPatch(ModernMethod(flightFlyer, "MakeFlyer"), null, "FlightMade");
		ModernPatch(ModernMethod(flightFlyer, "ToDiffMapTarget"), "FlightWorldTransition");
		ModernPatch(ModernMethod(flightFlyer, "ExposeData"), null, null, null, "FlightPawnReferenceIl");
		string[] array3 = new string[2] { "TryLaunch", "TryLaunchToSiteOrSettleMent" };
		foreach (string name in array3)
		{
			ModernPatch(ModernMethod(flightFlyer, name), "FlightLegacyChoice");
		}
		ModernPatch(AccessTools.DeclaredMethod(typeof(Caravan), "GetGizmos"), null, "FlightCaravanGizmos");
		ModernPatch(AccessTools.DeclaredMethod(typeof(Map), "MapPostTick"), null, "FlightMapTick");
		MP.RegisterSyncMethod(typeof(ExosuitFramework), "SyncFlightDestination").SetVersion(30);
		MP.RegisterSyncMethod(typeof(ExosuitFramework), "SyncFlightLanding").SetVersion(30);
		MP.RegisterSyncMethod(typeof(ExosuitFramework), "SyncFlightClaim").SetVersion(30);
		MP.RegisterSyncMethod(typeof(ExosuitFramework), "SyncFlightCancel").SetVersion(30);
		SessionServices.RegisterLifecycle("aoba.exosuit.flight", (int player) =>
		{
			if (FlightState != null)
			{
				foreach (ExosuitFlightRequest request in FlightState.Requests)
				{
					SessionServices.Retire(request.Ownership, player);
				}
			}
		}, () =>
		{
			flightPresented.Clear();
		});
		ModernFlightInstalled = true;
	}

	private static bool ModernGetInAllowed(Thing bay, Pawn actor)
	{
		if (ModernCanAct(bay) && actor != null && CommandAuthorityBridge.CanActForPawn(actor) && actor.Spawned && actor.Map == bay.Map && !actor.DeadOrDowned)
		{
			return (AcceptanceReport)ModernMethod(modernBay, "CanAcceptPawn").Invoke(bay, new object[1] { actor });
		}
		return false;
	}

	private static bool ModernRefGetIn(Thing __instance, object[] __args, MethodBase __originalMethod)
	{
		if (!MP.IsInMultiplayer || !MP.InInterface)
		{
			return true;
		}
		Pawn actor = __args.SelectMany(ModernCapturedThings).OfType<Pawn>().FirstOrDefault();
		if (ModernGetInAllowed(__instance, actor))
		{
			SyncModernGetIn(__instance, actor, refGetInMethods[true].method == __originalMethod);
		}
		return false;
	}

	private static void SyncModernGetIn(Thing bay, Pawn actor, bool targeted)
	{
		if (!ModernGetInAllowed(bay, actor))
		{
			return;
		}
		(MethodInfo, Type) tuple = refGetInMethods[targeted];
		object obj = Activator.CreateInstance(tuple.Item2);
		FieldInfo[] array = closureFields[tuple.Item2];
		foreach (FieldInfo fieldInfo in array)
		{
			if (fieldInfo.FieldType == typeof(LocalTargetInfo))
			{
				fieldInfo.SetValue(obj, new LocalTargetInfo(actor));
				continue;
			}
			if (typeof(IEnumerable<Pawn>).IsAssignableFrom(fieldInfo.FieldType))
			{
				fieldInfo.SetValue(obj, new List<Pawn> { actor });
				continue;
			}
			throw new InvalidOperationException("Exosuit native GetIn capture changed: " + fieldInfo);
		}
		using (new ModernOwnerScope(bay))
		{
			tuple.Item1.Invoke(bay, new object[1] { obj });
		}
	}

	private static void AddClosureModel(Type type)
	{
		if (closureFields.ContainsKey(type))
		{
			return;
		}
		FieldInfo[] array = (from f in AccessTools.GetDeclaredFields(type)
			where !f.IsStatic
			select f).OrderBy((FieldInfo f) => f.Name, StringComparer.Ordinal).ToArray();
		closureFields.Add(type, array);
		FieldInfo[] array2 = array;
		foreach (FieldInfo fieldInfo in array2)
		{
			Type fieldType = fieldInfo.FieldType;
			if (!typeof(Thing).IsAssignableFrom(fieldType) && !(fieldType == typeof(LocalTargetInfo)) && !typeof(IEnumerable<Pawn>).IsAssignableFrom(fieldType))
			{
				if (!(fieldType.Assembly == modernBay.Assembly) || !fieldType.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
				{
					throw new InvalidOperationException("Unreviewed Exosuit command capture: " + fieldInfo);
				}
				AddClosureModel(fieldType);
			}
		}
	}

	private static void SyncModernClosure(SyncWorker sync, ref object value)
	{
		BindClosure(sync, value, value.GetType());
	}

	private static void BindClosure(SyncWorker sync, object value, Type type)
	{
		FieldInfo[] array = closureFields[type];
		foreach (FieldInfo fieldInfo in array)
		{
			Type fieldType = fieldInfo.FieldType;
			if (typeof(Thing).IsAssignableFrom(fieldType))
			{
				Thing obj = (sync.isWriting ? (fieldInfo.GetValue(value) as Thing) : null);
				sync.Bind(ref obj);
				if (!sync.isWriting)
				{
					fieldInfo.SetValue(value, obj);
				}
			}
			else if (fieldType == typeof(LocalTargetInfo))
			{
				LocalTargetInfo obj2 = (sync.isWriting ? ((LocalTargetInfo)fieldInfo.GetValue(value)) : LocalTargetInfo.Invalid);
				sync.Bind(ref obj2);
				if (!sync.isWriting)
				{
					fieldInfo.SetValue(value, obj2);
				}
			}
			else if (typeof(IEnumerable<Pawn>).IsAssignableFrom(fieldType))
			{
				List<Pawn> obj3 = ((!sync.isWriting) ? null : ((IEnumerable<Pawn>)fieldInfo.GetValue(value))?.ToList());
				sync.Bind(ref obj3);
				if (!sync.isWriting)
				{
					fieldInfo.SetValue(value, obj3);
				}
			}
			else
			{
				object value2 = (sync.isWriting ? fieldInfo.GetValue(value) : Activator.CreateInstance(fieldType, nonPublic: true));
				BindClosure(sync, value2, fieldType);
				if (!sync.isWriting)
				{
					fieldInfo.SetValue(value, value2);
				}
			}
		}
	}

	private static void SyncModernVerb(SyncWorker sync, ref Verb verb)
	{
		ThingComp comp = (sync.isWriting ? (AccessTools.Field(verb.GetType(), "turretComp").GetValue(verb) as ThingComp) : null);
		SyncModernComp(sync, ref comp);
		if (!sync.isWriting)
		{
			verb = ((comp != null && modernTurret.IsInstanceOfType(comp)) ? (AccessTools.Property(modernTurret, "AttackVerb").GetValue(comp) as Verb) : null);
		}
	}

	private static IEnumerable<Thing> ModernCapturedThings(object value)
	{
		if (value is Thing thing)
		{
			yield return thing;
		}
		else if (value is ThingComp thingComp)
		{
			yield return thingComp.parent;
		}
		else
		{
			if (value == null || !closureFields.TryGetValue(value.GetType(), out var value2))
			{
				yield break;
			}
			FieldInfo[] array = value2;
			for (int i = 0; i < array.Length; i++)
			{
				object value3 = array[i].GetValue(value);
				if (value3 is LocalTargetInfo localTargetInfo)
				{
					if (localTargetInfo.Pawn != null)
					{
						yield return localTargetInfo.Pawn;
					}
					continue;
				}
				if (value3 is IEnumerable<Pawn> enumerable)
				{
					foreach (Pawn item in enumerable)
					{
						yield return item;
					}
					continue;
				}
				foreach (Thing item2 in ModernCapturedThings(value3))
				{
					yield return item2;
				}
			}
		}
	}

	private static Thing ModernRoot(Thing thing)
	{
		if (thing is Apparel { Wearer: { } wearer })
		{
			thing = wearer;
		}
		if (!(thing is Pawn key) || !dummyBays.TryGetValue(key, out var value))
		{
			return thing;
		}
		return value;
	}

	private static bool ModernCanAct(Thing thing)
	{
		thing = ModernRoot(thing);
		if (thing != null && !thing.Destroyed)
		{
			Map mapHeld = thing.MapHeld;
			if (mapHeld != null && !mapHeld.Disposed && Find.Maps.Contains(mapHeld))
			{
				return CommandAuthorityBridge.CanActForFaction(thing.Faction);
			}
		}
		return false;
	}

	private static bool ModernCommandAllowed(object __instance, object[] __args, MethodBase __originalMethod)
	{
		if (!MP.IsInMultiplayer || (!MP.InInterface && !MP.IsExecutingSyncCommand))
		{
			return true;
		}
		Thing[] source = ModernCapturedThings(__instance).Concat(__args.SelectMany(ModernCapturedThings)).ToArray();
		Thing thing = source.FirstOrDefault((Thing t) => t != null && modernBay.IsInstanceOfType(t)) ?? source.FirstOrDefault() ?? __args.OfType<Thing>().FirstOrDefault();
		if (!ModernCanAct(thing))
		{
			return false;
		}
		if (__originalMethod.Name.Contains("SyncGetIn", StringComparison.Ordinal))
		{
			Pawn[] array = source.OfType<Pawn>().ToArray();
			if (array.Length == 0)
			{
				return false;
			}
			Pawn[] array2 = array;
			foreach (Pawn pawn in array2)
			{
				if (!CommandAuthorityBridge.CanActForPawn(pawn) || pawn.Map != thing.Map || pawn.DeadOrDowned || !(AcceptanceReport)ModernMethod(modernBay, "CanAcceptPawn").Invoke(thing, new object[1] { pawn }))
				{
					return false;
				}
			}
		}
		if (__originalMethod.Name == "RequestInstallModule")
		{
			if (__args[0] is Thing { Destroyed: false, Spawned: not false } thing2 && thing2.Map == thing.Map)
			{
				Faction faction = thing2.Faction;
				if (faction == null || !faction.IsPlayer || CommandAuthorityBridge.CanActForFaction(thing2.Faction))
				{
					goto IL_0189;
				}
			}
			return false;
		}
		goto IL_0189;
		IL_0189:
		if (__originalMethod.Name == "TryAssignPawn" && __args.FirstOrDefault() is Pawn pawn2)
		{
			if (pawn2.Spawned && pawn2.Map == thing.Map)
			{
				return CommandAuthorityBridge.CanActForPawn(pawn2);
			}
			return false;
		}
		return true;
	}

	private static bool ModernParkingUnassign(CompAssignableToPawn __instance)
	{
		if (MP.IsInMultiplayer && !(__instance.GetType().FullName != "Exosuit.CompAssignableToPawn_Parking") && (MP.InInterface || MP.IsExecutingSyncCommand))
		{
			return ModernCanAct(__instance.parent);
		}
		return true;
	}

	private static void ModernOwnerBefore(Thing __instance, out IDisposable __state)
	{
		__state = (MP.IsInMultiplayer ? new ModernOwnerScope(__instance) : null);
	}

	private static Exception ModernOwnerAfter(Exception __exception, IDisposable __state)
	{
		__state?.Dispose();
		return __exception;
	}

	private static void ModernBaySpawned(Thing __instance)
	{
		if (MP.IsInMultiplayer)
		{
			AccessTools.Property(modernBay, "Dummy").GetValue(__instance);
			ModernMethod(modernBay, "TryUpdateCache").Invoke(__instance, new object[1] { true });
		}
	}

	private static void ModernDummyRead(Thing __instance, Pawn __result)
	{
		if (MP.IsInMultiplayer && __result != null)
		{
			dummyBays.Remove(__result);
			dummyBays.Add(__result, __instance);
		}
	}

	private static void ModernCreateDummyComponents(Pawn pawn)
	{
		if (pawn.kindDef == null)
		{
			pawn.kindDef = PawnKindDefOf.Colonist;
		}
		PawnComponentsUtility.CreateInitialComponents(pawn);
	}

	private static IEnumerable<CodeInstruction> ModernDummyComponentsTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo create = AccessTools.Method(typeof(PawnComponentsUtility), "CreateInitialComponents");
		int count = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(create))
			{
				count++;
				yield return new CodeInstruction(instruction)
				{
					opcode = OpCodes.Call,
					operand = AccessTools.Method(typeof(ExosuitFramework), "ModernCreateDummyComponents")
				};
			}
			else
			{
				yield return instruction;
			}
		}
		if (count != 1)
		{
			throw new InvalidOperationException("Exosuit dummy initialization changed: " + count);
		}
	}

	private static void ModernCacheUpdated(Thing __instance)
	{
		if (!MP.IsInMultiplayer || !(dummyPawn.GetValue(__instance) is Pawn pawn))
		{
			return;
		}
		foreach (Apparel item in pawn.apparel.WornApparel)
		{
			moduleBays.Remove(item);
			moduleBays.Add(item, __instance);
		}
	}

	private static void ModernTurretExpose(object __instance)
	{
		if (MP.IsInMultiplayer)
		{
			int value = (int)turretLastAttack.GetValue(__instance);
			int value2 = (int)turretLastSearch.GetValue(__instance);
			Scribe_Values.Look(ref value, "mpExosuitLastAttack", 0);
			Scribe_Values.Look(ref value2, "mpExosuitLastSearch", -9999);
			if (Scribe.mode == LoadSaveMode.LoadingVars)
			{
				turretLastAttack.SetValue(__instance, value);
				turretLastSearch.SetValue(__instance, value2);
			}
		}
	}

	private static void ModernVisualBefore(out bool __state)
	{
		__state = MP.IsInMultiplayer;
		if (__state)
		{
			Rand.PushState();
		}
	}

	private static Exception ModernVisualAfter(Exception __exception, bool __state)
	{
		if (__state)
		{
			Rand.PopState();
		}
		return __exception;
	}

	private static void ModernFinishDamage(Action action)
	{
		if (MP.IsInMultiplayer && Scribe.mode == LoadSaveMode.Inactive && (MP.IsExecutingSyncCommand || modernTicking() || modernSimEvent()))
		{
			action();
		}
		else
		{
			LongEventHandler.ExecuteWhenFinished(action);
		}
	}

	private static IEnumerable<CodeInstruction> ModernDamageTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo finish = AccessTools.Method(typeof(LongEventHandler), "ExecuteWhenFinished");
		int count = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(finish))
			{
				count++;
				yield return new CodeInstruction(instruction)
				{
					opcode = OpCodes.Call,
					operand = AccessTools.Method(typeof(ExosuitFramework), "ModernFinishDamage")
				};
			}
			else
			{
				yield return instruction;
			}
		}
		if (count != 1)
		{
			throw new InvalidOperationException("Exosuit damage callback changed: " + count);
		}
	}

	private static Thing ModernTabBay(object tab)
	{
		return AccessTools.Property(tab.GetType(), "SelThingPublic").GetValue(tab) as Thing;
	}

	private static void InstallModernUi()
	{
		Type tab = ModernType("Exosuit.ITab_Exosuit");
		ModernPatch(ModernMethod(tab, "GizmoFloatMenu"), null, "ModernMenuCreated");
		foreach (MethodInfo item in from m in (from t in tab.Assembly.GetTypes()
				where t == tab || t.FullName.StartsWith(tab.FullName + "+", StringComparison.Ordinal)
				select t).SelectMany(AccessTools.GetDeclaredMethods)
			where m.GetMethodBody() != null && PatchProcessor.GetOriginalInstructions(m).Any((CodeInstruction i) => i.Calls(AccessTools.Method(typeof(CompColorableUtility), "SetColor")))
			select m)
		{
			ModernPatch(item, null, null, null, "ModernColorTranspiler");
		}
		MP.RegisterSyncMethod(typeof(ExosuitFramework), "SyncModernModuleColor").SetVersion(30);
		ModernPatch(AccessTools.GetDeclaredConstructors(typeof(Dialog_ChooseColor)).Single((ConstructorInfo c) => c.GetParameters().Any((ParameterInfo p) => p.ParameterType == typeof(Action<Color>))), "ModernColorDialog");
		Type type = ModernType("Exosuit.Window_ExosuitCompanion");
		ModernPatch(AccessTools.GetDeclaredConstructors(type).Single(), null, "ModernCompanionCreated");
		ModernPatch(ModernMethod(type, "WindowUpdate"), "ModernCompanionAllowed");
		string[] array = new string[2] { "Exosuit.Window_ExosuitTopExtension", "Exosuit.Window_ExosuitSideExtension" };
		for (int num = 0; num < array.Length; num++)
		{
			ModernPatch(ModernMethod(ModernType(array[num]), "DoWindowContents"), "ModernCompanionAllowed");
		}
	}

	private static void ModernMenuCreated(object __instance, List<FloatMenuOption> __result)
	{
		if (!MP.IsInMultiplayer)
		{
			return;
		}
		ModernUiLifetime token = new ModernUiLifetime(__instance);
		foreach (FloatMenuOption item in __result.Where((FloatMenuOption o) => o.action != null))
		{
			Action action = item.action;
			item.action = () =>
			{
				if (!token.Valid)
				{
					return;
				}
				ModernUiLifetime modernUiLifetime = openingModernMenu;
				openingModernMenu = token;
				try
				{
					action();
				}
				finally
				{
					openingModernMenu = modernUiLifetime;
				}
			};
		}
	}

	private static void ModernColorDialog(object[] __args)
	{
		ModernUiLifetime token = openingModernMenu;
		if (!MP.IsInMultiplayer || token == null)
		{
			return;
		}
		for (int i = 0; i < __args.Length; i++)
		{
			object obj = __args[i];
			Action<Color> action = obj as Action<Color>;
			if (action == null)
			{
				continue;
			}
			__args[i] = (Action<Color>)((Color color) =>
			{
				if (token.Valid)
				{
					action(color);
				}
			});
		}
	}

	private static void ModernCompanionCreated(Window __instance, object[] __args)
	{
		if (MP.IsInMultiplayer)
		{
			companionWindows.Add(__instance, new ModernUiLifetime(__args[0]));
		}
	}

	private static bool ModernCompanionAllowed(Window __instance)
	{
		if (!MP.IsInMultiplayer || !companionWindows.TryGetValue(__instance, out var value) || value.Valid)
		{
			return true;
		}
		__instance.Close(doCloseSound: false);
		return false;
	}

	private static IEnumerable<CodeInstruction> ModernColorTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo old = AccessTools.Method(typeof(CompColorableUtility), "SetColor");
		int count = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(old))
			{
				count++;
				yield return new CodeInstruction(instruction)
				{
					opcode = OpCodes.Call,
					operand = AccessTools.Method(typeof(ExosuitFramework), "RequestModernModuleColor")
				};
			}
			else
			{
				yield return instruction;
			}
		}
		if (count != 1)
		{
			throw new InvalidOperationException("Exosuit color callback changed: " + count);
		}
	}

	private static void RequestModernModuleColor(Thing module, Color color, bool reportFailure)
	{
		Thing value;
		if (!MP.IsInMultiplayer)
		{
			module.SetColor(color, reportFailure);
		}
		else if (module != null && moduleBays.TryGetValue(module, out value))
		{
			SyncModernModuleColor(value, module.thingIDNumber, color);
		}
	}

	private static void SyncModernModuleColor(Thing bay, int moduleId, Color color)
	{
		if (!ModernCanAct(bay) || !(dummyPawn.GetValue(bay) is Pawn pawn))
		{
			return;
		}
		Apparel apparel = pawn.apparel.WornApparel.FirstOrDefault((Apparel m) => m.thingIDNumber == moduleId);
		if (apparel == null)
		{
			return;
		}
		using (new ModernOwnerScope(bay))
		{
			apparel.SetColor(color);
		}
	}

	private static void InstallModernSerialization()
	{
		modernFuelComp = ModernType("Exosuit.CompFuelCell");
		modernWeaponComp = ModernType("Exosuit.CompModuleWeapon");
		MP.RegisterSyncWorker<ThingComp>(SyncModernComp, modernTurret);
		MP.RegisterSyncWorker<ThingComp>(SyncModernComp, modernFuelComp);
		MP.RegisterSyncWorker<ThingComp>(SyncModernComp, modernWeaponComp);
	}

	private static byte ModernComponentKind(ThingComp comp)
	{
		if (comp?.GetType() == modernTurret)
		{
			return 0;
		}
		if (comp?.GetType() == modernFuelComp)
		{
			return 1;
		}
		if (comp?.GetType() == modernWeaponComp)
		{
			return 2;
		}
		return byte.MaxValue;
	}

	private static Thing ModernModuleBay(ThingWithComps module)
	{
		if (module is Apparel { Wearer: { } wearer } apparel && dummyBays.TryGetValue(wearer, out var value) && value != null && !value.Destroyed && modernBay.IsInstanceOfType(value) && dummyPawn.GetValue(value) == wearer)
		{
			Pawn_ApparelTracker apparel2 = wearer.apparel;
			if (apparel2 != null && apparel2.WornApparel.Contains(apparel))
			{
				return value;
			}
		}
		return null;
	}

	private static bool ModernOrdinaryParentReachable(ThingWithComps parent)
	{
		if (parent == null || parent.Destroyed)
		{
			return false;
		}
		if (parent.Spawned || ThingOwnerUtility.GetFirstSpawnedParentThing(parent) != null || (parent is Pawn p && p.IsWorldPawn()))
		{
			return true;
		}
		IThingHolder parentHolder = parent.ParentHolder;
		int num = 0;
		while (parentHolder != null && num < 64)
		{
			if ((parentHolder is WorldObject || parentHolder is WorldObjectComp) ? true : false)
			{
				return true;
			}
			num++;
			parentHolder = parentHolder.ParentHolder;
		}
		return false;
	}

	private static int ModernSameTypeIndex(ThingComp comp, byte kind)
	{
		if (comp?.parent?.AllComps == null)
		{
			return -1;
		}
		int num = 0;
		foreach (ThingComp allComp in comp.parent.AllComps)
		{
			if (ModernComponentKind(allComp) == kind)
			{
				if (allComp == comp)
				{
					return num;
				}
				num++;
			}
		}
		return -1;
	}

	private static void SyncModernComp(SyncWorker sync, ref ThingComp comp)
	{
		byte obj = 0;
		byte kind = byte.MaxValue;
		Thing obj2 = null;
		int moduleId = -1;
		int obj3 = -1;
		if (sync.isWriting && comp != null)
		{
			kind = ModernComponentKind(comp);
			obj3 = ModernSameTypeIndex(comp, kind);
			ThingWithComps parent = comp.parent;
			if (kind != byte.MaxValue && obj3 >= 0 && parent != null && !parent.Destroyed)
			{
				Thing thing = ModernModuleBay(parent);
				if (thing != null)
				{
					obj = 2;
					obj2 = thing;
					moduleId = parent.thingIDNumber;
				}
				else if (ModernOrdinaryParentReachable(parent))
				{
					obj = 1;
					obj2 = parent;
					moduleId = parent.thingIDNumber;
				}
			}
		}
		sync.Bind(ref obj);
		sync.Bind(ref kind);
		sync.Bind(ref obj2);
		sync.Bind(ref moduleId);
		sync.Bind(ref obj3);
		if (sync.isWriting)
		{
			return;
		}
		comp = null;
		if (obj3 < 0 || kind > 2 || obj2 == null || obj2.Destroyed)
		{
			return;
		}
		ThingWithComps thingWithComps = null;
		Thing value;
		if (obj == 1 && obj2 is ThingWithComps thingWithComps2 && thingWithComps2.thingIDNumber == moduleId)
		{
			thingWithComps = thingWithComps2;
		}
		else if (obj == 2 && modernBay.IsInstanceOfType(obj2) && dummyPawn.GetValue(obj2) is Pawn { apparel: not null } pawn && dummyBays.TryGetValue(pawn, out value) && value == obj2)
		{
			Apparel apparel = pawn.apparel.WornApparel.FirstOrDefault((Apparel m) => m.thingIDNumber == moduleId);
			if (apparel != null && apparel.Wearer == pawn)
			{
				thingWithComps = apparel;
			}
		}
		if (thingWithComps?.AllComps != null)
		{
			comp = thingWithComps.AllComps.Where((ThingComp c) => ModernComponentKind(c) == kind).Skip(obj3).FirstOrDefault();
		}
	}

	private static void InstallModernWeaponCommands()
	{
		InstallModernWeaponRuntime();
		MP.RegisterSyncMethod(typeof(ExosuitFramework), "SyncModernModuleWeapon").SetVersion(30);
	}

	private static void InstallModernWeaponRuntime()
	{
		modernWeaponType = ModernType("Exosuit.CompModuleWeapon");
		modernForcedWeaponType = ModernType("Exosuit.CompApparelForcedWeapon");
		MethodInfo[] source = (from m in modernWeaponType.GetNestedTypes(AccessTools.all).SelectMany(AccessTools.GetDeclaredMethods)
			where m.Name.StartsWith("<CompGetWornGizmosExtra>", StringComparison.Ordinal) && m.ReturnType == typeof(void) && m.GetParameters().Length == 0
			select m).ToArray();
		MethodInfo remove = AccessTools.DeclaredMethod(typeof(Pawn_EquipmentTracker), "Remove", new Type[1] { typeof(ThingWithComps) });
		MethodInfo equip = AccessTools.DeclaredMethod(typeof(Pawn_EquipmentTracker), "AddEquipment");
		modernWeaponBareHands = source.Single((MethodInfo m) => PatchProcessor.GetOriginalInstructions(m).Any((CodeInstruction i) => i.Calls(remove)));
		modernWeaponWield = source.Single((MethodInfo m) => PatchProcessor.GetOriginalInstructions(m).Any((CodeInstruction i) => i.Calls(equip)));
		if (modernWeaponBareHands.DeclaringType != modernWeaponWield.DeclaringType)
		{
			throw new InvalidOperationException("Exosuit module-weapon actions no longer share their reviewed capture");
		}
		FieldInfo[] fields = (from f in AccessTools.GetDeclaredFields(modernWeaponWield.DeclaringType)
			where !f.IsStatic
			select f).ToArray();
		modernWeaponCompCapture = fields.Single((FieldInfo f) => f.FieldType == modernWeaponType);
		modernWeaponPawnCapture = fields.Single((FieldInfo f) => f.FieldType == typeof(Pawn));
		MethodInfo[] array = new MethodInfo[2] { modernWeaponBareHands, modernWeaponWield };
		foreach (MethodInfo methodInfo in array)
		{
			FieldInfo[] allowed = new FieldInfo[2] { modernWeaponCompCapture, modernWeaponPawnCapture };
			if (PatchProcessor.GetOriginalInstructions(methodInfo).Any((CodeInstruction i) => i.operand is FieldInfo value && fields.Contains(value, null) && !allowed.Contains(value, null)))
			{
				throw new InvalidOperationException("Exosuit weapon action uses a new local UI capture: " + methodInfo);
			}
			ModernPatch(methodInfo, "ModernWeaponBefore", null, null, null, 900);
		}
	}

	private static bool ModernWeaponAllowed(ThingComp module, Pawn wearer)
	{
		if (module != null && modernWeaponType.IsInstanceOfType(module))
		{
			ThingWithComps parent = module.parent;
			if (parent is Apparel apparel && !parent.Destroyed && apparel.Wearer == wearer && apparel.AllComps.Contains(module) && wearer != null && !wearer.Dead)
			{
				Pawn_ApparelTracker apparel2 = wearer.apparel;
				if (apparel2 != null && apparel2.WornApparel.Contains(apparel) && wearer.equipment != null)
				{
					return wearer.inventory != null;
				}
			}
		}
		return false;
	}

	private static int ModernWeaponIndex(ThingComp module)
	{
		return module.parent.AllComps.Where((ThingComp c) => modernWeaponType.IsInstanceOfType(c)).ToList().IndexOf(module);
	}

	private static ThingComp ModernWeaponResolve(Thing owner, int moduleId, int index, out Pawn wearer)
	{
		wearer = null;
		if (!ModernCanAct(owner) || index < 0)
		{
			return null;
		}
		Thing value2;
		if (owner is Pawn pawn && !dummyBays.TryGetValue(pawn, out var _))
		{
			wearer = pawn;
		}
		else if (modernBay.IsInstanceOfType(owner) && dummyPawn.GetValue(owner) is Pawn pawn2 && dummyBays.TryGetValue(pawn2, out value2) && value2 == owner)
		{
			wearer = pawn2;
		}
		Apparel apparel = wearer?.apparel?.WornApparel.FirstOrDefault((Apparel a) => a.thingIDNumber == moduleId && !a.Destroyed);
		ThingComp thingComp = apparel?.AllComps.Where((ThingComp c) => modernWeaponType.IsInstanceOfType(c)).Skip(index).FirstOrDefault();
		if (!ModernWeaponAllowed(thingComp, wearer))
		{
			return null;
		}
		if ((owner is Pawn) ? (apparel.Wearer != owner) : (ModernModuleBay(apparel) != owner))
		{
			return null;
		}
		return thingComp;
	}

	private static bool ModernWeaponBefore(object __instance, MethodBase __originalMethod)
	{
		if (!MP.IsInMultiplayer || !MP.InInterface)
		{
			return true;
		}
		ThingComp thingComp = modernWeaponCompCapture.GetValue(__instance) as ThingComp;
		Pawn pawn = modernWeaponPawnCapture.GetValue(__instance) as Pawn;
		if (ModernWeaponAllowed(thingComp, pawn))
		{
			Thing owner = ModernModuleBay(thingComp.parent) ?? pawn;
			int index = ModernWeaponIndex(thingComp);
			if (ModernWeaponResolve(owner, thingComp.parent.thingIDNumber, index, out var wearer) == thingComp && wearer == pawn)
			{
				SyncModernModuleWeapon(owner, thingComp.parent.thingIDNumber, index, __originalMethod == modernWeaponWield);
			}
		}
		return false;
	}

	private static void SyncModernModuleWeapon(Thing owner, int moduleId, int index, bool wield)
	{
		ThingComp thingComp = ModernWeaponResolve(owner, moduleId, index, out var wearer);
		if (thingComp == null)
		{
			return;
		}
		using (new ModernOwnerScope(owner))
		{
			if (wield)
			{
				if (!(AccessTools.Property(modernWeaponType, "Weapon").GetValue(thingComp) is ThingWithComps { Destroyed: false } thingWithComps) || wearer.equipment.Primary == thingWithComps)
				{
					return;
				}
			}
			else
			{
				ThingComp thingComp2 = wearer.equipment.Primary?.AllComps.FirstOrDefault((ThingComp c) => modernForcedWeaponType.IsInstanceOfType(c));
				if (thingComp2 == null || !(AccessTools.Property(modernForcedWeaponType, "ParentComp").GetValue(thingComp2) is ThingComp { parent: Apparel parent }) || parent.Wearer != wearer)
				{
					return;
				}
			}
			MethodInfo methodInfo = (wield ? modernWeaponWield : modernWeaponBareHands);
			object obj = Activator.CreateInstance(methodInfo.DeclaringType, nonPublic: true);
			modernWeaponCompCapture.SetValue(obj, thingComp);
			modernWeaponPawnCapture.SetValue(obj, wearer);
			methodInfo.Invoke(obj, null);
		}
	}
}
