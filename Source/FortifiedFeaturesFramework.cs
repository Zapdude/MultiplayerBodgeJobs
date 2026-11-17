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

namespace Multiplayer.Compat;

[MpCompatFor("AOBA.Framework")]
public class FortifiedFeaturesFramework
{
	private sealed class ScannerOverlayState
	{
		internal object[] Values;
	}

	private sealed class BatteryClock
	{
		internal bool ValidNegative;
	}

	private sealed class TrampleState
	{
		internal int Tick = -1;

		internal readonly HashSet<Thing> Things = new HashSet<Thing>();
	}

	private sealed class NotificationState
	{
		internal float Last = -99999f;
	}

	private sealed class AirPermitDraft
	{
		internal Pawn Caller;

		internal Map Map;

		internal Faction Faction;

		internal bool Free;
	}

	private sealed class ChargeEdit
	{
		internal object Mech;

		internal float Min;

		internal float Max;

		internal bool Restored;
	}

	private sealed class AmmoEdit
	{
		internal object Turret;

		internal Dictionary<ThingDef, int> Settings;

		internal Dictionary<ThingDef, int> Before;

		internal bool Restored;
	}

	private static readonly string[] ScannerCacheFields = new string[5] { "cachedGeoCells", "cachedLosCells", "geoCacheOrigin", "geoCacheRot", "nextLosCacheTick" };

	private static readonly ConditionalWeakTable<object, BatteryClock> BatteryClocks = new ConditionalWeakTable<object, BatteryClock>();

	private static FieldInfo batteryLastUpdate;

	private static FieldInfo batteryDirty;

	private const string Ns = "Fortified.";

	private static bool installed;

	private static Type trampleType;

	private static Func<int> getWorldTicks;

	private static FieldInfo trampleTick;

	private static FieldInfo trampleThings;

	private static readonly ConditionalWeakTable<Map, TrampleState> trampleMaps = new ConditionalWeakTable<Map, TrampleState>();

	private static readonly ConditionalWeakTable<Map, NotificationState> notificationMaps = new ConditionalWeakTable<Map, NotificationState>();

	private static readonly Dictionary<Type, MethodInfo> PermitDirectEffects = new Dictionary<Type, MethodInfo>();

	private static readonly Dictionary<Type, MethodInfo> PermitTargetEffects = new Dictionary<Type, MethodInfo>();

	private static readonly Dictionary<Type, MethodInfo> PermitCaravanEffects = new Dictionary<Type, MethodInfo>();

	private static readonly Dictionary<Type, FieldInfo> FffPermitFactions = new Dictionary<Type, FieldInfo>();

	private static Type mechPermit;

	private static Type airPermit;

	private static Type shuttlePermit;

	private static MethodInfo shuttleCaravanLaunch;

	private static MethodInfo airAfterCall;

	private static FieldInfo mechPermitMechanitor;

	private static readonly FieldInfo TargetCallerField = AccessTools.DeclaredField(typeof(RoyalTitlePermitWorker_Targeted), "caller");

	private static readonly FieldInfo TargetMapField = AccessTools.DeclaredField(typeof(RoyalTitlePermitWorker_Targeted), "map");

	private static readonly FieldInfo TargetFreeField = AccessTools.DeclaredField(typeof(RoyalTitlePermitWorker_Targeted), "free");

	private static readonly PropertyInfo TargetClampedRange = AccessTools.DeclaredProperty(typeof(RoyalTitlePermitWorker_Targeted), "RangeClamped");

	[ThreadStatic]
	private static int permitEffectDepth;

	private static readonly ConditionalWeakTable<RoyalTitlePermitWorker_Targeted, AirPermitDraft> AirPermitDrafts = new ConditionalWeakTable<RoyalTitlePermitWorker_Targeted, AirPermitDraft>();

	private static readonly string[] PermitTypes = new string[6] { "RoyalTitlePermitWorker_Trader", "RoyalTitlePermitWorker_Bandwidth", "RoyalTitlePermitWorker_PawnJoin", "RoyalTitlePermitWorker_MechJoin", "RoyalTitlePermitWorker_CallShuttle_Custom", "RoyalTitlePermitWorker_CallAirSupport" };

	private static Type patrolType;

	private static MethodInfo patrolTick;

	private static MethodInfo companyRaid;

	private static Dictionary<string, Def> companyDefs;

	private static Func<Map, object> mapClock;

	private static FieldInfo mapTickField;

	private static FieldInfo tickingMapField;

	private static Func<bool> initializingCaches;

	[ThreadStatic]
	private static int patrolMapDepth;

	[ThreadStatic]
	private static Map processingCompanyMap;

	private static FieldInfo serializedTurrets;

	private static object emptySerializedTurrets;

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

	private static void InstallAsyncRuntime()
	{
		MethodInfo getter = AccessTools.PropertyGetter(typeof(TickManager), "TicksGame");
		Type[] array = new Type[8]
		{
			T("AirSupportComp_Bombard"),
			T("AirSupportComp_Effecter"),
			T("AirSupportComp_Sound"),
			T("AirSupportComp_Strafe"),
			T("AirSupportComp_StrafeII"),
			T("BombardmentDispatcher"),
			T("WorldObjectComp_PeriodicAirSupport"),
			T("GameComponent_CAS")
		};
		for (int i = 0; i < array.Length; i++)
		{
			foreach (MethodInfo declaredMethod in AccessTools.GetDeclaredMethods(array[i]))
			{
				if (declaredMethod.GetMethodBody() != null && PatchProcessor.GetOriginalInstructions(declaredMethod).Any((CodeInstruction code) => code.Calls(getter)))
				{
					Patch(declaredMethod, null, null, "WorldClockTranspiler");
				}
			}
		}
		(string, string)[] array2 = new (string, string)[2]
		{
			("CompAmmoSwitch", "cooldownUntilTick"),
			("CompCamouflage", "lastDetectedTick")
		};
		for (int i = 0; i < array2.Length; i++)
		{
			(string, string) tuple = array2[i];
			Type type = T(tuple.Item1);
			FieldInfo field = AccessTools.DeclaredField(type, tuple.Item2) ?? throw new MissingFieldException(type.FullName, tuple.Item2);
			PatchingUtilities.RegisterTimestampFields(type, (object value, int offset) =>
			{
				ShiftDeadline(value, field, offset);
			});
		}
		Type type2 = T("CompAlertScanner");
		FieldInfo checkTick = AccessTools.DeclaredField(type2, "nextCheckTick");
		MethodInfo invalidate = AccessTools.DeclaredMethod(type2, "InvalidateScanCache");
		PatchingUtilities.RegisterTimestampFields(type2, (object value, int offset) =>
		{
			ShiftDeadline(value, checkTick, offset);
			invalidate.Invoke(value, null);
		});
		Type type3 = T("SubTurret");
		FieldInfo attackTick = AccessTools.DeclaredField(type3, "lastAttackTargetTick");
		PatchingUtilities.RegisterTimestampFields(type3, (object value, int offset) =>
		{
			if (((LocalTargetInfo)Get(value, "lastAttackedTarget")).IsValid)
			{
				ShiftDeadline(value, attackTick, offset);
			}
		});
		Patch(AccessTools.DeclaredMethod(type2, "PostDrawExtraSelectionOverlays"), "ScannerOverlayPrefix", null, null, "ScannerOverlayFinalizer");
		InstallBatteryClock();
	}

	private static void ShiftDeadline(object value, FieldInfo field, int offset)
	{
		field.SetValue(value, (int)field.GetValue(value) + offset);
	}

	private static void ScannerOverlayPrefix(object __instance, out ScannerOverlayState __state)
	{
		__state = null;
		if (MP.IsInMultiplayer)
		{
			__state = new ScannerOverlayState
			{
				Values = ScannerCacheFields.Select((string name) => Get(__instance, name)).ToArray()
			};
		}
	}

	private static Exception ScannerOverlayFinalizer(object __instance, Exception __exception, ScannerOverlayState __state)
	{
		if (__state != null)
		{
			for (int i = 0; i < ScannerCacheFields.Length; i++)
			{
				Set(__instance, ScannerCacheFields[i], __state.Values[i]);
			}
		}
		return __exception;
	}

