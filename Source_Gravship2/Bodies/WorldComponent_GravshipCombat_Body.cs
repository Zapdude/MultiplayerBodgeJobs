using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using VanillaGravshipExpanded;
using VanillaGravshipExpanded2;
using Verse;

namespace Multiplayer.Compat.Workshop17.Bodies;

public class WorldComponent_GravshipCombat_Body
{
	public static void ExposeData(WorldComponent_GravshipCombat __instance)
	{
		//IL_019a: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a0: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ce: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d3: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d4: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d6: Invalid comparison between Unknown and I4
		//IL_01d8: Unknown result type (might be due to invalid IL or missing references)
		//IL_01da: Invalid comparison between Unknown and I4
		WorldComponent_GravshipCombat.Instance = __instance;
		((WorldComponent)__instance).ExposeData();
		Scribe_Values.Look<float>(ref __instance.visibility, "visibility", 0f, false);
		Scribe_Values.Look<bool>(ref __instance.warned400k, "warned400k", false, false);
		Scribe_Values.Look<bool>(ref __instance.warned600k, "warned600k", false, false);
		Scribe_Values.Look<bool>(ref __instance.detectionImminent, "detectionImminent", false, false);
		Scribe_Values.Look<int>(ref __instance.detectionTick, "detectionTick", -1, false);
		Scribe_Values.Look<bool>(ref __instance.incomingWarplatform, "incomingWarplatform", false, false);
		Scribe_Values.Look<int>(ref __instance.warplatformTick, "warplatformTick", -1, false);
		Scribe_Defs.Look<GravshipThreatDef>(ref __instance.activeThreatDef, "activeThreatDef");
		Scribe_Values.Look<int>(ref __instance.salvagerTributeAmount, "salvagerTributeAmount", 0, false);
		Scribe_Values.Look<int>(ref __instance.salvagerDelayDays, "salvagerDelayDays", 0, false);
		Scribe_Values.Look<int>(ref __instance.tributeDemandTick, "tributeDemandTick", -1, false);
		Scribe_Values.Look<bool>(ref __instance.engineLockedRemotely, "engineLockedRemotely", false, false);
		Scribe_Values.Look<string>(ref __instance.salvagerStationName, "salvagerStationName", (string)null, false);
		Scribe_Deep.Look<Pawn>(ref __instance.salvagerLeader, "salvagerLeader", Array.Empty<object>());
		Scribe_Values.Look<int>(ref __instance.salvagerDropshipTick, "salvagerDropshipTick", -1, false);
		Scribe_Values.Look<string>(ref __instance.enemyGravshipName, "enemyGravshipName", (string)null, false);
		Scribe_Values.Look<int>(ref __instance.gravjumperLandingTick, "gravjumperLandingTick", -1, false);
		Scribe_Values.Look<string>(ref __instance.gravjumperLandingName, "gravjumperLandingName", (string)null, false);
		Scribe_Values.Look<bool>(ref __instance.gravjumperLandedLocal, "gravjumperLandedLocal", false, false);
		Scribe_Deep.Look<EnemyStructure>(ref __instance.enemyGravjumper, "enemyGravjumper", Array.Empty<object>());
		Scribe_References.Look<Map>(ref __instance.lastKnownShipMap, "lastKnownShipMap", false);
		Scribe_Values.Look<PlanetTile>(ref __instance.lastKnownShipTile, "lastKnownShipTile", default(PlanetTile), false);
		Scribe_References.Look<Thing>(ref __instance.activeEmergencyGravshipGenerator, "activeEmergencyGravshipGenerator", false);
		Scribe_Values.Look<int>(ref __instance.emergencyGravshipGeneratorCooldownTicks, "emergencyGravshipGeneratorCooldownTicks", -1000000, false);
		LoadSaveMode mode = Scribe.mode;
		if (((int)mode != 1 && (int)mode != 4) || 1 == 0)
		{
			return;
		}
		if (!ThingUtility.DestroyedOrNull(__instance.activeEmergencyGravshipGenerator))
		{
			CompPowerEmergencyGravshipGenerator val = ThingCompUtility.TryGetComp<CompPowerEmergencyGravshipGenerator>(__instance.activeEmergencyGravshipGenerator);
			if (val != null && val.IsActive)
			{
				return;
			}
		}
		__instance.activeEmergencyGravshipGenerator = null;
	}

