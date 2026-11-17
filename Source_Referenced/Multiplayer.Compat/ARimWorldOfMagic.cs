using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using AbilityUser;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using TorannMagic;
using TorannMagic.Golems;
using TorannMagic.ModOptions;
using TorannMagic.TMDefs;
using TorannMagic.Utils;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Multiplayer.Compat;

[MpCompatFor("Torann.ARimworldOfMagic")]
public class ARimWorldOfMagic
{
	private static bool isAutoSaving;

	private static AccessTools.FieldRef<object, JobDriver_PortalDestination> portalDestinationInnerClassThisField;

	private static AccessTools.FieldRef<object, Building_TMPortal> portalBuildingInnerClassThisField;

	[MpCompatSyncField(typeof(TMPawnGolem), "showDormantPosition")]
	protected static ISyncField compGolemShowDormantPosition;

	[MpCompatSyncField(typeof(CompGolem), "followsMaster")]
	protected static ISyncField compGolemFollowsMaster;

	[MpCompatSyncField(typeof(CompGolem), "followsMasterDrafted")]
	protected static ISyncField compGolemFollowsMasterDrafted;

	[MpCompatSyncField(typeof(CompGolem), "checkThreatPath")]
	protected static ISyncField compGolemCheckThreatPath;

	[MpCompatSyncField(typeof(CompGolem), "remainDormantWhenUpgrading")]
	protected static ISyncField compGolemRemainDormantWhenUpgrading;

	[MpCompatSyncField(typeof(CompGolem), "useAbilitiesWhenDormant")]
	protected static ISyncField compGolemUseAbilitiesWhenDormant;

	[MpCompatSyncField(typeof(CompGolem), "threatRange", bufferChanges = true)]
	protected static ISyncField compGolemThreatRange;

	[MpCompatSyncField(typeof(CompGolem), "minEnergyPctForAbilities", bufferChanges = true)]
	protected static ISyncField compGolemMinEnergyPctForAbilities;

	[MpCompatSyncField(typeof(CompGolem), "energyPctShouldRest", bufferChanges = true)]
	protected static ISyncField compGolemEnergyPctShouldRest;

	[MpCompatSyncField(typeof(CompGolem), "energyPctShouldAwaken", bufferChanges = true)]
	protected static ISyncField compGolemEnergyPctShouldAwaken;

	[MpCompatSyncField(typeof(CompGolem), "Upgrades/[]", "enabled")]
	protected static ISyncField golemUpgradeEnabled;

	[MpCompatSyncField(typeof(CompGolem), "Golem/golemDef/golemWorkTypes/[]", "enabled")]
	protected static ISyncField golemWorkTypeEnabled;

	public ARimWorldOfMagic(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
		PatchingUtilities.PatchSystemRand((IEnumerable<string>)new string[19]
		{
			"TorannMagic.Laser_LightningBolt:Explosion", "TorannMagic.Projectile_Icebolt:Explosion", "TorannMagic.Projectile_LightningCloud:Explosion", "TorannMagic.TM_Action:DoAction_ApplySplashDamage", "TorannMagic.Projectile_LightningStorm:Explosion", "TorannMagic.Projectile_Overwhelm:Explosion", "TorannMagic.FlyingObject_Advanced_Icebolt:Explosion", "TorannMagic.Projectile_Snowball:Explosion", "TorannMagic.Projectile_SummonElemental:Impact", "TorannMagic.Projectile_SummonMinion:Impact",
			"TorannMagic.Projectile_SummonPoppi:Impact", "TorannMagic.Weapon.Projectile_FireWand:Explosion", "TorannMagic.Weapon.Projectile_LightningWand:Explosion", "TorannMagic.Weapon.SeerRing_Fire:Explosion", "TorannMagic.Weapon.SeerRing_Lightning:Explosion", "TorannMagic.Projectile_DisablingShot:Impact", "TorannMagic.Projectile_Fireball:Explosion", "TorannMagic.Projectile_Fireclaw:Explosion", "TorannMagic.Verb_ShootDifferentProjectiles:TryCastShot"
		}, false);
		foreach (MethodInfo declaredMethod in AccessTools.GetDeclaredMethods(typeof(TM_MoteMaker)))
		{
			if (!declaredMethod.IsStatic)
			{
				Log.Warning("TM_MoteMaker had a non-static method: " + declaredMethod.Name);
			}
			else
			{
				if (declaredMethod.ReturnType != typeof(void))
				{
					continue;
				}
				bool flag;
				switch (declaredMethod.Name)
				{
				case "ThrowDiseaseMote":
					if (declaredMethod.GetParameters().Length != 6)
					{
						flag = false;
						break;
					}
					goto default;
				case "ThrowArcaneMote":
					if (declaredMethod.GetParameters().Length != 8)
					{
						flag = false;
						break;
					}
					goto default;
				case "ThrowShadowMote":
					if (declaredMethod.GetParameters().Length != 6)
					{
						flag = false;
						break;
					}
					goto default;
				default:
					flag = true;
					break;
				}
				if (flag)
				{
					PatchingUtilities.PatchPushPopRand((MethodBase)declaredMethod, (HarmonyMethod)null);
				}
			}
		}
		MP.RegisterSyncMethod(typeof(TM_Action), "PromoteWanderer");
		MP.RegisterSyncMethod(typeof(TM_Action), "PromoteWayfarer");
		MP.RegisterSyncMethod(typeof(TM_Action), "RemoveSymbiosisCommand");
		MP.RegisterSyncMethodLambda(typeof(Building_60mmMortar), "GetGizmos", 0);
		MpCompat.RegisterLambdaMethod(typeof(Building_TMArcaneForge), "GetGizmos", new int[2] { 0, 1 });
		MP.RegisterSyncMethod(typeof(Building_TMGolemBase), "InterfaceChangeTargetTemperature");
		MpCompat.RegisterLambdaMethod(typeof(Building_TMGolemBase), "GetGizmos", new int[4] { 1, 3, 4, 8 });
		MP.RegisterSyncDelegateLambda(typeof(Building_TMGolemBase), "GetGizmos", 0);
		MpCompat.RegisterLambdaMethod(typeof(TMPawnGolem), "GetGizmos", new int[4] { 0, 1, 3, 4 });
		MP.RegisterSyncDelegateLambda(typeof(GolemUtility), "MasterButton", 1);
		MP.RegisterSyncMethod(typeof(PawnColumnWorker_GolemActive), "SetValue").CancelIfAnyArgNull().TransformArgument(2, Serializer.SimpleReader(() => new PawnTable(PawnTableDefOf.Work, null, 0, 0)));
	}

	private static void LatePatch()
	{
		MpCompatPatchLoader.LoadPatch<ARimWorldOfMagic>();
		PatchingUtilities.PatchSystemRand((IEnumerable<string>)new string[8] { "TorannMagic.Building_TMElementalRift_Defenders:DetermineElementalType", "TorannMagic.Building_TMElementalRift:DetermineElementalType", "TorannMagic.FlyingObject_ValiantCharge:Explosion", "TorannMagic.FlyingObject_Whirlwind:ApplyWhirlwindDamage", "TorannMagic.FlyingObject_DemonFlight:Explosion", "TorannMagic.Projectile_ValiantCharge:Explosion", "TorannMagic.MovingObject:ApplyWhirlwindDamage", "TorannMagic.Verb_Cleave:ApplyCleaveDamage" }, true);
		MpCompat.RegisterLambdaMethod(typeof(CompAbilityUserMagic), "GetGizmoCommands", new int[3] { 3, 5, 7 });
		MpCompat.RegisterLambdaMethod(typeof(CompAbilityUserMight), "GetGizmoCommands", new int[4] { 2, 4, 6, 8 });
		MP.RegisterSyncMethod(typeof(CompAbilityUserMagic), "LevelUp").SetDebugOnly();
		MP.RegisterSyncMethod(typeof(CompAbilityUserMagic), "ResetSkills").SetDebugOnly();
		MP.RegisterSyncMethod(typeof(CompAbilityUserMight), "LevelUp").SetDebugOnly();
		MP.RegisterSyncMethod(typeof(CompAbilityUserMight), "ResetSkills").SetDebugOnly();
		MpCompat.RegisterLambdaDelegate(typeof(JobDriver_PortalDestination), "ChooseWorldTarget", new string[2] { "map", "<>4__this" }, new int[1] { 1 })[0].CancelIfAnyFieldNull().TransformField("<>4__this", Serializer.New((Func<JobDriver_PortalDestination, (Pawn, Building_TMPortal)>)((JobDriver_PortalDestination job) => (Pawn: ((JobDriver)(object)job).pawn, Portal: job.portalBldg)), (Func<(Pawn, Building_TMPortal), JobDriver_PortalDestination>)(((Pawn Pawn, Building_TMPortal Portal) networked) =>
		{
			//IL_0000: Unknown result type (might be due to invalid IL or missing references)
			//IL_0005: Unknown result type (might be due to invalid IL or missing references)
			//IL_0011: Expected Obj, but got Unknown
			//IL_0011: Unknown result type (might be due to invalid IL or missing references)
			//IL_0022: Unknown result type (might be due to invalid IL or missing references)
			//IL_002f: Expected Obj, but got Unknown
			JobDriver_PortalDestination val = new JobDriver_PortalDestination();
			((JobDriver)val).pawn = networked.Pawn;
			val.comp = TM_Calc.GetCompAbilityUserMagic((ThingWithComps)networked.Pawn);
			val.portalBldg = networked.Portal;
			return val;
		}))).TransformField("map", Serializer.New((Map map) => map.uniqueID, (int id) => Find.Maps.Find((Map map) => map.uniqueID == id)));
		portalDestinationInnerClassThisField = AccessTools.FieldRefAccess<JobDriver_PortalDestination>(MpMethodUtil.GetLambda(typeof(JobDriver_PortalDestination), "ChooseWorldTarget", MethodType.Normal, (Type[])null, 1).DeclaringType, "<>4__this");
		portalBuildingInnerClassThisField = AccessTools.FieldRefAccess<Building_TMPortal>(MpMethodUtil.GetLambda(typeof(Building_TMPortal), "GetFloatMenuOptions", MethodType.Normal, (Type[])null, 0).DeclaringType, "<>4__this");
	}

