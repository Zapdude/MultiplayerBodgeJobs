using System;
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
    /// Both mods start the customize job through <c>Pawn_JobTracker.TryTakeOrderedJob</c>, which
    /// Multiplayer already syncs. The chosen design is committed afterwards by
    /// <c>JobDriver_CustomizeWeapon.SetSpec</c>, and that call only happens on the client that
    /// pressed Confirm, so every other client keeps the old spec and desyncs.
    ///
    /// SetSpec has one caller per mod, so patching it covers both the gizmo and the float-menu path.
    /// The spec is a plain IExposable with no SyncWorker, so the synced parameter is marked to
    /// travel by Scribe.
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
                    nameof(CommitPersonaSpec));
            }
            else if (packageId == "shunter.uniqueweaponsunbound")
            {
                unique = Install(packageId,
                    "UniqueWeaponsUnbound.JobDriver_CustomizeWeapon",
                    "UniqueWeaponsUnbound.CustomizationSpec",
                    nameof(CommitUniqueSpec));
            }
        }

        private static Target Install(string packageId, string driverName, string specName, string commitName)
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

            // SetSpec is the patched method; calling it here would re-enter the prefix.
            target.SpecField.SetValue(driver, spec);
        }
    }
}
