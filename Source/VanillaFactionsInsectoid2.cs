using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("OskarPotocki.VFE.Insectoid2")]
public class VanillaFactionsInsectoid2
{
	internal sealed class InsectoidSettingsSnapshot : IExposable
	{
		internal float Distance = 10.9f;

		internal float Penalty = 0.25f;

		internal float Interval = 5f;

		internal int Jelly = 10;

		internal int Creeper = 45000;

		internal int Insects = 2;

		internal IntRange Tendril = new IntRange(15000, 25000);

		internal bool Valid
		{
			get
			{
				if (float.IsFinite(Distance) && Distance >= 1f && Distance <= 50f && float.IsFinite(Penalty) && Penalty >= 0f && Penalty <= 1f && Jelly >= 1 && Jelly <= 100 && Creeper >= 2500 && Creeper <= 120000 && Tendril.min >= 2500 && Tendril.max <= 120000 && Tendril.min <= Tendril.max && Insects >= 1 && Insects <= 5 && float.IsFinite(Interval) && Interval >= 0.1f)
				{
					return Interval <= 10f;
				}
				return false;
			}
		}

		internal object[] Values => new object[7] { Distance, Penalty, Jelly, Creeper, Tendril, Insects, Interval };

		public void ExposeData()
		{
			Scribe_Values.Look(ref Distance, SettingsNames[0], 10.9f);
			Scribe_Values.Look(ref Penalty, SettingsNames[1], 0.25f);
			Scribe_Values.Look(ref Jelly, SettingsNames[2], 10);
			Scribe_Values.Look(ref Creeper, SettingsNames[3], 45000);
			Scribe_Values.Look(ref Tendril, SettingsNames[4], new IntRange(15000, 25000));
			Scribe_Values.Look(ref Insects, SettingsNames[5], 2);
			Scribe_Values.Look(ref Interval, SettingsNames[6], 5f);
		}

		internal bool SameAs(InsectoidSettingsSnapshot other)
		{
			if (Distance == other.Distance && Penalty == other.Penalty && Jelly == other.Jelly && Creeper == other.Creeper && Tendril == other.Tendril && Insects == other.Insects)
			{
				return Interval == other.Interval;
			}
			return false;
		}
	}

	private sealed class SettingsEdit
	{
		internal InsectoidSettingsSnapshot Before;

		internal bool Enabled;
	}

	private struct MaintenancePreview
	{
		internal bool Active;

		internal bool? Maintained;

		internal int Tick;
	}

	private static AccessTools.FieldRef<Gizmo, CompSpawnerPawn> hiveGizmoCompField;

	private static AccessTools.FieldRef<GameComponent> insectoidsGameCompInstanceField;

	private static AccessTools.FieldRef<GameComponent, object> insectoidsGameCompHordeManagerField;

	private static AccessTools.FieldRef<object, IList> hordeManagerWaveActivitiesField;

	private static FastInvokeHandler waveManagerInitializeWaveActivitiesMethod;

	private static AccessTools.FieldRef<object, IDictionary> waveActivityInsectsField;

	private static FastInvokeHandler waveActivityFormRaidCompositionMethod;

	private static readonly string[] SettingsNames = new string[7] { "minHiveStabilityDistance", "stabilityHiveMaintenancePenalty", "jellyFarmProductionAmount", "creeperSpawnSpeed", "tendrilmossSpawnSpeed", "baseArtificalInsectCount", "baseTimeBetweenHanHordeWaves" };

	private static FieldInfo[] settingsFields;

	private static FastInvokeHandler applySettingsMethod;

	private static FastInvokeHandler setWaveTicksMethod;

	private static bool applyingSettings;

	private static InsectoidSettingsSnapshot localSettings;

	private static AccessTools.FieldRef<object, bool?> maintainedByHivenodeField;

	private static AccessTools.FieldRef<object, int> ticksSinceLastCheckField;