	private static bool ReplacedDirectPathThread(bool threadLocked, FlyingObject_LivingWall instance)
	{
		if (!MP.IsInMultiplayer)
		{
			return threadLocked;
		}
		if (instance.idleFor == 0 && ((Thing)(object)instance).Map == FlyingObject_LivingWall.closestThreat.Map && instance.OccupiedWall != null)
		{
			instance.DirectPath();
		}
		return true;
	}

	private static bool ReplacedDoThreadedActionsThread(bool threadLocked, FlyingObject_LivingWall instance)
	{
		if (!MP.IsInMultiplayer)
		{
			return threadLocked;
		}
		int nextWallUpdate = instance.nextWallUpdate;
		instance.DoThreadedActions();
		if (nextWallUpdate > instance.nextWallUpdate)
		{
			instance.nextWallUpdate *= 6;
		}
		return true;
	}

	[MpCompatTranspiler(typeof(FlyingObject_LivingWall), "Tick", null, MethodType.Normal)]
	private static IEnumerable<CodeInstruction> ReplaceLivingWallMultithreading(IEnumerable<CodeInstruction> instr, MethodBase baseMethod)
	{
		FieldInfo targetField = AccessTools.DeclaredField(typeof(FlyingObject_LivingWall), "threadLocked");
		MethodInfo firstTarget = MpMethodUtil.MethodOf((Delegate)new Func<bool, FlyingObject_LivingWall, bool>(ReplacedDirectPathThread));
		MethodInfo secondTarget = MpMethodUtil.MethodOf((Delegate)new Func<bool, FlyingObject_LivingWall, bool>(ReplacedDoThreadedActionsThread));
		int encounteredFields = 0;
		foreach (CodeInstruction ci in instr)
		{
			yield return ci;
			if (ci.opcode == OpCodes.Ldfld && ci.operand is FieldInfo fieldInfo && fieldInfo == targetField)
			{
				if (encounteredFields < 2)
				{
					yield return new CodeInstruction(OpCodes.Ldarg_0);
					yield return new CodeInstruction(OpCodes.Call, (encounteredFields == 0) ? firstTarget : secondTarget);
				}
				encounteredFields++;
			}
		}
		if (encounteredFields != 2)
		{
			string arg = ((baseMethod.DeclaringType?.Namespace).NullOrEmpty() ? baseMethod.Name : (baseMethod.DeclaringType.Name + ":" + baseMethod.Name));
			Log.Warning($"Tried to replace incorrect number of calls to FlyingObject_LivingWall.threadLocked field (encountered {encounteredFields}, expected {2}) for method {arg}");
		}
	}

	private static void SyncMagicPowerAutoCast(MagicPower magicPower, bool target, Pawn pawn)
	{
		if (!MP.IsInMultiplayer)
		{
			magicPower.AutoCast = target;
		}
		else if (magicPower.interactionTick < Find.TickManager.TicksGame)
		{
			magicPower.interactionTick = Find.TickManager.TicksGame + 5;
			SyncedSetMagicAutoCast(pawn, magicPower.abilityDef, target);
		}
	}

	private static void SyncMightPowerAutoCast(MightPower mightPower, bool target, Pawn pawn)
	{
		if (!MP.IsInMultiplayer)
		{
			mightPower.AutoCast = target;
		}
		else if (mightPower.interactionTick < Find.TickManager.TicksGame)
		{
			mightPower.interactionTick = Find.TickManager.TicksGame + 5;
			SyncedSetMightAutoCast(pawn, mightPower.abilityDef, target);
		}
	}

	[MpCompatSyncMethod(SyncContext.None, cancelIfAnyArgNull = true)]
	private static void SyncedSetMagicAutoCast(Pawn pawn, AbilityDef abilityDef, bool target)
	{
		CompAbilityUserMagic compAbilityUserMagic = TM_Calc.GetCompAbilityUserMagic((ThingWithComps)pawn);
		object obj;
		if (compAbilityUserMagic == null)
		{
			obj = null;
		}
		else
		{
			MagicData magicData = compAbilityUserMagic.MagicData;
			obj = ((magicData == null) ? null : magicData.AllMagicPowers?.Find((MagicPower p) => p.abilityDef == abilityDef));
		}
		MagicPower val = (MagicPower)obj;
		if (val != null)
		{
			val.interactionTick = 0;
			val.AutoCast = target;
		}
	}

	[MpCompatSyncMethod(SyncContext.None, cancelIfAnyArgNull = true)]
	private static void SyncedSetMightAutoCast(Pawn pawn, AbilityDef abilityDef, bool target)
	{
		CompAbilityUserMight compAbilityUserMight = TM_Calc.GetCompAbilityUserMight((ThingWithComps)pawn);
		object obj;
		if (compAbilityUserMight == null)
		{
			obj = null;
		}
		else
		{
			MightData mightData = compAbilityUserMight.MightData;
			obj = ((mightData == null) ? null : mightData.AllMightPowers?.Find((MightPower p) => p.abilityDef == abilityDef));
		}
		MightPower val = (MightPower)obj;
		if (val != null)
		{
			val.interactionTick = 0;
			val.AutoCast = target;
		}
	}

	[MpCompatTranspiler(typeof(TM_Action), "DrawAutoCastForGizmo", null, MethodType.Normal)]
	private static IEnumerable<CodeInstruction> SyncAutocastingSetter(IEnumerable<CodeInstruction> instr)
	{
		MethodInfo magicAutoCastTarget = AccessTools.DeclaredPropertySetter(typeof(MagicPower), "AutoCast");
		MethodInfo mightAutoCastTarget = AccessTools.DeclaredPropertySetter(typeof(MightPower), "AutoCast");
		MethodInfo magicAutoCastReplacement = AccessTools.Method(typeof(ARimWorldOfMagic), "SyncMagicPowerAutoCast");
		MethodInfo mightAutoCastReplacement = AccessTools.Method(typeof(ARimWorldOfMagic), "SyncMightPowerAutoCast");
		FieldInfo pawnAbilityField = AccessTools.Field(typeof(Command_PawnAbility), "pawnAbility");
		MethodInfo pawnGetter = AccessTools.PropertyGetter(typeof(PawnAbility), "Pawn");
		foreach (CodeInstruction ci in instr)
		{
			if (ci.opcode == OpCodes.Callvirt && ci.operand is MethodInfo methodInfo)
			{
				bool flag = false;
				if (methodInfo == magicAutoCastTarget)
				{
					ci.operand = magicAutoCastReplacement;
					flag = true;
				}
				else if (methodInfo == mightAutoCastTarget)
				{
					ci.operand = mightAutoCastReplacement;
					flag = true;
				}
				if (flag)
				{
					yield return new CodeInstruction(OpCodes.Ldarg_0);
					yield return new CodeInstruction(OpCodes.Ldfld, pawnAbilityField);
					yield return new CodeInstruction(OpCodes.Callvirt, pawnGetter);
				}
			}
			yield return ci;
		}
	}

	[MpCompatPrefix(typeof(TM_Action), "PromoteWanderer", null, MethodType.Normal)]
	private static bool ShouldCancelPromoteWanderer(Pawn pawn)
	{
		return ShouldCancelPromotion(pawn, "TM_Gifted");
	}