	public static Map GetPlayerTargetMap(WorldComponent_GravshipCombat __instance)
	{
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		Building_GravEngine playerGravEngine = GravEngineTracker.GetPlayerGravEngine();
		if (playerGravEngine != null)
		{
			__instance.lastKnownShipMap = ((Thing)playerGravEngine).Map;
			__instance.lastKnownShipTile = ((Thing)playerGravEngine).Tile;
			return ((Thing)playerGravEngine).Map;
		}
		if (__instance.lastKnownShipMap != null && Find.Maps.Contains(__instance.lastKnownShipMap) && !((WorldObject)__instance.lastKnownShipMap.Parent).Destroyed && !(__instance.lastKnownShipMap.Parent is MapParent_WarPlatform))
		{
			return __instance.lastKnownShipMap;
		}
		if (__instance.lastKnownShipTile.Valid)
		{
			Map val = GenCollection.FirstOrDefault<Map>(Find.Maps, (Predicate<Map>)(m => _003CGetPlayerTargetMap_003Eb__27_2(__instance, m)));
			if (val != null)
			{
				return val;
			}
		}
		return GenCollection.FirstOrDefault<Map>(Find.Maps, (Predicate<Map>)((Map m) => m.mapPawns.AnyFreeColonistSpawned && !(m.Parent is MapParent_WarPlatform) && !m.IsPocketMap)) ?? Find.AnyPlayerHomeMap ?? GenCollection.FirstOrDefault<Map>(Find.Maps, (Predicate<Map>)((Map m) => !(m.Parent is MapParent_WarPlatform) && !m.IsPocketMap));
	}

	public static void WorldComponentTick(WorldComponent_GravshipCombat __instance)
	{
		((WorldComponent)__instance).WorldComponentTick();
		if (__instance.visibility > 0f)
		{
			__instance.RemoveVisibility(0.0448f);
		}
		if (__instance.incomingWarplatform && Find.TickManager.TicksGame >= __instance.warplatformTick && __instance.activeThreatDef != InternalDefOf.VGE_SalvagerStation)
		{
			__instance.SpawnActiveWarplatform();
		}
		if (__instance.tributeDemandTick > 0 && Find.TickManager.TicksGame >= __instance.tributeDemandTick)
		{
			__instance.ShowTributeDemandDialog(__instance.engineLockedRemotely);
			__instance.tributeDemandTick = -1;
		}
		if (__instance.salvagerDropshipTick > 0 && Find.TickManager.TicksGame >= __instance.salvagerDropshipTick)
		{
			__instance.salvagerDropshipTick = -1;
			IncidentParms val = PawnsArrivalModeWorker_SalvagerDropshipRaid.CreateRaidParms((IIncidentTarget)(object)__instance.GetPlayerTargetMap());
			IncidentDefOf.RaidEnemy.Worker.TryExecute(val);
		}
		if (__instance.gravjumperLandingTick > 0 && Find.TickManager.TicksGame >= __instance.gravjumperLandingTick)
		{
			__instance.gravjumperLandingTick = -1;
			__instance.LandGravjumperOnPlayerMap(__instance.GetPlayerTargetMap());
		}
		if (__instance.emergencyGravshipGeneratorCooldownTicks > 0)
		{
			__instance.emergencyGravshipGeneratorCooldownTicks--;
		}
	}

	public static void PayTribute(WorldComponent_GravshipCombat __instance, Map map)
	{
		TradeUtility.LaunchSilver(map, __instance.salvagerTributeAmount);
		__instance.RemoveVisibility(__instance.visibility);
		__instance.incomingWarplatform = false;
		__instance.engineLockedRemotely = false;
		__instance.tributeDemandTick = -1;
		((Thing)__instance.salvagerLeader).Destroy((DestroyMode)0);
		__instance.salvagerLeader = null;
	}

