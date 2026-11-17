using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using AlteredCarbon;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("hlx.UltratechAlteredCarbon")]
public class AlteredCarbon
{
	private static class Window_StackEditor_ResetIndices_Patch
	{
		public static void Prefix(Window_StackEditor __instance)
		{
			__instance.allFactions.AddRange(Find.FactionManager.allFactions.FindAll((Faction faction) => faction.IsPlayer && faction.Hidden && !__instance.allFactions.Contains(faction)));
		}
	}

	private static class NeuralDataRefreshDummyPawn_Patch
	{
		private static bool preDontSync;

		private static bool prevUseLocalId;

		public static void Prefix()
		{
			preDontSync = (bool)DontSync.GetValue(null);
			DontSync.SetValue(null, true);
			prevUseLocalId = (bool)UseLocalIdsOverride.GetValue(null);
			UseLocalIdsOverride.SetValue(null, true);
			IgnoreTraces.SetValue(null, (int)IgnoreTraces.GetValue(null) + 1);
			Rand.PushState();
		}

		public static void Finalizer()
		{
			Rand.PopState();
			IgnoreTraces.SetValue(null, (int)IgnoreTraces.GetValue(null) - 1);
			DontSync.SetValue(null, preDontSync);
			UseLocalIdsOverride.SetValue(null, prevUseLocalId);
		}
	}

	private static class Window_NeuralMatrixManagementDoWindowContents_Patch
	{
		public static void Prefix(Window_NeuralMatrixManagement __instance)
		{
			MP.WatchBegin();
			CompNeuralCache compCache = __instance.matrix.compCache;
			FieldCompNeuralCacheAllowColonistNeuralStacks.Watch(compCache);
			FieldCompNeuralCacheAllowHostileNeuralStacks.Watch(compCache);
			FieldCompNeuralCacheAllowStrangerNeuralStacks.Watch(compCache);
		}

		public static void Finalizer(Window __instance)
		{
			MP.WatchEnd();
		}
	}

	private static class Window_NeuralMatrixManagementDrawStackEntry_Patch
	{
		public static void Prefix(IStackHolder stack)
		{
			FieldNeuralDataTrackedToMatrix.Watch(stack.NeuralData);
		}
	}

	private static class Window_NeuralMatrixManagementDrawBottom_Patch
	{
		public static void Prefix(Window_NeuralMatrixManagement __instance)
		{
			DontSync.SetValue(null, false);
			if (__instance.selectedStack?.ThingHolder is NeuralStack neuralStack)
			{
				Log.Warning($"{neuralStack} {neuralStack.autoLoad}");
				FieldNeuralStackAutoLoad.Watch(neuralStack);
			}
		}

		public static void Finalizer()
		{
			DontSync.SetValue(null, true);
		}
	}

	private static class Building_SleeveGestator_StartGrowth_Patch
	{
		public static void Postfix(Pawn newSleeve)
		{
			HashSet<Pawn> emptySleeves = AlteredCarbonManager.Instance.emptySleeves;
			emptySleeves.RemoveWhere((Pawn p) => p.thingIDNumber < 0);
			emptySleeves.Add(newSleeve);
		}
	}

	private static class Pawn_IdeoTracker_ExposeData_Patch
	{
		public static void Prefix(Pawn_IdeoTracker __instance)
		{
			if (__instance.pawn.story?.Childhood?.defName == "AC_VatGrownChild")
			{
				__instance.ideo = new Ideo();
			}
		}

		public static void Postfix(Pawn_IdeoTracker __instance)
		{
			if (__instance.pawn.story?.Childhood?.defName == "AC_VatGrownChild")
			{
				__instance.ideo = null;
			}
		}
	}

	private static class Pawn_NeedsTracker_ShouldHaveNeed_Patch
	{
		private static HashSet<string> bodyNeeds = new HashSet<string> { "Food", "Bladder", "Hygiene", "DBHThirst" };

