using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Multiplayer.Compat
{
    /// <summary>Character Development (Wants and Quirks) by ferny</summary>
    /// <remarks>
    /// Every want, quirk, point and reward action mutates the pawn locally, so each is rerouted
    /// through a synced method taking a stable key plus a seed. Redirects only fire in the
    /// interface. Bubble positions are scribed and their physics is frame-based, so it is frozen.
    /// </remarks>
    [MpCompatFor("ferny.characterdevelopment")]
    class CharacterDevelopment
    {
        private const int RerollSalt = 257;
        private const int AddQuirkSalt = 514;
        private const int GenerateBubblesSalt = 771;
        private const int ValidateNodesSalt = 1028;
        private const int ClaimRewardSalt = 1285;
        private const int RecycleBubblesSalt = 1853;

        private sealed class BubblePositions
        {
            internal readonly List<object> Nodes = new List<object>();
            internal readonly List<Vector2> Positions = new List<Vector2>();
        }

        // ---- Types ----
        private static Type utilityType;
        private static Type stateType;
        private static Type wantsDataType;
        private static Type activeWantType;
        private static Type wantWithPawnTargetType;
        private static Type wantWithDefTargetType;
        private static Type wantWithBaselineType;
        private static Type wantDefType;
        private static Type rewardDefType;
        private static Type rewardNodeType;
        private static Type rewardWorkerType;
        private static Type quirkType;
        private static Type modType;
        private static Type settingsType;
        private static Type mainTabType;
        private static Type itabType;
        private static Type dialogType;

        // ---- Fields ----
        private static FieldInfo activeWantsField;
        private static FieldInfo quirksField;
        private static FieldInfo rewardPointsField;
        private static FieldInfo rewardNodesField;
        private static FieldInfo globalRewardPointsField;
        private static FieldInfo quirkCounterField;
        private static FieldInfo settingsField;
        private static FieldInfo pawnSpecificPointsField;
        private static FieldInfo rerollBubblesField;

        private static FieldInfo wantDefField;
        private static FieldInfo wantAssignedTickField;
        private static FieldInfo wantRerollCountField;
        private static FieldInfo wantPawnTargetField;
        private static FieldInfo wantDefTargetField;
        private static FieldInfo wantBaselineField;
        private static FieldInfo wantIsMentalBreakField;

        private static FieldInfo nodeDefField;
        private static FieldInfo nodeItemField;
        private static FieldInfo nodePawnField;
        private static FieldInfo nodePosField;
        private static PropertyInfo nodeDescriptionProperty;

        private static FieldInfo quirkDefField;
        private static FieldInfo quirkLoadIdField;

        private static FieldInfo rewardRequiresItemField;
        private static FieldInfo rewardRequiresPawnField;
        private static PropertyInfo rewardWorkerProperty;

        private static FieldInfo dialogNodeField;
        private static FieldInfo dialogRecipientsField;
        private static FieldInfo dialogScrollField;

        // ---- Methods ----
        private static MethodInfo getWantsDataMethod;
        private static MethodInfo tryGetWantsDataMethod;
        private static MethodInfo canHaveWantsMethod;
        private static MethodInfo rerollWantMethod;
        private static MethodInfo completeWantMethod;
        private static MethodInfo addCharacterPointsMethod;
        private static MethodInfo generateBubblesMethod;
        private static MethodInfo generateSingleBubbleMethod;
        private static MethodInfo addQuirkMethod;

        private static MethodInfo workerCanBestowOnMethod;
        private static MethodInfo workerOnRemovedMethod;

        /// <summary>Player code that is not the simulation and not a running sync command.</summary>
        private static bool InInterface => MP.IsInMultiplayer && MP.InInterface && !MP.IsExecutingSyncCommand;

        public CharacterDevelopment(ModContentPack mod)
        {
            LongEventHandler.ExecuteWhenFinished(Install);
        }

        private static void Install()
        {
            utilityType = Type("WantsAndQuirks.WantsAndQuirksUtility");
            stateType = Type("WantsAndQuirks.State");
            wantsDataType = Type("WantsAndQuirks.PawnWantsData");
            activeWantType = Type("WantsAndQuirks.ActiveWant");
            wantWithPawnTargetType = Type("WantsAndQuirks.ActiveWantWithPawnTarget");
            wantWithDefTargetType = Type("WantsAndQuirks.ActiveWantWithTarget");
            wantWithBaselineType = Type("WantsAndQuirks.ActiveWantWithRecordBaseline");
            wantDefType = Type("WantsAndQuirks.WantDef");
            rewardDefType = Type("WantsAndQuirks.RewardDef");
            rewardNodeType = Type("WantsAndQuirks.RewardNode");
            rewardWorkerType = Type("WantsAndQuirks.RewardWorker");
            quirkType = Type("WantsAndQuirks.Quirk");
            modType = Type("WantsAndQuirks.WantsAndQuirksMod");
            settingsType = Type("WantsAndQuirks.WantsAndQuirksSettings");
            mainTabType = Type("WantsAndQuirks.MainTabWindow_Characters");
            itabType = Type("WantsAndQuirks.ITab_Pawn_WantsAndQuirks");
            dialogType = Type("WantsAndQuirks.Dialog_BestowReward");

            activeWantsField = Field(wantsDataType, "activeWants");
            quirksField = Field(wantsDataType, "quirks");
            rewardPointsField = Field(wantsDataType, "rewardPoints");
            rewardNodesField = Field(stateType, "rewardNodes");
            globalRewardPointsField = Field(stateType, "rewardPoints");
            quirkCounterField = Field(stateType, "quirkLoadIDCounter");
            settingsField = Field(modType, "settings");
            pawnSpecificPointsField = Field(settingsType, "pawnSpecificRewardPoints");
            rerollBubblesField = Field(settingsType, "rerollBubblesOnSelection");

            wantDefField = Field(activeWantType, "def");
            wantAssignedTickField = Field(activeWantType, "assignedTick");
            wantRerollCountField = Field(activeWantType, "rerollCount");
            wantPawnTargetField = Field(wantWithPawnTargetType, "targetPawn");
            wantDefTargetField = Field(wantWithDefTargetType, "targetDef");
            wantBaselineField = Field(wantWithBaselineType, "baselineValue");
            wantIsMentalBreakField = Field(wantDefType, "isMentalBreakWant");

            nodeDefField = Field(rewardNodeType, "def");
            nodeItemField = Field(rewardNodeType, "item");
            nodePawnField = Field(rewardNodeType, "pawnTarget");
            nodePosField = Field(rewardNodeType, "pos");
            nodeDescriptionProperty = Property(rewardNodeType, "Description");

            quirkDefField = Field(quirkType, "def");
            quirkLoadIdField = Field(quirkType, "loadID");

            rewardRequiresItemField = Field(rewardDefType, "requiresItem");
            rewardRequiresPawnField = Field(rewardDefType, "requiresPawn");
            rewardWorkerProperty = Property(rewardDefType, "Worker");

            dialogNodeField = Field(dialogType, "node");
            dialogRecipientsField = Field(dialogType, "recipients");
            dialogScrollField = Field(dialogType, "scrollPos");

            getWantsDataMethod = Method(utilityType, "GetWantsData", typeof(Pawn));
            tryGetWantsDataMethod = Method(utilityType, "TryGetWantsData", typeof(Pawn), wantsDataType.MakeByRefType());
            canHaveWantsMethod = Method(utilityType, "CanHaveWants", typeof(Pawn));
            rerollWantMethod = Method(utilityType, "RerollWant", typeof(Pawn), wantsDataType, activeWantType);
            completeWantMethod = Method(utilityType, "CompleteWant", typeof(Pawn), wantsDataType, activeWantType);
            addCharacterPointsMethod = Method(utilityType, "AddCharacterPoints", typeof(Pawn), typeof(int));
            generateBubblesMethod = Method(utilityType, "GenerateGlobalRewardBubbles");
            generateSingleBubbleMethod = Method(utilityType, "GenerateSingleRewardBubble", typeof(List<>).MakeGenericType(rewardNodeType));
            addQuirkMethod = Method(utilityType, "AddQuirk", typeof(Pawn), rewardDefType, typeof(ThingDef), typeof(Pawn));
            workerCanBestowOnMethod = Method(rewardWorkerType, "CanBestowOn", typeof(Pawn), typeof(ThingDef), typeof(Pawn));
            workerOnRemovedMethod = Method(rewardWorkerType, "OnRemoved", typeof(Pawn), quirkType);

            RegisterSyncMethods();

            MpCompat.harmony.Patch(rerollWantMethod, prefix: Hook(nameof(RerollWantPrefix)));
            MpCompat.harmony.Patch(completeWantMethod, prefix: Hook(nameof(CompleteWantPrefix)));
            MpCompat.harmony.Patch(addCharacterPointsMethod, prefix: Hook(nameof(AddCharacterPointsPrefix)));
            MpCompat.harmony.Patch(addQuirkMethod, prefix: Hook(nameof(AddQuirkPrefix)));
            MpCompat.harmony.Patch(generateBubblesMethod, prefix: Hook(nameof(GenerateBubblesPrefix)));

            MpCompat.harmony.Patch(Method(mainTabType, "ValidateRewardNodes"), prefix: Hook(nameof(ValidateNodesPrefix)));
            MpCompat.harmony.Patch(Method(mainTabType, "RemoveWantsWithNullPawns"), prefix: Hook(nameof(CleanupWantsPrefix)));
            MpCompat.harmony.Patch(Method(mainTabType, "PhysicsTick", typeof(float), typeof(bool)),
                prefix: Hook(nameof(FreezeBubblePhysics)));
            MpCompat.harmony.Patch(Method(mainTabType, "DoWindowContents", typeof(Rect)),
                prefix: Hook(nameof(SaveBubblePositions)),
                finalizer: Hook(nameof(RestoreBubblePositions)));
            MpCompat.harmony.Patch(Method(dialogType, "DoWindowContents", typeof(Rect)),
                prefix: Hook(nameof(DrawRewardDialog)));
            MpCompat.harmony.Patch(Method(itabType, "DrawWants", typeof(Rect), typeof(Pawn), wantsDataType),
                transpiler: Hook(nameof(RemoveWantTranspiler)));

            PatchRemovalLambda();
            PatchReorderLambda();
        }

        private static void RegisterSyncMethods()
        {
            MP.RegisterSyncMethod(typeof(CharacterDevelopment), nameof(RerollWant)).SetContext(SyncContext.CurrentMap);
            MP.RegisterSyncMethod(typeof(CharacterDevelopment), nameof(RemoveWant)).SetContext(SyncContext.CurrentMap);
            MP.RegisterSyncMethod(typeof(CharacterDevelopment), nameof(ReorderWant)).SetContext(SyncContext.CurrentMap);
            MP.RegisterSyncMethod(typeof(CharacterDevelopment), nameof(RemoveQuirk)).SetContext(SyncContext.CurrentMap);
            MP.RegisterSyncMethod(typeof(CharacterDevelopment), nameof(AddQuirkSeeded)).SetContext(SyncContext.CurrentMap);
            MP.RegisterSyncMethod(typeof(CharacterDevelopment), nameof(CompleteWant)).SetContext(SyncContext.CurrentMap);
            MP.RegisterSyncMethod(typeof(CharacterDevelopment), nameof(AddCharacterPoints)).SetContext(SyncContext.CurrentMap);
            MP.RegisterSyncMethod(typeof(CharacterDevelopment), nameof(GenerateRewardBubbles)).SetContext(SyncContext.CurrentMap);
            MP.RegisterSyncMethod(typeof(CharacterDevelopment), nameof(ValidateRewardNodes)).SetContext(SyncContext.CurrentMap);
            MP.RegisterSyncMethod(typeof(CharacterDevelopment), nameof(CleanupNullTargetWants)).SetContext(SyncContext.CurrentMap);
            MP.RegisterSyncMethod(typeof(CharacterDevelopment), nameof(ClaimReward)).SetContext(SyncContext.CurrentMap);
        }

        // ---- Reflection helpers ----

        private static Type Type(string name)
        {
            return AccessTools.TypeByName(name) ?? throw new TypeLoadException("Character Development: missing type " + name);
        }

        private static MethodInfo Method(Type type, string name, params Type[] arguments)
        {
            return AccessTools.DeclaredMethod(type, name, arguments) ?? throw new MissingMethodException(type?.FullName, name);
        }

        private static FieldInfo Field(Type type, string name)
        {
            return AccessTools.DeclaredField(type, name) ?? throw new MissingFieldException(type?.FullName, name);
        }

        private static PropertyInfo Property(Type type, string name)
        {
            return AccessTools.DeclaredProperty(type, name) ?? throw new MissingMemberException(type?.FullName, name);
        }

        private static HarmonyMethod Hook(string name)
        {
            return new HarmonyMethod(typeof(CharacterDevelopment), name);
        }

        // ---- Shared state access ----

        private static object WantsData(Pawn pawn)
        {
            return getWantsDataMethod.Invoke(null, new object[] { pawn });
        }

        private static object ExistingWantsData(Pawn pawn)
        {
            object[] arguments = { pawn, null };
            return (bool)tryGetWantsDataMethod.Invoke(null, arguments) ? arguments[1] : null;
        }

        private static IList Wants(object data)
        {
            return data == null ? null : activeWantsField.GetValue(data) as IList;
        }

        private static IList Nodes()
        {
            return rewardNodesField.GetValue(null) as IList;
        }

        private static string NameOf(object def)
        {
            return (def as Def)?.defName ?? string.Empty;
        }

        private static object Worker(object def)
        {
            return def == null ? null : rewardWorkerProperty.GetValue(def, null);
        }

        private static object Settings()
        {
            return settingsField.GetValue(null);
        }

        private static bool CanHaveWants(Pawn pawn)
        {
            return pawn != null && !pawn.Destroyed && (bool)canHaveWantsMethod.Invoke(null, new object[] { pawn });
        }

        private static bool UsePawnRewardPoints()
        {
            return (bool)pawnSpecificPointsField.GetValue(Settings());
        }

        private static bool RerollBubblesOnClaim()
        {
            return (bool)rerollBubblesField.GetValue(Settings());
        }

        private static Pawn FindWantsOwner(IList wants)
        {
            if (wants == null)
            {
                return null;
            }
            foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_OfPlayerFaction)
            {
                if (pawn == null)
                {
                    continue;
                }
                object data = ExistingWantsData(pawn);
                if (data != null && ReferenceEquals(activeWantsField.GetValue(data), wants))
                {
                    return pawn;
                }
            }
            return null;
        }

        // ---- Stable keys and seeds ----

        private static int StableHash(string text)
        {
            unchecked
            {
                uint hash = 2166136261u;
                if (text != null)
                {
                    foreach (char c in text)
                    {
                        hash = (hash ^ c) * 16777619u;
                    }
                }
                return (int)hash;
            }
        }

        /// <summary>One seed per player action, computed by the acting client and sent to every client.</summary>
        private static int Seed(Pawn pawn, string key, int salt)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (pawn?.thingIDNumber ?? 0);
                hash = hash * 31 + (Find.TickManager?.TicksGame ?? 0);
                hash = hash * 31 + StableHash(key);
                hash = hash * 31 + (int)quirkCounterField.GetValue(null);
                return hash * 31 + salt;
            }
        }

        /// <summary>The want's identity without its list position, which differs while a command is queued.</summary>
        private static string WantKey(object want)
        {
            if (want == null)
            {
                return null;
            }
            string target = "-";
            if (wantWithPawnTargetType.IsInstanceOfType(want))
            {
                Pawn pawn = wantPawnTargetField.GetValue(want) as Pawn;
                target = "pawn:" + (pawn == null ? "-" : pawn.thingIDNumber.ToString(CultureInfo.InvariantCulture));
            }
            else if (wantWithDefTargetType.IsInstanceOfType(want))
            {
                Def def = wantDefTargetField.GetValue(want) as Def;
                target = "def:" + (def == null ? "-" : def.GetType().Name + ":" + def.defName);
            }
            else if (wantWithBaselineType.IsInstanceOfType(want))
            {
                float baseline = (float)wantBaselineField.GetValue(want);
                target = "base:" + baseline.ToString("R", CultureInfo.InvariantCulture);
            }
            return NameOf(wantDefField.GetValue(want)) + "|"
                + (int)wantAssignedTickField.GetValue(want) + "|"
                + (int)wantRerollCountField.GetValue(want) + "|" + target;
        }

        private static string NodeKey(object node)
        {
            if (node == null)
            {
                return null;
            }
            Pawn pawn = nodePawnField.GetValue(node) as Pawn;
            return NameOf(nodeDefField.GetValue(node)) + "|"
                + NameOf(nodeItemField.GetValue(node)) + "|"
                + (pawn == null ? "-" : pawn.thingIDNumber.ToString(CultureInfo.InvariantCulture));
        }

        private static object FindWant(Pawn pawn, string key, out int index)
        {
            index = -1;
            if (pawn == null || key == null)
            {
                return null;
            }
            IList wants = Wants(WantsData(pawn));
            if (wants == null)
            {
                return null;
            }
            for (int i = 0; i < wants.Count; i++)
            {
                if (WantKey(wants[i]) == key)
                {
                    index = i;
                    return wants[i];
                }
            }
            return null;
        }

        private static object FindNode(string key)
        {
            IList nodes = Nodes();
            if (nodes == null || key == null)
            {
                return null;
            }
            for (int i = 0; i < nodes.Count; i++)
            {
                if (NodeKey(nodes[i]) == key)
                {
                    return nodes[i];
                }
            }
            return null;
        }

        private static object CapturedValue(object holder, Type type)
        {
            return CapturedValue(holder, type, 3);
        }

        private static object CapturedValue(object holder, Type type, int depth)
        {
            if (holder == null || depth < 0)
            {
                return null;
            }
            FieldInfo[] fields = holder.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (FieldInfo field in fields)
            {
                object value = Read(field, holder);
                if (value != null && type.IsInstanceOfType(value))
                {
                    return value;
                }
            }
            // The removal confirmation nests its display class inside DrawQuirks' own display
            // class, so the pawn sits one level below the callback's target.
            foreach (FieldInfo field in fields)
            {
                object value = Read(field, holder);
                if (value != null && IsClosure(value.GetType()))
                {
                    object found = CapturedValue(value, type, depth - 1);
                    if (found != null)
                    {
                        return found;
                    }
                }
            }
            return null;
        }

        private static object Read(FieldInfo field, object holder)
        {
            try
            {
                return field.GetValue(holder);
            }
            catch
            {
                return null;
            }
        }

        private static bool IsClosure(Type type)
        {
            for (Type current = type; current != null; current = current.DeclaringType)
            {
                if (current.Name.IndexOf("DisplayClass", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        // ---- UI redirects ----

        private static bool RerollWantPrefix(object[] __args)
        {
            if (!InInterface)
            {
                return true;
            }
            Pawn pawn = __args[0] as Pawn;
            object want = __args[2];
            if (pawn == null || want == null)
            {
                return false;
            }
            object def = wantDefField.GetValue(want);
            if (def == null || (bool)wantIsMentalBreakField.GetValue(def))
            {
                return false;
            }
            string key = WantKey(want);
            RerollWant(pawn, key, Seed(pawn, key, RerollSalt));
            return false;
        }

        private static bool CompleteWantPrefix(Pawn pawn, object[] __args)
        {
            if (!InInterface)
            {
                return true;
            }
            object want = __args[2];
            if (pawn != null && want != null)
            {
                CompleteWant(pawn, WantKey(want));
            }
            return false;
        }

        private static bool AddCharacterPointsPrefix(Pawn pawn, int amount)
        {
            if (!InInterface)
            {
                return true;
            }
            AddCharacterPoints(pawn, amount);
            return false;
        }

        private static bool AddQuirkPrefix(object[] __args)
        {
            if (!InInterface)
            {
                return true;
            }
            Pawn pawn = __args[0] as Pawn;
            object def = __args[1];
            object item = __args[2];
            Pawn target = __args[3] as Pawn;
            if (pawn == null || def == null)
            {
                return false;
            }
            string key = NameOf(def) + "|" + NameOf(item) + "|" + (target == null ? "-" : target.thingIDNumber.ToString(CultureInfo.InvariantCulture));
            AddQuirkSeeded(pawn, NameOf(def), NameOf(item), target, Seed(pawn, key, AddQuirkSalt));
            return false;
        }

        private static bool GenerateBubblesPrefix()
        {
            if (!InInterface)
            {
                return true;
            }
            GenerateRewardBubbles(Seed(null, "bubbles:" + (int)globalRewardPointsField.GetValue(null), GenerateBubblesSalt));
            return false;
        }

        private static bool ValidateNodesPrefix()
        {
            if (!InInterface)
            {
                return true;
            }
            ValidateRewardNodes(Seed(null, "validate:" + (Nodes()?.Count ?? 0), ValidateNodesSalt));
            return false;
        }

        private static bool CleanupWantsPrefix()
        {
            if (!InInterface)
            {
                return true;
            }
            CleanupNullTargetWants();
            return false;
        }

        private static bool FreezeBubblePhysics(ref float __result)
        {
            if (!MP.IsInMultiplayer)
            {
                return true;
            }
            __result = 0f;
            return false;
        }

        private static void SaveBubblePositions(out BubblePositions __state)
        {
            __state = null;
            IList nodes = Nodes();
            if (!MP.IsInMultiplayer || nodes == null)
            {
                return;
            }
            __state = new BubblePositions();
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i] != null)
                {
                    __state.Nodes.Add(nodes[i]);
                    __state.Positions.Add((Vector2)nodePosField.GetValue(nodes[i]));
                }
            }
        }

        private static Exception RestoreBubblePositions(Exception __exception, BubblePositions __state)
        {
            if (__state != null)
            {
                for (int i = 0; i < __state.Nodes.Count; i++)
                {
                    nodePosField.SetValue(__state.Nodes[i], __state.Positions[i]);
                }
            }
            return __exception;
        }

        private static IEnumerable<CodeInstruction> RemoveWantTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo removeAt = AccessTools.Method(typeof(List<>).MakeGenericType(activeWantType), "RemoveAt", new[] { typeof(int) })
                ?? throw new MissingMethodException(typeof(List<>).MakeGenericType(activeWantType).FullName, "RemoveAt");
            MethodInfo replacement = AccessTools.Method(typeof(CharacterDevelopment), nameof(RemoveWantFromUiList), new[] { typeof(IList), typeof(int), typeof(Pawn) });
            int count = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (!instruction.Calls(removeAt))
                {
                    yield return instruction;
                    continue;
                }
                count++;
                // DrawWants(Rect rect, Pawn pawn, PawnWantsData data): the stack already holds the
                // list and the index, so the pawn is pushed on top for the replacement.
                yield return new CodeInstruction(OpCodes.Ldarg_2).WithLabels(instruction.labels).WithBlocks(instruction.blocks);
                yield return new CodeInstruction(OpCodes.Call, replacement);
            }
            if (count != 1)
            {
                throw new InvalidOperationException($"Character Development want removal moved: RemoveAt calls={count}");
            }
        }

        private static void RemoveWantFromUiList(IList wants, int index, Pawn pawn)
        {
            if (wants == null || index < 0 || index >= wants.Count)
            {
                return;
            }
            Pawn owner = pawn ?? FindWantsOwner(wants);
            if (owner == null || !InInterface)
            {
                wants.RemoveAt(index);
                return;
            }
            RemoveWant(owner, WantKey(wants[index]));
        }

        private static void PatchRemovalLambda()
        {
            int patched = PatchLambdas("DrawQuirks", method => method.GetParameters().Length == 0, nameof(RemoveQuirkPrefix));
            if (patched != 1)
            {
                Log.Warning($"[MPCompat] Character Development: expected one quirk removal callback, patched {patched}.");
            }
        }

        private static void PatchReorderLambda()
        {
            int patched = PatchLambdas("DrawWants", method =>
            {
                ParameterInfo[] parameters = method.GetParameters();
                return parameters.Length == 2 && parameters[0].ParameterType == typeof(int) && parameters[1].ParameterType == typeof(int);
            }, nameof(ReorderWantPrefix));
            if (patched != 1)
            {
                Log.Warning($"[MPCompat] Character Development: expected one want reorder callback, patched {patched}.");
            }
        }

        private static int PatchLambdas(string marker, Func<MethodInfo, bool> filter, string prefix)
        {
            int patched = 0;
            foreach (Type nested in itabType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                foreach (MethodInfo method in nested.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method.ReturnType != typeof(void) || method.Name.IndexOf(marker, StringComparison.Ordinal) < 0 || !filter(method))
                    {
                        continue;
                    }
                    MpCompat.harmony.Patch(method, prefix: Hook(prefix));
                    patched++;
                }
            }
            return patched;
        }

        private static bool RemoveQuirkPrefix(object __instance)
        {
            if (!InInterface)
            {
                return true;
            }
            Pawn pawn = CapturedValue(__instance, typeof(Pawn)) as Pawn;
            object quirk = CapturedValue(__instance, quirkType);
            if (pawn == null || quirk == null)
            {
                Log.Warning("[MPCompat] Character Development: could not resolve the pawn and quirk behind the removal confirmation.");
                return false;
            }
            RemoveQuirk(pawn, (int)quirkLoadIdField.GetValue(quirk));
            return false;
        }

        private static bool ReorderWantPrefix(object __instance, int __0, int __1)
        {
            if (!InInterface)
            {
                return true;
            }
            object data = CapturedValue(__instance, wantsDataType);
            IList wants = Wants(data);
            if (wants == null || __0 < 0 || __0 >= wants.Count)
            {
                Log.Warning("[MPCompat] Character Development: could not resolve the want list behind the reorder callback.");
                return false;
            }
            Pawn pawn = FindWantsOwner(wants);
            if (pawn == null)
            {
                Log.Warning("[MPCompat] Character Development: could not resolve the pawn owning a reordered want.");
                return false;
            }
            ReorderWant(pawn, WantKey(wants[__0]), __1);
            return false;
        }

        // ---- Replacement reward dialog ----

        private static bool DrawRewardDialog(Window __instance, Rect inRect)
        {
            if (!MP.IsInMultiplayer)
            {
                return true;
            }
            object node = dialogNodeField.GetValue(__instance);
            object def = node == null ? null : nodeDefField.GetValue(node);
            IList recipients = dialogRecipientsField.GetValue(__instance) as IList;
            bool valid = def != null && recipients != null
                && (!(bool)rewardRequiresItemField.GetValue(def) || nodeItemField.GetValue(node) != null)
                && (!(bool)rewardRequiresPawnField.GetValue(def) || nodePawnField.GetValue(node) != null);
            if (!valid)
            {
                // The mod's own dialog would fail on this node; the node is cleaned up by
                // ValidateRewardNodes, so just stop here instead of claiming something stale.
                Log.ErrorOnce("[MPCompat] Character Development: the reward dialog opened for an unreadable reward.", 728611);
                __instance.Close();
                return false;
            }
            string key = NodeKey(node);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 35f), "WQ_BestowReward".Translate());
            Text.Font = GameFont.Small;
            string description = (string)nodeDescriptionProperty.GetValue(node, null) ?? string.Empty;
            float descriptionHeight = Text.CalcHeight(description, inRect.width);
            Widgets.Label(new Rect(0f, 35f, inRect.width, descriptionHeight), description);

            float top = 35f + descriptionHeight + 10f;
            Rect listRect = new Rect(0f, top, inRect.width, inRect.height - top - 50f);
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, recipients.Count * 50f);
            Vector2 scroll = (Vector2)dialogScrollField.GetValue(__instance);
            bool claimed = false;
            Widgets.BeginScrollView(listRect, ref scroll, viewRect);
            float y = 0f;
            foreach (object entry in recipients)
            {
                if (!(entry is Pawn pawn) || pawn == null)
                {
                    continue;
                }
                Rect row = new Rect(0f, y, viewRect.width, 46f);
                if (!claimed && Widgets.ButtonInvisible(row))
                {
                    ClaimReward(pawn, key, Seed(pawn, key, ClaimRewardSalt));
                    SoundDefOf.Quest_Succeded.PlayOneShotOnCamera();
                    claimed = true;
                }
                Widgets.DrawOptionBackground(row, false);
                Rect portrait = new Rect(row.x + 4f, row.y + 3f, 40f, 40f);
                GUI.DrawTexture(portrait, PortraitsCache.Get(pawn, new Vector2(40f, 40f), Rot4.South));
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(portrait.xMax + 10f, row.y, row.width - portrait.width - 20f, row.height), pawn.LabelShortCap);
                Text.Anchor = TextAnchor.UpperLeft;
                y += 50f;
            }
            Widgets.EndScrollView();
            dialogScrollField.SetValue(__instance, scroll);

            if (claimed)
            {
                __instance.Close();
                return false;
            }
            if (Widgets.ButtonText(new Rect(inRect.width - 100f, inRect.height - 40f, 100f, 35f), "CloseButton".Translate()))
            {
                __instance.Close();
            }
            return false;
        }

        // ---- Synced actions ----

        private static void RerollWant(Pawn pawn, string wantKey, int seed)
        {
            object want = FindWant(pawn, wantKey, out _);
            if (want == null)
            {
                return;
            }
            object def = wantDefField.GetValue(want);
            if (def == null || (bool)wantIsMentalBreakField.GetValue(def))
            {
                return;
            }
            object data = WantsData(pawn);
            if (data == null)
            {
                return;
            }
            Rand.PushState(seed);
            try
            {
                rerollWantMethod.Invoke(null, new[] { pawn, data, want });
            }
            finally
            {
                Rand.PopState();
            }
        }

        private static void RemoveWant(Pawn pawn, string wantKey)
        {
            object want = FindWant(pawn, wantKey, out int index);
            if (want == null || index < 0)
            {
                return;
            }
            object def = wantDefField.GetValue(want);
            if (def != null && (bool)wantIsMentalBreakField.GetValue(def))
            {
                return;
            }
            Wants(WantsData(pawn))?.RemoveAt(index);
        }

        private static void ReorderWant(Pawn pawn, string wantKey, int to)
        {
            object want = FindWant(pawn, wantKey, out int index);
            IList wants = Wants(WantsData(pawn));
            if (want == null || wants == null || index < 0)
            {
                return;
            }
            int target = Math.Max(0, Math.Min(to, wants.Count));
            wants.Insert(target, want);
            wants.RemoveAt(index < target ? index : index + 1);
        }

        private static void RemoveQuirk(Pawn pawn, int loadId)
        {
            if (pawn == null || pawn.Destroyed)
            {
                return;
            }
            IList list = quirksField.GetValue(WantsData(pawn)) as IList;
            if (list == null)
            {
                return;
            }
            for (int i = 0; i < list.Count; i++)
            {
                object quirk = list[i];
                if (quirk == null || (int)quirkLoadIdField.GetValue(quirk) != loadId)
                {
                    continue;
                }
                object worker = Worker(quirkDefField.GetValue(quirk));
                if (worker != null)
                {
                    workerOnRemovedMethod.Invoke(worker, new[] { pawn, quirk });
                }
                list.RemoveAt(i);
                return;
            }
        }

        private static void AddQuirkSeeded(Pawn pawn, string rewardDef, string itemDef, Pawn targetPawn, int seed)
        {
            if (pawn == null || pawn.Destroyed || rewardDef.NullOrEmpty())
            {
                return;
            }
            Def def = GenDefDatabase.GetDefSilentFail(rewardDefType, rewardDef);
            ThingDef item = itemDef.NullOrEmpty() ? null : GenDefDatabase.GetDefSilentFail(typeof(ThingDef), itemDef) as ThingDef;
            if (def == null)
            {
                return;
            }
            Rand.PushState(seed);
            try
            {
                addQuirkMethod.Invoke(null, new object[] { pawn, def, item, targetPawn });
            }
            finally
            {
                Rand.PopState();
            }
        }

        private static void CompleteWant(Pawn pawn, string wantKey)
        {
            object want = FindWant(pawn, wantKey, out _);
            object data = WantsData(pawn);
            if (want == null || data == null)
            {
                return;
            }
            completeWantMethod.Invoke(null, new[] { pawn, data, want });
        }

        private static void AddCharacterPoints(Pawn pawn, int amount)
        {
            if (pawn != null && pawn.Destroyed)
            {
                return;
            }
            addCharacterPointsMethod.Invoke(null, new object[] { pawn, amount });
        }

        private static void GenerateRewardBubbles(int seed)
        {
            Rand.PushState(seed);
            try
            {
                generateBubblesMethod.Invoke(null, null);
            }
            finally
            {
                Rand.PopState();
            }
        }

        private static void ValidateRewardNodes(int seed)
        {
            IList nodes = Nodes();
            if (nodes == null)
            {
                rewardNodesField.SetValue(null, nodes = (IList)Activator.CreateInstance(rewardNodesField.FieldType));
            }
            Rand.PushState(seed);
            try
            {
                for (int i = nodes.Count - 1; i >= 0; i--)
                {
                    object node = nodes[i];
                    object def = node == null ? null : nodeDefField.GetValue(node);
                    bool invalid = node == null || def == null
                        || ((bool)rewardRequiresItemField.GetValue(def) && nodeItemField.GetValue(node) == null)
                        || ((bool)rewardRequiresPawnField.GetValue(def) && nodePawnField.GetValue(node) == null);
                    if (!invalid)
                    {
                        continue;
                    }
                    nodes.RemoveAt(i);
                    object replacement = generateSingleBubbleMethod.Invoke(null, new object[] { nodes });
                    if (replacement != null)
                    {
                        nodes.Add(replacement);
                    }
                }
            }
            finally
            {
                Rand.PopState();
            }
        }

        private static void CleanupNullTargetWants()
        {
            foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_OfPlayerFaction)
            {
                if (!CanHaveWants(pawn))
                {
                    continue;
                }
                IList wants = Wants(WantsData(pawn));
                if (wants == null)
                {
                    continue;
                }
                for (int i = wants.Count - 1; i >= 0; i--)
                {
                    object want = wants[i];
                    if (want != null && wantWithPawnTargetType.IsInstanceOfType(want) && wantPawnTargetField.GetValue(want) == null)
                    {
                        wants.RemoveAt(i);
                    }
                }
            }
        }

        private static void ClaimReward(Pawn recipient, string nodeKey, int seed)
        {
            if (!CanHaveWants(recipient))
            {
                return;
            }
            object node = FindNode(nodeKey);
            if (node == null)
            {
                return;
            }
            object def = nodeDefField.GetValue(node);
            object item = nodeItemField.GetValue(node);
            Pawn target = nodePawnField.GetValue(node) as Pawn;
            object worker = Worker(def);
            if (worker == null || !(bool)workerCanBestowOnMethod.Invoke(worker, new[] { recipient, item, target }))
            {
                return;
            }
            if (UsePawnRewardPoints())
            {
                object data = WantsData(recipient);
                int points = data == null ? 0 : (int)rewardPointsField.GetValue(data);
                if (points <= 0)
                {
                    return;
                }
                rewardPointsField.SetValue(data, points - 1);
            }
            else
            {
                int points = (int)globalRewardPointsField.GetValue(null);
                if (points <= 0)
                {
                    return;
                }
                globalRewardPointsField.SetValue(null, points - 1);
            }

            Rand.PushState(seed);
            try
            {
                addQuirkMethod.Invoke(null, new object[] { recipient, def, item, target });
            }
            finally
            {
                Rand.PopState();
            }

            Rand.PushState(seed ^ RecycleBubblesSalt);
            try
            {
                if (RerollBubblesOnClaim())
                {
                    generateBubblesMethod.Invoke(null, null);
                    return;
                }
                IList nodes = Nodes();
                if (nodes != null)
                {
                    nodes.Remove(FindNode(nodeKey));
                    object replacement = generateSingleBubbleMethod.Invoke(null, new object[] { nodes });
                    if (replacement != null)
                    {
                        nodes.Add(replacement);
                    }
                }
            }
            finally
            {
                Rand.PopState();
            }
        }
    }
}