	private static AccessTools.FieldRef<GameComponent, int> lastBossArrivalField;

	private static Func<int> worldTicks;

	internal static bool SettingsInstalled { get; private set; }

	public VanillaFactionsInsectoid2(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
		Type type = AccessTools.TypeByName("VFEInsectoids.CompHive");
		MP.RegisterSyncMethod(type, "ChangePawnKind");
		MP.RegisterSyncMethod(type, "DoSpawn").SetDebugOnly();
		MP.RegisterSyncMethodLambda(AccessTools.TypeByName("VFEInsectoids.HediffComp_Spawn"), "CompGetGizmos", 0).SetDebugOnly();
		MP.RegisterSyncMethodLambda(AccessTools.TypeByName("VFEInsectoids.CompMindfulSpawner"), "CompGetGizmosExtra", 0).SetDebugOnly();
		PatchingUtilities.PatchSystemRandCtor("VFEInsectoids.Tunneler");
	}

	private static void LatePatch()
	{
		MpCompatPatchLoader.LoadPatch<VanillaFactionsInsectoid2>();
		Type type = AccessTools.TypeByName("VFEInsectoids.Gizmo_Hive");
		hiveGizmoCompField = AccessTools.FieldRefAccess<CompSpawnerPawn>(type, "compHive");
		MP.RegisterSyncMethodLambda(type, "GizmoOnGUI", 1);
		Type type2 = AccessTools.TypeByName("VFEInsectoids.GameComponent_Insectoids");
		insectoidsGameCompInstanceField = AccessTools.StaticFieldRefAccess<GameComponent>(AccessTools.DeclaredField(type2, "Instance"));
		insectoidsGameCompHordeManagerField = AccessTools.FieldRefAccess<object>(type2, "hordeModeManager");
		Type type3 = AccessTools.TypeByName("VFEInsectoids.HordeModeManager");
		hordeManagerWaveActivitiesField = AccessTools.FieldRefAccess<IList>(type3, "waveActivities");
		waveManagerInitializeWaveActivitiesMethod = HarmonyLib.MethodInvoker.GetHandler(AccessTools.DeclaredMethod(type3, "InitializeWaveActivities"));
		Type type4 = AccessTools.TypeByName("VFEInsectoids.WaveActivity");
		waveActivityInsectsField = AccessTools.FieldRefAccess<IDictionary>(type4, "insects");
		waveActivityFormRaidCompositionMethod = HarmonyLib.MethodInvoker.GetHandler(AccessTools.DeclaredMethod(type4, "FormRaidComposition"));
		InstallSettings();
		InstallMaintenancePreview();
		InstallBossCooldownClock();
	}

	[MpCompatSyncWorker("VFEInsectoids.Gizmo_Hive", shouldConstruct = true)]
	private static void SyncGizmoHive(SyncWorker sync, ref Gizmo gizmo)
	{
		if (sync.isWriting)
		{
			sync.Write(hiveGizmoCompField(gizmo));
		}
		else
		{
			hiveGizmoCompField(gizmo) = sync.Read<CompSpawnerPawn>();
		}
	}

	private static int ModifyScreenWidthValue(int screenWidth)
	{
		if (MP.IsInMultiplayer)
		{
			return screenWidth - 96;
		}
		return screenWidth;
	}

	[MpCompatTranspiler("VFEInsectoids.HordeModeManager", "DrawWaveOverlay", MethodType.Normal)]
	private static IEnumerable<CodeInstruction> MoveHordeOverlayPosition(IEnumerable<CodeInstruction> instr, MethodBase baseMethod)
	{
		FieldInfo target = AccessTools.DeclaredField(typeof(UI), "screenWidth");
		MethodInfo replacement = MpMethodUtil.MethodOf(new Func<int, int>(ModifyScreenWidthValue));
		int replacedCount = 0;
		foreach (CodeInstruction ci in instr)
		{
			yield return ci;
			if (ci.LoadsField(target))
			{
				yield return new CodeInstruction(OpCodes.Call, replacement);
				replacedCount++;
			}
		}
		if (replacedCount != 1)
		{
			string value = ((baseMethod.DeclaringType?.Namespace).NullOrEmpty() ? baseMethod.Name : (baseMethod.DeclaringType.Name + ":" + baseMethod.Name));
			Log.Warning($"Patched incorrect number of Find.CameraDriver.MapPosition calls (patched {replacedCount}, expected {1}) for method {value}");
		}
	}