		public static bool Prefix(Pawn ___pawn, NeedDef nd, ref bool __result)
		{
			if (___pawn.story?.Childhood?.defName != "AC_VatGrownChild")
			{
				return true;
			}
			if (___pawn.ParentHolder != null && !(___pawn.ParentHolder is Building_SleeveGestator))
			{
				return true;
			}
			if (___pawn.Spawned)
			{
				return true;
			}
			__result = bodyNeeds.Contains(nd.defName);
			return false;
		}
	}

	private static class Window_StackEditor_Draw_Patch
	{
		private static bool preDontSync;

		public static void PreDoWindowContents()
		{
			preDontSync = (bool)DontSync.GetValue(null);
			DontSync.SetValue(null, true);
		}

		public static void PreDrawAcceptCancelButtons(Window_StackEditor __instance)
		{
			DontSync.SetValue(null, false);
			MP.WatchBegin();
			NeuralData neuralData = __instance.thingWithStack.GetNeuralData();
			FieldNeuralDataNeuralDataRewritten.Watch(neuralData);
			FieldNeuralDataEditTime.Watch(neuralData);
			FieldNeuralDataStackDegradationToAdd.Watch(neuralData);
		}

		public static void FinalizerDrawAcceptCancelButtons()
		{
			MP.WatchEnd();
			DontSync.SetValue(null, true);
		}

		public static void FinalizerDoWindowContents()
		{
			DontSync.SetValue(null, preDontSync);
		}
	}

	private static class Window_NeuralMatrixManagementDrawRightPanel_Patch
	{
		private static bool preDontSync;

		public static void Prefix()
		{
			preDontSync = (bool)DontSync.GetValue(null);
			DontSync.SetValue(null, true);
		}

		public static void Finalizer()
		{
			DontSync.SetValue(null, preDontSync);
		}
	}

	private static class Window_SleeveCustomizationCreateSleeve_Patch
	{
		private static bool preDontSync;

		public static void Prefix()
		{
			preDontSync = (bool)DontSync.GetValue(null);
			DontSync.SetValue(null, true);
			Rand.PushState();
		}

		public static void Finalizer()
		{
			Rand.PopState();
			DontSync.SetValue(null, preDontSync);
		}
	}

	private static FieldInfo DontSync;

	private static FieldInfo UseLocalIdsOverride;

	private static FieldInfo IgnoreTraces;

	private static ISyncField FieldCompNeuralCacheAllowColonistNeuralStacks;

	private static ISyncField FieldCompNeuralCacheAllowHostileNeuralStacks;

	private static ISyncField FieldCompNeuralCacheAllowStrangerNeuralStacks;

	private static ISyncField FieldNeuralDataNeuralDataRewritten;

	private static ISyncField FieldNeuralDataEditTime;

	private static ISyncField FieldNeuralDataStackDegradationToAdd;

	private static ISyncField FieldNeuralDataTrackedToMatrix;

	private static ISyncField FieldNeuralStackAutoLoad;

	private static SyncType SyncTypeNeuralData;

	public AlteredCarbon(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
	}

