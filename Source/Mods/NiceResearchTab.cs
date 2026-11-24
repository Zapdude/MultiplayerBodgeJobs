using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat
{
    /// <summary>Nice Research Tab (Andromeda.NiceResearchTab) by Andromeda.</summary>
    /// <remarks>
    /// The mod syncs queue edits itself, but its Apply* methods solve the chain through
    /// ResearchTreeWidget.Instance, which only exists on a client that opened the research tab in this
    /// session. The receiving client throws a NullReferenceException at the first instruction of
    /// ApplyRestartQueue, the queues diverge, and the game desyncs.
    ///
    /// This module intercepts the three interface edits, solves the prerequisite chain from research
    /// defs on the client that clicked, sends that list once, and applies it on every client from game
    /// data only. Behaviour reference: Multiplayer Fix (MIT); this is a reimplementation against the
    /// same mod API, not a copy.
    /// </remarks>
    [MpCompatFor("andromeda.niceresearchtab")]
    class NiceResearchTab
    {
        private const int OpAdd = 0;
        private const int OpInsertFront = 1;
        private const int OpRestart = 2;

        private static Type queueType;
        private static Type widgetType;
        private static Type tabType;
        private static Type nodeType;

        private static FieldInfo queueInstanceField;
        private static FieldInfo widgetInstanceField;
        private static FieldInfo tabInstanceField;
        private static FieldInfo gravtechQueueField;
        private static FieldInfo finalizedIndexField;
        private static FieldInfo nodeProjectField;

        private static MethodInfo findNodeMethod;
        private static MethodInfo solveQueueMethod;
        private static MethodInfo canStartMethod;

        private static MethodInfo getQueueForMethod;
        private static MethodInfo notifyGravtechMethod;
        private static MethodInfo ensureStartedMethod;
        private static MethodInfo stopAllMethod;
        private static MethodInfo setQueueDirtyMethod;
        private static MethodInfo updateNodesMethod;

        private static MethodInfo addMethod;
        private static MethodInfo insertMethod;
        private static MethodInfo restartMethod;
        private static MethodInfo applyAddMethod;
        private static MethodInfo applyInsertMethod;
        private static MethodInfo applyRestartMethod;

        private static MethodInfo discoveriesLockedMethod;
        private static Type medievalType;
        private static FieldInfo medievalInstanceField;
        private static MethodInfo medievalLockedMethod;

        public NiceResearchTab(ModContentPack mod)
        {
            LongEventHandler.ExecuteWhenFinished(InstallSafely);
        }

        private static void InstallSafely()
        {
            try
            {
                Install();
            }
            catch (Exception e)
            {
                // A changed mod API must disable this module, not abort the rest of compat.
                Log.Error("[MPCompat] Nice Research Tab: install failed; module disabled.\n" + e);
            }
        }

        private static void Install()
        {
            queueType = Type("NiceResearchTab.ResearchQueue");
            widgetType = Type("NiceResearchTab.ResearchTreeWidget");
            tabType = Type("NiceResearchTab.MainTabWindow_ResearchTree");
            nodeType = Type("NiceResearchTab.ResearchNode");

            queueInstanceField = Field(queueType, "Instance");
            widgetInstanceField = Field(widgetType, "Instance");
            tabInstanceField = Field(tabType, "Instance");
            gravtechQueueField = Field(queueType, "GravtechQueue");
            finalizedIndexField = Field(queueType, "CachedFinalizedIndex");
            nodeProjectField = Field(nodeType, "Project");

            findNodeMethod = Method(widgetType, "FindByProject", typeof(ResearchProjectDef));
            solveQueueMethod = Method(widgetType, "SolveQueue", nodeType);
            canStartMethod = Method(queueType, "CanStart", nodeType);

            getQueueForMethod = Method(queueType, "GetQueueFor", typeof(ResearchProjectDef));
            notifyGravtechMethod = Method(queueType, "NotifyGravtechHintIfFilledFromEmpty", typeof(bool), typeof(ResearchProjectDef));
            ensureStartedMethod = Method(queueType, "EnsureAllQueuesIsStarted");
            stopAllMethod = Method(queueType, "StopAllProjects");
            setQueueDirtyMethod = Method(tabType, "SetQueueDirty");
            updateNodesMethod = Method(widgetType, "UpdateNodes");

            addMethod = Method(queueType, "AddToQueue", typeof(ResearchProjectDef));
            insertMethod = Method(queueType, "InsertToFront", typeof(ResearchProjectDef));
            restartMethod = Method(queueType, "RestartQueue", typeof(ResearchProjectDef));
            applyAddMethod = Method(queueType, "ApplyAddToQueue", typeof(ResearchProjectDef));
            applyInsertMethod = Method(queueType, "ApplyInsertToFront", typeof(ResearchProjectDef));
            applyRestartMethod = Method(queueType, "ApplyRestartQueue", typeof(ResearchProjectDef));

            // Optional lock checks used by the UI-free solver. Missing integrations are not fatal.
            discoveriesLockedMethod = AccessTools.Method("NiceResearchTab.ModIntegrations.DiscoveriesIntegration:IsResearchLocked");
            medievalType = AccessTools.TypeByName("NiceResearchTab.ModIntegrations.MedievalOverhaulIntegration");
            if (medievalType != null)
            {
                medievalInstanceField = AccessTools.Field(medievalType, "Instance");
                medievalLockedMethod = AccessTools.Method(medievalType, "IsResearchLocked", new[] { typeof(ResearchProjectDef) });
            }

            MpCompat.harmony.Patch(addMethod, prefix: FirstHook(nameof(AddPrefix)));
            MpCompat.harmony.Patch(insertMethod, prefix: FirstHook(nameof(InsertPrefix)));
            MpCompat.harmony.Patch(restartMethod, prefix: FirstHook(nameof(RestartPrefix)));

            // A command from a client without this module still has to apply without the UI.
            MpCompat.harmony.Patch(applyAddMethod, prefix: Hook(nameof(ApplyAddPrefix)));
            MpCompat.harmony.Patch(applyInsertMethod, prefix: Hook(nameof(ApplyInsertPrefix)));
            MpCompat.harmony.Patch(applyRestartMethod, prefix: Hook(nameof(ApplyRestartPrefix)));

            MpCompat.harmony.Patch(Method(queueType, "EnsureCurrentInQueue"), prefix: Hook(nameof(EnsureCurrentPrefix)));
            MpCompat.harmony.Patch(Method(queueType, "EnsureGravtechInQueue"), prefix: Hook(nameof(EnsureGravtechPrefix)));

            // Drawing the tab inserts the active project into the queue on that client only; the
            // synced path must not depend on it. The remaining Apply* methods get null-safe UI
            // refreshes so a client without the tab does not throw halfway through.
            HarmonyMethod nullSafe = new HarmonyMethod(typeof(NiceResearchTab), nameof(NullSafeRefreshes));
            foreach (string name in new[] { "ApplyRemoveFromQueue", "ApplyReorderToIndex", "AssignProject", "ApplyAddToQueue", "ApplyInsertToFront", "ApplyRestartQueue" })
            {
                MethodInfo method = AccessTools.Method(queueType, name);
                if (method != null)
                    MpCompat.harmony.Patch(method, transpiler: nullSafe);
            }

            MP.RegisterSyncMethod(typeof(NiceResearchTab), nameof(SyncedQueueOp));
        }

        private static Type Type(string name) => AccessTools.TypeByName(name)
            ?? throw new TypeLoadException("Nice Research Tab: missing type " + name);

        private static MethodInfo Method(Type type, string name, params Type[] arguments) =>
            AccessTools.DeclaredMethod(type, name, arguments)
            ?? throw new MissingMethodException(type?.FullName, name);

        private static FieldInfo Field(Type type, string name) =>
            AccessTools.DeclaredField(type, name)
            ?? throw new MissingFieldException(type?.FullName, name);

        private static HarmonyMethod Hook(string name) => new(typeof(NiceResearchTab), name);

        private static HarmonyMethod FirstHook(string name) => new(typeof(NiceResearchTab), name)
        {
            priority = Priority.First
        };

        // ---- Interface entry points ----

        private static bool AddPrefix(object __instance, ResearchProjectDef project) =>
            Intercept(__instance, OpAdd, project);

        private static bool InsertPrefix(object __instance, ResearchProjectDef project) =>
            Intercept(__instance, OpInsertFront, project);

        private static bool RestartPrefix(object __instance, ResearchProjectDef project) =>
            Intercept(__instance, OpRestart, project);

        private static bool Intercept(object queue, int op, ResearchProjectDef project)
        {
            if (!MP.IsInMultiplayer)
                return true;
            if (project == null)
                return false;

            if (!MP.InInterface)
            {
                // Simulation or an already-synced command: every client runs the same def-only apply.
                Apply(queue, op, SolveFromDefs(project));
                return false;
            }

            List<ResearchProjectDef> chain = SolveOnThisClient(queue, project);
            if (chain.Count > 0)
                SyncedQueueOp(op, chain);
            return false;
        }

        private static bool ApplyAddPrefix(object __instance, ResearchProjectDef project) =>
            ApplyInSimulation(__instance, OpAdd, project);

        private static bool ApplyInsertPrefix(object __instance, ResearchProjectDef project) =>
            ApplyInSimulation(__instance, OpInsertFront, project);

        private static bool ApplyRestartPrefix(object __instance, ResearchProjectDef project) =>
            ApplyInSimulation(__instance, OpRestart, project);

        private static bool ApplyInSimulation(object queue, int op, ResearchProjectDef project)
        {
            if (!MP.IsInMultiplayer || MP.InInterface)
                return true;
            Apply(queue, op, SolveFromDefs(project));
            return false;
        }

        private static bool EnsureCurrentPrefix() => !MP.IsInMultiplayer || !MP.InInterface;

        private static bool EnsureGravtechPrefix(object __instance)
        {
            if (!MP.IsInMultiplayer || !MP.InInterface)
                return true;
            // Keep the list usable, but do not put the active project into it on the drawing client.
            if (gravtechQueueField.GetValue(__instance) == null)
                gravtechQueueField.SetValue(__instance, new List<ResearchProjectDef>());
            return false;
        }

        // ---- Synced command ----

        /// <summary>Runs on every client; uses only research defs and the queue component.</summary>
        private static void SyncedQueueOp(int op, List<ResearchProjectDef> chain)
        {
            object queue = queueInstanceField.GetValue(null);
            if (queue != null && chain != null)
                Apply(queue, op, chain);
        }

        private static void Apply(object queue, int op, List<ResearchProjectDef> chain)
        {
            List<ResearchProjectDef> projects = new List<ResearchProjectDef>();
            foreach (ResearchProjectDef project in chain)
            {
                if (project != null && !project.IsFinished && !projects.Contains(project))
                    projects.Add(project);
            }
            if (projects.Count == 0)
                return;

            List<ResearchProjectDef> gravtech = gravtechQueueField.GetValue(queue) as List<ResearchProjectDef>;
            bool gravtechWasEmpty = gravtech == null || gravtech.Count == 0;
            ResearchProjectDef firstGravtech = null;

            switch (op)
            {
                case OpAdd:
                    foreach (ResearchProjectDef project in projects)
                    {
                        List<ResearchProjectDef> target = QueueFor(queue, project);
                        if (target.Contains(project))
                            continue;
                        target.Add(project);
                        if (firstGravtech == null && ReferenceEquals(target, gravtech))
                            firstGravtech = project;
                    }
                    break;

                case OpInsertFront:
                {
                    List<List<ResearchProjectDef>> queues = new List<List<ResearchProjectDef>>();
                    List<List<ResearchProjectDef>> items = new List<List<ResearchProjectDef>>();
                    foreach (ResearchProjectDef project in projects)
                    {
                        List<ResearchProjectDef> target = QueueFor(queue, project);
                        int index = IndexOf(queues, target);
                        if (index < 0)
                        {
                            queues.Add(target);
                            items.Add(new List<ResearchProjectDef>());
                            index = queues.Count - 1;
                        }
                        items[index].Add(project);
                    }
                    for (int i = 0; i < queues.Count; i++)
                    {
                        List<ResearchProjectDef> target = queues[i];
                        foreach (ResearchProjectDef project in items[i])
                            target.Remove(project);
                        for (int j = 0; j < items[i].Count; j++)
                        {
                            target.Insert(j, items[i][j]);
                            if (firstGravtech == null && ReferenceEquals(target, gravtech))
                                firstGravtech = items[i][j];
                        }
                    }
                    break;
                }

                case OpRestart:
                {
                    stopAllMethod.Invoke(queue, null);
                    List<List<ResearchProjectDef>> cleared = new List<List<ResearchProjectDef>>();
                    foreach (ResearchProjectDef project in projects)
                    {
                        List<ResearchProjectDef> target = QueueFor(queue, project);
                        if (IndexOf(cleared, target) < 0)
                            cleared.Add(target);
                    }
                    foreach (List<ResearchProjectDef> target in cleared)
                        target.Clear();
                    foreach (ResearchProjectDef project in projects)
                    {
                        List<ResearchProjectDef> target = QueueFor(queue, project);
                        target.Add(project);
                        if (firstGravtech == null && ReferenceEquals(target, gravtech))
                            firstGravtech = project;
                    }
                    break;
                }

                default:
                    return;
            }

            notifyGravtechMethod.Invoke(queue, new object[] { gravtechWasEmpty, firstGravtech });
            ensureStartedMethod.Invoke(queue, null);
            if (op != OpAdd)
                (finalizedIndexField.GetValue(queue) as IDictionary)?.Clear();

            object tab = tabInstanceField.GetValue(null);
            if (tab != null)
                setQueueDirtyMethod.Invoke(tab, null);
            object widget = widgetInstanceField.GetValue(null);
            if (widget != null)
                updateNodesMethod.Invoke(widget, null);
        }

        private static List<ResearchProjectDef> QueueFor(object queue, ResearchProjectDef project) =>
            (List<ResearchProjectDef>)getQueueForMethod.Invoke(queue, new object[] { project });

        private static int IndexOf(List<List<ResearchProjectDef>> lists, List<ResearchProjectDef> list)
        {
            for (int i = 0; i < lists.Count; i++)
                if (ReferenceEquals(lists[i], list))
                    return i;
            return -1;
        }

        // ---- Solving the chain ----

        /// <summary>The mod's own solver when the research tree exists on this client, else defs only.</summary>
        private static List<ResearchProjectDef> SolveOnThisClient(object queue, ResearchProjectDef project)
        {
            object widget = widgetInstanceField.GetValue(null);
            if (widget != null)
            {
                try
                {
                    object node = findNodeMethod.Invoke(widget, new object[] { project });
                    if (node == null || !(bool)canStartMethod.Invoke(queue, new object[] { node }))
                        return new List<ResearchProjectDef>();

                    List<ResearchProjectDef> chain = new List<ResearchProjectDef>();
                    if (solveQueueMethod.Invoke(widget, new object[] { node }) is IEnumerable nodes)
                    {
                        foreach (object item in nodes)
                        {
                            if (item != null && nodeProjectField.GetValue(item) is ResearchProjectDef def)
                                chain.Add(def);
                        }
                    }
                    chain.Reverse();
                    return chain;
                }
                catch (Exception e)
                {
                    Log.WarningOnce("[MPCompat] Nice Research Tab: tree solver failed, using defs. " + e, 0x4E525401);
                }
            }
            return SolveFromDefs(project);
        }

        /// <summary>
        /// The queue the mod builds, from research defs only: every unfinished prerequisite, cheapest
        /// first (Anomaly advanced projects after the rest, ties by defName), target last. Empty when
        /// anything in the chain is locked or hidden.
        /// </summary>
        private static List<ResearchProjectDef> SolveFromDefs(ResearchProjectDef target)
        {
            List<ResearchProjectDef> result = new List<ResearchProjectDef>();
            if (target == null || target.IsFinished)
                return result;

            HashSet<ResearchProjectDef> pending = new HashSet<ResearchProjectDef>();
            CollectUnfinished(target, pending);
            foreach (ResearchProjectDef project in pending)
            {
                if (IsLocked(project))
                    return result;
            }

            HashSet<ResearchProjectDef> taken = new HashSet<ResearchProjectDef>();
            while (pending.Count > 0)
            {
                ResearchProjectDef best = null;
                int bestCost = 0;
                foreach (ResearchProjectDef project in pending)
                {
                    if (!PrerequisitesDone(project, taken))
                        continue;
                    int cost = SortingCost(project);
                    if (best == null || cost < bestCost
                        || (cost == bestCost && string.CompareOrdinal(project.defName, best.defName) < 0))
                    {
                        best = project;
                        bestCost = cost;
                    }
                }
                if (best == null)
                    break; // circular prerequisites: stop rather than loop
                result.Add(best);
                taken.Add(best);
                pending.Remove(best);
            }
            return result;
        }

        private static IEnumerable<ResearchProjectDef> Prerequisites(ResearchProjectDef project)
        {
            if (project.prerequisites != null)
                foreach (ResearchProjectDef p in project.prerequisites)
                    if (p != null)
                        yield return p;
            if (project.hiddenPrerequisites != null)
                foreach (ResearchProjectDef p in project.hiddenPrerequisites)
                    if (p != null)
                        yield return p;
        }

        private static void CollectUnfinished(ResearchProjectDef project, HashSet<ResearchProjectDef> set)
        {
            if (project == null || project.IsFinished || !set.Add(project))
                return;
            foreach (ResearchProjectDef p in Prerequisites(project))
                CollectUnfinished(p, set);
        }

        private static bool PrerequisitesDone(ResearchProjectDef project, HashSet<ResearchProjectDef> taken)
        {
            foreach (ResearchProjectDef p in Prerequisites(project))
                if (!p.IsFinished && !taken.Contains(p))
                    return false;
            return true;
        }

        private static int SortingCost(ResearchProjectDef project) =>
            (int)project.CostApparent + (project.knowledgeCategory == KnowledgeCategoryDefOf.Advanced ? 1000 : 0);

        private static bool IsLocked(ResearchProjectDef project)
        {
            if (project.IsHidden
                || !project.TechprintRequirementMet
                || !project.AnalyzedThingsRequirementsMet
                || !project.PlayerMechanitorRequirementMet
                || !project.InspectionRequirementsMet)
                return true;

            try
            {
                if (discoveriesLockedMethod != null
                    && discoveriesLockedMethod.Invoke(null, new object[] { project }) is bool locked && locked)
                    return true;
                object medieval = medievalInstanceField?.GetValue(null);
                if (medieval != null && medievalLockedMethod != null
                    && medievalLockedMethod.Invoke(medieval, new object[] { project }) is bool medievalLocked && medievalLocked)
                    return true;
            }
            catch (Exception e)
            {
                Log.WarningOnce("[MPCompat] Nice Research Tab: lock check failed. " + e, 0x4E525402);
            }
            return false;
        }

        // ---- UI refresh null-safety ----

        private static IEnumerable<CodeInstruction> NullSafeRefreshes(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo safeDirty = AccessTools.Method(typeof(NiceResearchTab), nameof(SafeSetQueueDirty));
            MethodInfo safeNodes = AccessTools.Method(typeof(NiceResearchTab), nameof(SafeUpdateNodes));
            foreach (CodeInstruction instruction in instructions)
            {
                if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt)
                    && instruction.operand is MethodInfo method)
                {
                    if (method == setQueueDirtyMethod)
                    {
                        instruction.opcode = OpCodes.Call;
                        instruction.operand = safeDirty;
                    }
                    else if (method == updateNodesMethod)
                    {
                        instruction.opcode = OpCodes.Call;
                        instruction.operand = safeNodes;
                    }
                }
                yield return instruction;
            }
        }

        public static void SafeSetQueueDirty(object tab)
        {
            if (tab != null)
                setQueueDirtyMethod.Invoke(tab, null);
        }

        public static void SafeUpdateNodes(object widget)
        {
            if (widget != null)
                updateNodesMethod.Invoke(widget, null);
        }
    }
}
