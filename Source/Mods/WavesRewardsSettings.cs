using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat
{
    /// <summary>Storyteller: Waves &amp; Rewards (Ruslan.WavesRewards) settings.</summary>
    /// <remarks>
    /// The simulation reads WavesRewardsMod.Settings continuously. Multiplayer copies the host's
    /// settings at join, but a later change in the settings window (or the "who comes" dialog)
    /// changed one PC only, and even while a slider moved. In a multiplayer game the two windows
    /// edit a copy; when the settings window closes, the fields that player actually changed are
    /// sent as one command and every PC applies and saves them. Language and the verbose-log switch
    /// stay per PC. Behaviour reference: Multiplayer Fix (MIT); this is a reimplementation.
    /// </remarks>
    [MpCompatFor("ruslan.wavesrewards")]
    class WavesRewardsSettings
    {
        private const char ListSeparator = '\u001f';

        private static Type modType;
        private static Type settingsType;
        private static Type whoComesType;
        private static FieldInfo settingsField;

        private static readonly List<FieldInfo> synced = new List<FieldInfo>();
        private static readonly List<FieldInfo> local = new List<FieldInfo>();

        private static object live;
        private static object edit;
        private static Dictionary<string, string> baseline;
        private static int swapDepth;

        public WavesRewardsSettings(ModContentPack mod)
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
                Log.Error("[MPCompat] Waves & Rewards settings: install failed; module disabled.\n" + e);
            }
        }

        private static void Install()
        {
            modType = WavesRewardsShared.Type("WavesRewardsMod");
            settingsType = WavesRewardsShared.Type("WavesRewardsSettings");
            whoComesType = WavesRewardsShared.Type("Waves.Dialog_WhoComes");
            if (modType == null || settingsType == null)
                throw new TypeLoadException("Waves & Rewards settings types missing");

            settingsField = WavesRewardsShared.Field(modType, "Settings");
            if (settingsField == null)
                throw new MissingFieldException(modType.FullName, "Settings");

            foreach (FieldInfo field in settingsType.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.Name == "modLanguage" || field.Name == "verboseLog")
                    local.Add(field);
                else if (Supported(field.FieldType))
                    synced.Add(field);
            }
            if (synced.Count == 0)
                throw new MissingFieldException(settingsType.FullName, "synced settings");

            Patch(WavesRewardsShared.Method(modType, "DoSettingsWindowContents"),
                prefix: nameof(SettingsWindowPrefix), finalizer: nameof(SwapOutFinalizer));
            Patch(WavesRewardsShared.Method(whoComesType, "DoWindowContents"),
                prefix: nameof(SwapInIfEditingPrefix), finalizer: nameof(SwapOutFinalizer));
            Patch(WavesRewardsShared.Method(whoComesType, "PostClose"),
                prefix: nameof(SwapInIfEditingPrefix), finalizer: nameof(SwapOutFinalizer));
            Patch(AccessTools.Method(typeof(Dialog_ModSettings), "PreClose"),
                postfix: nameof(ModSettingsPreClosePostfix));

            MP.RegisterSyncMethod(typeof(WavesRewardsSettings), nameof(SyncedApply));
        }

        private static void Patch(MethodInfo method, string prefix = null, string postfix = null, string finalizer = null)
        {
            if (method == null)
                throw new MissingMethodException("Waves & Rewards settings", "target");
            MpCompat.harmony.Patch(method,
                prefix: prefix == null ? null : new HarmonyMethod(typeof(WavesRewardsSettings), prefix),
                postfix: postfix == null ? null : new HarmonyMethod(typeof(WavesRewardsSettings), postfix),
                finalizer: finalizer == null ? null : new HarmonyMethod(typeof(WavesRewardsSettings), finalizer));
        }

        private static bool Supported(Type type) =>
            type == typeof(float) || type == typeof(double) || type == typeof(int) || type == typeof(bool)
            || type == typeof(string) || type.IsEnum || type == typeof(HashSet<string>);

        private static bool InMultiplayerGame =>
            MP.IsInMultiplayer && Current.ProgramState == ProgramState.Playing;

        // ---- Editing a copy while the window draws ----

        private static void SettingsWindowPrefix()
        {
            if (!InMultiplayerGame)
                return;
            if (edit == null || live != settingsField.GetValue(null))
                BeginEdit();
            SwapIn();
        }

        private static void SwapInIfEditingPrefix()
        {
            if (edit != null && InMultiplayerGame)
                SwapIn();
        }

        private static void SwapIn()
        {
            if (swapDepth++ == 0)
                settingsField.SetValue(null, edit);
        }

        private static void SwapOutFinalizer()
        {
            if (swapDepth > 0 && --swapDepth == 0)
                settingsField.SetValue(null, live);
        }

        private static void BeginEdit()
        {
            live = settingsField.GetValue(null);
            edit = Activator.CreateInstance(settingsType, true);
            for (Type type = settingsType; type != null && type != typeof(object); type = type.BaseType)
            {
                foreach (FieldInfo field in type.GetFields(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    object value = field.GetValue(live);
                    field.SetValue(edit, value is HashSet<string> set ? new HashSet<string>(set) : value);
                }
            }
            baseline = Snapshot(live);
        }

        private static void ModSettingsPreClosePostfix(Dialog_ModSettings __instance)
        {
            if (edit == null)
                return;
            try
            {
                if (whoComesType != null && Find.WindowStack != null)
                {
                    foreach (Window window in Find.WindowStack.Windows.ToList())
                        if (whoComesType.IsInstanceOfType(window))
                            window.Close(false);
                }
                EndEdit();
            }
            catch (Exception e)
            {
                Log.Error("[MPCompat] Waves & Rewards settings: close failed. " + e);
            }
            finally
            {
                edit = null;
                baseline = null;
                swapDepth = 0;
                if (live != null)
                    settingsField.SetValue(null, live);
            }
        }

        private static void EndEdit()
        {
            if (!InMultiplayerGame || live == null)
                return;

            bool languageChanged = false;
            foreach (FieldInfo field in local)
            {
                object value = field.GetValue(edit);
                if (Equals(value, field.GetValue(live)))
                    continue;
                languageChanged |= field.Name == "modLanguage";
                field.SetValue(live, value);
            }

            Dictionary<string, string> now = Snapshot(edit);
            StringBuilder diff = new StringBuilder();
            foreach (KeyValuePair<string, string> entry in now)
                if (!baseline.TryGetValue(entry.Key, out string before) || before != entry.Value)
                    diff.Append(entry.Key).Append('=').Append(entry.Value).Append('\n');

            if (languageChanged && modType != null)
            {
                try
                {
                    AccessTools.Method(modType.Assembly.GetType("WavesRewards.ModLanguage"), "Apply")
                        ?.Invoke(null, null);
                }
                catch
                {
                    // language switch is per PC and not worth failing the edit for
                }
            }

            if (diff.Length > 0)
                SyncedApply(diff.ToString());
            else if (languageChanged)
                ((ModSettings)live).Write();
        }

        // ---- Synced apply ----

        /// <summary>Every PC: apply the changed values to the live settings and save.</summary>
        private static void SyncedApply(string diff)
        {
            object target = live ?? settingsField.GetValue(null);
            if (swapDepth > 0 || target == edit)
                target = live;
            if (target == null)
                return;

            Dictionary<string, FieldInfo> byName = synced.ToDictionary(field => field.Name);
            foreach (string line in diff.Split('\n'))
            {
                int split = line.IndexOf('=');
                if (split <= 0 || !byName.TryGetValue(line.Substring(0, split), out FieldInfo field))
                    continue;
                string text = line.Substring(split + 1);
                try
                {
                    SetFromText(target, field, text);
                    if (edit != null && baseline != null
                        && baseline.TryGetValue(field.Name, out string before)
                        && ToText(field.GetValue(edit)) == before)
                    {
                        SetFromText(edit, field, text);
                        baseline[field.Name] = text;
                    }
                }
                catch (Exception e)
                {
                    Log.Warning("[MPCompat] Waves & Rewards setting " + field.Name + " = " + text
                        + " not applied: " + e.Message);
                }
            }

            try
            {
                ((ModSettings)target).Write();
            }
            catch (Exception e)
            {
                Log.Warning("[MPCompat] Waves & Rewards settings could not be saved: " + e.Message);
            }
        }

        private static Dictionary<string, string> Snapshot(object settings)
        {
            Dictionary<string, string> values = new Dictionary<string, string>();
            foreach (FieldInfo field in synced)
                values[field.Name] = ToText(field.GetValue(settings));
            return values;
        }

        private static string ToText(object value)
        {
            switch (value)
            {
                case null:
                    return "\0";
                case float number:
                    return number.ToString("R", CultureInfo.InvariantCulture);
                case double number:
                    return number.ToString("R", CultureInfo.InvariantCulture);
                case HashSet<string> set:
                    return string.Join(ListSeparator.ToString(), set.OrderBy(x => x, StringComparer.Ordinal).ToArray());
                default:
                    return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }

        private static void SetFromText(object settings, FieldInfo field, string text)
        {
            Type type = field.FieldType;
            if (type == typeof(HashSet<string>))
            {
                HashSet<string> set = field.GetValue(settings) as HashSet<string>;
                if (set == null)
                {
                    set = new HashSet<string>();
                    field.SetValue(settings, set);
                }
                set.Clear();
                foreach (string item in text.Split(ListSeparator))
                    if (item.Length > 0)
                        set.Add(item);
                return;
            }

            object value;
            if (text == "\0")
                value = null;
            else if (type == typeof(float))
                value = float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
            else if (type == typeof(double))
                value = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
            else if (type == typeof(int))
                value = int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
            else if (type == typeof(bool))
                value = bool.Parse(text);
            else if (type.IsEnum)
                value = Enum.Parse(type, text);
            else
                value = text;
            field.SetValue(settings, value);
        }
    }
}