	public static DiaOption GetPayTributeOption(WorldComponent_GravshipCombat __instance, Map map)
	{
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Unknown result type (might be due to invalid IL or missing references)
		//IL_005c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0061: Unknown result type (might be due to invalid IL or missing references)
		//IL_006b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0070: Unknown result type (might be due to invalid IL or missing references)
		//IL_0077: Unknown result type (might be due to invalid IL or missing references)
		//IL_007c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0081: Unknown result type (might be due to invalid IL or missing references)
		//IL_0086: Unknown result type (might be due to invalid IL or missing references)
		//IL_009b: Expected Obj, but got Unknown
		//IL_009b: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ae: Expected Obj, but got Unknown
		//IL_00cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d2: Unknown result type (might be due to invalid IL or missing references)
		WorldComponent_GravshipCombat _instance = __instance;
		int expectedLeaderId = ((Thing)__instance.salvagerLeader).thingIDNumber;
		string text = ColoredText.Colorize(__instance.salvagerLeader.Name.ToStringFull, ColoredText.NameColor);
		string text2 = ColoredText.Colorize(__instance.salvagerTributeAmount.ToString(), ColoredText.CurrencyColor);
		DiaOption val = new DiaOption(((string)(TranslatorFormattedStringExtensions.Translate("VGE_PaySilver", ((NamedArgument)(text2))))))
		{
			link = new DiaNode(TranslatorFormattedStringExtensions.Translate("VGE_TributePaidReply", ((NamedArgument)(text))))
			{
				options = { DiaOption.DefaultOK }
			},
			action = () =>
			{
				ResolveTributeDecision(_instance, 0, expectedLeaderId, map);
			}
		};
		if (!TradeUtility.ColonyHasEnoughSilver(map, __instance.salvagerTributeAmount))
		{
			val.Disable(((string)(TranslatorFormattedStringExtensions.Translate("NeedSilverLaunchable", ((NamedArgument)(__instance.salvagerTributeAmount))))));
		}
		return val;
	}

	public static void ShowTributeDemandDialog(WorldComponent_GravshipCombat __instance, bool postponed)
	{
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0061: Unknown result type (might be due to invalid IL or missing references)
		//IL_009e: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_00af: Unknown result type (might be due to invalid IL or missing references)
		//IL_0075: Unknown result type (might be due to invalid IL or missing references)
		//IL_007b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0081: Unknown result type (might be due to invalid IL or missing references)
		//IL_008c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0091: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bb: Expected Obj, but got Unknown
		//IL_0155: Unknown result type (might be due to invalid IL or missing references)
		//IL_015f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0164: Unknown result type (might be due to invalid IL or missing references)
		//IL_016b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0171: Unknown result type (might be due to invalid IL or missing references)
		//IL_0176: Unknown result type (might be due to invalid IL or missing references)
		//IL_017b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0180: Unknown result type (might be due to invalid IL or missing references)
		//IL_0195: Expected Obj, but got Unknown
		//IL_0195: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ac: Expected Obj, but got Unknown
		//IL_01b6: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c0: Expected Obj, but got Unknown
		//IL_00e8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ed: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0103: Unknown result type (might be due to invalid IL or missing references)
		//IL_010e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0113: Unknown result type (might be due to invalid IL or missing references)
		//IL_0118: Unknown result type (might be due to invalid IL or missing references)
		//IL_011d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0132: Expected Obj, but got Unknown
		//IL_0132: Unknown result type (might be due to invalid IL or missing references)
		//IL_0149: Expected Obj, but got Unknown
		WorldComponent_GravshipCombat _instance = __instance;
		Map map = __instance.GetPlayerTargetMap();
		int expectedLeaderId = ((Thing)__instance.salvagerLeader).thingIDNumber;
		string text = ColoredText.Colorize(__instance.salvagerLeader.Name.ToStringFull, ColoredText.NameColor);
		string text2 = ColoredText.Colorize(__instance.salvagerStationName, ColoredText.FactionColor_Hostile);
		string text3 = ColoredText.Colorize(__instance.salvagerTributeAmount.ToString(), ColoredText.CurrencyColor);
		DiaNode val = new DiaNode(postponed ? TranslatorFormattedStringExtensions.Translate("VGE_PostponedTributeDemandPopup", ((NamedArgument)(text)), ((NamedArgument)(text2)), ((NamedArgument)(text3))) : TranslatorFormattedStringExtensions.Translate("VGE_TributeDemandPopup", ((NamedArgument)(text)), ((NamedArgument)(text2)), ((NamedArgument)(text3)), ((NamedArgument)(__instance.salvagerDelayDays))));
		val.options.Add(__instance.GetPayTributeOption(map));
		if (!postponed)
		{
			val.options.Add(new DiaOption(((string)(TranslatorFormattedStringExtensions.Translate("VGE_PostponeTribute", ((NamedArgument)(__instance.salvagerDelayDays))))))
			{
				link = new DiaNode(TranslatorFormattedStringExtensions.Translate("VGE_PostponeReply", ((NamedArgument)(text)), ((NamedArgument)(__instance.salvagerDelayDays))))
				{
					options = { DiaOption.DefaultOK }
				},
				action = () =>
				{
					ResolveTributeDecision(_instance, 1, expectedLeaderId, map);
				}
			});
		}
		val.options.Add(new DiaOption(((string)(Translator.Translate("VGE_RefuseTribute"))))
		{
			link = new DiaNode(TranslatorFormattedStringExtensions.Translate("VGE_RefuseReply", ((NamedArgument)(text)), ((NamedArgument)(text2))))
			{
				options = { DiaOption.DefaultOK }
			},
			action = () =>
			{
				ResolveTributeDecision(_instance, 2, expectedLeaderId, map);
			}
		});
		Find.WindowStack.Add((Window)new Dialog_NodeTree(val, true, false, (string)null));
	}