	private static void LatePatch()
	{
		MP.RegisterSyncMethod(typeof(CompCastingRelay), "TuneTo");
		MpCompat.RegisterLambdaMethod(typeof(CompNeuralCache), "CompGetGizmosExtra", new int[1]);
		Extensions.SetDebugOnly((IEnumerable<ISyncMethod>)MpCompat.RegisterLambdaMethod(typeof(CompNeuralCache), "CompGetGizmosExtra", new int[1] { 1 }));
		MpCompat.RegisterLambdaMethod(typeof(Building_NeuralConnector), "GetGizmos", new int[3] { 3, 4, 11 });
		MpCompat.RegisterLambdaDelegate(typeof(Building_NeuralConnector), "GetGizmos", new int[2] { 6, 10 });
		Extensions.SetDebugOnly((IEnumerable<ISyncMethod>)MpCompat.RegisterLambdaMethod(typeof(Building_NeuralConnector), "GetGizmos", new int[2] { 0, 12 }));
		MpCompat.RegisterLambdaDelegate(typeof(Building_NeuralConnector), "GetFloatMenuOptions", new int[1]);
		MpCompat.harmony.Patch(AccessTools.Method(typeof(Building_SleeveCasket), "GetInspectString"), null, null, new HarmonyMethod(typeof(AlteredCarbon), "TranspileBuilding_SleeveCasketGetInspectString"));
		MP.RegisterSyncMethod(typeof(Building_SleeveGestator), "StartGrowth").ExposeParameter(0);
		MP.RegisterSyncMethod(typeof(Building_SleeveGestator), "FinishGrowth").SetDebugOnly();
		MP.RegisterSyncMethod(typeof(Building_SleeveGestator), "AddGrowth").SetDebugOnly();
		MP.RegisterSyncMethod(typeof(Building_SleeveGestator), "OrderToCancel");
		MpCompat.RegisterLambdaMethod(typeof(Building_SleeveGestator), "RepurposeCorpse", new int[1]);
		MP.RegisterSyncMethod(typeof(Building_NeuralEditor), "PostInstall");
		MpCompat.harmony.Patch(AccessTools.Method(typeof(Window_SleeveCustomization), "CreateSleeve"), new HarmonyMethod(typeof(Window_SleeveCustomizationCreateSleeve_Patch), "Prefix"), null, null, new HarmonyMethod(typeof(Window_SleeveCustomizationCreateSleeve_Patch), "Finalizer"));
		DontSync = AccessTools.Field(AccessTools.TypeByName("Multiplayer.Client.Multiplayer"), "dontSync");
		UseLocalIdsOverride = AccessTools.Field(AccessTools.TypeByName("Multiplayer.Client.Patches.UniqueIdsPatch"), "useLocalIdsOverride");
		IgnoreTraces = AccessTools.Field(AccessTools.TypeByName("Multiplayer.Client.Desyncs.DeferredStackTracing"), "ignoreTraces");
		MpCompat.harmony.Patch(AccessTools.Method(typeof(Pawn_NeedsTracker), "ShouldHaveNeed"), new HarmonyMethod(typeof(Pawn_NeedsTracker_ShouldHaveNeed_Patch), "Prefix"));
		MpCompat.harmony.Patch(AccessTools.Method(typeof(Pawn_IdeoTracker), "ExposeData"), new HarmonyMethod(typeof(Pawn_IdeoTracker_ExposeData_Patch), "Prefix"), new HarmonyMethod(typeof(Pawn_IdeoTracker_ExposeData_Patch), "Postfix"));
		MpCompat.RegisterLambdaDelegate(typeof(Window_NeuralMatrixManagement), "DrawStackEntry", new int[2] { 2, 3 });
		MP.RegisterSyncMethod(typeof(Hediff_NeuralStack), "NeedlecastTo");
		MP.RegisterSyncMethod(typeof(NeuralStack), "NeedlecastTo");
		MP.RegisterSyncMethod(typeof(Hediff_RemoteStack), "EndNeedlecasting");
		MP.RegisterSyncMethod(typeof(NeuralStack), "InstallStackRecipe");
		MpCompat.RegisterLambdaDelegate(typeof(NeuralStack), "GetGizmos", new int[1] { 10 });
		FieldCompNeuralCacheAllowColonistNeuralStacks = MP.RegisterSyncField(typeof(CompNeuralCache), "allowColonistNeuralStacks");
		FieldCompNeuralCacheAllowHostileNeuralStacks = MP.RegisterSyncField(typeof(CompNeuralCache), "allowHostileNeuralStacks");
		FieldCompNeuralCacheAllowStrangerNeuralStacks = MP.RegisterSyncField(typeof(CompNeuralCache), "allowStrangerNeuralStacks");
		MP.RegisterSyncWorker<CompNeuralCache>(SyncCompNeuralCache);
		MP.RegisterSyncWorker<NeuralData>(SyncNeuralData);
		MP.RegisterSyncWorker<IStackHolder>(SyncIStackHolder);
		MP.RegisterSyncWorker<INeedlecastable>(SyncINeedlecastable);
		MP.RegisterSyncWorker<StackInstallInfo>(SyncStackInstallInfo);
		SyncType syncTypeNeuralData = new SyncType(typeof(NeuralData));
		syncTypeNeuralData.expose = true;
		SyncTypeNeuralData = syncTypeNeuralData;
		MP.RegisterSyncWorker<Window_NeuralMatrixManagement>(SyncWindow_NeuralMatrixManagement);
		MpCompat.harmony.Patch(AccessTools.Method(typeof(Window_NeuralMatrixManagement), "DoWindowContents"), new HarmonyMethod(typeof(Window_NeuralMatrixManagementDoWindowContents_Patch), "Prefix"), null, null, new HarmonyMethod(typeof(Window_NeuralMatrixManagementDoWindowContents_Patch), "Finalizer"));
		MpCompat.harmony.Patch(AccessTools.Method(typeof(Window_NeuralMatrixManagement), "DrawRightPanel"), new HarmonyMethod(typeof(Window_NeuralMatrixManagementDrawRightPanel_Patch), "Prefix"), new HarmonyMethod(typeof(Window_NeuralMatrixManagementDrawRightPanel_Patch), "Finalizer"));
		MpCompat.harmony.Patch(AccessTools.Method(typeof(Window_NeuralMatrixManagement), "DrawStackEntry"), new HarmonyMethod(typeof(Window_NeuralMatrixManagementDrawStackEntry_Patch), "Prefix"));
		MpCompat.harmony.Patch(AccessTools.Method(typeof(Window_NeuralMatrixManagement), "DrawBottom"), new HarmonyMethod(typeof(Window_NeuralMatrixManagementDrawBottom_Patch), "Prefix"), null, null, new HarmonyMethod(typeof(Window_NeuralMatrixManagementDrawBottom_Patch), "Finalizer"));
		MpCompat.harmony.Patch(AccessTools.Method(typeof(Window_StackEditor), "ResetIndices"), new HarmonyMethod(typeof(Window_StackEditor_ResetIndices_Patch), "Prefix"));
		MpCompat.harmony.Patch(AccessTools.Method(typeof(Window_StackEditor), "DoWindowContents"), new HarmonyMethod(typeof(Window_StackEditor_Draw_Patch), "PreDoWindowContents"), new HarmonyMethod(typeof(Window_StackEditor_Draw_Patch), "FinalizerDoWindowContents"));
		MpCompat.harmony.Patch(AccessTools.Method(typeof(Window_StackEditor), "DrawAcceptCancelButtons"), new HarmonyMethod(typeof(Window_StackEditor_Draw_Patch), "PreDrawAcceptCancelButtons"), new HarmonyMethod(typeof(Window_StackEditor_Draw_Patch), "FinalizerDrawAcceptCancelButtons"));
		FieldNeuralDataNeuralDataRewritten = MP.RegisterSyncField(AccessTools.Field(typeof(NeuralData), "neuralDataRewritten"));
		FieldNeuralDataEditTime = MP.RegisterSyncField(AccessTools.Field(typeof(NeuralData), "editTime"));
		FieldNeuralDataStackDegradationToAdd = MP.RegisterSyncField(AccessTools.Field(typeof(NeuralData), "stackDegradation"));
		FieldNeuralDataTrackedToMatrix = MP.RegisterSyncField(typeof(NeuralData), "trackedToMatrix");
		FieldNeuralStackAutoLoad = MP.RegisterSyncField(typeof(NeuralStack), "autoLoad");
		MpCompat.harmony.Patch(AccessTools.Method(typeof(NeuralData), "RefreshDummyPawn"), new HarmonyMethod(typeof(NeuralDataRefreshDummyPawn_Patch), "Prefix"), new HarmonyMethod(typeof(NeuralDataRefreshDummyPawn_Patch), "Finalizer"));
	}

