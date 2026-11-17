using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
		MpCompat.RegisterLambdaMethod(typeof(LordToil_ArtExhibit_Wait), "ExtraFloatMenuOptions", new int[1]);
		MP.RegisterSyncMethod(typeof(Command_ArtExhibit), "ProcessInput");
		MpCompat.RegisterLambdaDelegate(typeof(LordJob_ArtExhibit), "GetPawnGizmos", new int[2] { 0, 2 });
		MP.RegisterSyncWorker<LordToil_GrandBall_Wait>(SyncGrandBallWait);
		MP.RegisterSyncWorker<Command_GrandBall>(SyncGrandBallCommand);
		MpCompat.RegisterLambdaMethod(typeof(LordToil_GrandBall_Wait), "ExtraFloatMenuOptions", new int[1]);
		MP.RegisterSyncMethod(typeof(Command_GrandBall), "ProcessInput");
		MpCompat.RegisterLambdaDelegate(typeof(LordJob_GrandBall), "GetPawnGizmos", new int[2] { 0, 2 });
		MP.RegisterSyncWorker<LordToil_Parade_Wait>(SyncParadeWait);
		MP.RegisterSyncWorker<Command_Parade>(SyncParadeCommand);
		MpCompat.RegisterLambdaMethod(typeof(LordToil_Parade_Wait), "ExtraFloatMenuOptions", new int[1]);
		MP.RegisterSyncMethod(typeof(Command_Parade), "ProcessInput");
		MpCompat.RegisterLambdaDelegate(typeof(LordJob_Parade), "GetPawnGizmos", new int[2] { 0, 2 });
		MP.RegisterSyncWorker<MainTabWindow_Royalty>(SyncMainTabWindow_Royalty, null, isImplicit: false, shouldConstruct: true);
		MP.RegisterSyncWorker<RoyaltyTabWorker>(SyncRoyaltyTabWorker, null, isImplicit: true, shouldConstruct: true);
		LongEventHandler.ExecuteWhenFinished(() =>
		{
			MP.RegisterSyncMethod(typeof(RoyaltyTabWorker_Hierarchy), "Notify_Open");
			MpCompat.RegisterLambdaDelegate(typeof(RoyaltyTabWorker_Hierarchy), "DoMainSection", new int[1] { 1 })[0].TransformField("CS$<>8__locals1/pawn", Serializer.New<Pawn, int>(WriteRoyalPawn, ReadRoyalPawn));
		});
		MP.RegisterSyncMethod(typeof(HonorUtility), "AddHonor");
		MP.RegisterSyncMethod(typeof(HonorUtility), "RemoveHonor");
		MP.RegisterSyncMethod(typeof(HonorUtility), "RemoveAllHonors");
		MP.RegisterSyncWorker<Honor>(SyncHonor);
		MP.RegisterSyncMethod(typeof(VanillaFactionsEmpire), "SyncedVassalizeSettlement");
		MpCompat.RegisterLambdaDelegate(typeof(RoyaltyTabWorker_Vassals), "DoVassal", new int[3] { 2, 3, 4 });
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(RoyaltyTabWorker_Vassals), "DoPotentialVassal"), null, new HarmonyMethod(typeof(VanillaFactionsEmpire), "PostDoPotentialVassal"));
		MP.RegisterSyncWorker<TitheInfo>(SyncTitheInfo);
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(WorldComponent_Vassals), "GetTitheInfo"), new HarmonyMethod(typeof(VanillaFactionsEmpire), "PreGetTitheInfo"), new HarmonyMethod(typeof(VanillaFactionsEmpire), "PostGetTitheInfo"));
		MP.RegisterSyncMethod(typeof(WorldComponent_Vassals), "ReleaseAllVassalsOf");
		MP.RegisterSyncMethod(typeof(VanillaFactionsEmpire), "SyncSlingBeam");
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(RoyalTitlePermitWorker_Slicing), "OrderForceTarget"), new HarmonyMethod(typeof(VanillaFactionsEmpire), "PreSlicingBeamOrderForceTarget"));
		MP.RegisterSyncMethod(typeof(RoyalTitlePermitWorker_Call), "OrderForceTarget");
		MP.RegisterSyncWorker<RoyalTitlePermitWorker_Call>(SyncCallPermit, null, isImplicit: true);
		MpCompat.RegisterLambdaDelegate(typeof(RoyalTitlePermitWorker_CallAbsolver), "GetRoyalAidOptions", new int[1]);
		MpCompat.RegisterLambdaDelegate(typeof(RoyalTitlePermitWorker_CallTechfriar), "GetRoyalAidOptions", new int[1]);
		Extensions.SetDebugOnly((IEnumerable<ISyncMethod>)MpCompat.RegisterLambdaMethod(typeof(HonorsTracker), "GetGizmos", new int[1] { 4 }));
		MP.RegisterSyncWorker<HonorsTracker>(SyncHonorsTracker);
		PatchingUtilities.PatchCancelInInterface(new MethodBase[1] { AccessTools.DeclaredMethod(typeof(ColonistTitleCache.RoyaltyTracker), "Postfix") });
	}

	private static void SyncHonorsTracker(SyncWorker sync, ref HonorsTracker tracker)
	{
		if (sync.isWriting)
		{
			sync.Write(tracker.pawn);
		}
		else
		{
			tracker = sync.Read<Pawn>().Honors();
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
			enumerable = enumerable.Concat(pawn.Honors().AllHonors);
		}
		honor = enumerable.FirstOrDefault((Honor honor2) => honor2.idNumber == id);
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
		RoyaltyTabWorker royaltyTabWorker = worker;
		if (royaltyTabWorker.parent == null)
		{
			royaltyTabWorker.parent = new MainTabWindow_Royalty();
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
			sync.Write(toil.lord);
		}
		else
		{
			toil = sync.Read<Lord>()?.curLordToil as LordToil_ArtExhibit_Wait;
		}
	}

	private static void SyncArtExhibitCommand(SyncWorker sync, ref Command_ArtExhibit command)
	{
		if (sync.isWriting)
		{
			sync.Write(command.job.lord);
			return;
		}
		Lord lord = sync.Read<Lord>();
		if (lord != null && lord.CurLordToil is LordToil_ArtExhibit_Wait lordToil_ArtExhibit_Wait)
		{
			command = (Command_ArtExhibit)lordToil_ArtExhibit_Wait.GetPawnGizmos(lordToil_ArtExhibit_Wait.bestNoble).FirstOrDefault();
		}
	}

	private static void SyncGrandBallWait(SyncWorker sync, ref LordToil_GrandBall_Wait toil)
	{
		if (sync.isWriting)
		{
			sync.Write(toil.lord);
		}
		else
		{
			toil = sync.Read<Lord>()?.curLordToil as LordToil_GrandBall_Wait;
		}
	}

	private static void SyncGrandBallCommand(SyncWorker sync, ref Command_GrandBall command)
	{
		if (sync.isWriting)
		{
			sync.Write(command.job.lord);
			return;
		}
		Lord lord = sync.Read<Lord>();
		if (lord != null && lord.CurLordToil is LordToil_GrandBall_Wait lordToil_GrandBall_Wait)
		{
			command = (Command_GrandBall)lordToil_GrandBall_Wait.GetPawnGizmos(lordToil_GrandBall_Wait.bestNoble).FirstOrDefault();
		}
	}

	private static void SyncParadeWait(SyncWorker sync, ref LordToil_Parade_Wait toil)
	{
		if (sync.isWriting)
		{
			sync.Write(toil.lord);
		}
		else
		{
			toil = sync.Read<Lord>()?.curLordToil as LordToil_Parade_Wait;
		}
	}

	private static void SyncParadeCommand(SyncWorker sync, ref Command_Parade command)
	{
		if (sync.isWriting)
		{
			sync.Write(command.job.lord);
			return;
		}
		Lord lord = sync.Read<Lord>();
		if (lord != null && lord.CurLordToil is LordToil_Parade_Wait lordToil_Parade_Wait)
		{
			command = (Command_Parade)lordToil_Parade_Wait.GetPawnGizmos(lordToil_Parade_Wait.bestNoble).FirstOrDefault();
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

	private static void PreGetTitheInfo(Settlement settlement)
	{
		if (MP.IsInMultiplayer)
		{
			Rand.PushState(Gen.HashCombineInt(settlement.ID, settlement.Tile));
		}
	}

	private static void PostGetTitheInfo()
	{
		if (MP.IsInMultiplayer)
		{
			Rand.PopState();
		}
	}

	private static void PostDoPotentialVassal(TitheInfo vassal, bool canVassalize)
	{
		if (MP.IsInMultiplayer && canVassalize && vassal.Lord != null)
		{
			Pawn lord = vassal.Lord;
			vassal.Lord = null;
			if (vassal.Setting != TitheSetting.Special)
			{
				vassal.Setting = TitheSetting.Never;
			}
			SyncedVassalizeSettlement(lord, vassal.Settlement);
		}
	}

	private static void SyncedVassalizeSettlement(Pawn pawn, Settlement settlement)
	{
		TitheInfo titheInfo = WorldComponent_Vassals.Instance.GetTitheInfo(settlement);
		titheInfo.Lord = pawn;
		titheInfo.DaysSinceDelivery = 0;
		if (titheInfo.Setting != TitheSetting.Special)
		{
			titheInfo.Setting = TitheSetting.EveryWeek;
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
		permitWorker.OrderForceTarget(target);
	}
}