	[MpCompatPrefix(typeof(TM_Action), "PromoteWayfarer", null, MethodType.Normal)]
	private static bool ShouldCancelPromoteWayfarer(Pawn pawn)
	{
		return ShouldCancelPromotion(pawn, "PhysicalProdigy");
	}

	private static bool ShouldCancelPromotion(Pawn pawn, string requiredTraitDefName)
	{
		if (!MP.IsInMultiplayer || !MP.IsExecutingSyncCommand || pawn?.story?.traits?.allTraits == null)
		{
			return true;
		}
		return pawn.story.traits.allTraits.Any((Trait trait) => trait.def.defName == requiredTraitDefName);
	}

	[MpCompatPrefix(typeof(Building_TMArcaneForge), "GetGizmos", 0, MethodType.Normal)]
	private static bool PreReplicateRecipe(Building_WorkTable __instance)
	{
		if (!MP.IsInMultiplayer || !MP.IsExecutingSyncCommand)
		{
			return true;
		}
		return __instance.BillStack.Bills.All((Bill bill) => bill.recipe.defName != "ArcaneForge_Replication");
	}

	[MpCompatPrefix(typeof(JobDriver_PortalDestination), "ChooseWorldTarget", 1, MethodType.Normal)]
	private static bool PreChoosePortalDestination(object __instance)
	{
		if (!MP.IsInMultiplayer || !MP.IsExecutingSyncCommand)
		{
			return true;
		}
		JobDriver_PortalDestination val = portalDestinationInnerClassThisField(__instance);
		if (val.portalBldg == null || val.portalBldg.isPaired)
		{
			return false;
		}
		Pawn pawn = ((JobDriver)(object)val).pawn;
		CompAbilityUserMagic val2 = ((pawn != null) ? TM_Calc.GetCompAbilityUserMagic((ThingWithComps)pawn) : null);
		if (val2 == null)
		{
			return false;
		}
		if (!val2.spell_FoldReality && !val2.MagicData.MagicPowersA.FirstOrDefault((MagicPower p) => (object)p.abilityDef == TorannMagicDefOf.TM_FoldReality).learned)
		{
			return false;
		}
		Need_Mana mana = val2.Mana;
		if (mana != null)
		{
			return ((Need)(object)mana).CurLevel >= 0.7f;
		}
		return false;
	}

	[MpCompatSyncWorker(typeof(CompAbilityUserMagic))]
	private static void SyncMagicUserComp(SyncWorker sync, ref ThingComp comp)
	{
		if (sync.isWriting)
		{
			sync.Write(comp?.parent);
			return;
		}
		ThingWithComps thingWithComps = sync.Read<ThingWithComps>();
		comp = (ThingComp)(object)((thingWithComps != null) ? TM_Calc.GetCompAbilityUserMagic(thingWithComps) : null);
	}

	[MpCompatSyncWorker(typeof(CompAbilityUserMight))]
	private static void SyncMightUserComp(SyncWorker sync, ref ThingComp comp)
	{
		if (sync.isWriting)
		{
			sync.Write(comp?.parent);
			return;
		}
		ThingWithComps thingWithComps = sync.Read<ThingWithComps>();
		comp = (ThingComp)(object)((thingWithComps != null) ? TM_Calc.GetCompAbilityUserMight(thingWithComps) : null);
	}

	[MpCompatPrefix(typeof(Building_TMPortal), "GetFloatMenuOptions", 0, MethodType.Normal)]
	private static bool PreStartChoosingDestinationJob(Pawn ___myPawn, object __instance)
	{
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Expected Obj, but got Unknown
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Expected Obj, but got Unknown
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		Building_TMPortal val = portalBuildingInnerClassThisField(__instance);
		JobDriver_PortalDestination val2 = new JobDriver_PortalDestination();
		((JobDriver)val2).pawn = ___myPawn;
		val2.portalBldg = val;
		((JobDriver)val2).job = new Job(null, (Thing)(object)val);
		val2.StartChoosingDestination();
		return false;
	}

	[MpCompatSyncMethod(SyncContext.None)]
	private static void SyncedLevelUpMagicSkill(Pawn pawn, AbilityDef abilityDef, int magicPowerSkillIndex, int currentLevel)
	{
		if (pawn == null || magicPowerSkillIndex < 0)
		{
			return;
		}
		CompAbilityUserMagic compAbilityUserMagic = TM_Calc.GetCompAbilityUserMagic((ThingWithComps)pawn);
		object obj;
		if (compAbilityUserMagic == null)
		{
			obj = null;
		}
		else
		{
			MagicData magicData = compAbilityUserMagic.MagicData;
			obj = ((magicData != null) ? magicData.AllMagicPowerSkills : null);
		}
		if (obj == null || compAbilityUserMagic.MagicData.AllMagicPowerSkills.Count <= magicPowerSkillIndex)
		{
			return;
		}
		if (abilityDef != null)
		{
			MagicPower val = compAbilityUserMagic.MagicData.AllMagicPowers.Find((MagicPower p) => p.abilityDef == abilityDef);
			if (val == null || !val.learned)
			{
				return;
			}
		}
		MagicPowerSkill val2 = compAbilityUserMagic.MagicData.AllMagicPowerSkills[magicPowerSkillIndex];
		if (val2.level != currentLevel || val2.level >= val2.levelMax || compAbilityUserMagic.MagicData.MagicAbilityPoints < val2.costToLevel)
		{
			return;
		}
		val2.level++;
		MagicData magicData2 = compAbilityUserMagic.MagicData;
		magicData2.MagicAbilityPoints -= val2.costToLevel;
		if (val2.label == "TM_Cantrips_eff")
		{
			if (val2.level >= 15)
			{
				TM_PawnTracker.ResolveMightComp(TM_Calc.GetCompAbilityUserMight((ThingWithComps)pawn));
			}
		}
		else if (val2.label == "TM_LightSkip_pwr")
		{
			if (val2.level == 1)
			{
				((CompAbilityUser)compAbilityUserMagic).AddPawnAbility((AbilityDef)(object)TorannMagicDefOf.TM_LightSkipMass, true, -1f);
			}
			else if (val2.level == 2)
			{
				((CompAbilityUser)compAbilityUserMagic).AddPawnAbility((AbilityDef)(object)TorannMagicDefOf.TM_LightSkipGlobal, true, -1f);
			}
		}
	}

	private static bool ReplacedLevelUpMagicButton(Rect rect, string label, bool drawBackground, bool doMouseoverSound, bool active, TextAnchor? overrideTextAnchor, CompAbilityUserMagic compMagic, MagicPower power, List<MagicPowerSkill>.Enumerator skillEnumerator)
	{
		bool flag = Widgets.ButtonText(rect, label, drawBackground, doMouseoverSound, active, overrideTextAnchor);
		if (!MP.IsInMultiplayer || !flag || ((CompAbilityUser)compMagic).Pawn.Faction != Faction.OfPlayer)
		{
			return flag;
		}
		Pawn pawn = ((CompAbilityUser)compMagic).Pawn;
		if (power != null)
		{
			Pawn_StoryTracker story = pawn.story;
			if (story != null && story.DisabledWorkTagsBackstoryAndTraits == WorkTags.Violent && power.abilityDef.MainVerb.isViolent)
			{
				Messages.Message("IsIncapableOfViolenceLower".Translate(((ThingComp)(object)compMagic).parent.LabelShort, ((ThingComp)(object)compMagic).parent), MessageTypeDefOf.RejectInput, historical: false);
				return false;
			}
		}
		MagicPowerSkill current = skillEnumerator.Current;
		int num = compMagic.MagicData.AllMagicPowerSkills.IndexOf(current);
		if (num < 0)
		{
			return false;
		}
		SyncedLevelUpMagicSkill(pawn, (power != null) ? power.abilityDef : null, num, current.level);
		return false;
	}

	private static bool ReplacedGlobalLevelUpMagicButton(Rect rect, string label, bool drawBackground, bool doMouseoverSound, bool active, TextAnchor? overrideTextAnchor, CompAbilityUserMagic compMagic, List<MagicPowerSkill>.Enumerator clarityEnumerator, List<MagicPowerSkill>.Enumerator focusEnumerator, List<MagicPowerSkill>.Enumerator spiritEnumerator)
	{
		List<MagicPowerSkill>.Enumerator skillEnumerator;
		if (clarityEnumerator.Current != null)
		{
			skillEnumerator = clarityEnumerator;
		}
		else if (focusEnumerator.Current != null)
		{
			skillEnumerator = focusEnumerator;
		}
		else
		{
			if (spiritEnumerator.Current == null)
			{
				return false;
			}
			skillEnumerator = spiritEnumerator;
		}
		return ReplacedLevelUpMagicButton(rect, label, drawBackground, doMouseoverSound, active, overrideTextAnchor, compMagic, null, skillEnumerator);
	}

