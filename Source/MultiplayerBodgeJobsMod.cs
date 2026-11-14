using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;
using Verse.AI;

namespace MultiplayerBodgeJobs
{
    // Syncs the weapon customization commit for Persona Weapons Unbound and Unique Weapons Unbound.
    //
    // Both mods start the customize job through Pawn_JobTracker.TryTakeOrderedJob, which Multiplayer
    // already syncs. The dialog then commits the chosen spec by calling SetSpec on the running driver,
    // and that call only happens on the client that pressed Confirm. SetSpec has one caller in each mod,
    // so patching it covers both the gizmo and float-menu paths.
    [StaticConstructorOnStartup]
    public static class MultiplayerBodgeJobsMod
    {
        private const string PwuPackageId = "shunter.personaweaponsunbound";
        private const string UwuPackageId = "shunter.uniqueweaponsunbound";

        private const string HarmonyId = "zap.multiplayerbodgejobs";

        private static FieldInfo SpecFieldPwu;
        private static FieldInfo SpecFieldUwu;

        static MultiplayerBodgeJobsMod()
        {
            // Registration happens once at startup, before MP's PostInitHandlers, and is reused for
            // every session. It must not be gated on IsInMultiplayer, which is false at this point.
            if (ModsConfig.IsActive(PwuPackageId))
            {
                RegisterFor(
                    "PersonaWeaponsUnbound.JobDriver_CustomizeWeapon",
                    "PersonaWeaponsUnbound.CustomizationSpec",
                    nameof(CommitSpecPwu));
            }

            if (ModsConfig.IsActive(UwuPackageId))
            {
                RegisterFor(
                    "UniqueWeaponsUnbound.JobDriver_CustomizeWeapon",
                    "UniqueWeaponsUnbound.CustomizationSpec",
                    nameof(CommitSpecUwu));
            }
        }

        private static void RegisterFor(string driverTypeName, string specTypeName, string commitMethodName)
        {
            Type driverType = AccessTools.TypeByName(driverTypeName);
            Type specType = AccessTools.TypeByName(specTypeName);
            if (driverType == null || specType == null)
            {
                Log.Warning($"[MultiplayerBodgeJobs] Could not resolve {driverTypeName} / {specTypeName}; customization commits will not sync.");
                return;
            }

            MethodInfo setSpec = AccessTools.Method(driverType, "SetSpec", new[] { specType });
            if (setSpec == null)
            {
                Log.Warning($"[MultiplayerBodgeJobs] {driverTypeName}.SetSpec({specTypeName}) not found; customization commits will not sync.");
                return;
            }

            FieldInfo specField = AccessTools.Field(driverType, "spec");
            if (specField == null)
            {
                Log.Warning($"[MultiplayerBodgeJobs] {driverTypeName}.spec not found; customization commits will not sync.");
                return;
            }

            MethodInfo commit = AccessTools.Method(typeof(MultiplayerBodgeJobsMod), commitMethodName);
            // The spec is a plain IExposable with no SyncWorker, so it must travel by Scribe round-trip.
            // The pawn is a Thing and is covered by MP's built-in worker.
            MP.RegisterSyncMethod(commit, new[] { new SyncType(typeof(Pawn)), new SyncType(specType) })
                .SetContext(SyncContext.QueueOrder_Down)
                .ExposeParameter(1);

            if (commitMethodName == nameof(CommitSpecPwu))
            {
                SpecFieldPwu = specField;
            }
            else
            {
                SpecFieldUwu = specField;
            }

            new Harmony(HarmonyId).Patch(
                setSpec,
                prefix: new HarmonyMethod(typeof(MultiplayerBodgeJobsMod), nameof(SetSpecPrefix)));

            Log.Message($"[MultiplayerBodgeJobs] Syncing {driverTypeName}.SetSpec in multiplayer.");
        }

        // Runs on the client that pressed Confirm. In multiplayer the local apply is replaced by a
        // synced call so every client sets the same spec on its own copy of the driver.
        private static bool SetSpecPrefix(object __instance, object s)
        {
            if (!MP.IsInMultiplayer || !MP.InInterface)
            {
                return true;
            }

            Pawn pawn = ((JobDriver)__instance).pawn;
            if (pawn == null)
            {
                return true;
            }

            if (__instance is PersonaWeaponsUnbound.JobDriver_CustomizeWeapon)
            {
                CommitSpecPwu(pawn, (PersonaWeaponsUnbound.CustomizationSpec)s);
            }
            else if (__instance is UniqueWeaponsUnbound.JobDriver_CustomizeWeapon)
            {
                CommitSpecUwu(pawn, (UniqueWeaponsUnbound.CustomizationSpec)s);
            }
            else
            {
                return true;
            }

            return false;
        }

        private static void CommitSpecPwu(Pawn pawn, PersonaWeaponsUnbound.CustomizationSpec spec)
        {
            if (pawn.jobs?.curDriver is PersonaWeaponsUnbound.JobDriver_CustomizeWeapon driver)
            {
                // SetSpec is the patched method; calling it here would re-enter the prefix.
                SpecFieldPwu?.SetValue(driver, spec);
            }
        }

        private static void CommitSpecUwu(Pawn pawn, UniqueWeaponsUnbound.CustomizationSpec spec)
        {
            if (pawn.jobs?.curDriver is UniqueWeaponsUnbound.JobDriver_CustomizeWeapon driver)
            {
                // Same re-entry reason as CommitSpecPwu.
                SpecFieldUwu?.SetValue(driver, spec);
            }
        }
    }
}
