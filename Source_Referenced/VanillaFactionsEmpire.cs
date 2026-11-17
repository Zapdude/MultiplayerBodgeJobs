using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using VFEEmpire;
using VFEEmpire.HarmonyPatches;
using Verse;
using Verse.AI.Group;

namespace Multiplayer.Compat;

[MpCompatFor("OskarPotocki.VFE.Empire")]
public class VanillaFactionsEmpire
{
	public VanillaFactionsEmpire(ModContentPack mod)
	{
		MP.RegisterSyncWorker<LordToil_ArtExhibit_Wait>(SyncArtExhibitWait);
		MP.RegisterSyncWorker<Command_ArtExhibit>(SyncArtExhibitCommand);
		VanillaSeriesRegistration17.Append(MpCompat.RegisterLambdaMethod(typeof(LordToil_ArtExhibit_Wait), "ExtraFloatMenuOptions", default(int)));
		MP.RegisterSyncMethod(typeof(Command_ArtExhibit), "ProcessInput").SetVersion(17);
		VanillaSeriesRegistration17.Append(MpCompat.RegisterLambdaDelegate(typeof(LordJob_ArtExhibit), "GetPawnGizmos", 0, 2));
		MP.RegisterSyncWorker<LordToil_GrandBall_Wait>(SyncGrandBallWait);
		MP.RegisterSyncWorker<Command_GrandBall>(SyncGrandBallCommand);
		VanillaSeriesRegistration17.Append(MpCompat.RegisterLambdaMethod(typeof(LordToil_GrandBall_Wait), "ExtraFloatMenuOptions", default(int)));
		MP.RegisterSyncMethod(typeof(Command_GrandBall), "ProcessInput").SetVersion(17);
		VanillaSeriesRegistration17.Append(MpCompat.RegisterLambdaDelegate(typeof(LordJob_GrandBall), "GetPawnGizmos", 0, 2));
		MP.RegisterSyncWorker<LordToil_Parade_Wait>(SyncParadeWait);
		MP.RegisterSyncWorker<Command_Parade>(SyncParadeCommand);
		VanillaSeriesRegistration17.Append(MpCompat.RegisterLambdaMethod(typeof(LordToil_Parade_Wait), "ExtraFloatMenuOptions", default(int)));
		MP.RegisterSyncMethod(typeof(Command_Parade), "ProcessInput").SetVersion(17);
		VanillaSeriesRegistration17.Append(MpCompat.RegisterLambdaDelegate(typeof(LordJob_Parade), "GetPawnGizmos", 0, 2));
		MP.RegisterSyncWorker<MainTabWindow_Royalty>(SyncMainTabWindow_Royalty, null, isImplicit: false, shouldConstruct: true);
		MP.RegisterSyncWorker<RoyaltyTabWorker>(SyncRoyaltyTabWorker, null, isImplicit: true, shouldConstruct: true);
		LongEventHandler.ExecuteWhenFinished(() =>
		{
			MP.RegisterSyncMethod(typeof(RoyaltyTabWorker_Hierarchy), "Notify_Open").SetVersion(17);
			VanillaSeriesRegistration17.Append(MpCompat.RegisterLambdaDelegate(typeof(RoyaltyTabWorker_Hierarchy), "DoMainSection", 1))[0].TransformField("CS$<>8__locals2/CS$<>8__locals1/pawn", Serializer.New<Pawn, int>(WriteRoyalPawn, ReadRoyalPawn));
		});
		MP.RegisterSyncMethod(typeof(HonorUtility), "AddHonor").SetVersion(17);
		MP.RegisterSyncMethod(typeof(HonorUtility), "RemoveHonor").SetVersion(17);
		MP.RegisterSyncMethod(typeof(HonorUtility), "RemoveAllHonors").SetVersion(17);
		MP.RegisterSyncWorker<Honor>(SyncHonor);
		MP.RegisterSyncMethod(typeof(VanillaFactionsEmpire), "SyncedVassalizeSettlement").SetVersion(17);
		VanillaSeriesRegistration17.Append(MpCompat.RegisterLambdaDelegate(typeof(RoyaltyTabWorker_Vassals), "DoVassal", 2, 3, 4));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(RoyaltyTabWorker_Vassals), "DoPotentialVassal"), null, new HarmonyMethod(typeof(VanillaFactionsEmpire), "PostDoPotentialVassal"));
		MP.RegisterSyncWorker<TitheInfo>(SyncTitheInfo);
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(WorldComponent_Vassals), "GetTitheInfo"), new HarmonyMethod(typeof(VanillaFactionsEmpire), "PreGetTitheInfo"), null, null, new HarmonyMethod(typeof(VanillaFactionsEmpire), "PostGetTitheInfo"));
		MP.RegisterSyncMethod(typeof(WorldComponent_Vassals), "ReleaseAllVassalsOf").SetVersion(17);
		MP.RegisterSyncMethod(typeof(VanillaFactionsEmpire), "SyncSlingBeam").SetVersion(17);
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(RoyalTitlePermitWorker_Slicing), "OrderForceTarget"), new HarmonyMethod(typeof(VanillaFactionsEmpire), "PreSlicingBeamOrderForceTarget"));
		MP.RegisterSyncMethod(typeof(RoyalTitlePermitWorker_Call), "OrderForceTarget").SetVersion(17);
		MP.RegisterSyncWorker<RoyalTitlePermitWorker_Call>(SyncCallPermit, null, isImplicit: true);
		VanillaSeriesRegistration17.Append(MpCompat.RegisterLambdaDelegate(typeof(RoyalTitlePermitWorker_CallAbsolver), "GetRoyalAidOptions", default(int)));
		VanillaSeriesRegistration17.Append(MpCompat.RegisterLambdaDelegate(typeof(RoyalTitlePermitWorker_CallTechfriar), "GetRoyalAidOptions", default(int)));
		VanillaSeriesRegistration17.Append(MpCompat.RegisterLambdaMethod(typeof(HonorsTracker), "GetGizmos", 4)).SetDebugOnly();
		MP.RegisterSyncWorker<HonorsTracker>(SyncHonorsTracker);
		PatchingUtilities.PatchCancelInInterface(AccessTools.DeclaredMethod(typeof(ColonistTitleCache.RoyaltyTracker), "Postfix"));
	}

	private static void SyncHonorsTracker(SyncWorker sync, ref HonorsTracker tracker)
	{
		if (sync.isWriting)
		{
			sync.Write(tracker.pawn);
		}
		else
		{
			tracker = HonorUtility.Honors(sync.Read<Pawn>());
		}
	}

	private static void SyncHonor(SyncWorker sync, ref Honor honor)
	{
		if (sync.isWriting)
		{
			sync.Write(honor.Pawn);
			sync.Write(honor.idNumber);
			return;
		}
		Pawn pawn = sync.Read<Pawn>();
		int id = sync.Read<int>();
		IEnumerable<Honor> enumerable = GameComponent_Honors.Instance.honors;
		if (pawn != null)
		{
			enumerable = enumerable.Concat(HonorUtility.Honors(pawn).AllHonors);
		}
		honor = enumerable.FirstOrDefault((Honor val) => val.idNumber == id);
	}

	private static void SyncMainTabWindow_Royalty(SyncWorker sync, ref MainTabWindow_Royalty tab)
	{
		sync.Bind(ref tab.CurCharacter);
		if (sync.isWriting)
		{
			sync.Write(MP.CanUseDevMode && tab.DevMode);
		}
		else
		{
			tab.DevMode = sync.Read<bool>();
		}
	}

	private static void SyncRoyaltyTabWorker(SyncWorker sync, ref RoyaltyTabWorker worker)
	{
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0016: Expected Obj, but got Unknown
		RoyaltyTabWorker val = worker;
		if (val.parent == null)
		{
			val.parent = new MainTabWindow_Royalty();
		}
		sync.Bind(ref worker.parent);
	}

	private static void SyncTitheInfo(SyncWorker sync, ref TitheInfo titheInfo)
	{
		if (sync.isWriting)
		{
			sync.Write(titheInfo.Settlement);
		}
		else
		{
			titheInfo = WorldComponent_Vassals.Instance.GetTitheInfo(sync.Read<Settlement>());
		}
	}

	private static void SyncCallPermit(SyncWorker sync, ref RoyalTitlePermitWorker_Call permitWorker)
	{
		sync.Bind(ref permitWorker.faction);
	}

	private static void SyncArtExhibitWait(SyncWorker sync, ref LordToil_ArtExhibit_Wait toil)
	{
		if (sync.isWriting)
		{
			sync.Write(((LordToil)(object)toil).lord);
			return;
		}
		LordToil lordToil = sync.Read<Lord>()?.curLordToil;
		toil = (LordToil_ArtExhibit_Wait)(object)((lordToil is LordToil_ArtExhibit_Wait) ? lordToil : null);
	}

	private static void SyncArtExhibitCommand(SyncWorker sync, ref Command_ArtExhibit command)
	{
		//IL_0047: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Expected Obj, but got Unknown
		if (sync.isWriting)
		{
			sync.Write(((LordJob)(object)command.job).lord);
			return;
		}
		Lord lord = sync.Read<Lord>();
		if (lord != null)
		{
			LordToil curLordToil = lord.CurLordToil;
			LordToil_ArtExhibit_Wait val = (LordToil_ArtExhibit_Wait)(object)((curLordToil is LordToil_ArtExhibit_Wait) ? curLordToil : null);
			if (val != null)
			{
				command = (Command_ArtExhibit)((LordToil)(object)val).GetPawnGizmos(val.bestNoble).FirstOrDefault();
			}
		}
	}

	private static void SyncGrandBallWait(SyncWorker sync, ref LordToil_GrandBall_Wait toil)
	{
		if (sync.isWriting)
		{
			sync.Write(((LordToil)(object)toil).lord);
			return;
		}
		LordToil lordToil = sync.Read<Lord>()?.curLordToil;
		toil = (LordToil_GrandBall_Wait)(object)((lordToil is LordToil_GrandBall_Wait) ? lordToil : null);
	}

	private static void SyncGrandBallCommand(SyncWorker sync, ref Command_GrandBall command)
	{
		//IL_0047: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Expected Obj, but got Unknown
		if (sync.isWriting)
		{
			sync.Write(((LordJob)(object)command.job).lord);
			return;
		}
		Lord lord = sync.Read<Lord>();
		if (lord != null)
		{
			LordToil curLordToil = lord.CurLordToil;
			LordToil_GrandBall_Wait val = (LordToil_GrandBall_Wait)(object)((curLordToil is LordToil_GrandBall_Wait) ? curLordToil : null);
			if (val != null)
			{
				command = (Command_GrandBall)((LordToil)(object)val).GetPawnGizmos(val.bestNoble).FirstOrDefault();
			}
		}
	}

	private static void SyncParadeWait(SyncWorker sync, ref LordToil_Parade_Wait toil)
	{
		if (sync.isWriting)
		{
			sync.Write(((LordToil)(object)toil).lord);
			return;
		}
		LordToil lordToil = sync.Read<Lord>()?.curLordToil;
		toil = (LordToil_Parade_Wait)(object)((lordToil is LordToil_Parade_Wait) ? lordToil : null);
	}

	private static void SyncParadeCommand(SyncWorker sync, ref Command_Parade command)
	{
		//IL_0047: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Expected Obj, but got Unknown
		if (sync.isWriting)
		{
			sync.Write(((LordJob)(object)command.job).lord);
			return;
		}
		Lord lord = sync.Read<Lord>();
		if (lord != null)
		{
			LordToil curLordToil = lord.CurLordToil;
			LordToil_Parade_Wait val = (LordToil_Parade_Wait)(object)((curLordToil is LordToil_Parade_Wait) ? curLordToil : null);
			if (val != null)
			{
				command = (Command_Parade)((LordToil)(object)val).GetPawnGizmos(val.bestNoble).FirstOrDefault();
			}
		}
	}

	private static int WriteRoyalPawn(Pawn pawn)
	{
		return pawn.thingIDNumber;
	}

	private static Pawn ReadRoyalPawn(int pawnId)
	{
		return WorldComponent_Hierarchy.Instance.TitleHolders?.Find((Pawn pawn) => pawn.thingIDNumber == pawnId);
	}

	private static void PreGetTitheInfo(Settlement settlement, out bool __state)
	{
		__state = false;
		if (MP.IsInMultiplayer)
		{
			Rand.PushState(Gen.HashCombineInt(settlement.ID, settlement.Tile));
			__state = true;
		}
	}

	private static void PostGetTitheInfo(bool __state)
	{
		if (__state)
		{
			Rand.PopState();
		}
	}

	private static void PostDoPotentialVassal(TitheInfo vassal, bool canVassalize)
	{
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Invalid comparison between Unknown and I4
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		if (MP.IsInMultiplayer && canVassalize && vassal.Lord != null)
		{
			Pawn lord = vassal.Lord;
			vassal.Lord = null;
			if ((int)vassal.Setting != 4)
			{
				vassal.Setting = (TitheSetting)3;
			}
			SyncedVassalizeSettlement(lord, vassal.Settlement);
		}
	}

	private static void SyncedVassalizeSettlement(Pawn pawn, Settlement settlement)
	{
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Invalid comparison between Unknown and I4
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		TitheInfo titheInfo = WorldComponent_Vassals.Instance.GetTitheInfo(settlement);
		titheInfo.Lord = pawn;
		titheInfo.DaysSinceDelivery = 0;
		if ((int)titheInfo.Setting != 4)
		{
			titheInfo.Setting = (TitheSetting)0;
		}
	}

	private static bool PreSlicingBeamOrderForceTarget(RoyalTitlePermitWorker_Slicing __instance, LocalTargetInfo target)
	{
		if (!MP.IsInMultiplayer || !__instance.origin.IsValid || MP.IsExecutingSyncCommand)
		{
			return true;
		}
		SyncSlingBeam(__instance, target, __instance.origin, __instance.faction);
		return false;
	}

	private static void SyncSlingBeam(RoyalTitlePermitWorker_Slicing permitWorker, LocalTargetInfo target, LocalTargetInfo origin, Faction faction)
	{
		permitWorker.faction = faction;
		permitWorker.origin = origin;
		((RoyalTitlePermitWorker_Targeted)(object)permitWorker).OrderForceTarget(target);
	}
}