	[MpCompatSyncMethod(SyncContext.None, cancelIfAnyArgNull = true)]
	private static void SyncedLevelUpMightSkill(Pawn pawn, int mightPowerSkillIndex, int currentLevel)
	{
		if (mightPowerSkillIndex < 0)
		{
			return;
		}
		CompAbilityUserMight compAbilityUserMight = TM_Calc.GetCompAbilityUserMight((ThingWithComps)pawn);
		object obj;
		if (compAbilityUserMight == null)
		{
			obj = null;
		}
		else
		{
			MightData mightData = compAbilityUserMight.MightData;
			obj = ((mightData != null) ? mightData.AllMightPowerSkills : null);
		}
		if (obj == null || compAbilityUserMight.MightData.AllMightPowerSkills.Count <= mightPowerSkillIndex)
		{
			return;
		}
		MightPowerSkill val = compAbilityUserMight.MightData.AllMightPowerSkills[mightPowerSkillIndex];
		if (val.level == currentLevel && val.level < val.levelMax && compAbilityUserMight.MightData.MightAbilityPoints >= val.costToLevel)
		{
			val.level++;
			MightData mightData2 = compAbilityUserMight.MightData;
			mightData2.MightAbilityPoints -= val.costToLevel;
			if (val.label == "TM_FieldTraining_eff" && val.level >= 15)
			{
				TM_PawnTracker.ResolveMagicComp(TM_Calc.GetCompAbilityUserMagic((ThingWithComps)pawn));
			}
		}
	}

	private static bool ReplacedLevelUpMightButton(Rect rect, string label, bool drawBackground, bool doMouseoverSound, bool active, TextAnchor? overrideTextAnchor, CompAbilityUserMight compMight, MightPower power, List<MightPowerSkill>.Enumerator skillEnumerator)
	{
		bool flag = Widgets.ButtonText(rect, label, drawBackground, doMouseoverSound, active, overrideTextAnchor);
		if (!MP.IsInMultiplayer || !flag || ((CompAbilityUser)compMight).Pawn.Faction != Faction.OfPlayer)
		{
			return flag;
		}
		Pawn pawn = ((CompAbilityUser)compMight).Pawn;
		if (power != null)
		{
			Pawn_StoryTracker story = pawn.story;
			if (story != null && story.DisabledWorkTagsBackstoryAndTraits == WorkTags.Violent && power.abilityDef.MainVerb.isViolent)
			{
				Messages.Message("IsIncapableOfViolenceLower".Translate(((ThingComp)(object)compMight).parent.LabelShort, ((ThingComp)(object)compMight).parent), MessageTypeDefOf.RejectInput, historical: false);
				return false;
			}
		}
		MightPowerSkill current = skillEnumerator.Current;
		int num = compMight.MightData.AllMightPowerSkills.IndexOf(current);
		if (num < 0)
		{
			return false;
		}
		SyncedLevelUpMightSkill(pawn, num, current.level);
		return false;
	}

	private static bool ReplacedGlobalLevelUpMightButton(Rect rect, string label, bool drawBackground, bool doMouseoverSound, bool active, TextAnchor? overrideTextAnchor, CompAbilityUserMight compMight, List<MightPowerSkill>.Enumerator fitnessEnumerator, List<MightPowerSkill>.Enumerator coordinationEnumerator, List<MightPowerSkill>.Enumerator strengthEnumerator, List<MightPowerSkill>.Enumerator enduranceEnumerator)
	{
		List<MightPowerSkill>.Enumerator skillEnumerator;
		if (fitnessEnumerator.Current != null)
		{
			skillEnumerator = fitnessEnumerator;
		}
		else if (coordinationEnumerator.Current != null)
		{
			skillEnumerator = coordinationEnumerator;
		}
		else if (strengthEnumerator.Current != null)
		{
			skillEnumerator = strengthEnumerator;
		}
		else
		{
			if (enduranceEnumerator.Current == null)
			{
				return false;
			}
			skillEnumerator = enduranceEnumerator;
		}
		return ReplacedLevelUpMightButton(rect, label, drawBackground, doMouseoverSound, active, overrideTextAnchor, compMight, null, skillEnumerator);
	}

	[MpCompatTranspiler(typeof(MagicCardUtility), "CustomSkillHandler", null, MethodType.Normal)]
	[MpCompatTranspiler(typeof(MightCardUtility), "CustomSkillHandler", null, MethodType.Normal)]
	private static IEnumerable<CodeInstruction> UniversalReplaceLevelUpPlusButton(IEnumerable<CodeInstruction> instr, MethodBase baseMethod)
	{
		MethodInfo methodInfo = AccessTools.DeclaredMethod(typeof(Widgets), "ButtonText", new Type[6]
		{
			typeof(Rect),
			typeof(string),
			typeof(bool),
			typeof(bool),
			typeof(bool),
			typeof(TextAnchor?)
		});
		MethodInfo methodInfo2;
		if (baseMethod.DeclaringType == typeof(MagicCardUtility))
		{
			methodInfo2 = MpMethodUtil.MethodOf((Delegate)new Func<Rect, string, bool, bool, bool, TextAnchor?, CompAbilityUserMagic, MagicPower, List<MagicPowerSkill>.Enumerator, bool>(ReplacedLevelUpMagicButton));
		}
		else
		{
			if (!(baseMethod.DeclaringType == typeof(MightCardUtility)))
			{
				throw new Exception("Trying to apply transpiler (UniversalReplaceLevelUpPlusButton) for an unsupported type (" + baseMethod.DeclaringType.FullDescription() + ").");
			}
			methodInfo2 = MpMethodUtil.MethodOf((Delegate)new Func<Rect, string, bool, bool, bool, TextAnchor?, CompAbilityUserMight, MightPower, List<MightPowerSkill>.Enumerator, bool>(ReplacedLevelUpMightButton));
		}
		return PatchingUtilities.ReplaceMethod(instr, (MethodBase)methodInfo, (MethodBase)methodInfo2, baseMethod, (Func<CodeInstruction, IEnumerable<CodeInstruction>>)ExtraInstructions, (Func<CodeInstruction, IEnumerable<CodeInstruction>>)null, 1, "+", (string)null, (Func<CodeInstruction, bool>)null, (Func<CodeInstruction, bool>)null);
		static IEnumerable<CodeInstruction> ExtraInstructions(CodeInstruction _)
		{
			return new _003C_003Ez__ReadOnlyArray<CodeInstruction>(new CodeInstruction[3]
			{
				new CodeInstruction(OpCodes.Ldarg_1),
				new CodeInstruction(OpCodes.Ldarg_2),
				new CodeInstruction(OpCodes.Ldloc_0)
			});
		}
	}

	[MpCompatTranspiler(typeof(MagicCardUtility), "DrawLevelBar", null, MethodType.Normal)]
	[MpCompatTranspiler(typeof(MightCardUtility), "DrawLevelBar", null, MethodType.Normal)]
	private static IEnumerable<CodeInstruction> UniversalReplaceGlobalLevelUpPlusButton(IEnumerable<CodeInstruction> instr, MethodBase baseMethod)
	{
		MethodInfo methodInfo = AccessTools.DeclaredMethod(typeof(Widgets), "ButtonText", new Type[6]
		{
			typeof(Rect),
			typeof(string),
			typeof(bool),
			typeof(bool),
			typeof(bool),
			typeof(TextAnchor?)
		});
		MethodInfo methodInfo2;
		int num;
		Func<CodeInstruction, IEnumerable<CodeInstruction>> func;
		if (baseMethod.DeclaringType == typeof(MagicCardUtility))
		{
			methodInfo2 = MpMethodUtil.MethodOf((Delegate)new Func<Rect, string, bool, bool, bool, TextAnchor?, CompAbilityUserMagic, List<MagicPowerSkill>.Enumerator, List<MagicPowerSkill>.Enumerator, List<MagicPowerSkill>.Enumerator, bool>(ReplacedGlobalLevelUpMagicButton));
			num = 3;
			func = (CodeInstruction _) => new _003C_003Ez__ReadOnlyArray<CodeInstruction>(new CodeInstruction[4]
			{
				new CodeInstruction(OpCodes.Ldarg_1),
				new CodeInstruction(OpCodes.Ldloc_S, 19),
				new CodeInstruction(OpCodes.Ldloc_S, 28),
				new CodeInstruction(OpCodes.Ldloc_S, 36)
			});
		}
		else
		{
			if (!(baseMethod.DeclaringType == typeof(MightCardUtility)))
			{
				throw new Exception("Trying to apply transpiler (UniversalReplaceLevelUpPlusButton) for an unsupported type (" + baseMethod.DeclaringType.FullDescription() + ").");
			}
			methodInfo2 = MpMethodUtil.MethodOf((Delegate)new Func<Rect, string, bool, bool, bool, TextAnchor?, CompAbilityUserMight, List<MightPowerSkill>.Enumerator, List<MightPowerSkill>.Enumerator, List<MightPowerSkill>.Enumerator, List<MightPowerSkill>.Enumerator, bool>(ReplacedGlobalLevelUpMightButton));
			num = 4;
			func = (CodeInstruction _) => new _003C_003Ez__ReadOnlyArray<CodeInstruction>(new CodeInstruction[5]
			{
				new CodeInstruction(OpCodes.Ldarg_1),
				new CodeInstruction(OpCodes.Ldloc_S, 22),
				new CodeInstruction(OpCodes.Ldloc_S, 31),
				new CodeInstruction(OpCodes.Ldloc_S, 39),
				new CodeInstruction(OpCodes.Ldloc_S, 47)
			});
		}
		return PatchingUtilities.ReplaceMethod(instr, (MethodBase)methodInfo, (MethodBase)methodInfo2, baseMethod, func, (Func<CodeInstruction, IEnumerable<CodeInstruction>>)null, num, "+", (string)null, (Func<CodeInstruction, bool>)null, (Func<CodeInstruction, bool>)null);
	}