	[MpCompatPrefix("VFEInsectoids.HordeModeManager", "Tick", MethodType.Normal)]
	private static void PreHordeManagerTick(object __instance, IList ___waveActivities)
	{
		if (___waveActivities == null || ___waveActivities.Count <= 0)
		{
			waveManagerInitializeWaveActivitiesMethod(__instance);
		}
	}

	[MpCompatPostfix("VFEInsectoids.HordeModeManager", "InitializeWaveActivities", MethodType.Normal)]
	[MpCompatPostfix("VFEInsectoids.HordeModeManager", "AddNextWave", MethodType.Normal)]
	private static void InitializeHordeManagerWaveActivities(IList ___waveActivities)
	{
		EnsureCurrentRaidCompositionIsInitialized(___waveActivities);
	}

	[MpCompatPostfix("VFEInsectoids.HordeModeManager", "ExposeData", MethodType.Normal)]
	private static void InitializeHordeManagerWaveActivitiesExposeData(IList ___waveActivities)
	{
		if (Scribe.mode == LoadSaveMode.PostLoadInit)
		{
			EnsureCurrentRaidCompositionIsInitialized(___waveActivities);
		}
	}

	private static void EnsureCurrentRaidCompositionIsInitialized(IList waveActivities)
	{
		if (waveActivities != null && waveActivities.Count > 0)
		{
			object obj = waveActivities[0];
			if (waveActivityInsectsField(obj) == null)
			{
				waveActivityFormRaidCompositionMethod(obj);
			}
		}
	}

	[MpCompatSyncMethod(SyncContext.None, hostOnly = true)]
	[MpCompatPrefix("VFEInsectoids.HordeModeManager", "StartWave_Debug", MethodType.Normal)]
	[MpCompatPrefix("VFEInsectoids.HordeModeManager", "CompleteWave_Debug", MethodType.Normal)]
	private static void InitializeWaveActivities()
	{
		object obj = insectoidsGameCompHordeManagerField(insectoidsGameCompInstanceField());
		IList list = hordeManagerWaveActivitiesField(obj);
		if (list == null || list.Count <= 0)
		{
			waveManagerInitializeWaveActivitiesMethod(obj);
		}
		else
		{
			EnsureCurrentRaidCompositionIsInitialized(list);
		}
	}

	[MpCompatPrefix("VFEInsectoids.HordeModeManager", "DrawWaveOverlay", MethodType.Normal)]
	private static bool PreHordeManagerGUI(IList ___waveActivities)
	{
		if (!MP.IsInMultiplayer || !MP.InInterface)
		{
			return true;
		}
		if (___waveActivities != null && ___waveActivities.Count > 0 && waveActivityInsectsField(___waveActivities[0]) != null)
		{
			return true;
		}
		if (MP.IsHosting && Time.frameCount % 100 == 0)
		{
			InitializeWaveActivities();
		}
		return false;
	}