	public static void ResolveTributeDecision(WorldComponent_GravshipCombat __instance, int choice, int expectedLeaderId, Map map)
	{
		if (!__instance.incomingWarplatform || __instance.activeThreatDef == null || __instance.salvagerLeader == null || ((Thing)__instance.salvagerLeader).Destroyed || ((Thing)__instance.salvagerLeader).thingIDNumber != expectedLeaderId)
		{
			return;
		}
		switch (choice)
		{
		case 0:
			if (map != null && Find.Maps.Contains(map) && TradeUtility.ColonyHasEnoughSilver(map, __instance.salvagerTributeAmount))
			{
				__instance.PayTribute(map);
			}
			break;
		case 1:
		{
			if (__instance.engineLockedRemotely)
			{
				break;
			}
			__instance.engineLockedRemotely = true;
			Building_GravEngine playerGravEngine = GravEngineTracker.GetPlayerGravEngine();
			int num = __instance.salvagerDelayDays * 60000;
			PatchingUtilities.TimeSnapshot? andSetFromWorld = PatchingUtilities.TimeSnapshot.GetAndSetFromWorld();
			try
			{
				__instance.tributeDemandTick = Find.TickManager.TicksGame + num;
			}
			finally
			{
				andSetFromWorld?.Set();
			}
			if (playerGravEngine == null)
			{
				break;
			}
			PatchingUtilities.TimeSnapshot? timeSnapshot = EngineClock(playerGravEngine);
			try
			{
				int ticksGame = Find.TickManager.TicksGame;
				if (playerGravEngine.cooldownCompleteTick < ticksGame)
				{
					playerGravEngine.cooldownCompleteTick = ticksGame + num;
				}
				break;
			}
			finally
			{
				timeSnapshot?.Set();
			}
		}
		case 2:
			__instance.SpawnActiveWarplatform();
			break;
		}
	}

	public static PatchingUtilities.TimeSnapshot? EngineClock(Building_GravEngine engine)
	{
		for (IThingHolder parentHolder = ((Thing)engine).ParentHolder; parentHolder != null; parentHolder = parentHolder.ParentHolder)
		{
			Pawn val = (Pawn)(object)((parentHolder is Pawn) ? parentHolder : null);
			if (val != null)
			{
				return PatchingUtilities.TimeSnapshot.GetAndSetFromPawn(val);
			}
		}
		Map val2 = ((Thing)engine).MapHeld ?? ((Thing)engine).Map;
		if (val2 == null)
		{
			return PatchingUtilities.TimeSnapshot.GetAndSetFromWorld();
		}
		return PatchingUtilities.TimeSnapshot.GetAndSetFromMap(val2);
	}