	[MpCompatSyncMethod(SyncContext.None, cancelIfAnyArgNull = true)]
	private static void SyncedLearnMagicSkill(Pawn pawn, TMAbilityDef abilityDef)
	{
		CompAbilityUserMagic compAbilityUserMagic = TM_Calc.GetCompAbilityUserMagic((ThingWithComps)pawn);
		object obj;
		if (compAbilityUserMagic == null)
		{
			obj = null;
		}
		else
		{
			MagicData magicData = compAbilityUserMagic.MagicData;
			obj = ((magicData == null) ? null : magicData.AllMagicPowers?.Find((MagicPower p) => (object)p.abilityDef == abilityDef));
		}
		MagicPower val = (MagicPower)obj;
		if (val == null || val.learned || val.requiresScroll || compAbilityUserMagic.MagicData.MagicAbilityPoints < val.costToLevel)
		{
			return;
		}
		val.learned = true;
		if (abilityDef.shouldInitialize && ((Def)(object)abilityDef).defName != "TM_TechnoBit")
		{
			((CompAbilityUser)compAbilityUserMagic).AddPawnAbility((AbilityDef)(object)abilityDef, true, -1f);
		}
		if (((Def)(object)abilityDef).defName == "TM_TechnoWeapon")
		{
			((CompAbilityUser)compAbilityUserMagic).AddPawnAbility((AbilityDef)(object)TorannMagicDefOf.TM_NanoStimulant, true, -1f);
			compAbilityUserMagic.MagicData.MagicPowersStandalone.FirstOrDefault((MagicPower p) => (object)p.abilityDef == TorannMagicDefOf.TM_NanoStimulant).learned = true;
		}
		List<TMAbilityDef> childAbilities = abilityDef.childAbilities;
		if (childAbilities != null && childAbilities.Count > 0)
		{
			for (int num = 0; num < abilityDef.childAbilities.Count; num++)
			{
				TMAbilityDef val2 = abilityDef.childAbilities[num];
				if (val2.shouldInitialize)
				{
					((CompAbilityUser)compAbilityUserMagic).AddPawnAbility((AbilityDef)(object)val2, true, -1f);
				}
			}
		}
		MagicData magicData2 = compAbilityUserMagic.MagicData;
		magicData2.MagicAbilityPoints -= val.learnCost;
	}

	private static bool ReplacedLearnMagicSkillButton(Rect rect, string label, bool drawBackground, bool doMouseoverSound, bool active, TextAnchor? overrideTextAnchor, CompAbilityUserMagic compMagic, List<MagicPower>.Enumerator powerEnumerator)
	{
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Expected Obj, but got Unknown
		bool flag = Widgets.ButtonText(rect, label, drawBackground, doMouseoverSound, active, overrideTextAnchor);
		if (!MP.IsInMultiplayer || !flag || ((CompAbilityUser)compMagic).Pawn.Faction != Faction.OfPlayer)
		{
			return flag;
		}
		MagicPower current = powerEnumerator.Current;
		if (current != null)
		{
			SyncedLearnMagicSkill(((CompAbilityUser)compMagic).Pawn, (TMAbilityDef)current.abilityDef);
		}
		return false;
	}

	[MpCompatSyncMethod(SyncContext.None, cancelIfAnyArgNull = true)]
	private static void SyncedLevelUpMagicPower(Pawn pawn, AbilityDef abilityDef, int currentLevel)
	{
		CompAbilityUserMagic compAbilityUserMagic = TM_Calc.GetCompAbilityUserMagic((ThingWithComps)pawn);
		object obj;
		if (compAbilityUserMagic == null)
		{
			obj = null;
		}
		else
		{
			MagicData magicData = compAbilityUserMagic.MagicData;
			obj = ((magicData != null) ? magicData.AllMagicPowerSkills : null);
		}
		if (obj != null)
		{
			MagicPower val = compAbilityUserMagic.MagicData.AllMagicPowers.Find((MagicPower p) => p.abilityDef == abilityDef);
			if (val != null && val.learned && val.level == currentLevel && val.level < val.maxLevel && compAbilityUserMagic.MagicData.MagicAbilityPoints >= val.costToLevel)
			{
				compAbilityUserMagic.LevelUpPower(val);
				MagicData magicData2 = compAbilityUserMagic.MagicData;
				magicData2.MagicAbilityPoints -= val.costToLevel;
			}
		}
	}

	private static bool ReplacedLevelUpMagicPowerButton(Rect butRect, Texture2D tex, bool doMouseoverSound, string tooltip, CompAbilityUserMagic compMagic, List<MagicPower>.Enumerator powerEnumerator)
	{
		bool flag = Widgets.ButtonImage(butRect, tex, doMouseoverSound, tooltip);
		if (!MP.IsInMultiplayer || !flag || ((CompAbilityUser)compMagic).Pawn.Faction != Faction.OfPlayer)
		{
			return flag;
		}
		MagicPower current = powerEnumerator.Current;
		if (current != null)
		{
			SyncedLevelUpMagicPower(((CompAbilityUser)compMagic).Pawn, current.abilityDef, current.level);
		}
		return false;
	}

	[MpCompatSyncMethod(SyncContext.None, cancelIfAnyArgNull = true)]
	private static void SyncedLearnMightSkill(Pawn pawn, TMAbilityDef abilityDef)
	{
		CompAbilityUserMight compAbilityUserMight = TM_Calc.GetCompAbilityUserMight((ThingWithComps)pawn);
		object obj;
		if (compAbilityUserMight == null)
		{
			obj = null;
		}
		else
		{
			MightData mightData = compAbilityUserMight.MightData;
			obj = ((mightData == null) ? null : mightData.AllMightPowers?.Find((MightPower p) => (object)p.abilityDef == abilityDef));
		}
		MightPower val = (MightPower)obj;
		if (val != null && !val.learned)
		{
			val.learned = true;
			if (abilityDef == TorannMagicDefOf.TM_PistolSpec)
			{
				((CompAbilityUser)compAbilityUserMight).AddPawnAbility((AbilityDef)(object)TorannMagicDefOf.TM_PistolWhip, true, -1f);
				compAbilityUserMight.skill_PistolWhip = true;
			}
			else if (abilityDef == TorannMagicDefOf.TM_RifleSpec)
			{
				((CompAbilityUser)compAbilityUserMight).AddPawnAbility((AbilityDef)(object)TorannMagicDefOf.TM_SuppressingFire, true, -1f);
				compAbilityUserMight.skill_SuppressingFire = true;
				((CompAbilityUser)compAbilityUserMight).AddPawnAbility((AbilityDef)(object)TorannMagicDefOf.TM_Mk203GL, true, -1f);
				compAbilityUserMight.skill_Mk203GL = true;
			}
			else if (abilityDef == TorannMagicDefOf.TM_ShotgunSpec)
			{
				((CompAbilityUser)compAbilityUserMight).AddPawnAbility((AbilityDef)(object)TorannMagicDefOf.TM_Buckshot, true, -1f);
				compAbilityUserMight.skill_Buckshot = true;
				((CompAbilityUser)compAbilityUserMight).AddPawnAbility((AbilityDef)(object)TorannMagicDefOf.TM_BreachingCharge, true, -1f);
				compAbilityUserMight.skill_BreachingCharge = true;
			}
		}
	}

