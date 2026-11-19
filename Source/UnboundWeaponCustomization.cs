using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;
using Verse.AI;

namespace Multiplayer.Compat
{
    /// <summary>Persona Weapons Unbound and Unique Weapons Unbound by Sam Hunt</summary>
    /// <remarks>
    /// Both mods commit the chosen spec through <c>JobDriver_CustomizeWeapon.SetSpec</c>, on the
    /// client that pressed Confirm only. That single caller is the sync point, and the spec travels
    /// by Scribe because it has no SyncWorker.
    ///
    /// The dialog's ingredient reservation, haul plan and close are deferred into the same command;
    /// otherwise the confirming client finishes a tick early and the others keep waiting.
    /// </remarks>
    [MpCompatFor("shunter.personaweaponsunbound")]
    [MpCompatFor("shunter.uniqueweaponsunbound")]
    public class UnboundWeaponCustomization
    {
        private sealed class Target
        {
            internal Type DriverType;
            internal Type SpecType;
            internal FieldInfo SpecField;
            internal FieldInfo CostField;
            internal MethodInfo Reserve;
            internal MethodInfo IsSuccess;
            internal MethodInfo Commit;
        }

        private static Target persona;
        private static Target unique;

        public UnboundWeaponCustomization(ModContentPack mod)
        {
            string packageId = mod.PackageId.NoModIdSuffix();

            if (packageId == "shunter.personaweaponsunbound")
            {
                persona = Install(packageId,
                    "PersonaWeaponsUnbound.JobDriver_CustomizeWeapon",
                    "PersonaWeaponsUnbound.CustomizationSpec",
                    "PersonaWeaponsUnbound.Dialog_WeaponCustomization",
                    nameof(CommitPersonaSpec));
            }
            else if (packageId == "shunter.uniqueweaponsunbound")
            {
                unique = Install(packageId,
                    "UniqueWeaponsUnbound.JobDriver_CustomizeWeapon",
                    "UniqueWeaponsUnbound.CustomizationSpec",
                    "UniqueWeaponsUnbound.Dialog_WeaponCustomization",
                    nameof(CommitUniqueSpec));
            }
        }

        private static Target Install(string packageId, string driverName, string specName, string dialogName, string commitName)
        {
            Type driverType = AccessTools.TypeByName(driverName);
            Type specType = AccessTools.TypeByName(specName);
            if (driverType == null || specType == null)
            {
                Log.Warning($"[MPCompat] Unbound weapons: could not resolve {driverName} / {specName}; customization commits will not sync.");
                return null;
            }

            MethodInfo setSpec = AccessTools.Method(driverType, "SetSpec", new[] { specType });
            FieldInfo specField = AccessTools.Field(driverType, "spec");
            if (setSpec == null || specField == null)
            {
                Log.Warning($"[MPCompat] Unbound weapons: {driverName} changed; customization commits will not sync.");
                return null;
            }

            Type dialogType = AccessTools.TypeByName(dialogName);
            if (dialogType == null)
                Log.Warning($"[MPCompat] Unbound weapons: could not resolve {dialogName}; closing the customization dialog will not sync.");
            else
                DialogUtilities.RegisterDialogCloseSync(dialogType);

            // Skip the dialog's reservation and repeat it in the synced commit, but only when a
            // default ReservationResult reports success; skipping leaves the dialog checking it.
            MethodInfo reserve = null;
            FieldInfo costField = null;
            MethodInfo isSuccess = null;
            Type reservationType = AccessTools.TypeByName($"{driverType.Namespace}.HaulPlanning.IngredientReservation");
            Type resultType = reservationType?.GetNestedType("ReservationResult");
            MethodInfo reserveMethod = reservationType == null
                ? null
                : AccessTools.Method(reservationType, "TryReserveIngredientsForJob",
                    new[] { typeof(Pawn), typeof(List<ThingDefCountClass>) });
            FieldInfo costs = AccessTools.Field(specType, "totalCost");
            MethodInfo successCheck = resultType?.GetProperty("IsSuccess")?.GetGetMethod();
            if (reserveMethod != null && costs != null && successCheck != null &&
                successCheck.Invoke(Activator.CreateInstance(resultType), null) is true)
            {
                reserve = reserveMethod;
                costField = costs;
                isSuccess = successCheck;
                MpCompat.harmony.Patch(reserve, prefix: new HarmonyMethod(
                    typeof(UnboundWeaponCustomization), nameof(SkipLocalReservationPrefix)));
            }
            else
            {
                Log.Warning($"[MPCompat] Unbound weapons: could not resolve the reservation types for {driverName}; ingredients will only be reserved on the confirming client.");
            }

            MethodInfo commit = AccessTools.Method(typeof(UnboundWeaponCustomization), commitName)
                .MakeGenericMethod(specType);
            MP.RegisterSyncMethod(commit, new[] { new SyncType(typeof(Pawn)), new SyncType(specType) })
                .SetContext(SyncContext.QueueOrder_Down)
                .ExposeParameter(1);

            MpCompat.harmony.Patch(setSpec,
                prefix: new HarmonyMethod(typeof(UnboundWeaponCustomization),
                    packageId == "shunter.personaweaponsunbound"
                        ? nameof(SetSpecPersonaPrefix)
                        : nameof(SetSpecUniquePrefix)));

            Log.Message($"[MPCompat] Syncing {driverName}.SetSpec for {packageId}.");
            return new Target
            {
                DriverType = driverType,
                SpecType = specType,
                SpecField = specField,
                CostField = costField,
                Reserve = reserve,
                IsSuccess = isSuccess,
                Commit = commit
            };
        }