	private static void SyncStackInstallInfo(SyncWorker sync, ref StackInstallInfo obj)
	{
		if (sync.isWriting)
		{
			StackInstallInfo info = obj;
			sync.Write(AC_Utils.stackRecipesByDef.First((KeyValuePair<ThingDef, StackInstallInfo> pair) => pair.Value.recipe == info.recipe).Key);
		}
		else
		{
			AC_Utils.stackRecipesByDef.TryGetValue(sync.Read<ThingDef>(), obj);
		}
	}

	private static void SyncINeedlecastable(SyncWorker sync, ref INeedlecastable obj)
	{
		if (sync.isWriting)
		{
			if (obj is NeuralStack)
			{
				sync.Write(obj as Thing);
			}
			else
			{
				sync.Write((Thing)(((Hediff_NeuralStack)obj)?.pawn));
			}
			return;
		}
		Thing thing = sync.Read<Thing>();
		if (thing is NeuralStack)
		{
			obj = thing as NeuralStack;
		}
		else
		{
			obj = (thing as Pawn).health.hediffSet.GetFirstHediff<Hediff_NeuralStack>();
		}
	}

	private static void SyncIStackHolder(SyncWorker sync, ref IStackHolder obj)
	{
		if (sync.isWriting)
		{
			if (obj is NeuralStack)
			{
				sync.Write(obj as Thing);
			}
			else
			{
				sync.Write((Thing)(((Hediff_NeuralStack)obj)?.pawn));
			}
			return;
		}
		Thing thing = sync.Read<Thing>();
		if (thing is NeuralStack)
		{
			obj = thing as NeuralStack;
		}
		else
		{
			obj = (thing as Pawn).health.hediffSet.GetFirstHediff<Hediff_NeuralStack>();
		}
	}