	private static bool ReplacedLearnMightSkillButton(Rect rect, string label, bool drawBackground, bool doMouseoverSound, bool active, TextAnchor? overrideTextAnchor, CompAbilityUserMight compMight, List<MightPower>.Enumerator powerEnumerator)
	{
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Expected Obj, but got Unknown
		bool flag = Widgets.ButtonText(rect, label, drawBackground, doMouseoverSound, active, overrideTextAnchor);
		if (!MP.IsInMultiplayer || !flag || ((CompAbilityUser)compMight).Pawn.Faction != Faction.OfPlayer)
		{
			return flag;
		}
		MightPower current = powerEnumerator.Current;
		if (current != null)
		{
			SyncedLearnMightSkill(((CompAbilityUser)compMight).Pawn, (TMAbilityDef)current.abilityDef);
		}
		return false;
	}

	[MpCompatSyncMethod(SyncContext.None, cancelIfAnyArgNull = true)]
	private static void SyncedLevelUpMightPower(Pawn pawn, AbilityDef abilityDef, int currentLevel)
	{
		CompAbilityUserMight compAbilityUserMight = TM_Calc.GetCompAbilityUserMight((ThingWithComps)pawn);
		object obj;
		if (compAbilityUserMight == null)
		{
			obj = null;
		}
		else
		{
			MightData mightData = compAbilityUserMight.MightData;
			obj = ((mightData != null) ? mightData.AllMightPowerSkills : null);
		}
		if (obj != null)
		{
			MightPower val = compAbilityUserMight.MightData.AllMightPowers.Find((MightPower p) => p.abilityDef == abilityDef);
			if (val != null && val.learned && val.level == currentLevel && val.level < val.maxLevel && compAbilityUserMight.MightData.MightAbilityPoints >= val.costToLevel)
			{
				compAbilityUserMight.LevelUpPower(val);
				MightData mightData2 = compAbilityUserMight.MightData;
				mightData2.MightAbilityPoints -= val.costToLevel;
			}
		}
	}

	private static bool ReplacedLevelUpMightPowerButton(Rect butRect, Texture2D tex, bool doMouseoverSound, string tooltip, CompAbilityUserMight compMight, List<MightPower>.Enumerator powerEnumerator)
	{
		bool flag = Widgets.ButtonImage(butRect, tex, doMouseoverSound, tooltip);
		if (!MP.IsInMultiplayer || !flag || ((CompAbilityUser)compMight).Pawn.Faction != Faction.OfPlayer)
		{
			return flag;
		}
		MightPower current = powerEnumerator.Current;
		if (current != null)
		{
			SyncedLevelUpMightPower(((CompAbilityUser)compMight).Pawn, current.abilityDef, current.level);
		}
		return false;
	}

	[MpCompatTranspiler(typeof(MagicCardUtility), "CustomPowersHandler", null, MethodType.Normal)]
	[MpCompatTranspiler(typeof(MightCardUtility), "CustomPowersHandler", null, MethodType.Normal)]
	private static IEnumerable<CodeInstruction> ReplaceLearnSkillButton(IEnumerable<CodeInstruction> instr, MethodBase baseMethod)
	{
		MethodInfo methodInfo = AccessTools.DeclaredMethod(typeof(Widgets), "ButtonText", new Type[6]
		{
			typeof(Rect),
			typeof(string),
			typeof(bool),
			typeof(bool),
			typeof(bool),
			typeof(TextAnchor?)
		});
		MethodInfo methodInfo2 = AccessTools.DeclaredMethod(typeof(Widgets), "ButtonImage", new Type[4]
		{
			typeof(Rect),
			typeof(Texture2D),
			typeof(bool),
			typeof(string)
		});
		MethodInfo methodInfo3;
		MethodInfo methodInfo4;
		int enumeratorIndex;
		if (baseMethod.DeclaringType == typeof(MagicCardUtility))
		{
			methodInfo3 = MpMethodUtil.MethodOf((Delegate)new Func<Rect, string, bool, bool, bool, TextAnchor?, CompAbilityUserMagic, List<MagicPower>.Enumerator, bool>(ReplacedLearnMagicSkillButton));
			methodInfo4 = MpMethodUtil.MethodOf((Delegate)new Func<Rect, Texture2D, bool, string, CompAbilityUserMagic, List<MagicPower>.Enumerator, bool>(ReplacedLevelUpMagicPowerButton));
			enumeratorIndex = 4;
		}
		else
		{
			if (!(baseMethod.DeclaringType == typeof(MightCardUtility)))
			{
				throw new Exception("Trying to apply transpiler (ReplaceLearnSkillButton) for an unsupported type (" + baseMethod.DeclaringType.FullDescription() + ").");
			}
			methodInfo3 = MpMethodUtil.MethodOf((Delegate)new Func<Rect, string, bool, bool, bool, TextAnchor?, CompAbilityUserMight, List<MightPower>.Enumerator, bool>(ReplacedLearnMightSkillButton));
			methodInfo4 = MpMethodUtil.MethodOf((Delegate)new Func<Rect, Texture2D, bool, string, CompAbilityUserMight, List<MightPower>.Enumerator, bool>(ReplacedLevelUpMightPowerButton));
			enumeratorIndex = 5;
		}
		return PatchingUtilities.ReplaceMethod(PatchingUtilities.ReplaceMethod(instr, (MethodBase)methodInfo, (MethodBase)methodInfo3, baseMethod, (Func<CodeInstruction, IEnumerable<CodeInstruction>>)ExtraInstructions, (Func<CodeInstruction, IEnumerable<CodeInstruction>>)null, 1, "TM_Learn", "TM_MCU_PointsToLearn", (Func<CodeInstruction, bool>)null, (Func<CodeInstruction, bool>)null), (MethodBase)methodInfo2, (MethodBase)methodInfo4, baseMethod, (Func<CodeInstruction, IEnumerable<CodeInstruction>>)ExtraInstructions, (Func<CodeInstruction, IEnumerable<CodeInstruction>>)null, 1, (string)null, (string)null, (Func<CodeInstruction, bool>)null, (Func<CodeInstruction, bool>)null);
		IEnumerable<CodeInstruction> ExtraInstructions(CodeInstruction _)
		{
			return new _003C_003Ez__ReadOnlyArray<CodeInstruction>(new CodeInstruction[2]
			{
				new CodeInstruction(OpCodes.Ldarg_1),
				new CodeInstruction(OpCodes.Ldloc_S, enumeratorIndex)
			});
		}
	}

	[MpCompatTranspiler(typeof(Verb_EarthSprites), "TryCastShot", null, MethodType.Normal)]
	private static IEnumerable<CodeInstruction> SpriteAreaInsertMapToVerbEarthSprites(IEnumerable<CodeInstruction> instr, MethodBase baseMethod)
	{
		MethodInfo casterPawnGetter = AccessTools.PropertyGetter(typeof(Verb_EarthSprites), "CasterPawn");
		MethodInfo mapGetter = AccessTools.PropertyGetter(typeof(Pawn), "Map");
		return PatchSpriteAreaMethod(instr, ExtraInstructions, baseMethod, 2);
		IEnumerable<CodeInstruction> ExtraInstructions(CodeInstruction ci)
		{
			ci.opcode = OpCodes.Ldarg_0;
			yield return new CodeInstruction(OpCodes.Callvirt, casterPawnGetter);
			yield return new CodeInstruction(OpCodes.Callvirt, mapGetter);
		}
	}

	[MpCompatTranspiler(typeof(CompAbilityUserMagic), "ResolveEarthSpriteAction", null, MethodType.Normal)]
	private static IEnumerable<CodeInstruction> SpriteAreaInsertMapToCompAbilityUserMagic(IEnumerable<CodeInstruction> instr, MethodBase baseMethod)
	{
		FieldInfo earthSpriteMapField = AccessTools.DeclaredField(typeof(CompAbilityUserMagic), "earthSpriteMap");
		return PatchSpriteAreaMethod(instr, ExtraInstructions, baseMethod, 4);
		IEnumerable<CodeInstruction> ExtraInstructions(CodeInstruction ci)
		{
			ci.opcode = OpCodes.Ldarg_0;
			yield return new CodeInstruction(OpCodes.Ldfld, earthSpriteMapField);
		}
	}

	private static IEnumerable<CodeInstruction> PatchSpriteAreaMethod(IEnumerable<CodeInstruction> instr, Func<CodeInstruction, IEnumerable<CodeInstruction>> method, MethodBase baseMethod, int expectedPatches)
	{
		MethodInfo target = MpMethodUtil.MethodOf((Delegate)new _003C_003Ef__AnonymousDelegate0<Map, bool, Area>(TM_Calc.GetSpriteArea));
		int patchedCount = 0;
		CodeInstruction[] instrArr = instr.ToArray();
		for (int i = 0; i < instrArr.Length; i++)
		{
			CodeInstruction ci = instrArr[i];
			yield return ci;
			if (!(ci.opcode == OpCodes.Ldnull) || i + 2 >= instrArr.Length || !instrArr[i + 2].Calls(target))
			{
				continue;
			}
			foreach (CodeInstruction item in method(ci))
			{
				yield return item;
			}
			patchedCount++;
		}
		if (patchedCount != expectedPatches)
		{
			string arg = ((baseMethod.DeclaringType?.Namespace).NullOrEmpty() ? baseMethod.Name : (baseMethod.DeclaringType.Name + ":" + baseMethod.Name));
			Log.Warning($"Patched incorrect number of TM_Calc.GetSpriteArea calls (patched {patchedCount}, expected {expectedPatches}) for method {arg}");
		}
	}