	public static void AddVisibility(WorldComponent_GravshipCombat __instance, float baseAmount, bool isLaunch = false, bool applyFactors = true)
	{
		//IL_0065: Unknown result type (might be due to invalid IL or missing references)
		Building_GravEngine playerGravEngine = GravEngineTracker.GetPlayerGravEngine();
		if (playerGravEngine == null)
		{
			return;
		}
		float num = 1f;
		if (applyFactors)
		{
			num = StatExtension.GetStatValue((Thing)(object)playerGravEngine, InternalDefOf.VGE_GravshipVisibilityFactor, true, -1);
			if (isLaunch)
			{
				num += StatExtension.GetStatValue((Thing)(object)playerGravEngine, InternalDefOf.VGE_GravshipLaunchVisibilityOffset, true, -1);
			}
		}
		__instance.visibility += baseAmount * num;
		if (__instance.visibility >= 400000f && !__instance.warned400k)
		{
			__instance.warned400k = true;
			Messages.Message(((string)(Translator.Translate("VGE_Visibility400k"))), MessageTypeDefOf.CautionInput, false);
		}
		if (__instance.visibility >= 600000f && !__instance.warned600k)
		{
			__instance.warned600k = true;
			__instance.Send600kLetter(playerGravEngine);
		}
		if (__instance.visibility >= 800000f && !__instance.detectionImminent)
		{
			__instance.detectionImminent = true;
			__instance.TriggerEncounter();
		}
	}

	public static void RemoveVisibility(WorldComponent_GravshipCombat __instance, float amount)
	{
		__instance.visibility -= amount;
		if (__instance.visibility < 0f)
		{
			__instance.visibility = 0f;
		}
		if (__instance.visibility < 400000f)
		{
			__instance.warned400k = false;
		}
		if (__instance.visibility < 600000f)
		{
			__instance.warned600k = false;
		}
		if (__instance.visibility < 800000f)
		{
			__instance.detectionImminent = false;
		}
	}

	public static void Send600kLetter(WorldComponent_GravshipCombat __instance, Building_GravEngine engine)
	{
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		TaggedString val = TranslatorFormattedStringExtensions.Translate("VGE_Visibility600kDesc", ((NamedArgument)(engine.RenamableLabel)), ((NamedArgument)(__instance.GetArmamentsList())));
		Find.LetterStack.ReceiveLetter(Translator.Translate("VGE_Visibility600k"), val, LetterDefOf.ThreatSmall, (string)null, 0, true);
	}

	public static string GetArmamentsList(WorldComponent_GravshipCombat __instance)
	{
		//IL_00bb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f5: Unknown result type (might be due to invalid IL or missing references)
		Dictionary<string, int> counts = new Dictionary<string, int>();
		List<Thing> list = new List<Thing>();
		if (Current.Game.Gravship != null)
		{
			list.AddRange(Current.Game.Gravship.Things);
		}
		foreach (Map map in Find.Maps)
		{
			list.AddRange((IEnumerable<Thing>)map.listerBuildings.allBuildingsColonist);
		}
		foreach (Thing item in list.Distinct())
		{
			if (((Def)item.def).HasModExtension<VisibilityGainExtension>())
			{
				AddCount(((Def)item.def).label);
			}
			else if (ThingCompUtility.TryGetComp<CompTransporter_Warpod>(item) != null)
			{
				AddCount(((string)(Translator.Translate("VGE_Warpods"))));
			}
		}
		if (counts.Count == 0)
		{
			return ((string)(Translator.Translate("VGE_NoArmamentsPresent")));
		}
		return GenText.ToLineList(counts.Select((KeyValuePair<string, int> kvp) => $"{kvp.Value}x {kvp.Key}"), (string)null, false);
		void AddCount(string label)
		{
			if (counts.ContainsKey(label))
			{
				counts[label]++;
			}
			else
			{
				counts[label] = 1;
			}
		}
	}

	public static void TriggerEncounter(WorldComponent_GravshipCombat __instance)
	{
		Building_GravEngine engine = GravEngineTracker.GetPlayerGravEngine();
		GravshipThreatDef val = default;
		if (GenCollection.TryRandomElementByWeight<GravshipThreatDef>(DefDatabase<GravshipThreatDef>.AllDefsListForReading.Where((GravshipThreatDef x) => engine == null || x.Worker.CanFire(engine)), (Func<GravshipThreatDef, float>)((GravshipThreatDef x) => x.weight), out val))
		{
			__instance.activeThreatDef = val;
			val.Worker.Fire(engine);
		}
	}