	private static void SyncWindow_NeuralMatrixManagement(SyncWorker sync, ref Window_NeuralMatrixManagement window)
	{
		if (sync.isWriting)
		{
			sync.Write((Building)window.matrix);
			return;
		}
		Building_NeuralMatrix matrix = sync.Read<Building>() as Building_NeuralMatrix;
		window = new Window_NeuralMatrixManagement(matrix);
	}

	private static void SyncCompNeuralCache(SyncWorker sync, ref CompNeuralCache comp)
	{
		if (sync.isWriting)
		{
			sync.Write(comp.parent);
			return;
		}
		ThingWithComps thingWithComps = sync.Read<ThingWithComps>();
		comp = thingWithComps.GetComp<CompNeuralCache>();
	}

	private static void SyncNeuralData(SyncWorker sync, ref NeuralData neuralData)
	{
		if (sync.isWriting)
		{
			if (neuralData.host.GetNeuralData() == neuralData)
			{
				sync.Write(obj: true);
				sync.Write((Thing)neuralData.host);
				return;
			}
			if (neuralData.hostPawn.GetNeuralData() == neuralData)
			{
				sync.Write(obj: true);
				sync.Write(neuralData.hostPawn);
				return;
			}
			sync.Write(obj: false);
			sync.Write(neuralData, SyncTypeNeuralData);
			sync.Write(neuralData.ideo);
			neuralData.dummyPawn = null;
		}
		else if (sync.Read<bool>())
		{
			neuralData = sync.Read<Thing>().GetNeuralData();
		}
		else
		{
			neuralData = sync.Read<NeuralData>(SyncTypeNeuralData);
			neuralData.ideo = sync.Read<Ideo>();
			neuralData.dummyPawn = null;
		}
	}

	private static IEnumerable<CodeInstruction> TranspileBuilding_SleeveCasketGetInspectString(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo methodInfo = AccessTools.PropertySetter(typeof(Building_Bed), "Medical");
		List<CodeInstruction> list = new List<CodeInstruction>(instructions);
		for (int i = 0; i < list.Count; i++)
		{
			CodeInstruction codeInstruction = list[i];
			if (codeInstruction.opcode == OpCodes.Call && (MethodInfo)codeInstruction.operand == methodInfo)
			{
				list.RemoveAt(i + 1);
				list.RemoveAt(i);
				list.RemoveAt(i - 1);
				i -= 2;
			}
		}
		return list;
	}
}