	internal static bool CommandAllowed(Thing actor)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (actor == null || actor.Destroyed)
		{
			return CommandAuthorityBridge.CanActForFaction(null);
		}
		if (actor.Faction != null)
		{
			return CommandAuthorityBridge.CanActForFaction(actor.Faction);
		}
		IThingHolder parentHolder = actor.ParentHolder;
		int num = 0;
		while (parentHolder != null && num < 64)
		{
			if (parentHolder is Thing { Faction: not null } thing)
			{
				return CommandAuthorityBridge.CanActForFaction(thing.Faction);
			}
			num++;
			parentHolder = parentHolder.ParentHolder;
		}
		return CommandAuthorityBridge.CanActForFaction(null);
	}

	private static void InstallCommandAuthority()
	{
		CommandAuthorityBridge.Install();
		(string, string)[] array = new (string, string)[2]
		{
			("CompAmmoSwitch", "SetAmmo"),
			("CompOverseer", "Connect")
		};
		for (int i = 0; i < array.Length; i++)
		{
			(string, string) tuple = array[i];
			Patch(AccessTools.DeclaredMethod(T(tuple.Item1), tuple.Item2), "CompCommandAllowed");
		}
		Patch(AccessTools.DeclaredMethod(T("CompAmmoSwitch"), "QueueSwitchJob"), "AmmoCommandAllowed");
		Patch(AccessTools.DeclaredMethod(T("Building_Overseer"), "OrderForceTarget"), "ThingCommandAllowed");
		Patch(AccessTools.DeclaredMethod(T("MechCapsuleUtility"), "DeactivateMech"), "PawnArgumentAllowed");
		string[] array2 = new string[2] { "OverseerMech", "HumanlikeOverseerMech" };
		for (int i = 0; i < array2.Length; i++)
		{
			Patch(AccessTools.PropertySetter(T(array2[i]), "WorkMode"), "ThingCommandAllowed");
		}
	}

	private static bool CompCommandAllowed(ThingComp __instance)
	{
		return CommandAllowed(__instance?.parent);
	}

	private static bool AmmoCommandAllowed(ThingComp __instance, Pawn __0)
	{
		if (CommandAllowed(__instance?.parent))
		{
			return CommandAuthorityBridge.CanActForPawn(__0);
		}
		return false;
	}

	private static bool ThingCommandAllowed(Thing __instance)
	{
		return CommandAllowed(__instance);
	}

	private static bool PawnArgumentAllowed(Pawn __0)
	{
		return CommandAuthorityBridge.CanActForPawn(__0);
	}

	private static void InstallBatteryClock()
	{
		Type type = T("CompPowerTrader_InternalBattery");
		batteryLastUpdate = AccessTools.DeclaredField(type, "lastUpdateTick") ?? throw new MissingFieldException(type.FullName, "lastUpdateTick");
		batteryDirty = AccessTools.DeclaredField(type, "dirty") ?? throw new MissingFieldException(type.FullName, "dirty");
		PatchingUtilities.RegisterTimestampFields(type, ShiftBatteryClock);
		Patch(AccessTools.DeclaredMethod(type, "CompTick"), null, null, "BatteryMarkerTranspiler");
		Patch(AccessTools.DeclaredMethod(type, "PostExposeData"), null, "BatteryExpose");
	}

	private static void ShiftBatteryClock(object value, int offset)
	{
		int num = (int)batteryLastUpdate.GetValue(value);
		BatteryClock orCreateValue = BatteryClocks.GetOrCreateValue(value);
		if (num >= 0 || orCreateValue.ValidNegative)
		{
			int num2 = num + offset;
			batteryLastUpdate.SetValue(value, num2);
			orCreateValue.ValidNegative = num2 < 0;
		}
	}

	private static int BatteryMarker(object value)
	{
		int num = (int)batteryLastUpdate.GetValue(value);
		if (!MP.IsInMultiplayer || num >= 0 || !BatteryClocks.TryGetValue(value, out var value2) || !value2.ValidNegative)
		{
			return num;
		}
		return 0;
	}

	private static IEnumerable<CodeInstruction> BatteryMarkerTranspiler(IEnumerable<CodeInstruction> values)
	{
		List<CodeInstruction> list = values.ToList();
		int changed = 0;
		for (int i = 0; i < list.Count; i++)
		{
			CodeInstruction codeInstruction = list[i];
			if (codeInstruction.LoadsField(batteryLastUpdate) && i + 1 < list.Count && list[i + 1].LoadsConstant(0L))
			{
				changed++;
				yield return new CodeInstruction(codeInstruction)
				{
					opcode = OpCodes.Call,
					operand = AccessTools.DeclaredMethod(typeof(FortifiedFeaturesFramework), "BatteryMarker")
				};
			}
			else
			{
				yield return codeInstruction;
			}
		}
		if (changed != 1)
		{
			throw new InvalidOperationException("FFF internal battery marker IL changed: " + changed);
		}
	}

	private static void BatteryExpose(object __instance)
	{
		if (MP.IsInMultiplayer)
		{
			int value = (int)batteryLastUpdate.GetValue(__instance);
			bool value2 = (bool)batteryDirty.GetValue(__instance);
			BatteryClock orCreateValue = BatteryClocks.GetOrCreateValue(__instance);
			if (Scribe.mode == LoadSaveMode.Saving && value >= 0)
			{
				orCreateValue.ValidNegative = false;
			}
			Scribe_Values.Look(ref value, "mpFFF_batteryLastUpdateTick", -1);
			Scribe_Values.Look(ref value2, "mpFFF_batteryDirty", defaultValue: true);
			Scribe_Values.Look(ref orCreateValue.ValidNegative, "mpFFF_batteryValidNegative", defaultValue: false);
			batteryLastUpdate.SetValue(__instance, value);
			batteryDirty.SetValue(__instance, value2);
		}
	}

	private static void RegisterCommands()
	{
		InstallCommandAuthority();
		(string, string)[] array = new (string, string)[5]
		{
			("CompAmmoSwitch", "QueueSwitchJob"),
			("CompAmmoSwitch", "SetAmmo"),
			("Building_Overseer", "OrderForceTarget"),
			("CompOverseer", "Connect"),
			("MechCapsuleUtility", "DeactivateMech")
		};
		for (int i = 0; i < array.Length; i++)
		{
			(string, string) tuple = array[i];
			MP.RegisterSyncMethod(AccessTools.DeclaredMethod(T(tuple.Item1), tuple.Item2));
		}
		string[] array2 = new string[2] { "OverseerMech", "HumanlikeOverseerMech" };
		for (int i = 0; i < array2.Length; i++)
		{
			MP.RegisterSyncMethod(AccessTools.PropertySetter(T(array2[i]), "WorkMode"));
		}
		array2 = new string[7] { "SetCharge", "SetTurretAmmo", "TurretAction", "ToggleCamouflage", "ToggleDamage", "PastePaint", "SelectOutfit" };
		foreach (string methodOrPropertyName in array2)
		{
			MP.RegisterSyncMethod(typeof(FortifiedFeaturesFramework), methodOrPropertyName);
		}
		MpCompat.RegisterLambdaMethod(T("CompFueledSpawner"), "CompGetGizmosExtra", default(int)).SetDebugOnly();
		MpCompat.RegisterLambdaMethod(T("CompInteracte"), "CompGetGizmosExtra", default(int)).SetDebugOnly();
		MP.RegisterSyncMethod(T("CompAccessKeyDoorLink"), "Unlock").SetDebugOnly();
		MpCompat.RegisterLambdaMethod(T("CompPaintable"), "CompGetGizmosExtra", 1);
		InstallPermits();
	}

	private static void SetCharge(Pawn pawn, float min, float max)
	{
		if (Valid(pawn) && T("IOverseerMech").IsInstanceOfType(pawn) && float.IsFinite(min) && float.IsFinite(max) && !(min < 0f) && !(max > 1f) && !(min > max))
		{
			Set(pawn, "MinCharge", min);
			Set(pawn, "MaxCharge", max);
		}
	}

	private static ThingComp OwnerOf(object turret)
	{
		if (!(Get(turret, "parent") is ThingWithComps thingWithComps))
		{
			return null;
		}
		IEnumerable<ThingWithComps> enumerable = new ThingWithComps[1] { thingWithComps };
		if (thingWithComps is Pawn pawn)
		{
			IEnumerable<ThingWithComps> first = enumerable;
			Pawn_ApparelTracker apparel = pawn.apparel;
			object obj;
			if (apparel == null)
			{
				obj = null;
			}
			else
			{
				List<Apparel> wornApparel = apparel.WornApparel;
				obj = ((wornApparel != null) ? wornApparel.Cast<ThingWithComps>() : null);
			}
			if (obj == null)
			{
				obj = Enumerable.Empty<ThingWithComps>();
			}
			IEnumerable<ThingWithComps> first2 = first.Concat((IEnumerable<ThingWithComps>)obj);
			IEnumerable<ThingWithComps> enumerable2 = pawn.equipment?.AllEquipmentListForReading;
			enumerable = first2.Concat(enumerable2 ?? Enumerable.Empty<ThingWithComps>());
		}
		return enumerable.SelectMany((ThingWithComps t) =>
		{
			IEnumerable<ThingComp> allComps = t.AllComps;
			return allComps ?? Enumerable.Empty<ThingComp>();
		}).FirstOrDefault((ThingComp c) => c.GetType() == T("CompMultipleTurretGun") && ((IList)Get(c, "turrets")).Contains(turret));
	}

	private static object Turret(ThingComp owner, string id)
	{
		if (owner == null || !Valid(owner.parent) || owner.GetType() != T("CompMultipleTurretGun") || id == null)
		{
			return null;
		}
		return ((IEnumerable)Get(owner, "turrets")).Cast<object>().SingleOrDefault((object t) => (string)Get(t, "ID") == id);
	}

	private static void SetTurretAmmo(ThingComp owner, string id, ThingDef def, int count, bool selection)
	{
		object obj = Turret(owner, id);
		object obj2 = ((obj == null) ? null : Get(obj, "Ammo"));
		if (obj2 == null || (def != null && !(bool)Call(Get(obj2, "Props"), "AcceptsAmmo", def)))
		{
			return;
		}
		if (selection)
		{
			Set(obj2, "selectedAmmoDef", def);
			Set(obj, "cannotShootNoAmmo", false);
		}
		else if (def != null && count >= 0 && count <= def.stackLimit)
		{
			Dictionary<ThingDef, int> dictionary = (Dictionary<ThingDef, int>)Get(obj2, "ammoSettings");
			if (count == 0)
			{
				dictionary.Remove(def);
			}
			else
			{
				dictionary[def] = count;
			}
		}
	}

	private static void TurretAction(ThingComp owner, string id, int action, LocalTargetInfo target, bool value)
	{
		object obj = Turret(owner, id);
		if (obj == null)
		{
			return;
		}
		switch (action)
		{
		case 0:
			Call(obj, "RemoveWeapon", value);
			break;
		case 1:
			AccessTools.DeclaredMethod(T("SubTurret"), "SwitchAutoFire", new Type[1] { typeof(bool) }).Invoke(obj, new object[1] { value });
			break;
		case 2:
			if (target.IsValid && (!target.HasThing || target.Thing.MapHeld == owner.parent.MapHeld) && Get(obj, "CurrentEffectiveVerb") is Verb verb && verb.ValidateTarget(target, showMessages: false))
			{
				Set(obj, "targetForced", true);
				Set(obj, "currentTarget", target);
			}
			break;
		case 3:
			Set(obj, "targetForced", false);
			Set(obj, "currentTarget", LocalTargetInfo.Invalid);
			Set(obj, "burstWarmupTicksLeft", 0);
			break;
		}
	}

	private static void ToggleCamouflage(ThingComp comp)
	{
		if (comp != null && !(comp.GetType() != T("CompCamouflage")) && Valid(comp.parent))
		{
			bool flag = !(bool)Get(comp, "active");
			Set(comp, "active", flag);
			if (!flag && Get(comp, "Invisibility") is HediffComp_Invisibility hediffComp_Invisibility)
			{
				hediffComp_Invisibility.BecomeVisible();
			}
			Set(comp, "lastDetectedTick", GenTicks.TicksGame);
		}
	}

	private static void ToggleDamage(ThingComp comp)
	{
		if (comp != null && !(comp.GetType() != T("CompContinuousDamager")) && Valid(comp.parent))
		{
			Call(comp, ((bool)Get(comp, "active")) ? "Deactivate" : "Activate");
		}
	}

	private static void PastePaint(ThingComp comp, Color first, Color second, Color third, Def camo, float brightness, Def overlay)
	{
		if (comp != null && !(comp.GetType() != T("CompPaintable")) && Valid(comp.parent) && float.IsFinite(brightness) && (camo == null || T("FFF_CamoDef").IsInstanceOfType(camo)) && (overlay == null || T("FFF_OverlayDef").IsInstanceOfType(overlay)))
		{
			Set(comp, "activePaintRequest", true);
			Set(comp, "requestColor", first);
			Set(comp, "requestColor2", second);
			Set(comp, "requestColor3", third);
			Set(comp, "requestCamo", camo);
			Set(comp, "requestBrightness", brightness);
			Set(comp, "requestOverlay", overlay);
			comp.Notify_ColorChanged();
		}
	}

	private static void SelectOutfit(Pawn pawn, ApparelPolicy outfit)
	{
		if (Valid(pawn) && pawn.outfits != null && outfit != null && Current.Game.outfitDatabase.AllOutfits.Contains(outfit))
		{
			pawn.outfits.CurrentApparelPolicy = outfit;
			AccessTools.DeclaredMethod(T("HumanlikeMechApparelUtility"), "TryOptimizeApparel").Invoke(null, new object[1] { pawn });
		}
	}

	private static Type T(string name)
	{
		return AccessTools.TypeByName("Fortified." + name) ?? throw new TypeLoadException("Fortified." + name);
	}

	private static object Get(object target, string name)
	{
		object? obj = AccessTools.Field(target.GetType(), name)?.GetValue(target);
		if (obj == null)
		{
			PropertyInfo propertyInfo = AccessTools.Property(target.GetType(), name);
			if ((object)propertyInfo == null)
			{
				return null;
			}
			obj = propertyInfo.GetValue(target);
		}
		return obj;
	}

	private static void Set(object target, string name, object value)
	{
		FieldInfo fieldInfo = AccessTools.Field(target.GetType(), name);
		if (fieldInfo != null)
		{
			fieldInfo.SetValue(target, value);
		}
		else
		{
			AccessTools.Property(target.GetType(), name).SetValue(target, value);
		}
	}

	private static object Call(object target, string name, params object[] args)
	{
		return AccessTools.Method(target.GetType(), name).Invoke(target, args);
	}

	private static bool Valid(Thing thing)
	{
		if (thing != null && !thing.Destroyed && thing.SpawnedOrAnyParentSpawned)
		{
			return CommandAllowed(thing);
		}
		return false;
	}

	private static void Patch(MethodBase method, string prefix = null, string postfix = null, string transpiler = null, string finalizer = null)
	{
		MpCompat.harmony.Patch(method, (prefix == null) ? null : new HarmonyMethod(typeof(FortifiedFeaturesFramework), prefix)
		{
			priority = 800
		}, (postfix == null) ? null : new HarmonyMethod(typeof(FortifiedFeaturesFramework), postfix), (transpiler == null) ? null : new HarmonyMethod(typeof(FortifiedFeaturesFramework), transpiler), (finalizer == null) ? null : new HarmonyMethod(typeof(FortifiedFeaturesFramework), finalizer));
	}

	public FortifiedFeaturesFramework(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(Install);
	}

	private static void Install()
	{
		if (!installed)
		{
			InstallRuntime();
			RegisterCommands();
			installed = true;
		}
	}

	private static void InstallRuntime()
	{
		trampleType = T("CompTrampleBuildings");
		trampleTick = AccessTools.DeclaredField(trampleType, "processedTick");
		trampleThings = AccessTools.DeclaredField(trampleType, "processedThings");
		Patch(AccessTools.DeclaredMethod(trampleType, "TrampleOnce"), null, null, "TrampleTranspiler");
		Patch(AccessTools.DeclaredMethod(T("CompCamouflage"), "Notify_BecameVisible"), null, null, "NotificationTranspiler");
		Patch(AccessTools.DeclaredMethod(typeof(Map), "ExposeData"), null, "MapExpose");
		InstallAsyncRuntime();
		InstallReadinessRuntime();
		InstallTurretSerialization();
		Type[] array = new Type[4]
		{
			T("HuntdownUtility"),
			T("HuntdownInstance"),
			T("GameComponent_Huntdown"),
			T("Alert_FFF_Huntdown")
		};
		for (int i = 0; i < array.Length; i++)
		{
			foreach (MethodInfo declaredMethod in AccessTools.GetDeclaredMethods(array[i]))
			{
				if (declaredMethod.GetMethodBody() != null && PatchProcessor.GetOriginalInstructions(declaredMethod).Any((CodeInstruction code) => code.Calls(AccessTools.PropertyGetter(typeof(TickManager), "TicksGame"))))
				{
					Patch(declaredMethod, null, null, "WorldClockTranspiler");
				}
			}
		}
		foreach (MethodInfo item in new MethodInfo[3]
		{
			AccessTools.DeclaredMethod(T("CompCastFlecker"), "BurstFleck"),
			AccessTools.DeclaredMethod(T("CompFlyingFleckThrower"), "CompTick"),
			AccessTools.DeclaredMethod(T("CompFlyByThrownFleckEmitter"), "Emit")
		}.Concat(from m in AccessTools.GetDeclaredMethods(T("FleckMakerEx"))
			where m.Name.StartsWith("Throw", StringComparison.Ordinal)
			select m))
		{
			Patch(item, "VisualPrefix", null, null, "VisualFinalizer");
		}
		Patch(AccessTools.DeclaredMethod(T("CompCastFlecker"), "DrawPos"), "UnityVisualPrefix", null, null, "UnityVisualFinalizer");
		InstallUiRuntime();
	}

	private static void VisualPrefix(out bool __state)
	{
		__state = MP.IsInMultiplayer;
		if (__state)
		{
			Rand.PushState();
		}
	}

	private static Exception VisualFinalizer(Exception __exception, bool __state)
	{
		if (__state)
		{
			Rand.PopState();
		}
		return __exception;
	}

	private static void UnityVisualPrefix(out UnityEngine.Random.State? __state)
	{
		__state = (MP.IsInMultiplayer ? new UnityEngine.Random.State?(UnityEngine.Random.state) : ((UnityEngine.Random.State?)null));
	}

	private static Exception UnityVisualFinalizer(Exception __exception, UnityEngine.Random.State? __state)
	{
		if (__state.HasValue)
		{
			UnityEngine.Random.state = __state.Value;
		}
		return __exception;
	}

	private static TrampleState TrampleFor(ThingComp comp)
	{
		return trampleMaps.GetValue(comp.parent.Map, (Map _) => new TrampleState());
	}

	private static int TrampleTick(ThingComp comp)
	{
		if (!MP.IsInMultiplayer || comp.parent.Map == null)
		{
			return (int)trampleTick.GetValue(null);
		}
		return TrampleFor(comp).Tick;
	}

	private static void SetTrampleTick(int tick, ThingComp comp)
	{
		if (MP.IsInMultiplayer && comp.parent.Map != null)
		{
			TrampleFor(comp).Tick = tick;
		}
		else
		{
			trampleTick.SetValue(null, tick);
		}
	}

	private static HashSet<Thing> TrampleThings(ThingComp comp)
	{
		if (!MP.IsInMultiplayer || comp.parent.Map == null)
		{
			return (HashSet<Thing>)trampleThings.GetValue(null);
		}
		return TrampleFor(comp).Things;
	}

	private static IEnumerable<CodeInstruction> TrampleTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		int reads = 0;
		int writes = 0;
		int sets = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.LoadsField(trampleTick))
			{
				reads++;
				yield return new CodeInstruction(OpCodes.Ldarg_0).WithLabels(instruction.labels).WithBlocks(instruction.blocks);
				yield return CodeInstruction.Call(typeof(FortifiedFeaturesFramework), "TrampleTick");
			}
			else if (instruction.StoresField(trampleTick))
			{
				writes++;
				yield return new CodeInstruction(OpCodes.Ldarg_0).WithLabels(instruction.labels).WithBlocks(instruction.blocks);
				yield return CodeInstruction.Call(typeof(FortifiedFeaturesFramework), "SetTrampleTick");
			}
			else if (instruction.LoadsField(trampleThings))
			{
				sets++;
				yield return new CodeInstruction(OpCodes.Ldarg_0).WithLabels(instruction.labels).WithBlocks(instruction.blocks);
				yield return CodeInstruction.Call(typeof(FortifiedFeaturesFramework), "TrampleThings");
			}
			else
			{
				yield return instruction;
			}
		}
		if (reads != 1 || writes != 1 || sets != 2)
		{
			throw new InvalidOperationException($"FFF trample IL changed: {reads}/{writes}/{sets}");
		}
	}

	private static float NotificationClock()
	{
		if (!MP.IsInMultiplayer)
		{
			return RealTime.LastRealTime;
		}
		return (float)GenTicks.TicksGame / 60f;
	}

	internal static int WorldTicks()
	{
		if (!MP.IsInMultiplayer)
		{
			return Find.TickManager.TicksGame;
		}
		if (getWorldTicks == null)
		{
			getWorldTicks = (Func<int>)AccessTools.DeclaredMethod(AccessTools.TypeByName("Multiplayer.Client.AsyncTimeComp"), "GetWorldTicks").CreateDelegate(typeof(Func<int>));
		}
		return getWorldTicks();
	}

	internal static IEnumerable<CodeInstruction> WorldClockTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo ticks = AccessTools.PropertyGetter(typeof(TickManager), "TicksGame");
		int changed = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (!instruction.Calls(ticks))
			{
				yield return instruction;
				continue;
			}
			changed++;
			yield return new CodeInstruction(OpCodes.Pop).WithLabels(instruction.labels).WithBlocks(instruction.blocks);
			yield return CodeInstruction.Call(typeof(FortifiedFeaturesFramework), "WorldTicks");
		}
		if (changed == 0)
		{
			throw new InvalidOperationException("FFF world deadline no longer reads TickManager.TicksGame");
		}
	}

	private static float LastNotification(ThingComp comp)
	{
		if (!MP.IsInMultiplayer || comp.parent.MapHeld == null)
		{
			return (float)AccessTools.DeclaredField(T("CompCamouflage"), "lastNotified").GetValue(null);
		}
		return notificationMaps.GetValue(comp.parent.MapHeld, (Map _) => new NotificationState()).Last;
	}

	private static void SetLastNotification(float value, ThingComp comp)
	{
		if (!MP.IsInMultiplayer || comp.parent.MapHeld == null)
		{
			AccessTools.DeclaredField(T("CompCamouflage"), "lastNotified").SetValue(null, value);
			return;
		}
		notificationMaps.GetValue(comp.parent.MapHeld, (Map _) => new NotificationState()).Last = value;
	}

	private static void MapExpose(Map __instance)
	{
		Scribe_Values.Look(ref notificationMaps.GetValue(__instance, (Map _) => new NotificationState()).Last, "mpFFF_camouflageNotificationTime", -99999f);
	}

	private static IEnumerable<CodeInstruction> NotificationTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo clock = AccessTools.PropertyGetter(typeof(RealTime), "LastRealTime");
		FieldInfo last = AccessTools.DeclaredField(T("CompCamouflage"), "lastNotified");
		int clocks = 0;
		int reads = 0;
		int writes = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(clock))
			{
				clocks++;
				yield return CodeInstruction.Call(typeof(FortifiedFeaturesFramework), "NotificationClock").WithLabels(instruction.labels).WithBlocks(instruction.blocks);
			}
			else if (instruction.LoadsField(last))
			{
				reads++;
				yield return new CodeInstruction(OpCodes.Ldarg_0).WithLabels(instruction.labels).WithBlocks(instruction.blocks);
				yield return CodeInstruction.Call(typeof(FortifiedFeaturesFramework), "LastNotification");
			}
			else if (instruction.StoresField(last))
			{
				writes++;
				yield return new CodeInstruction(OpCodes.Ldarg_0).WithLabels(instruction.labels).WithBlocks(instruction.blocks);
				yield return CodeInstruction.Call(typeof(FortifiedFeaturesFramework), "SetLastNotification");
			}
			else
			{
				yield return instruction;
			}
		}
		if (clocks != 2 || reads != 1 || writes != 1)
		{
			throw new InvalidOperationException($"FFF notification IL changed: {clocks}/{reads}/{writes}");
		}
	}

	private static MethodInfo PermitMethod(Type type, string name)
	{
		return AccessTools.DeclaredMethod(type, name) ?? throw new MissingMethodException(type.FullName, name);
	}

	private static FieldInfo PermitField(Type type, string name)
	{
		return AccessTools.Field(type, name) ?? throw new MissingFieldException(type.FullName, name);
	}

	private static object PermitCaptured(object closure, string name)
	{
		return PermitField(closure.GetType(), name).GetValue(closure);
	}

	private static RoyalTitlePermitWorker_Targeted FreshTargetPermit(RoyalTitlePermitDef def, Pawn caller, Map map, Faction faction, bool free, Pawn mechanitor)
	{
		RoyalTitlePermitWorker_Targeted royalTitlePermitWorker_Targeted = (RoyalTitlePermitWorker_Targeted)Activator.CreateInstance(def.workerClass);
		royalTitlePermitWorker_Targeted.def = def;
		TargetCallerField.SetValue(royalTitlePermitWorker_Targeted, caller);
		TargetMapField.SetValue(royalTitlePermitWorker_Targeted, map);
		TargetFreeField.SetValue(royalTitlePermitWorker_Targeted, free);
		if (FffPermitFactions.TryGetValue(royalTitlePermitWorker_Targeted.GetType(), out var value))
		{
			value.SetValue(royalTitlePermitWorker_Targeted, faction);
		}
		if (royalTitlePermitWorker_Targeted.GetType() == mechPermit)
		{
			mechPermitMechanitor.SetValue(royalTitlePermitWorker_Targeted, mechanitor);
		}
		return royalTitlePermitWorker_Targeted;
	}

	private static void InstallPermits()
	{
		InstallPermitRuntime();
		MP.RegisterSyncMethod(typeof(FortifiedFeaturesFramework), "SyncFffDirectPermit");
		MP.RegisterSyncMethod(typeof(FortifiedFeaturesFramework), "SyncFffTargetPermit");
		MP.RegisterSyncMethod(typeof(FortifiedFeaturesFramework), "SyncFffCaravanPermit");
		MP.RegisterSyncMethod(typeof(FortifiedFeaturesFramework), "SyncFffShuttleCaravan").ExposeParameter(6);
	}

	private static void InstallPermitRuntime()
	{
		mechPermit = T(PermitTypes[3]);
		shuttlePermit = T(PermitTypes[4]);
		airPermit = T(PermitTypes[5]);
		mechPermitMechanitor = PermitField(mechPermit, "mechanitor");
		foreach (string item in PermitTypes.Take(2))
		{
			Type type = T(item);
			MethodInfo methodInfo = PermitMethod(type, "DoEffect");
			PermitDirectEffects[type] = methodInfo;
			Patch(methodInfo, "BeforeFffDirectPermit");
		}
		foreach (string item2 in PermitTypes.Skip(2))
		{
			Type type2 = T(item2);
			MethodInfo methodInfo2 = PermitMethod(type2, "OrderForceTarget");
			PermitTargetEffects[type2] = methodInfo2;
			if (type2 != airPermit)
			{
				FffPermitFactions[type2] = PermitField(type2, (type2 == shuttlePermit) ? "calledFaction" : "faction");
			}
			Patch(methodInfo2, "BeforeFffTargetPermit");
		}
		string[] array = new string[2] { "RoyalTitlePermitWorker_PawnJoin", "RoyalTitlePermitWorker_MechJoin" };
		for (int i = 0; i < array.Length; i++)
		{
			Type type3 = T(array[i]);
			MethodInfo methodInfo3 = PermitMethod(type3, "CallPawnToCaravan");
			PermitCaravanEffects[type3] = methodInfo3;
			Patch(methodInfo3, "BeforeFffCaravanPermit");
		}
		array = new string[3] { "RoyalTitlePermitWorker_PawnJoin", "RoyalTitlePermitWorker_MechJoin", "RoyalTitlePermitWorker_CallShuttle_Custom" };
		for (int i = 0; i < array.Length; i++)
		{
			Patch(PermitMethod(T(array[i]), "GetCaravanGizmos"), null, "FffCaravanOptions");
		}
		Patch(PermitMethod(airPermit, "BeginCallSupport"), null, "AirPermitStarted");
		airAfterCall = PermitMethod(airPermit, "AfterCallActions");
		Patch(airAfterCall, "BeforeAirPermitPayment");
		Patch(PermitMethod(mechPermit, "BeginCallPawn"), "BeforeMechPermitTargeting");
		MethodInfo[] source = mechPermit.GetNestedTypes(AccessTools.all).SelectMany(AccessTools.GetDeclaredMethods).ToArray();
		MethodInfo method = source.Single((MethodInfo m) => m.Name.StartsWith("<GetRoyalAidOptions>g__SyncCallFaction|", StringComparison.Ordinal));
		MethodInfo method2 = source.Single((MethodInfo m) => m.Name.StartsWith("<GetCaravanGizmos>g__SyncPawnToCaravan|", StringComparison.Ordinal));
		Patch(method, "BeforeLegacyMechPermitMap");
		Patch(method2, "BeforeLegacyMechPermitCaravan");
		shuttleCaravanLaunch = shuttlePermit.GetNestedTypes(AccessTools.all).SelectMany(AccessTools.GetDeclaredMethods).Single((MethodInfo m) => m.Name.StartsWith("<CallShuttleToCaravan>g__Launch|", StringComparison.Ordinal));
		array = new string[5] { "<>4__this", "caller", "caravan", "faction", "free" };
		foreach (string name in array)
		{
			PermitField(shuttleCaravanLaunch.DeclaringType, name);
		}
		Patch(shuttleCaravanLaunch, "BeforeFffShuttleCaravan", null, "ShuttleCaravanPresentation");
	}

	private static bool FffPermitCanUse(RoyalTitlePermitDef def, Pawn caller, Faction faction, bool free)
	{
		if (def?.royalAid == null || caller == null || caller.Dead || caller.Downed || caller.royalty == null || faction == null || faction.HostileTo(Faction.OfPlayer) || !CommandAuthorityBridge.CanActForPawn(caller))
		{
			return false;
		}
		FactionPermit permit = caller.royalty.GetPermit(def, faction);
		if (permit != null)
		{
			if (!free)
			{
				return caller.royalty.GetFavor(faction) >= def.royalAid.favorCost;
			}
			return !permit.OnCooldown;
		}
		return false;
	}

	private static bool BeforeFffDirectPermit(RoyalTitlePermitWorker __instance, Pawn caller, Faction faction, bool free)
	{
		if (!MP.IsInMultiplayer || permitEffectDepth > 0)
		{
			return true;
		}
		if (Intercept)
		{
			SyncFffDirectPermit(__instance.def, caller, faction, free);
		}
		return false;
	}

	private static void SyncFffDirectPermit(RoyalTitlePermitDef def, Pawn caller, Faction faction, bool free)
	{
		if (!MP.IsInMultiplayer || !MP.IsExecutingSyncCommand || def?.workerClass == null || !PermitDirectEffects.TryGetValue(def.workerClass, out var value) || !FffPermitCanUse(def, caller, faction, free) || !caller.Spawned)
		{
			return;
		}
		RoyalTitlePermitWorker royalTitlePermitWorker = (RoyalTitlePermitWorker)Activator.CreateInstance(def.workerClass);
		royalTitlePermitWorker.def = def;
		permitEffectDepth++;
		try
		{
			value.Invoke(royalTitlePermitWorker, new object[3] { caller, faction, free });
		}
		finally
		{
			permitEffectDepth--;
		}
	}

	private static void BeforeMechPermitTargeting(object __instance, Pawn caller)
	{
		if (MP.IsInMultiplayer)
		{
			mechPermitMechanitor.SetValue(__instance, caller);
		}
	}

	private static void AirPermitStarted(RoyalTitlePermitWorker_Targeted __instance, Pawn caller, Map map, Faction faction, bool free)
	{
		if (MP.IsInMultiplayer)
		{
			AirPermitDrafts.Remove(__instance);
			AirPermitDrafts.Add(__instance, new AirPermitDraft
			{
				Caller = caller,
				Map = map,
				Faction = faction,
				Free = free
			});
		}
	}

	private static bool BeforeAirPermitPayment()
	{
		if (MP.IsInMultiplayer)
		{
			return permitEffectDepth > 0;
		}
		return true;
	}

	private static void FffCaravanOptions(RoyalTitlePermitWorker_Targeted __instance, ref IEnumerable<Gizmo> __result)
	{
		if (MP.IsInMultiplayer)
		{
			__result = SnapshotCaravanOptions(__instance, __result);
		}
	}

	private static IEnumerable<Gizmo> SnapshotCaravanOptions(RoyalTitlePermitWorker_Targeted worker, IEnumerable<Gizmo> options)
	{
		foreach (Gizmo option in options)
		{
			if (option is Command_Action { action: not null } command_Action)
			{
				bool free = (bool)TargetFreeField.GetValue(worker);
				Action original = command_Action.action;
				command_Action.action = () =>
				{
					bool flag = (bool)TargetFreeField.GetValue(worker);
					TargetFreeField.SetValue(worker, free);
					try
					{
						original();
					}
					finally
					{
						TargetFreeField.SetValue(worker, flag);
					}
				};
			}
			yield return option;
		}
	}

	private static bool BeforeFffTargetPermit(RoyalTitlePermitWorker_Targeted __instance, LocalTargetInfo target)
	{
		if (!MP.IsInMultiplayer || permitEffectDepth > 0)
		{
			return true;
		}
		if (!Intercept)
		{
			return false;
		}
		if (__instance.GetType() == airPermit)
		{
			if (!AirPermitDrafts.TryGetValue(__instance, out var value))
			{
				return false;
			}
			SyncFffTargetPermit(__instance.def, value.Caller, value.Map, value.Faction, value.Free, null, target);
		}
		else
		{
			Faction faction = (Faction)FffPermitFactions[__instance.GetType()].GetValue(__instance);
			Pawn mechanitor = ((__instance.GetType() == mechPermit) ? ((Pawn)mechPermitMechanitor.GetValue(__instance)) : null);
			SyncFffTargetPermit(__instance.def, (Pawn)TargetCallerField.GetValue(__instance), (Map)TargetMapField.GetValue(__instance), faction, (bool)TargetFreeField.GetValue(__instance), mechanitor, target);
		}
		return false;
	}

	private static bool FffPermitTargetValid(RoyalTitlePermitWorker_Targeted worker, LocalTargetInfo target)
	{
		Pawn pawn = (Pawn)TargetCallerField.GetValue(worker);
		Map map = (Map)TargetMapField.GetValue(worker);
		if (pawn == null || !pawn.Spawned || pawn.Map != map || !target.IsValid || !target.Cell.InBounds(map))
		{
			return false;
		}
		float num = ((worker.GetType() == shuttlePermit) ? ((float)TargetClampedRange.GetValue(worker)) : worker.def.royalAid.targetingRange);
		if (num > 0f && target.Cell.DistanceTo(pawn.Position) > num)
		{
			return false;
		}
		if (!worker.CanHitTarget(target) || target.Cell.Fogged(map))
		{
			return false;
		}
		if (worker.GetType() == shuttlePermit)
		{
			return worker.ValidateTarget(target, showMessages: false);
		}
		if (worker.GetType() == airPermit)
		{
			RoofDef roof = target.Cell.GetRoof(map);
			if (roof == null)
			{
				return true;
			}
			return !roof.isThickRoof;
		}
		return target.Cell.Walkable(map);
	}

	private static void SyncFffTargetPermit(RoyalTitlePermitDef def, Pawn caller, Map map, Faction faction, bool free, Pawn mechanitor, LocalTargetInfo target)
	{
		if (!MP.IsInMultiplayer || !MP.IsExecutingSyncCommand || def?.workerClass == null || !PermitTargetEffects.TryGetValue(def.workerClass, out var value) || !FffPermitCanUse(def, caller, faction, free))
		{
			return;
		}
		RoyalTitlePermitWorker_Targeted royalTitlePermitWorker_Targeted = FreshTargetPermit(def, caller, map, faction, free, mechanitor);
		if (!FffPermitTargetValid(royalTitlePermitWorker_Targeted, target) || (royalTitlePermitWorker_Targeted.GetType() == mechPermit && (mechanitor == null || mechanitor.MapHeld != map || !CommandAuthorityBridge.CanActForPawn(mechanitor))))
		{
			return;
		}
		permitEffectDepth++;
		try
		{
			value.Invoke(royalTitlePermitWorker_Targeted, new object[1] { target });
			if (royalTitlePermitWorker_Targeted.GetType() == airPermit)
			{
				airAfterCall.Invoke(royalTitlePermitWorker_Targeted, new object[4] { caller, map, faction, free });
			}
		}
		finally
		{
			permitEffectDepth--;
		}
	}

	private static bool BeforeLegacyMechPermitMap(object __instance)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (Intercept)
		{
			object obj = PermitCaptured(__instance, "<>4__this");
			PermitMethod(mechPermit, "BeginCallPawn").Invoke(obj, new object[4]
			{
				PermitCaptured(__instance, "pawn"),
				PermitCaptured(__instance, "faction"),
				PermitCaptured(__instance, "map"),
				PermitCaptured(__instance, "free")
			});
		}
		return false;
	}

	private static bool BeforeLegacyMechPermitCaravan(object __instance)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (Intercept)
		{
			RoyalTitlePermitWorker_Targeted royalTitlePermitWorker_Targeted = (RoyalTitlePermitWorker_Targeted)PermitCaptured(__instance, "<>4__this");
			SyncFffCaravanPermit(royalTitlePermitWorker_Targeted.def, (Pawn)PermitCaptured(__instance, "pawn"), (Faction)PermitCaptured(__instance, "faction"), (bool)TargetFreeField.GetValue(royalTitlePermitWorker_Targeted));
		}
		return false;
	}

	private static bool BeforeFffCaravanPermit(RoyalTitlePermitWorker __instance, Pawn caller, Faction faction, bool free)
	{
		if (!MP.IsInMultiplayer || permitEffectDepth > 0)
		{
			return true;
		}
		if (Intercept)
		{
			SyncFffCaravanPermit(__instance.def, caller, faction, free);
		}
		return false;
	}

	private static void SyncFffCaravanPermit(RoyalTitlePermitDef def, Pawn caller, Faction faction, bool free)
	{
		if (!MP.IsInMultiplayer || !MP.IsExecutingSyncCommand || def?.workerClass == null || !PermitCaravanEffects.TryGetValue(def.workerClass, out var value) || !FffPermitCanUse(def, caller, faction, free))
		{
			return;
		}
		Caravan caravan = caller.GetCaravan();
		if (caravan == null || !caravan.Spawned)
		{
			return;
		}
		RoyalTitlePermitWorker royalTitlePermitWorker = (RoyalTitlePermitWorker)Activator.CreateInstance(def.workerClass);
		royalTitlePermitWorker.def = def;
		permitEffectDepth++;
		try
		{
			value.Invoke(royalTitlePermitWorker, new object[3] { caller, faction, free });
		}
		finally
		{
			permitEffectDepth--;
		}
	}

	private static bool BeforeFffShuttleCaravan(object __instance, PlanetTile tile, TransportersArrivalAction arrivalAction)
	{
		if (!MP.IsInMultiplayer || permitEffectDepth > 0)
		{
			return true;
		}
		if (Intercept)
		{
			SyncFffShuttleCaravan(((RoyalTitlePermitWorker)PermitCaptured(__instance, "<>4__this")).def, (Pawn)PermitCaptured(__instance, "caller"), (Faction)PermitCaptured(__instance, "faction"), (bool)PermitCaptured(__instance, "free"), (Caravan)PermitCaptured(__instance, "caravan"), tile, arrivalAction);
		}
		return false;
	}

	private static void SyncFffShuttleCaravan(RoyalTitlePermitDef def, Pawn caller, Faction faction, bool free, Caravan caravan, PlanetTile tile, TransportersArrivalAction arrivalAction)
	{
		if (!MP.IsInMultiplayer || !MP.IsExecutingSyncCommand || def?.workerClass != shuttlePermit || !FffPermitCanUse(def, caller, faction, free) || caravan == null || !caravan.Spawned || caller.GetCaravan() != caravan || !tile.Valid)
		{
			return;
		}
		RoyalTitlePermitWorker royalTitlePermitWorker = (RoyalTitlePermitWorker)Activator.CreateInstance(shuttlePermit);
		royalTitlePermitWorker.def = def;
		TransportShipDef transportShipDef = (TransportShipDef)AccessTools.Property(shuttlePermit, "ShipDef").GetValue(royalTitlePermitWorker);
		if (transportShipDef == null || (transportShipDef.maxLaunchDistance > 0 && Find.WorldGrid.TraversalDistanceBetween(caravan.Tile, tile, passImpassable: true, transportShipDef.maxLaunchDistance) > transportShipDef.maxLaunchDistance) || (arrivalAction != null && !arrivalAction.StillValid(new IThingHolder[1] { caravan }, tile)))
		{
			return;
		}
		object obj = Activator.CreateInstance(shuttleCaravanLaunch.DeclaringType);
		(string, object)[] array = new (string, object)[5]
		{
			("<>4__this", royalTitlePermitWorker),
			("caller", caller),
			("faction", faction),
			("free", free),
			("caravan", caravan)
		};
		for (int i = 0; i < array.Length; i++)
		{
			(string, object) tuple = array[i];
			PermitField(obj.GetType(), tuple.Item1).SetValue(obj, tuple.Item2);
		}
		permitEffectDepth++;
		try
		{
			shuttleCaravanLaunch.Invoke(obj, new object[2] { tile, arrivalAction });
		}
		finally
		{
			permitEffectDepth--;
		}
	}

	private static void FffShuttleStopTargeting(WorldTargeter targeter)
	{
		if (!MP.IsInMultiplayer)
		{
			targeter.StopTargeting();
		}
	}

	private static IEnumerable<CodeInstruction> ShuttleCaravanPresentation(IEnumerable<CodeInstruction> instructions)
	{
		int replaced = 0;
		MethodInfo stop = AccessTools.DeclaredMethod(typeof(WorldTargeter), "StopTargeting");
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(stop))
			{
				replaced++;
				yield return new CodeInstruction(instruction)
				{
					opcode = OpCodes.Call,
					operand = AccessTools.DeclaredMethod(typeof(FortifiedFeaturesFramework), "FffShuttleStopTargeting")
				};
			}
			else
			{
				yield return instruction;
			}
		}
		if (replaced != 1)
		{
			throw new InvalidOperationException($"FFF shuttle final action UI changed: {replaced}");
		}
	}

	private static void InstallReadinessRuntime()
	{
		MethodInfo getter = AccessTools.PropertyGetter(typeof(TickManager), "TicksGame");
		string[] array = new string[6] { "GameComponent_Readiness", "ReadinessState", "ReadinessUtility", "Alert_FFF_Readiness", "GameComponent_FactionHostilityLock", "SitePartWorker_FFF_Company" };
		for (int i = 0; i < array.Length; i++)
		{
			foreach (MethodInfo declaredMethod in AccessTools.GetDeclaredMethods(T(array[i])))
			{
				if (declaredMethod.GetMethodBody() != null && PatchProcessor.GetOriginalInstructions(declaredMethod).Any((CodeInstruction code) => code.Calls(getter)))
				{
					Patch(declaredMethod, null, null, "WorldClockTranspiler");
				}
			}
		}
		patrolType = T("WorldObjectComp_ReadinessPatrol");
		patrolTick = AccessTools.DeclaredMethod(patrolType, "CompTick");
		Patch(patrolTick, "PatrolTickPrefix", null, "PatrolClockTranspiler");
		Patch(AccessTools.DeclaredMethod(patrolType, "ScheduleNext"), null, null, "PatrolClockTranspiler");
		Patch(AccessTools.DeclaredMethod(typeof(Map), "MapPostTick"), null, "ReadinessMapTick");
		companyRaid = AccessTools.DeclaredMethod(T("CompanyUtility"), "RaidMap");
		Patch(companyRaid, "CompanyRaidPrefix");
		Patch(AccessTools.DeclaredMethod(T("ReadinessAction_Incident"), "Run"), "ReadinessIncidentPrefix");
		companyDefs = ((IEnumerable)AccessTools.PropertyGetter(typeof(DefDatabase<>).MakeGenericType(T("CompanyDef")), "AllDefsListForReading").Invoke(null, null)).Cast<Def>().ToDictionary((Def d) => d.defName, StringComparer.Ordinal);
		Type type = AccessTools.TypeByName("Multiplayer.Client.AsyncTimeComp");
		mapClock = AccessTools.DeclaredMethod(AccessTools.TypeByName("Multiplayer.Client.Extensions"), "AsyncTime", new Type[1] { typeof(Map) }).CreateDelegate<Func<Map, object>>();
		mapTickField = AccessTools.DeclaredField(type, "mapTicks");
		tickingMapField = AccessTools.DeclaredField(type, "tickingMap");
		initializingCaches = AccessTools.PropertyGetter(AccessTools.TypeByName("Multiplayer.Client.LoadPatch"), "IsInitializingCaches").CreateDelegate<Func<bool>>();
	}

	private static Map PatrolMap(object comp)
	{
		if (!(((WorldObjectComp)comp).parent is MapParent mapParent))
		{
			return null;
		}
		return mapParent.Map;
	}

	private static int PatrolTicks(object comp)
	{
		if (!MP.IsInMultiplayer)
		{
			return Find.TickManager.TicksGame;
		}
		Map map = PatrolMap(comp);
		object obj = ((map == null) ? null : mapClock(map));
		if (obj != null)
		{
			return (int)mapTickField.GetValue(obj);
		}
		return WorldTicks();
	}

	private static bool PatrolInterval(WorldObject parent, int interval)
	{
		if (!MP.IsInMultiplayer)
		{
			return parent.IsHashIntervalTick(interval);
		}
		if (interval <= 0)
		{
			return true;
		}
		Map map = (parent as MapParent)?.Map;
		object obj = ((map == null) ? null : mapClock(map));
		if (obj != null)
		{
			return ((int)mapTickField.GetValue(obj) + parent.HashOffset()) % interval == 0;
		}
		return false;
	}

	private static bool PatrolTickPrefix()
	{
		if (MP.IsInMultiplayer)
		{
			return patrolMapDepth > 0;
		}
		return true;
	}

	private static IEnumerable<CodeInstruction> PatrolClockTranspiler(IEnumerable<CodeInstruction> values)
	{
		MethodInfo getter = AccessTools.PropertyGetter(typeof(TickManager), "TicksGame");
		MethodInfo interval = AccessTools.Method(typeof(Gen), "IsHashIntervalTick", new Type[2]
		{
			typeof(WorldObject),
			typeof(int)
		});
		int clocks = 0;
		foreach (CodeInstruction value in values)
		{
			if (value.Calls(getter))
			{
				clocks++;
				yield return new CodeInstruction(OpCodes.Pop).WithLabels(value.labels).WithBlocks(value.blocks);
				yield return new CodeInstruction(OpCodes.Ldarg_0);
				yield return CodeInstruction.Call(typeof(FortifiedFeaturesFramework), "PatrolTicks");
			}
			else if (value.Calls(interval))
			{
				yield return CodeInstruction.Call(typeof(FortifiedFeaturesFramework), "PatrolInterval").WithLabels(value.labels).WithBlocks(value.blocks);
			}
			else
			{
				yield return value;
			}
		}
		if (clocks != 1)
		{
			throw new InvalidOperationException("FFF patrol clock IL changed: " + clocks);
		}
	}

	private static bool CompanyRaidPrefix(Map map, object company, Faction faction, ref bool __result)
	{
		if (!MP.IsInMultiplayer || map == processingCompanyMap)
		{
			return true;
		}
		__result = false;
		if (map == null || map.Disposed || !(company is Def def) || faction == null || !companyDefs.ContainsKey(def.defName) || !(Get(company, "squads") is IList { Count: >0 }))
		{
			return false;
		}
		Current.Game.GetComponent<FffCompanyMapOperations>().Raids.Add(new FffPendingCompanyRaid
		{
			Map = map,
			Company = def.defName,
			Faction = faction
		});
		__result = true;
		return false;
	}

	private static bool ReadinessIncidentPrefix(object __instance, Faction faction, ref bool __result)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		__result = false;
		Map anyPlayerHomeMap = Find.AnyPlayerHomeMap;
		if (anyPlayerHomeMap == null || anyPlayerHomeMap.Disposed || !(Get(__instance, "incident") is IncidentDef incident) || faction == null)
		{
			return false;
		}
		Current.Game.GetComponent<FffCompanyMapOperations>().Incidents.Add(new FffPendingReadinessIncident
		{
			Map = anyPlayerHomeMap,
			Incident = incident,
			Faction = faction,
			PointsFactor = (float)Get(__instance, "pointsFactor")
		});
		__result = true;
		return false;
	}

	private static void ReadinessMapTick(Map __instance)
	{
		if (!MP.IsInMultiplayer || initializingCaches() || (Map)tickingMapField.GetValue(null) != __instance)
		{
			return;
		}
		patrolMapDepth++;
		try
		{
			foreach (WorldObjectComp allComp in __instance.Parent.AllComps)
			{
				if (patrolType.IsInstanceOfType(allComp))
				{
					patrolTick.Invoke(allComp, null);
				}
			}
		}
		finally
		{
			patrolMapDepth--;
		}
		FffCompanyMapOperations component = Current.Game.GetComponent<FffCompanyMapOperations>();
		int num = 0;
		while (num < component.Raids.Count)
		{
			FffPendingCompanyRaid fffPendingCompanyRaid = component.Raids[num];
			if (fffPendingCompanyRaid.Map == null || fffPendingCompanyRaid.Map.Disposed)
			{
				component.Raids.RemoveAt(num);
				continue;
			}
			if (fffPendingCompanyRaid.Map != __instance)
			{
				num++;
				continue;
			}
			component.Raids.RemoveAt(num);
			if (companyDefs.TryGetValue(fffPendingCompanyRaid.Company, out var value) && fffPendingCompanyRaid.Faction != null)
			{
				Map map = processingCompanyMap;
				processingCompanyMap = __instance;
				try
				{
					companyRaid.Invoke(null, new object[3] { __instance, value, fffPendingCompanyRaid.Faction });
				}
				finally
				{
					processingCompanyMap = map;
				}
			}
		}
		int num2 = 0;
		while (num2 < component.Incidents.Count)
		{
			FffPendingReadinessIncident fffPendingReadinessIncident = component.Incidents[num2];
			if (fffPendingReadinessIncident.Map == null || fffPendingReadinessIncident.Map.Disposed)
			{
				component.Incidents.RemoveAt(num2);
				continue;
			}
			if (fffPendingReadinessIncident.Map != __instance)
			{
				num2++;
				continue;
			}
			component.Incidents.RemoveAt(num2);
			if (fffPendingReadinessIncident.Incident != null && fffPendingReadinessIncident.Faction != null)
			{
				IncidentParms incidentParms = StorytellerUtility.DefaultParmsNow(fffPendingReadinessIncident.Incident.category, __instance);
				incidentParms.faction = fffPendingReadinessIncident.Faction;
				incidentParms.forced = true;
				incidentParms.points *= fffPendingReadinessIncident.PointsFactor;
				if (fffPendingReadinessIncident.Incident.Worker.CanFireNow(incidentParms))
				{
					fffPendingReadinessIncident.Incident.Worker.TryExecute(incidentParms);
				}
			}
		}
	}

	private static void InstallTurretSerialization()
	{
		Type turret = T("SubTurret");
		MethodInfo method = (AccessTools.TypeByName("Fortified.MultiplayerCompatibility.Multiplayer") ?? throw new TypeLoadException("Fortified.MultiplayerCompatibility.Multiplayer")).GetNestedTypes(AccessTools.all).SelectMany(AccessTools.GetDeclaredMethods).Single((MethodInfo m) =>
		{
			ParameterInfo[] parameters = m.GetParameters();
			return parameters.Length == 2 && parameters[0].ParameterType == typeof(SyncWorker) && parameters[1].ParameterType == turret.MakeByRefType();
		});
		serializedTurrets = AccessTools.DeclaredField(T("CompMultipleTurretGun"), "turrets") ?? throw new MissingFieldException("FFF CompMultipleTurretGun.turrets");
		emptySerializedTurrets = Activator.CreateInstance(serializedTurrets.FieldType);
		Patch(method, null, null, "TurretWorkerTranspiler");
	}

	private static object TurretsForRead(object owner)
	{
		object value;
		if (owner != null)
		{
			value = serializedTurrets.GetValue(owner);
			if (value == null)
			{
				return emptySerializedTurrets;
			}
		}
		else
		{
			value = emptySerializedTurrets;
		}
		return value;
	}

	private static IEnumerable<CodeInstruction> TurretWorkerTranspiler(IEnumerable<CodeInstruction> values)
	{
		int changed = 0;
		foreach (CodeInstruction value in values)
		{
			if (value.LoadsField(serializedTurrets))
			{
				changed++;
				yield return CodeInstruction.Call(typeof(FortifiedFeaturesFramework), "TurretsForRead").WithLabels(value.labels).WithBlocks(value.blocks);
				yield return new CodeInstruction(OpCodes.Castclass, serializedTurrets.FieldType);
			}
			else
			{
				yield return value;
			}
		}
		if (changed != 1)
		{
			throw new InvalidOperationException("FFF turret worker IL changed: " + changed);
		}
	}

	private static void InstallUiRuntime()
	{
		Patch(AccessTools.DeclaredMethod(T("Dialog_OverseerRechargeSettings"), "DoWindowContents"), "ChargeBefore", "ChargeAfter", null, "ChargeFinalizer");
		Patch(AccessTools.DeclaredMethod(T("Dialog_SelectTurretsAmmo"), "DrawSubTurret"), "AmmoBefore", "AmmoAfter", null, "AmmoFinalizer");
		Patch(AccessTools.Constructor(T("Dialog_SelectTurretsAmmo"), new Type[1] { typeof(List<>).MakeGenericType(T("SubTurret")) }), "AmmoDialogBefore");
		Patch(AccessTools.DeclaredMethod(T("SubTurret"), "RemoveWeapon"), "RemoveWeaponBefore");
		Patch(AccessTools.DeclaredMethod(T("CompCamouflage"), "CompGetGizmosExtra"), null, "CompGizmos");
		Patch(AccessTools.DeclaredMethod(T("CompContinuousDamager"), "CompGetGizmosExtra"), null, "CompGizmos");
		Patch(AccessTools.DeclaredMethod(T("CompPaintable"), "CompGetGizmosExtra"), null, "CompGizmos");
		Patch(AccessTools.DeclaredMethod(T("CompOverseer"), "UpdateDummy"), "DummyBefore");
		Patch(AccessTools.DeclaredMethod(T("CompOverseer"), "CompTick"), "DummyTick");
		Type[] types = T("SubTurret").Assembly.GetTypes();
		foreach (Type type in types)
		{
			foreach (MethodInfo declaredMethod in AccessTools.GetDeclaredMethods(type))
			{
				string? fullName = type.FullName;
				if (fullName != null && fullName.StartsWith("Fortified.", StringComparison.Ordinal))
				{
					if (type.DeclaringType == T("Command_SelectApparelOutfit") && declaredMethod.Name.Contains("GetFloatMenu", StringComparison.Ordinal) && declaredMethod.GetParameters().Length == 0 && declaredMethod.ReturnType == typeof(void))
					{
						Patch(declaredMethod, "SelectOutfitBefore");
					}
					if (declaredMethod.Name.Contains("g__SyncTarget|", StringComparison.Ordinal) || declaredMethod.Name.Contains("g__SyncClearTarget|", StringComparison.Ordinal) || declaredMethod.Name.Contains("g__SyncAutoFire|", StringComparison.Ordinal))
					{
						Patch(declaredMethod, "NativeTurretCommandBefore");
					}
					if (declaredMethod.Name.Contains("g__SyncCurrentTurret|", StringComparison.Ordinal))
					{
						Patch(declaredMethod, "CurrentTurretCommandBefore");
					}
					if (type.DeclaringType == T("SubturretGizmo") && declaredMethod.GetParameters().Length == 0 && declaredMethod.ReturnType == typeof(void) && PatchProcessor.GetOriginalInstructions(declaredMethod).Any((CodeInstruction code) => code.StoresField(AccessTools.DeclaredField(T("CompTurretProjectile"), "selectedAmmoDef"))))
					{
						Patch(declaredMethod, "SelectAmmoBefore");
					}
				}
			}
		}
	}

	private static void ChargeBefore(object __instance, out ChargeEdit __state)
	{
		__state = null;
		if (Intercept)
		{
			object obj = Get(__instance, "mech");
			if (obj is Pawn)
			{
				__state = new ChargeEdit
				{
					Mech = obj,
					Min = (float)Get(obj, "MinCharge"),
					Max = (float)Get(obj, "MaxCharge")
				};
			}
		}
	}

	private static void RestoreCharge(ChargeEdit state)
	{
		if (state != null && !state.Restored)
		{
			Set(state.Mech, "MinCharge", state.Min);
			Set(state.Mech, "MaxCharge", state.Max);
			state.Restored = true;
		}
	}

	private static void ChargeAfter(ChargeEdit __state)
	{
		if (__state != null)
		{
			float num = (float)Get(__state.Mech, "MinCharge");
			float num2 = (float)Get(__state.Mech, "MaxCharge");
			RestoreCharge(__state);
			if (num != __state.Min || num2 != __state.Max)
			{
				SetCharge((Pawn)__state.Mech, num, num2);
			}
		}
	}

	private static Exception ChargeFinalizer(Exception __exception, ChargeEdit __state)
	{
		RestoreCharge(__state);
		return __exception;
	}

	private static void AmmoBefore(object __0, out AmmoEdit __state)
	{
		__state = null;
		if (Intercept)
		{
			object obj = Get(__0, "Ammo");
			if (obj != null)
			{
				Dictionary<ThingDef, int> dictionary = (Dictionary<ThingDef, int>)Get(obj, "ammoSettings");
				__state = new AmmoEdit
				{
					Turret = __0,
					Settings = dictionary,
					Before = new Dictionary<ThingDef, int>(dictionary)
				};
			}
		}
	}

	private static void RestoreAmmo(AmmoEdit state)
	{
		if (state == null || state.Restored)
		{
			return;
		}
		state.Settings.Clear();
		foreach (KeyValuePair<ThingDef, int> item in state.Before)
		{
			state.Settings.Add(item.Key, item.Value);
		}
		state.Restored = true;
	}

	private static void AmmoAfter(AmmoEdit __state)
	{
		if (__state == null)
		{
			return;
		}
		(ThingDef, int)[] array = (from d in __state.Settings.Keys.Concat(__state.Before.Keys).Distinct().OrderBy((ThingDef d) => d.defName, StringComparer.Ordinal)
			where __state.Settings.GetValueOrDefault(d) != __state.Before.GetValueOrDefault(d)
			select (Def: d, Count: __state.Settings.GetValueOrDefault(d))).ToArray();
		RestoreAmmo(__state);
		ThingComp thingComp = OwnerOf(__state.Turret);
		if (thingComp != null)
		{
			(ThingDef, int)[] array2 = array;
			for (int num = 0; num < array2.Length; num++)
			{
				(ThingDef, int) tuple = array2[num];
				SetTurretAmmo(thingComp, (string)Get(__state.Turret, "ID"), tuple.Item1, tuple.Item2, selection: false);
			}
		}
	}

	private static Exception AmmoFinalizer(Exception __exception, AmmoEdit __state)
	{
		RestoreAmmo(__state);
		return __exception;
	}

	private static void AmmoDialogBefore(ref object __0)
	{
		if (!MP.IsInMultiplayer || !(__0 is IList list))
		{
			return;
		}
		IList list2 = (IList)Activator.CreateInstance(__0.GetType());
		foreach (object item in list)
		{
			list2.Add(item);
		}
		__0 = list2;
	}

	private static bool RemoveWeaponBefore(object __instance, bool __0)
	{
		if (!Intercept)
		{
			if (MP.IsInMultiplayer && MP.IsExecutingSyncCommand)
			{
				ThingComp thingComp = OwnerOf(__instance);
				if (thingComp != null)
				{
					return CommandAllowed(thingComp.parent);
				}
				return false;
			}
			return true;
		}
		ThingComp thingComp2 = OwnerOf(__instance);
		if (thingComp2 != null)
		{
			TurretAction(thingComp2, (string)Get(__instance, "ID"), 0, LocalTargetInfo.Invalid, __0);
		}
		return false;
	}

	private static bool NativeTurretCommandBefore(object __instance, object[] __args, MethodBase __originalMethod)
	{
		if (!MP.IsInMultiplayer || (!Intercept && !MP.IsExecutingSyncCommand))
		{
			return true;
		}
		object obj = __args.FirstOrDefault((object a) => a != null && T("SubTurret").IsInstanceOfType(a));
		if (obj == null)
		{
			return false;
		}
		ThingComp thingComp = OwnerOf(obj);
		if (thingComp == null)
		{
			return false;
		}
		if (!CommandAllowed(thingComp.parent))
		{
			return false;
		}
		if (!Intercept)
		{
			return true;
		}
		string id = (string)Get(obj, "ID");
		if (__originalMethod.Name.Contains("SyncAutoFire", StringComparison.Ordinal))
		{
			bool value = (bool)Get(__args.FirstOrDefault((object a) => a != null && AccessTools.DeclaredField(a.GetType(), "flag")?.FieldType == typeof(bool)) ?? __instance, "flag");
			TurretAction(thingComp, id, 1, LocalTargetInfo.Invalid, value);
		}
		else if (__originalMethod.Name.Contains("SyncClearTarget", StringComparison.Ordinal))
		{
			TurretAction(thingComp, id, 3, LocalTargetInfo.Invalid, value: false);
		}
		else
		{
			TurretAction(thingComp, id, 2, __args.OfType<LocalTargetInfo>().Single(), value: false);
		}
		return false;
	}

	private static bool CurrentTurretCommandBefore(ThingComp __0, string __1)
	{
		if (MP.IsInMultiplayer)
		{
			if (__0 != null && CommandAllowed(__0.parent))
			{
				return Turret(__0, __1) != null;
			}
			return false;
		}
		return true;
	}

	private static bool SelectAmmoBefore(object __instance)
	{
		if (!Intercept)
		{
			return true;
		}
		object obj = (from f in AccessTools.GetDeclaredFields(__instance.GetType())
			select f.GetValue(__instance)).FirstOrDefault((object v) => v != null && T("SubTurret").IsInstanceOfType(v));
		object closure = __instance;
		if (obj == null)
		{
			foreach (FieldInfo declaredField in AccessTools.GetDeclaredFields(__instance.GetType()))
			{
				object value = declaredField.GetValue(__instance);
				if (value != null && !(value.GetType().DeclaringType != T("SubturretGizmo")))
				{
					obj = (from f in AccessTools.GetDeclaredFields(value.GetType())
						select f.GetValue(value)).FirstOrDefault((object v) => v != null && T("SubTurret").IsInstanceOfType(v));
					if (obj != null)
					{
						break;
					}
				}
			}
		}
		if (obj != null)
		{
			ThingComp thingComp = OwnerOf(obj);
			ThingDef def = (from f in AccessTools.GetDeclaredFields(closure.GetType())
				where f.FieldType == typeof(ThingDef)
				select f.GetValue(closure) as ThingDef).FirstOrDefault();
			if (thingComp != null)
			{
				SetTurretAmmo(thingComp, (string)Get(obj, "ID"), def, 0, selection: true);
			}
		}
		return false;
	}

	private static void CompGizmos(ThingComp __instance, ref IEnumerable<Gizmo> __result)
	{
		__result = WrapGizmos(__instance, __result);
	}

	private static IEnumerable<Gizmo> WrapGizmos(ThingComp comp, IEnumerable<Gizmo> gizmos)
	{
		foreach (Gizmo gizmo in gizmos)
		{
			if (MP.IsInMultiplayer && comp.GetType() == T("CompCamouflage") && gizmo is Command_Toggle command_Toggle)
			{
				command_Toggle.toggleAction = () =>
				{
					ToggleCamouflage(comp);
				};
			}
			else if (MP.IsInMultiplayer && comp.GetType() == T("CompContinuousDamager") && gizmo is Command_Action command_Action)
			{
				command_Action.action = () =>
				{
					ToggleDamage(comp);
				};
			}
			else if (MP.IsInMultiplayer && comp.GetType() == T("CompPaintable") && gizmo is Command_Action command_Action2 && command_Action2.defaultLabel == "FFF_PastePaintConfig".Translate())
			{
				command_Action2.action = () =>
				{
					Type type = comp.GetType();
					PastePaint(comp, (Color)Clip("clipColor1"), (Color)Clip("clipColor2"), (Color)Clip("clipColor3"), (Def)Clip("clipCamoDef"), (float)Clip("clipBrightness"), (Def)Clip("clipOverlayDef"));
					object Clip(string name)
					{
						return AccessTools.Field(type, name).GetValue(null);
					}
				};
			}
			yield return gizmo;
		}
	}

	private static bool SelectOutfitBefore(object __instance)
	{
		if (!Intercept)
		{
			return true;
		}
		object obj = Get(__instance, "<>4__this");
		Pawn_OutfitTracker pawn_OutfitTracker = ((obj == null) ? null : (Get(obj, "outfitSource") as Pawn_OutfitTracker));
		if (pawn_OutfitTracker != null && Get(__instance, "outfit") is ApparelPolicy outfit)
		{
			SelectOutfit(pawn_OutfitTracker.pawn, outfit);
		}
		return false;
	}

	private static bool DummyBefore()
	{
		return !Intercept;
	}

	private static void DummyTick(ThingComp __instance)
	{
		if (MP.IsInMultiplayer && !MP.InInterface && __instance.parent.Faction == Faction.OfPlayerSilentFail && Get(__instance, "dummyPawn") == null)
		{
			Call(__instance, "UpdateDummy");
		}
	}
}