	private static void InstallSettings()
	{
		Type type = AccessTools.TypeByName("VFEInsectoids.VFEInsectoidsSettings");
		settingsFields = new FieldInfo[SettingsNames.Length];
		for (int i = 0; i < SettingsNames.Length; i++)
		{
			settingsFields[i] = AccessTools.DeclaredField(type, SettingsNames[i]) ?? throw new MissingFieldException(type.FullName, SettingsNames[i]);
		}
		applySettingsMethod = HarmonyLib.MethodInvoker.GetHandler(AccessTools.DeclaredMethod("VFEInsectoids.SettingsApplier:ApplySettings"));
		setWaveTicksMethod = HarmonyLib.MethodInvoker.GetHandler(AccessTools.DeclaredMethod("VFEInsectoids.WaveActivity:SetTicksStarting"));
		MP.RegisterSyncMethod(typeof(VanillaFactionsInsectoid2), "SyncSettings").SetHostOnly().SetVersion(15);
		MethodInfo original = AccessTools.DeclaredMethod(type, "DoSettingsWindowContents");
		MpCompat.harmony.Patch(original, new HarmonyMethod(typeof(VanillaFactionsInsectoid2), "BeforeSettings"), null, new HarmonyMethod(typeof(VanillaFactionsInsectoid2), "SettingsTimerTranspiler"), new HarmonyMethod(typeof(VanillaFactionsInsectoid2), "AfterSettings"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod("VFEInsectoids.SettingsApplier:ApplySettings"), new HarmonyMethod(typeof(VanillaFactionsInsectoid2), "BeforeApplySettings"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod("VFEInsectoids.VFEInsectoidsMod:WriteSettings"), new HarmonyMethod(typeof(VanillaFactionsInsectoid2), "BeforeWriteSettings"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod("Multiplayer.Client.Multiplayer:StopMultiplayer"), null, new HarmonyMethod(typeof(VanillaFactionsInsectoid2), "RestoreLocalSettings"));
		SettingsInstalled = true;
	}

	internal static InsectoidSettingsSnapshot CaptureSettings()
	{
		return new InsectoidSettingsSnapshot
		{
			Distance = (float)settingsFields[0].GetValue(null),
			Penalty = (float)settingsFields[1].GetValue(null),
			Jelly = (int)settingsFields[2].GetValue(null),
			Creeper = (int)settingsFields[3].GetValue(null),
			Tendril = (IntRange)settingsFields[4].GetValue(null),
			Insects = (int)settingsFields[5].GetValue(null),
			Interval = (float)settingsFields[6].GetValue(null)
		};
	}

	private static void ApplySettings(InsectoidSettingsSnapshot snapshot, bool rebuild)
	{
		object[] values = snapshot.Values;
		for (int i = 0; i < settingsFields.Length; i++)
		{
			settingsFields[i].SetValue(null, values[i]);
		}
		if (rebuild)
		{
			bool flag = applyingSettings;
			applyingSettings = true;
			try
			{
				applySettingsMethod(null);
			}
			finally
			{
				applyingSettings = flag;
			}
		}
	}

	internal static void RestoreSessionSettings(InsectoidSettingsSnapshot snapshot)
	{
		if (SettingsInstalled && MP.IsInMultiplayer)
		{
			if (localSettings == null)
			{
				localSettings = CaptureSettings();
			}
			if (snapshot != null && snapshot.Valid)
			{
				ApplySettings(snapshot, rebuild: true);
			}
		}
	}

	private static void RestoreLocalSettings()
	{
		if (localSettings != null)
		{
			Current.Game?.GetComponent<VanillaFactionsInsectoid2SettingsComponent>()?.RememberSessionBeforeExit();
			ApplySettings(localSettings, rebuild: true);
			localSettings = null;
		}
	}

	private static void BeforeSettings(out SettingsEdit __state)
	{
		__state = null;
		if (MP.IsInMultiplayer)
		{
			if (localSettings == null)
			{
				localSettings = CaptureSettings();
			}
			__state = new SettingsEdit
			{
				Before = CaptureSettings(),
				Enabled = GUI.enabled
			};
			GUI.enabled &= MP.IsHosting;
		}
	}

	private static Exception AfterSettings(Exception __exception, SettingsEdit __state)
	{
		if (__state == null)
		{
			return __exception;
		}
		InsectoidSettingsSnapshot insectoidSettingsSnapshot = CaptureSettings();
		ApplySettings(__state.Before, rebuild: false);
		GUI.enabled = __state.Enabled;
		if (__exception == null && MP.IsHosting && !insectoidSettingsSnapshot.SameAs(__state.Before))
		{
			SyncSettings(insectoidSettingsSnapshot.Distance, insectoidSettingsSnapshot.Penalty, insectoidSettingsSnapshot.Jelly, insectoidSettingsSnapshot.Creeper, insectoidSettingsSnapshot.Tendril, insectoidSettingsSnapshot.Insects, insectoidSettingsSnapshot.Interval);
		}
		return __exception;
	}

	private static void SyncSettings(float distance, float penalty, int jelly, int creeper, IntRange tendril, int insects, float interval)
	{
		InsectoidSettingsSnapshot insectoidSettingsSnapshot = new InsectoidSettingsSnapshot
		{
			Distance = distance,
			Penalty = penalty,
			Jelly = jelly,
			Creeper = creeper,
			Tendril = tendril,
			Insects = insects,
			Interval = interval
		};
		if (!insectoidSettingsSnapshot.Valid)
		{
			return;
		}
		if (localSettings == null)
		{
			localSettings = CaptureSettings();
		}
		bool flag = (float)settingsFields[6].GetValue(null) != interval;
		ApplySettings(insectoidSettingsSnapshot, rebuild: true);
		GameComponent gameComponent = insectoidsGameCompInstanceField();
		if (!flag || gameComponent == null)
		{
			return;
		}
		object obj = insectoidsGameCompHordeManagerField(gameComponent);
		if (obj == null)
		{
			return;
		}
		IList list = hordeManagerWaveActivitiesField(obj);
		if (list == null)
		{
			return;
		}
		foreach (object item in list)
		{
			setWaveTicksMethod(item);
		}
	}

	private static bool BeforeApplySettings()
	{
		if (!applyingSettings && MP.IsInMultiplayer)
		{
			return !MP.InInterface;
		}
		return true;
	}

	private static bool BeforeWriteSettings()
	{
		return !MP.IsInMultiplayer;
	}

	private static void SetWaveTicksFromSettings(object wave)
	{
		if (!MP.IsInMultiplayer || !MP.InInterface)
		{
			setWaveTicksMethod(wave);
		}
	}

	private static IEnumerable<CodeInstruction> SettingsTimerTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo target = AccessTools.DeclaredMethod("VFEInsectoids.WaveActivity:SetTicksStarting");
		int replaced = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(target))
			{
				replaced++;
				yield return new CodeInstruction(instruction)
				{
					opcode = OpCodes.Call,
					operand = AccessTools.DeclaredMethod(typeof(VanillaFactionsInsectoid2), "SetWaveTicksFromSettings")
				};
			}
			else
			{
				yield return instruction;
			}
		}
		if (replaced != 1)
		{
			throw new InvalidOperationException($"VFE Insectoids settings timer calls changed: {replaced}");
		}
	}