	public static void SpawnActiveWarplatform(WorldComponent_GravshipCombat __instance)
	{
		__instance.incomingWarplatform = false;
		__instance.tributeDemandTick = -1;
		__instance.activeThreatDef.Worker.SpawnThreat(false);
	}

	public static void LandGravjumperOnPlayerMap(WorldComponent_GravshipCombat __instance, Map map)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0040: Unknown result type (might be due to invalid IL or missing references)
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		//IL_0055: Unknown result type (might be due to invalid IL or missing references)
		//IL_005e: Expected Obj, but got Unknown
		Building_GravEngine playerGravEngine = GravEngineTracker.GetPlayerGravEngine();
		Rot4 facingRotation = WorldComponent_GravshipCombat.GetFacingRotation(map.Center, (playerGravEngine != null) ? ((Thing)playerGravEngine).Position : map.Center);
		IntVec3 val = WorldComponent_GravshipCombat.FindBestGravjumperLandingSpot(map, __instance.enemyGravjumper);
		LandingStructure_EnemyStructure val2 = (LandingStructure_EnemyStructure)ThingMaker.MakeThing(InternalDefOf.VGE_LandingStructure_EnemyGravjumperCaptured, (ThingDef)null);
		val2.structure = __instance.enemyGravjumper;
		__instance.enemyGravjumper = null;
		GenSpawn.Spawn((Thing)val2, val, map, facingRotation, (WipeMode)0, false, false);
		__instance.gravjumperLandedLocal = true;
	}

	public static CellRect GetEnemyStructureBounds(EnemyStructure structure)
	{
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_005b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0069: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d7: Unknown result type (might be due to invalid IL or missing references)
		//IL_0113: Unknown result type (might be due to invalid IL or missing references)
		//IL_0118: Unknown result type (might be due to invalid IL or missing references)
		//IL_011b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0129: Unknown result type (might be due to invalid IL or missing references)
		//IL_0137: Unknown result type (might be due to invalid IL or missing references)
		//IL_0145: Unknown result type (might be due to invalid IL or missing references)
		//IL_0181: Unknown result type (might be due to invalid IL or missing references)
		//IL_0186: Unknown result type (might be due to invalid IL or missing references)
		//IL_0189: Unknown result type (might be due to invalid IL or missing references)
		//IL_0197: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b3: Unknown result type (might be due to invalid IL or missing references)
		//IL_01dd: Unknown result type (might be due to invalid IL or missing references)
		int num = int.MaxValue;
		int num2 = int.MaxValue;
		int num3 = int.MinValue;
		int num4 = int.MinValue;
		foreach (KeyValuePair<Thing, PositionData> thing in structure.Things)
		{
			IntVec3 position = thing.Value.position;
			num = Math.Min(num, position.x);
			num3 = Math.Max(num3, position.x);
			num2 = Math.Min(num2, position.z);
			num4 = Math.Max(num4, position.z);
		}
		foreach (IntVec3 key in structure.Terrains.Keys)
		{
			num = Math.Min(num, key.x);
			num3 = Math.Max(num3, key.x);
			num2 = Math.Min(num2, key.z);
			num4 = Math.Max(num4, key.z);
		}
		foreach (IntVec3 key2 in structure.Roofs.Keys)
		{
			num = Math.Min(num, key2.x);
			num3 = Math.Max(num3, key2.x);
			num2 = Math.Min(num2, key2.z);
			num4 = Math.Max(num4, key2.z);
		}
		foreach (IntVec3 key3 in structure.Foundations.Keys)
		{
			num = Math.Min(num, key3.x);
			num3 = Math.Max(num3, key3.x);
			num2 = Math.Min(num2, key3.z);
			num4 = Math.Max(num4, key3.z);
		}
		return CellRect.FromLimits(num, num2, num3, num4);
	}

	public static Rot4 GetFacingRotation(IntVec3 from, IntVec3 to)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		IntVec3 val = to - from;
		return Rot4.FromAngleFlat(val.AngleFlat);
	}

	public static IntVec3 FindBestGravjumperLandingSpot(Map map, EnemyStructure structure)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0051: Unknown result type (might be due to invalid IL or missing references)
		//IL_0056: Unknown result type (might be due to invalid IL or missing references)
		//IL_009b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0064: Unknown result type (might be due to invalid IL or missing references)
		//IL_006c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Unknown result type (might be due to invalid IL or missing references)
		//IL_0082: Unknown result type (might be due to invalid IL or missing references)
		CellRect enemyStructureBounds = WorldComponent_GravshipCombat.GetEnemyStructureBounds(structure);
		int num = 10;
		CellRect val = new CellRect(num, num, map.Size.x - num * 2, map.Size.z - num * 2);
		IntVec3 result = map.Center;
		float num2 = float.MinValue;
		for (int i = 0; i < 300; i++)
		{
			IntVec3 randomCell = val.RandomCell;
			CellRect val2 = enemyStructureBounds.MovedBy(randomCell);
			if (!val2.InBounds(map) || !val2.FullyContainedWithin(val))
			{
				continue;
			}
			float num3 = WorldComponent_GravshipCombat.EvaluateLandingSpotScore(val2, map);
			if (num3 > num2)
			{
				num2 = num3;
				result = randomCell;
				if (num3 >= 0f)
				{
					break;
				}
			}
		}
		return result;
	}

	public static float EvaluateLandingSpotScore(CellRect rect, Map map)
	{
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_0058: Unknown result type (might be due to invalid IL or missing references)
		//IL_0069: Unknown result type (might be due to invalid IL or missing references)
		//IL_009f: Unknown result type (might be due to invalid IL or missing references)
		float num = 0f;
		CellRect.Enumerator enumerator = rect.GetEnumerator();
		try
		{
			while (enumerator.MoveNext())
			{
				IntVec3 current = enumerator.Current;
				if (GridsUtility.Fogged(current, map))
				{
					num -= 1000f;
				}
				if (GridsUtility.Roofed(current, map))
				{
					num -= 500f;
				}
				if (((Area)map.areaManager.Home)[current])
				{
					num -= 200f;
				}
				if (!GenGrid.Standable(current, map))
				{
					num -= 500f;
				}
				Building edifice = GridsUtility.GetEdifice(current, map);
				if (edifice != null)
				{
					BuildingProperties building = ((Thing)edifice).def.building;
					num = ((building == null || !building.isNaturalRock) ? (num - 1000f) : (num - 1000f));
				}
				List<Thing> thingList = GridsUtility.GetThingList(current, map);
				for (int i = 0; i < thingList.Count; i++)
				{
					Thing val = thingList[i];
					if (val is Building && (object)val != edifice)
					{
						num -= 1000f;
					}
					if (val is Pawn)
					{
						num -= 30f;
					}
				}
			}
			return num;
		}
		finally
		{
			((IDisposable)enumerator/*cast due to constrained. prefix*/).Dispose();
		}
	}

	public static void CheckLocalGravjumperDefeated(WorldComponent_GravshipCombat __instance, Map map)
	{
		//IL_009d: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a7: Unknown result type (might be due to invalid IL or missing references)
		if (__instance.gravjumperLandedLocal && !GenCollection.Any<Thing>(map.listerThings.ThingsOfDef(InternalDefOf.VGE_LandingStructure_EnemyGravjumper)))
		{
			bool flag = GenCollection.Any<Thing>(map.listerThings.ThingsOfDef(InternalDefOf.VGE_EnemyGravjumperEngine), (Predicate<Thing>)((Thing x) => !x.Destroyed));
			bool flag2 = GenCollection.Any<Thing>(map.listerThings.ThingsInGroup((ThingRequestGroup)10), (Predicate<Thing>)((Thing x) => x.Faction == Faction.OfSalvagers && x is Building_GravshipTurret && !x.Destroyed));
			if (!flag && !flag2)
			{
				__instance.gravjumperLandedLocal = false;
				Find.LetterStack.ReceiveLetter(Translator.Translate("VGE_GravjumperDefeated"), Translator.Translate("VGE_GravjumperDefeatedDesc"), LetterDefOf.PositiveEvent, (string)null, 0, true);
			}
		}
	}

	public static bool _003CGetPlayerTargetMap_003Eb__27_2(WorldComponent_GravshipCombat __instance, Map m)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		if (m.Tile == __instance.lastKnownShipTile)
		{
			return !(m.Parent is MapParent_WarPlatform);
		}
		return false;
	}
}
