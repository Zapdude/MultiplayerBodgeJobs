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
    /// <summary>Vanilla Expanded Framework mercenary contracts (Dialog_ContractInfo).</summary>
    /// <remarks>
    /// Two problems in the contract window. Multiplayer Compatibility syncs a piece of
    /// Dialog_ContractInfo by its position among the window's compiler-generated methods; after a
    /// VEF update that position is the pawn-list filter, so every frame the window tried to send a
    /// filter through Multiplayer and failed (no writer for the display class), and the window was
    /// not drawn. Separately, "Cancel contract" only ended the contract on the PC that clicked.
    ///
    /// Here the filter just filters, and cancelling is one synced command that sets the tracker's
    /// endTicks and closes the window everywhere. The static ideology cache used for hired pawns is
    /// cleared at every load, and the developer-mode save check no longer warns about the
    /// Hireable_ objects that VEF rebuilds before each load. Behaviour reference: Multiplayer Fix
    /// (MIT); this is a reimplementation.
    /// </remarks>
    [MpCompatFor("oskarpotocki.vanillafactionsexpanded.core")]
    class VefHiring
    {
        private static Type contractInfoType;
        private static Type trackerType;
        private static Type hireType;

        private static FieldInfo trackerInstanceField;
        private static FieldInfo endTicksField;
        private static FieldInfo hiredIdeoField;
        private static FieldInfo hireablesField;
        private static FieldInfo deepSavedField;

        private static bool cancelSyncReady;

        private static readonly List<string> applied = new List<string>();
        private static readonly List<string> missing = new List<string>();

        public VefHiring(ModContentPack mod)
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
                Log.Error("[MPCompat] VEF hiring: install failed; module disabled.\n" + e);
            }
        }

        private static void Install()
        {
            contractInfoType = AccessTools.TypeByName("VEF.Planet.Dialog_ContractInfo");
            trackerType = AccessTools.TypeByName("VEF.Planet.HiringContractTracker");
            hireType = AccessTools.TypeByName("VEF.Planet.Dialog_Hire");
            if (contractInfoType == null || trackerType == null)
            {
                Log.Message("[MPCompat] VEF hiring: no hiring contract in this VEF; skipped.");
                return;
            }

            trackerInstanceField = AccessTools.Field(trackerType, "Instance");
            endTicksField = AccessTools.Field(trackerType, "endTicks");
            hiredIdeoField = hireType == null ? null : AccessTools.Field(hireType, "hiredIdeo");

            MethodInfo filter = FindFilter();
            if (filter != null)
            {
                MpCompat.harmony.Patch(filter, prefix: First(nameof(FilterPrefix)));
                applied.Add("contract list filter");
            }
            else
            {
                missing.Add("list filter");
            }

            MethodInfo cancel = FindCancel();
            if (cancel != null && trackerInstanceField != null && endTicksField != null)
            {
                try
                {
                    MP.RegisterSyncMethod(typeof(VefHiring), nameof(SyncedCancelContract));
                    cancelSyncReady = true;
                }
                catch (Exception e)
                {
                    missing.Add("cancel sync (" + e.GetType().Name + ")");
                }
                if (cancelSyncReady)
                {
                    MpCompat.harmony.Patch(cancel, prefix: First(nameof(CancelPrefix)));
                    applied.Add("cancel contract sync");
                }
            }
            else
            {
                missing.Add("cancel lambda");
            }

            if (hiredIdeoField != null && hiredIdeoField.IsStatic)
            {
                MethodInfo finalize = AccessTools.Method(typeof(Game), nameof(Game.FinalizeInit));
                if (finalize != null)
                {
                    MpCompat.harmony.Patch(finalize, postfix: Hook(nameof(ResetHiredIdeo)));
                    applied.Add("hired ideology reset");
                }
                else
                {
                    missing.Add("Game.FinalizeInit");
                }
            }
            else
            {
                missing.Add("Dialog_Hire.hiredIdeo");
            }

            Type statics = AccessTools.TypeByName("VEF.Planet.HireableSystemStaticInitialization");
            hireablesField = statics == null ? null : AccessTools.Field(statics, "Hireables");
            deepSavedField = AccessTools.Field(typeof(DebugLoadIDsSavingErrorsChecker), "deepSaved");
            MethodInfo check = AccessTools.Method(typeof(DebugLoadIDsSavingErrorsChecker),
                nameof(DebugLoadIDsSavingErrorsChecker.CheckForErrorsAndClear));
            if (hireablesField != null && hireablesField.IsStatic && deepSavedField != null && check != null)
            {
                MpCompat.harmony.Patch(check, prefix: Hook(nameof(HireablesCountAsSaved)));
                applied.Add("dev-mode hireable save check");
            }
            else
            {
                missing.Add("hireable save check");
            }

            string detail = missing.Count == 0 ? "" : " Missing: " + string.Join(", ", missing) + ".";
            Log.Message("[MPCompat] VEF hiring: " + string.Join(", ", applied) + "." + detail);
        }

        private static HarmonyMethod Hook(string name) => new HarmonyMethod(typeof(VefHiring), name);

        private static HarmonyMethod First(string name) => new HarmonyMethod(typeof(VefHiring), name)
        {
            priority = Priority.First
        };

        /// <summary>The window's pawn-list filter: <c>(Pawn x) =&gt; x != null</c>, compiled into its display class.</summary>
        private static MethodInfo FindFilter()
        {
            foreach (Type nested in contractInfoType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                foreach (MethodInfo method in AccessTools.GetDeclaredMethods(nested))
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    if (!method.Name.StartsWith("<DoWindowContents>", StringComparison.Ordinal)
                        || method.ReturnType != typeof(bool)
                        || parameters.Length != 1
                        || parameters[0].ParameterType != typeof(Pawn))
                        continue;

                    List<CodeInstruction> code = PatchProcessor.GetOriginalInstructions(method);
                    if (code.Count == 4
                        && code[0].opcode == OpCodes.Ldarg_1
                        && code[1].opcode == OpCodes.Ldnull
                        && code[2].opcode == OpCodes.Cgt_Un
                        && code[3].opcode == OpCodes.Ret)
                        return method;
                }
            }
            return null;
        }

        /// <summary>The confirmation's code piece that sets HiringContractTracker.endTicks.</summary>
        private static MethodInfo FindCancel()
        {
            if (endTicksField == null)
                return null;

            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(contractInfoType))
            {
                if (!method.Name.StartsWith("<DoWindowContents>", StringComparison.Ordinal)
                    || method.ReturnType != typeof(void)
                    || method.GetParameters().Length != 0
                    || method.IsStatic)
                    continue;

                foreach (KeyValuePair<OpCode, object> instruction in PatchProcessor.ReadMethodBody(method))
                {
                    if (instruction.Key == OpCodes.Stfld && instruction.Value is FieldInfo field
                        && field.MetadataToken == endTicksField.MetadataToken
                        && field.Module == endTicksField.Module)
                        return method;
                }
            }
            return null;
        }

        private static bool FilterPrefix(object __0, ref bool __result)
        {
            __result = __0 != null;
            return false;
        }

        private static bool CancelPrefix()
        {
            if (!cancelSyncReady || !MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
                return true;
            SyncedCancelContract();
            return false;
        }

        /// <summary>Every PC: end the contract now and close the contract window.</summary>
        private static void SyncedCancelContract()
        {
            object tracker = trackerInstanceField?.GetValue(null);
            if (tracker != null && endTicksField != null)
                endTicksField.SetValue(tracker, Find.TickManager.TicksAbs);

            WindowStack stack = Find.WindowStack;
            if (stack == null)
                return;
            foreach (Window window in new List<Window>(stack.Windows))
                if (window != null && window.GetType() == contractInfoType)
                    stack.TryRemove(window);
        }

        private static void ResetHiredIdeo()
        {
            try
            {
                hiredIdeoField?.SetValue(null, null);
            }
            catch
            {
                // only a stale cache is at stake
            }
        }

        /// <summary>
        /// VEF rebuilds the Hireable_ objects before every load, so they are legitimately not saved;
        /// the developer-mode check otherwise warns at every save and every join point.
        /// </summary>
        private static void HireablesCountAsSaved(DebugLoadIDsSavingErrorsChecker __instance)
        {
            if (!Prefs.DevMode)
                return;
            try
            {
                if (!(hireablesField.GetValue(null) is IEnumerable hireables)
                    || !(deepSavedField.GetValue(__instance) is HashSet<string> deepSaved))
                    return;
                foreach (object hireable in hireables)
                    if (hireable is ILoadReferenceable referenceable)
                        deepSaved.Add(referenceable.GetUniqueLoadID());
            }
            catch
            {
                // only a developer-mode warning is at stake
            }
        }
    }
}