	private static void InstallMaintenancePreview()
	{
		Type type = AccessTools.TypeByName("VFEInsectoids.CompMaintenableHive");
		maintainedByHivenodeField = AccessTools.FieldRefAccess<bool?>(type, "maintainedByHivenode");
		ticksSinceLastCheckField = AccessTools.FieldRefAccess<int>(type, "ticksSinceLastCheck");
		MpCompat.harmony.Patch(AccessTools.PropertyGetter(type, "MaintainedByHivenode"), new HarmonyMethod(typeof(VanillaFactionsInsectoid2), "BeforeMaintenancePreview"), null, null, new HarmonyMethod(typeof(VanillaFactionsInsectoid2), "AfterMaintenancePreview"));
	}

	private static void BeforeMaintenancePreview(object __instance, out MaintenancePreview __state)
	{
		__state = default;
		if (MP.IsInMultiplayer && MP.InInterface)
		{
			__state = new MaintenancePreview
			{
				Active = true,
				Maintained = maintainedByHivenodeField(__instance),
				Tick = ticksSinceLastCheckField(__instance)
			};
		}
	}

	private static Exception AfterMaintenancePreview(object __instance, Exception __exception, MaintenancePreview __state)
	{
		if (__state.Active)
		{
			maintainedByHivenodeField(__instance) = __state.Maintained;
			ticksSinceLastCheckField(__instance) = __state.Tick;
		}
		return __exception;
	}