        private static bool SetSpecPersonaPrefix(object __instance, object[] __args)
        {
            return SetSpecPrefix(__instance, __args, persona);
        }

        private static bool SetSpecUniquePrefix(object __instance, object[] __args)
        {
            return SetSpecPrefix(__instance, __args, unique);
        }

        private static bool SetSpecPrefix(object instance, object[] args, Target target)
        {
            if (!MP.IsInMultiplayer || !MP.InInterface)
                return true;
            if (target?.Commit == null || args == null || args.Length != 1)
                return true;

            Pawn pawn = (instance as JobDriver)?.pawn;
            object spec = args[0];
            if (pawn == null || spec == null || !target.SpecType.IsInstanceOfType(spec))
                return true;

            target.Commit.Invoke(null, new[] { pawn, spec });
            return false;
        }

        /// <summary>Skips the dialog's reservation; the synced commit reserves on every client.</summary>
        private static bool SkipLocalReservationPrefix()
        {
            return !MP.IsInMultiplayer || !MP.InInterface;
        }

        private static void CommitPersonaSpec<T>(Pawn pawn, T spec)
        {
            CommitSpec(persona, pawn, spec);
        }

        private static void CommitUniqueSpec<T>(Pawn pawn, T spec)
        {
            CommitSpec(unique, pawn, spec);
        }

        private static void CommitSpec(Target target, Pawn pawn, object spec)
        {
            if (target == null || pawn == null || spec == null || !target.SpecType.IsInstanceOfType(spec))
                return;

            JobDriver driver = pawn.jobs?.curDriver;
            if (driver == null || !target.DriverType.IsInstanceOfType(driver))
                return;

            // Repeat the dialog's reservation here: its own call is skipped in the interface, and
            // commands run outside it, so this reaches the real method.
            if (target.Reserve != null)
            {
                object costs = target.CostField.GetValue(spec);
                object result = target.Reserve.Invoke(null, new[] { pawn, costs });
                if (result == null || target.IsSuccess.Invoke(result, null) is not true)
                {
                    Log.Error($"[MPCompat] Unbound weapons: could not reserve ingredients for {pawn.LabelShortCap}; the customization was not committed.");
                    return;
                }
            }

            target.SpecField.SetValue(driver, spec);
        }
    }
}