	[MpCompatPrefix(typeof(ITab_GolemPawn), "FillTab", null, MethodType.Normal)]
	[MpCompatPrefix(typeof(ITab_GolemWorkstation), "FillTab", null, MethodType.Normal)]
	private static void PreITabGolemFillTab(ref bool __state)
	{
		if (MP.IsInMultiplayer)
		{
			Thing singleSelectedThing = Find.Selector.SingleSelectedThing;
			object obj = ((singleSelectedThing is TMPawnGolem) ? singleSelectedThing : null);
			if (obj == null)
			{
				Thing obj2 = ((singleSelectedThing is Building_TMGolemBase) ? singleSelectedThing : null);
				obj = ((obj2 != null) ? ((Building_TMGolemBase)obj2).GolemPawn : null);
			}
			TMPawnGolem val = (TMPawnGolem)obj;
			if (val != null)
			{
				MP.WatchBegin();
				__state = true;
				compGolemShowDormantPosition.Watch(val);
				compGolemFollowsMaster.Watch(val.Golem);
				compGolemFollowsMasterDrafted.Watch(val.Golem);
				compGolemCheckThreatPath.Watch(val.Golem);
				compGolemRemainDormantWhenUpgrading.Watch(val.Golem);
				compGolemUseAbilitiesWhenDormant.Watch(val.Golem);
				compGolemThreatRange.Watch(val.Golem);
				compGolemMinEnergyPctForAbilities.Watch(val.Golem);
				compGolemEnergyPctShouldRest.Watch(val.Golem);
				compGolemEnergyPctShouldAwaken.Watch(val.Golem);
			}
		}
	}

	[MpCompatFinalizer(typeof(ITab_GolemPawn), "FillTab", null, MethodType.Normal)]
	[MpCompatFinalizer(typeof(ITab_GolemWorkstation), "FillTab", null, MethodType.Normal)]
	private static void PostITabGolemFillTab(bool __state)
	{
		if (__state)
		{
			MP.WatchEnd();
		}
	}

	[MpCompatSyncMethod(SyncContext.None)]
	private static void SyncedApplyChangesToGolemAbilitiesAndWorkTypes(CompGolem cg, HashSet<TM_GolemUpgradeDef> upgradeDefs)
	{
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0054: Expected Obj, but got Unknown
		if (cg != null)
		{
			((Window)new GolemAbilitiesWindow
			{
				cg = cg,
				upgrades = ((upgradeDefs == null) ? new List<TM_GolemUpgrade>() : cg.Upgrades.Where((TM_GolemUpgrade u) => upgradeDefs.Contains(u.golemUpgradeDef)).ToList())
			}).Close(true);
		}
	}

	[MpCompatPrefix(typeof(GolemAbilitiesWindow), "Close", null, MethodType.Normal)]
	private static bool PreCloseDialog(GolemAbilitiesWindow __instance, bool doCloseSound)
	{
		if (!MP.IsInMultiplayer || !MP.InInterface)
		{
			return true;
		}
		Find.WindowStack.TryRemove((Window)(object)__instance, doCloseSound);
		if (__instance.upgrades != null)
		{
			__instance.upgrades.RemoveAll((TM_GolemUpgrade x) => x == null);
			SyncedApplyChangesToGolemAbilitiesAndWorkTypes(__instance.cg, __instance.upgrades.Select((TM_GolemUpgrade u) => u.golemUpgradeDef).ToHashSet());
		}
		return false;
	}

	[MpCompatPrefix(typeof(GolemAbilitiesWindow), "DoWindowContents", null, MethodType.Normal)]
	private static void PreGolemAbilitiesWindow(GolemAbilitiesWindow __instance)
	{
		if (!MP.IsInMultiplayer)
		{
			return;
		}
		MP.WatchBegin();
		List<TM_GolemUpgrade> upgrades = __instance.cg.Upgrades;
		if (upgrades != null)
		{
			for (int i = 0; i < upgrades.Count; i++)
			{
				golemUpgradeEnabled.Watch(__instance.cg, i);
			}
		}
		List<GolemWorkTypes> list = __instance.cg.Golem.golemDef?.golemWorkTypes;
		if (list != null)
		{
			for (int j = 0; j < list.Count; j++)
			{
				golemWorkTypeEnabled.Watch(__instance.cg, j);
			}
		}
	}

	[MpCompatFinalizer(typeof(GolemAbilitiesWindow), "DoWindowContents", null, MethodType.Normal)]
	private static void PostGolemAbilitiesWindow()
	{
		if (MP.IsInMultiplayer)
		{
			MP.WatchEnd();
		}
	}

	[MpCompatSyncMethod(SyncContext.None, cancelIfAnyArgNull = true)]
	private static void SyncedSetGolemName(CompGolem golem, string targetName)
	{
		TMPawnGolem pawnGolem = golem.PawnGolem;
		Name name = (golem.GolemName = NameTriple.FromString(targetName));
		((Pawn)(object)pawnGolem).Name = name;
	}

	private static bool ReplacedApplyGolemNameButton(Rect rect, string label, bool drawBackground, bool doMouseoverSound, bool active, TextAnchor? overrideTextAnchor, GolemNameWindow window)
	{
		bool flag = Widgets.ButtonText(rect, label, drawBackground, doMouseoverSound, active, overrideTextAnchor);
		if (!MP.IsInMultiplayer || !flag)
		{
			return flag;
		}
		SyncedSetGolemName(window.cg, window.golemName);
		return false;
	}

	[MpCompatTranspiler(typeof(GolemNameWindow), "DoWindowContents", null, MethodType.Normal)]
	private static IEnumerable<CodeInstruction> ReplaceApplyGolemNameButtonTranspiler(IEnumerable<CodeInstruction> instr, MethodBase baseMethod)
	{
		MethodInfo methodInfo = AccessTools.DeclaredMethod(typeof(Widgets), "ButtonText", new Type[6]
		{
			typeof(Rect),
			typeof(string),
			typeof(bool),
			typeof(bool),
			typeof(bool),
			typeof(TextAnchor?)
		});
		MethodInfo methodInfo2 = MpMethodUtil.MethodOf((Delegate)new Func<Rect, string, bool, bool, bool, TextAnchor?, GolemNameWindow, bool>(ReplacedApplyGolemNameButton));
		return PatchingUtilities.ReplaceMethod(instr, (MethodBase)methodInfo, (MethodBase)methodInfo2, baseMethod, (Func<CodeInstruction, IEnumerable<CodeInstruction>>)ExtraInstructions, (Func<CodeInstruction, IEnumerable<CodeInstruction>>)null, 1, "Apply", (string)null, (Func<CodeInstruction, bool>)null, (Func<CodeInstruction, bool>)null);
		static IEnumerable<CodeInstruction> ExtraInstructions(CodeInstruction _)
		{
			return new _003C_003Ez__ReadOnlySingleElementList<CodeInstruction>(new CodeInstruction(OpCodes.Ldarg_0));
		}
	}

	[MpCompatPrefix(typeof(PawnColumnWorker_GolemAwakenPercent), "DoCell", null, MethodType.Normal)]
	private static void PreGolemPawnColumnWorkerAwakenPercentDoCell(Pawn pawn, ref bool __state)
	{
		PreGolemPawnColumnWorkerDoCell(pawn, compGolemEnergyPctShouldRest, ref __state);
	}

	[MpCompatPrefix(typeof(PawnColumnWorker_GolemRestPercent), "DoCell", null, MethodType.Normal)]
	private static void PreGolemPawnColumnWorkerRestPercentDoCell(Pawn pawn, ref bool __state)
	{
		PreGolemPawnColumnWorkerDoCell(pawn, compGolemEnergyPctShouldAwaken, ref __state);
	}

	[MpCompatPrefix(typeof(PawnColumnWorker_GolemThreatRange), "DoCell", null, MethodType.Normal)]
	private static void PreGolemPawnColumnWorkerThreatRangeDoCell(Pawn pawn, ref bool __state)
	{
		PreGolemPawnColumnWorkerDoCell(pawn, compGolemThreatRange, ref __state);
	}

	private static void PreGolemPawnColumnWorkerDoCell(Pawn pawn, ISyncField syncField, ref bool state)
	{
		if (MP.IsInMultiplayer && pawn.Faction == Faction.OfPlayer)
		{
			CompGolem comp = ((ThingWithComps)pawn).GetComp<CompGolem>();
			if (comp != null)
			{
				state = true;
				MP.WatchBegin();
				syncField.Watch(comp);
			}
		}
	}