	private static void InstallBossCooldownClock()
	{
		lastBossArrivalField = AccessTools.FieldRefAccess<int>(AccessTools.TypeByName("VFEInsectoids.GameComponent_Insectoids"), "lastInsectoidBossArrival");
		worldTicks = (Func<int>)AccessTools.DeclaredMethod("Multiplayer.Client.AsyncTimeComp:GetWorldTicks").CreateDelegate(typeof(Func<int>));
		Type type = AccessTools.TypeByName("VFEInsectoids.CompThumper");
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, "SpawnInfestation"), null, null, new HarmonyMethod(typeof(VanillaFactionsInsectoid2), "BossArrivalClockTranspiler"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, "CanBeUsedBy"), null, null, new HarmonyMethod(typeof(VanillaFactionsInsectoid2), "BossCooldownClockTranspiler"));
	}

	private static void StoreBossArrival(GameComponent component, int nativeTicks)
	{
		VanillaFactionsInsectoid2SettingsComponent vanillaFactionsInsectoid2SettingsComponent = Current.Game?.GetComponent<VanillaFactionsInsectoid2SettingsComponent>();
		if (vanillaFactionsInsectoid2SettingsComponent != null)
		{
			vanillaFactionsInsectoid2SettingsComponent.WorldBossClock = MP.IsInMultiplayer;
		}
		lastBossArrivalField(component) = ((vanillaFactionsInsectoid2SettingsComponent != null && vanillaFactionsInsectoid2SettingsComponent.WorldBossClock) ? worldTicks() : nativeTicks);
	}

	private static int BossCooldownTick(TickManager manager)
	{
		if (MP.IsInMultiplayer)
		{
			Game game = Current.Game;
			if (game != null && game.GetComponent<VanillaFactionsInsectoid2SettingsComponent>()?.WorldBossClock == true)
			{
				return worldTicks();
			}
		}
		return manager.TicksGame;
	}

	private static IEnumerable<CodeInstruction> BossArrivalClockTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		FieldInfo target = AccessTools.DeclaredField("VFEInsectoids.GameComponent_Insectoids:lastInsectoidBossArrival");
		int replaced = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.StoresField(target))
			{
				replaced++;
				yield return new CodeInstruction(instruction)
				{
					opcode = OpCodes.Call,
					operand = AccessTools.DeclaredMethod(typeof(VanillaFactionsInsectoid2), "StoreBossArrival")
				};
			}
			else
			{
				yield return instruction;
			}
		}
		if (replaced != 1)
		{
			throw new InvalidOperationException($"VFE Insectoids boss timestamp stores changed: {replaced}");
		}
	}

	private static IEnumerable<CodeInstruction> BossCooldownClockTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo target = AccessTools.PropertyGetter(typeof(TickManager), "TicksGame");
		int replaced = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(target))
			{
				replaced++;
				yield return new CodeInstruction(instruction)
				{
					opcode = OpCodes.Call,
					operand = AccessTools.DeclaredMethod(typeof(VanillaFactionsInsectoid2), "BossCooldownTick")
				};
			}
			else
			{
				yield return instruction;
			}
		}
		if (replaced != 2)
		{
			throw new InvalidOperationException($"VFE Insectoids boss cooldown clock reads changed: {replaced}");
		}
	}
}