	[MpCompatFinalizer(typeof(PawnColumnWorker_GolemAwakenPercent), "DoCell", null, MethodType.Normal)]
	[MpCompatFinalizer(typeof(PawnColumnWorker_GolemRestPercent), "DoCell", null, MethodType.Normal)]
	[MpCompatFinalizer(typeof(PawnColumnWorker_GolemThreatRange), "DoCell", null, MethodType.Normal)]
	private static void PostGolemPawnColumnWorkerDoCell(bool __state)
	{
		if (__state)
		{
			MP.WatchEnd();
		}
	}

	[MpCompatPrefix(typeof(PawnColumnWorker_GolemActive), "SetValue", null, MethodType.Normal)]
	private static bool CancelGolemActivationToggleIfStateAlreadyMatches(Pawn pawn, bool value)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		CompGolem comp = ((ThingWithComps)pawn).GetComp<CompGolem>();
		if (comp == null)
		{
			return false;
		}
		if (MP.IsExecutingSyncCommand)
		{
			return pawn.Spawned != value;
		}
		if (pawn.Spawned)
		{
			return !comp.shouldDespawn;
		}
		IThingHolder parentHolder = ((ThingComp)(object)comp).parent.ParentHolder;
		Building_TMGolemBase val = (Building_TMGolemBase)((parentHolder is Building_TMGolemBase) ? parentHolder : null);
		if (val != null)
		{
			return !val.activating;
		}
		return true;
	}

	[MpCompatTranspiler(typeof(PawnColumnWorker_GolemActive), "DoCell", null, MethodType.Normal)]
	private static IEnumerable<CodeInstruction> InsertCurrentStateToGolemActivation(IEnumerable<CodeInstruction> instr, MethodBase baseMethod)
	{
		MethodInfo target = AccessTools.DeclaredMethod(typeof(PawnColumnWorker_Checkbox), "SetValue");
		CodeInstruction lastLdc_I4_1 = null;
		int replacedCount = 0;
		foreach (CodeInstruction ci in instr)
		{
			yield return ci;
			if (ci.opcode == OpCodes.Ldc_I4_1)
			{
				lastLdc_I4_1 = ci;
			}
			if (lastLdc_I4_1 != null && ci.Calls(target))
			{
				lastLdc_I4_1.opcode = OpCodes.Ldloc_S;
				lastLdc_I4_1.operand = 8;
				replacedCount++;
			}
		}
		if (replacedCount != 1)
		{
			string arg = ((baseMethod.DeclaringType?.Namespace).NullOrEmpty() ? baseMethod.Name : (baseMethod.DeclaringType.Name + ":" + baseMethod.Name));
			Log.Warning($"Patched incorrect number of PawnColumnWorker_GolemActive.SetValue calls (patched {replacedCount}, expected {1}) for method {arg}");
		}
	}

	[MpCompatPrefix("Multiplayer.Client.Autosaving", "SaveGameToFile_Overwrite", MethodType.Normal)]
	private static void PreStartHosting()
	{
		isAutoSaving = true;
	}

	[MpCompatFinalizer("Multiplayer.Client.Autosaving", "SaveGameToFile_Overwrite", MethodType.Normal)]
	private static void PostStartHosting()
	{
		isAutoSaving = false;
	}

	[MpCompatPostfix("Multiplayer.Client.Comp.MultiplayerGameComp", "ExposeData", MethodType.Normal)]
	private static void PostMpCompExposeData()
	{
		if (isAutoSaving)
		{
			return;
		}
		Scribe_References.Look(ref FlyingObject_LivingWall.closestThreat, "RoMClosestThreat");
		Scribe_Values.Look(ref Constants.pawnInFlight, "RoMPawnInFlight", defaultValue: false);
		Scribe_Values.Look(ref Constants.lastGrowthMoteTick, "RoMLastGrowthMoteTick", 0);
		Scribe_Values.Look(ref Constants.technoWeaponCount, "RoMTechnoWeaponCount", 0);
		Scribe_Values.Look(ref Constants.bypassPrediction, "RoMBypassPrediction", defaultValue: false);
		Scribe_Deep.Look(ref Constants.undeadApparelPolicy, "RoMUndeadApparelPolicy");
		Scribe_Collections.Look(ref Constants.overdrivePawns, "RoMOverdrivePawns", LookMode.Reference);
		if (Constants.overdrivePawns == null)
		{
			Constants.overdrivePawns = new List<Pawn>();
		}
		Scribe_Values.Look(ref Constants.pistolSpecCount, "RoMPistolSpecCount", 0);
		Scribe_Values.Look(ref Constants.rifleSpecCount, "RoMRifleSpecCount", 0);
		Scribe_Values.Look(ref Constants.shotgunSpecCount, "RoMShotgunSpecCount", 0);
		if (Scribe.mode != LoadSaveMode.PostLoadInit)
		{
			return;
		}
		Constants.growthCells.Clear();
		foreach (TM_GolemUpgradeDef allDef in DefDatabase<TM_GolemUpgradeDef>.AllDefs)
		{
			if (allDef.workstationEffects == null)
			{
				continue;
			}
			foreach (GolemWorkstationEffect workstationEffect in allDef.workstationEffects)
			{
				workstationEffect.target = default;
				workstationEffect.parent = null;
				workstationEffect.parentUpgrade = null;
				workstationEffect.currentLevel = 0f;
				workstationEffect.nextEffectTick = 0;
				workstationEffect.startTick = 0;
			}
		}
	}

	[MpCompatPrefix(typeof(TM_Calc), "FindConnectedWalls", null, MethodType.Normal)]
	private static bool FasterFindConnectedWalls(Building start, float maxAllowedDistance, float maxDistanceFromStart, ref List<Building> __result)
	{
		Map map = start.Map;
		HashSet<Building> hashSet = new HashSet<Building> { start };
		HashSet<Building> hashSet2 = new HashSet<Building> { start };
		HashSet<Building> hashSet3 = new HashSet<Building>();
		float maxDistanceSquared = maxDistanceFromStart * maxDistanceFromStart;
		float num = maxAllowedDistance * maxAllowedDistance;
		List<Building> list = (from b in map.listerBuildings.allBuildingsColonist.Concat(map.listerBuildings.allBuildingsNonColonist)
			where TM_Calc.IsWall((Thing)b) && (float)(b.Position - start.Position).LengthHorizontalSquared <= maxDistanceSquared
			select b).ToList();
		for (int num2 = 0; num2 < 200; num2++)
		{
			hashSet3.Clear();
			foreach (Building item in hashSet2)
			{
				foreach (Building item2 in list)
				{
					if ((float)(item2.Position - item.Position).LengthHorizontalSquared <= num && hashSet.Add(item2))
					{
						hashSet3.Add(item2);
					}
				}
			}
			hashSet2.Clear();
			hashSet2.AddRange(hashSet3);
			if (hashSet2.Count <= 0)
			{
				break;
			}
		}
		__result = hashSet.OrderBy((Building x) => x.thingIDNumber).ToList();
		return false;
	}

	[MpCompatPrefix(typeof(FlyingObject_LivingWall), "FindClosestWallFromTarget", null, MethodType.Normal)]
	private static bool FasterFindClosestWallFromTarget(FlyingObject_LivingWall __instance, ref Thing __result)
	{
		Thing thing = null;
		float num = 998001f;
		foreach (Building item in ((Thing)(object)__instance).Map.listerBuildings.allBuildingsColonist.Concat(((Thing)(object)__instance).Map.listerBuildings.allBuildingsNonColonist).Where((Func<Building, bool>)TM_Calc.IsWall))
		{
			int lengthHorizontalSquared = (item.Position - FlyingObject_LivingWall.closestThreat.Position).LengthHorizontalSquared;
			if ((float)lengthHorizontalSquared < num)
			{
				num = lengthHorizontalSquared;
				thing = item;
			}
		}
		__result = thing;
		return false;
	}

	[MpCompatPrefix(typeof(TM_Calc), "FindNearestWall", null, MethodType.Normal)]
	private static bool FasterFindNearestWall(Map map, IntVec3 center, Faction faction, ref Building __result)
	{
		foreach (Building item in map.listerBuildings.allBuildingsColonist.Concat(map.listerBuildings.allBuildingsNonColonist))
		{
			if (TM_Calc.IsWall((Thing)item) && (float)(item.Position - center).LengthHorizontalSquared <= 1.9599999f)
			{
				if (faction == null)
				{
					__result = item;
					return false;
				}
				if (faction == item.Faction)
				{
					__result = item;
					return false;
				}
			}
		}
		__result = null;
		return false;
	}
}
