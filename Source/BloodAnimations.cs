using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Fuu.BloodAnimations")]
public sealed class BloodAnimations
{
	private struct VisualScope
	{
		internal bool Active;

		internal long Token;

		internal int Epoch;
	}

	private struct EmissionScope
	{
		internal bool Active;

		internal int Epoch;
	}

	internal sealed class SettingsSnapshot : IExposable
	{
		internal float[] floats = new float[4] { 1f, 1f, 0.15f, 1f };

		internal int[] ints = new int[2] { 10, 30 };

		internal bool[] bools = new bool[3] { true, true, true };

		internal bool Valid
		{
			get
			{
				float[] array = floats;
				if (array != null && array.Length == 4)
				{
					int[] array2 = ints;
					if (array2 != null && array2.Length == 2)
					{
						bool[] array3 = bools;
						if (array3 != null && array3.Length == 3 && floats.All((float v) => float.IsFinite(v) && v >= 0f && v <= 5f) && floats[2] <= 1f && floats[3] > 0f && floats[3] <= 3f && ints[0] >= 0 && ints[0] <= 300 && ints[1] >= 1)
						{
							return ints[1] <= 300000;
						}
					}
				}
				return false;
			}
		}

		internal bool Same(SettingsSnapshot other)
		{
			if (floats.SequenceEqual(other.floats) && ints.SequenceEqual(other.ints))
			{
				return bools.SequenceEqual(other.bools);
			}
			return false;
		}

		public void ExposeData()
		{
			for (int i = 0; i < floats.Length; i++)
			{
				Scribe_Values.Look(ref floats[i], FloatNames[i], 0f, forceSave: true);
			}
			for (int j = 0; j < ints.Length; j++)
			{
				Scribe_Values.Look(ref ints[j], IntNames[j], 0, forceSave: true);
			}
			for (int k = 0; k < bools.Length; k++)
			{
				Scribe_Values.Look(ref bools[k], BoolNames[k], defaultValue: false, forceSave: true);
			}
		}
	}

	private sealed class SettingsEdit
	{
		internal SettingsSnapshot Before;

		internal Game Game;

		internal int Epoch;

		internal bool Enabled;
	}

	internal static bool Installed;

	internal static Type SystemType;

	internal static Type ParticleType;

	internal static FieldInfo ParticleData;

	internal static readonly Dictionary<string, FieldInfo> ParticleFields = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);

	private static Func<bool> ticking;

	private static Func<bool> executing;

	private static Func<bool> longEvent;

	[ThreadStatic]
	private static int emissions;

	[ThreadStatic]
	private static int visuals;

	[ThreadStatic]
	private static int canonicalTicks;

	[ThreadStatic]
	private static Map canonicalMap;

	private static int epoch;

	private const string SettingsKey = "fuu.bloodanimations.settings";

	private static FieldInfo[] floatFields;

	private static FieldInfo[] intFields;

	private static FieldInfo[] boolFields;

	private static ModSettings personalSettings;

	private static readonly string[] FloatNames = new string[4] { "bloodMultiplier", "debrisMultiplier", "filthMultiplier", "bleedMultiplier" };

	private static readonly string[] IntNames = new string[2] { "bloodTime", "ticksUntilBleed" };

	private static readonly string[] BoolNames = new string[3] { "bulletCasingToggle", "bulletCasingFilthToggle", "groundImpactToggle" };

	private static bool InSimulation
	{
		get
		{
			if (MP.IsInMultiplayer)
			{
				if (!ticking() && !executing())
				{
					return longEvent();
				}
				return true;
			}
			return false;
		}
	}

	private static bool Canonical
	{
		get
		{
			if (canonicalTicks <= 0)
			{
				if (emissions > 0 && visuals == 0)
				{
					return InSimulation;
				}
				return false;
			}
			return true;
		}
	}

	public BloodAnimations(ModContentPack mod)
	{
		Install();
	}

	internal static void Install()
	{
		if (Installed)
		{
			return;
		}
		SessionServices.Install();
		SystemType = RequiredType("FleckSystemThrownBlood");
		ParticleType = RequiredType("FleckThrownBlood");
		ParticleData = AccessTools.Field(SystemType.BaseType, "dataGametime") ?? throw new MissingFieldException(SystemType.FullName, "dataGametime");
		string[] array = new string[13]
		{
			"baseData", "airTimeLeft", "velocity", "rotationRate", "link", "attacheeLastPosition", "orbitSpeed", "archHeight", "archDuration", "orbitSnapStrength",
			"orbitDistance", "orbitAccum", "skidSpeedMultiplierPerTick"
		};
		foreach (string text in array)
		{
			ParticleFields.Add(text, AccessTools.Field(ParticleType, text) ?? throw new MissingFieldException(ParticleType.FullName, text));
		}
		ticking = CoreFlag("Multiplayer.Client.Multiplayer", "Ticking");
		executing = CoreFlag("Multiplayer.Client.Multiplayer", "ExecutingCmds");
		longEvent = CoreFlag("Multiplayer.Client.Patches.SimLongEventContext", "InSimulation");
		SessionServices.RegisterLifecycle("fuu.bloodanimations.local", null, () =>
		{
			epoch++;
			emissions = (visuals = (canonicalTicks = 0));
			canonicalMap = null;
		});
		InstallSettings();
		MethodInfo shared = AccessTools.PropertyGetter(typeof(System.Random), "Shared");
		Type[] types = ParticleType.Assembly.GetTypes();
		for (int i = 0; i < types.Length; i++)
		{
			foreach (MethodInfo item in from m in AccessTools.GetDeclaredMethods(types[i])
				where m.HasMethodBody()
				select m)
			{
				if (PatchProcessor.GetCurrentInstructions(item).Any((CodeInstruction ci) => ci.Calls(shared)))
				{
					MpCompat.harmony.Patch(item, null, null, H("SharedRandomCalls"));
				}
			}
		}
		(string, string)[] array2 = new (string, string)[5]
		{
			("DamageWorker_AddInjury_ApplyToPawn", "ApplyToPawn"),
			("Pawn_Kill", "Kill"),
			("GenLeaving_DropFilthDueToDamage", "DropFilthDueToDamage"),
			("Verb_TryCastNextBurstShot", "TryCastNextBurstShot"),
			("SubEffecter_SprayerTriggeredBlood", "SubTrigger")
		};
		for (int i = 0; i < array2.Length; i++)
		{
			var (type, name) = array2[i];
			MpCompat.harmony.Patch(RequiredMethod(type, name), H("BeginEmission"), null, null, H("EndEmission"));
		}
		array2 = new (string, string)[9]
		{
			("FleckMaker", "CreateFleckBleed"),
			("FleckMaker", "CreateFleckBleedPuddle"),
			("FleckMaker", "CreateFleckRicochet"),
			("FleckMaker", "CreateMoteImpactFlyingPiece"),
			("FleckMaker", "CreateMoteGroundImpactFlyingPiece"),
			("Verb_TryCastNextBurstShot", "ThrowCasing"),
			("Verb_TryCastNextBurstShot", "ThrowCasingTurret"),
			("Pawn_HealthTracker_HealthTick", "HealthTick"),
			("MoteThrowCasing", "NextExactPosition")
		};
		for (int i = 0; i < array2.Length; i++)
		{
			var (type2, name2) = array2[i];
			MpCompat.harmony.Patch(RequiredMethod(type2, name2), H("BeginVisual"), null, null, H("EndVisual"));
		}
		MpCompat.harmony.Patch(RequiredMethod("Pawn_Kill", "Kill"), null, null, H("DeathFacing"));
		array2 = new (string, string)[3]
		{
			("FleckThrownBlood", "TimeInterval"),
			("Verb_TryCastNextBurstShot", "TryCastNextBurstShot"),
			("SubEffecter_SprayerTriggeredBlood", "SubTrigger")
		};
		for (int i = 0; i < array2.Length; i++)
		{
			var (type3, name3) = array2[i];
			MpCompat.harmony.Patch(RequiredMethod(type3, name3), null, null, H("FilthCalls"));
		}
		HarmonyMethod harmonyMethod = H("CreateBlood");
		harmonyMethod.before = new string[1] { "multiplayer" };
		harmonyMethod.priority = 800;
		MpCompat.harmony.Patch(AccessTools.Method(typeof(FleckManager), "CreateFleck"), harmonyMethod);
		MpCompat.harmony.Patch(AccessTools.Method(typeof(FleckManager), "FleckManagerDraw"), H("DrawBlood"));
		Installed = true;
	}

	private static Type RequiredType(string name)
	{
		return AccessTools.TypeByName("BloodAnimations." + name) ?? throw new TypeLoadException("BloodAnimations." + name);
	}

	private static MethodInfo RequiredMethod(string type, string name)
	{
		return AccessTools.DeclaredMethod(RequiredType(type), name) ?? throw new MissingMethodException(type, name);
	}

	private static HarmonyMethod H(string name)
	{
		return new HarmonyMethod(typeof(BloodAnimations), name);
	}

	private static Func<bool> CoreFlag(string type, string name)
	{
		return (Func<bool>)(AccessTools.PropertyGetter(type + ":" + name) ?? throw new MissingMemberException(type, name)).CreateDelegate(typeof(Func<bool>));
	}

	private static float NextSingle()
	{
		if (!Canonical)
		{
			return System.Random.Shared.NextSingle();
		}
		return Rand.Value;
	}

	private static void BeginEmission(out EmissionScope __state)
	{
		__state = new EmissionScope
		{
			Active = InSimulation,
			Epoch = epoch
		};
		if (__state.Active)
		{
			emissions++;
		}
	}

	private static void EndEmission(EmissionScope __state)
	{
		if (__state.Active && __state.Epoch == epoch)
		{
			emissions--;
		}
	}

	private static void BeginVisual(out VisualScope __state)
	{
		__state = default;
		if (MP.IsInMultiplayer && SessionServices.BeginLocalOnly(out var token))
		{
			visuals++;
			__state = new VisualScope
			{
				Active = true,
				Token = token,
				Epoch = epoch
			};
		}
	}

	private static void EndVisual(VisualScope __state)
	{
		if (!__state.Active)
		{
			return;
		}
		try
		{
			SessionServices.EndLocalOnly(__state.Token);
		}
		finally
		{
			if (__state.Epoch == epoch)
			{
				visuals--;
			}
		}
	}

	private static IEnumerable<CodeInstruction> SharedRandomCalls(IEnumerable<CodeInstruction> instructions)
	{
		List<CodeInstruction> code = instructions.ToList();
		MethodInfo shared = AccessTools.PropertyGetter(typeof(System.Random), "Shared");
		MethodInfo next = AccessTools.Method(typeof(System.Random), "NextSingle", Type.EmptyTypes);
		int replaced = 0;
		for (int i = 0; i < code.Count; i++)
		{
			CodeInstruction codeInstruction = code[i];
			if (!codeInstruction.Calls(shared))
			{
				yield return codeInstruction;
				continue;
			}
			if (i + 1 >= code.Count || !code[i + 1].Calls(next) || code[i + 1].labels.Count != 0 || code[i + 1].blocks.Count != 0)
			{
				throw new InvalidOperationException("Blood Animations Shared RNG callsite changed");
			}
			codeInstruction.opcode = OpCodes.Call;
			codeInstruction.operand = AccessTools.Method(typeof(BloodAnimations), "NextSingle");
			yield return codeInstruction;
			i++;
			replaced++;
		}
		if (replaced == 0)
		{
			throw new InvalidOperationException("Blood Animations Shared RNG target has no expected calls");
		}
	}

	private static IEnumerable<CodeInstruction> DeathFacing(IEnumerable<CodeInstruction> instructions)
	{
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(AccessTools.Method(typeof(PawnRenderer), "BodyAngle")))
			{
				instruction.opcode = OpCodes.Call;
				instruction.operand = AccessTools.Method(typeof(BloodAnimations), "BodyAngle");
			}
			yield return instruction;
		}
	}

	private static float BodyAngle(PawnRenderer renderer, PawnRenderFlags flags)
	{
		if (!Canonical || (!renderer.pawn.Downed && !renderer.pawn.Dead))
		{
			return renderer.BodyAngle(flags);
		}
		return renderer.pawn.Rotation.AsAngle;
	}

	private static bool CreateBlood(FleckManager __instance, ref FleckCreationData fleckData)
	{
		if (!MP.IsInMultiplayer || fleckData.def?.fleckSystemClass != SystemType || !fleckData.link.Target.HasThing)
		{
			return true;
		}
		if (!Canonical || fleckData.def.realTime)
		{
			return true;
		}
		Map parent = __instance.parent;
		if (parent == null || parent.Disposed)
		{
			return false;
		}
		fleckData.spawnPosition.y = fleckData.def.altitudeLayer.AltitudeFor(fleckData.def.altitudeLayerIncOffset);
		parent.GetComponent<BloodAnimationsMapComponent>().System.CreateFleck(fleckData);
		return false;
	}

	private static void DrawBlood(FleckManager __instance)
	{
		if (!MP.IsInMultiplayer || __instance.parent == null)
		{
			return;
		}
		FleckSystem existingSystem = __instance.parent.GetComponent<BloodAnimationsMapComponent>().ExistingSystem;
		if (existingSystem == null)
		{
			return;
		}
		SessionServices.BeginLocalOnly(out var token);
		try
		{
			existingSystem.Draw(__instance.drawBatch);
		}
		finally
		{
			SessionServices.EndLocalOnly(token);
		}
	}

	internal static void Tick(FleckSystem system, Map map)
	{
		Map map2 = canonicalMap;
		int num = epoch;
		canonicalMap = map;
		canonicalTicks++;
		try
		{
			system.Tick();
		}
		finally
		{
			if (epoch == num)
			{
				canonicalTicks--;
				canonicalMap = map2;
			}
		}
	}

	private static IEnumerable<CodeInstruction> FilthCalls(IEnumerable<CodeInstruction> instructions)
	{
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.operand is MethodInfo methodInfo && methodInfo.DeclaringType == typeof(FilthMaker) && methodInfo.Name == "TryMakeFilth")
			{
				MethodInfo methodInfo2 = AccessTools.DeclaredMethod(typeof(BloodAnimations), "MakeFilth", (from p in methodInfo.GetParameters()
					select p.ParameterType).ToArray());
				if (methodInfo2 == null)
				{
					throw new MissingMethodException("Blood Animations filth overload changed: " + methodInfo.FullDescription());
				}
				instruction.opcode = OpCodes.Call;
				instruction.operand = methodInfo2;
			}
			yield return instruction;
		}
	}

	private static bool MakeFilth(IntVec3 cell, Map map, ThingDef filth, int count, FilthSourceFlags source, bool check)
	{
		if (!MP.IsInMultiplayer || Canonical)
		{
			return FilthMaker.TryMakeFilth(cell, canonicalMap ?? map, filth, count, source, check);
		}
		return false;
	}

	private static bool MakeFilth(IntVec3 cell, Map map, ThingDef filth, int count, FilthSourceFlags source)
	{
		if (!MP.IsInMultiplayer || Canonical)
		{
			return FilthMaker.TryMakeFilth(cell, canonicalMap ?? map, filth, count, source);
		}
		return false;
	}

	private static bool MakeFilth(IntVec3 cell, Map map, ThingDef filth)
	{
		if (!MP.IsInMultiplayer || Canonical)
		{
			return FilthMaker.TryMakeFilth(cell, canonicalMap ?? map, filth);
		}
		return false;
	}

	private static void InstallSettings()
	{
		Type type = RequiredType("Settings");
		floatFields = FloatNames.Select(Field).ToArray();
		intFields = IntNames.Select(Field).ToArray();
		boolFields = BoolNames.Select(Field).ToArray();
		if (!(from f in AccessTools.GetDeclaredFields(type)
			where f.IsStatic && f.IsPublic
			select f.Name).OrderBy((string n) => n, StringComparer.Ordinal).SequenceEqual(FloatNames.Concat(IntNames).Concat(BoolNames).OrderBy((string n) => n, StringComparer.Ordinal)))
		{
			throw new InvalidOperationException("Blood Animations settings schema changed");
		}
		Mod mod = LoadedModManager.GetMod(RequiredType("BloodAnimations"));
		if (mod != null)
		{
			personalSettings = (ModSettings)AccessTools.Method(typeof(Mod), "GetSettings").MakeGenericMethod(type).Invoke(mod, null);
		}
		SessionServices.RegisterSettings("fuu.bloodanimations.settings", typeof(SettingsSnapshot), () => Capture(), () => new SettingsSnapshot(), (IExposable value) =>
		{
			Apply((SettingsSnapshot)value);
		}, (IExposable value) => value is SettingsSnapshot settingsSnapshot && settingsSnapshot.Valid, () => personalSettings, "mpBloodAnimationsSettings");
		MP.RegisterSyncMethod(typeof(BloodAnimations), "SyncSettings").SetHostOnly().SetVersion(33);
		MpCompat.harmony.Patch(RequiredMethod("BloodAnimations", "DoSettingsWindowContents"), H("SettingsBefore"), null, null, H("SettingsAfter"));
		FieldInfo Field(string name)
		{
			return AccessTools.DeclaredField(type, name) ?? throw new MissingFieldException(type.FullName, name);
		}
	}

	private static SettingsSnapshot Capture()
	{
		return new SettingsSnapshot
		{
			floats = floatFields.Select((FieldInfo f) => (float)f.GetValue(null)).ToArray(),
			ints = intFields.Select((FieldInfo f) => (int)f.GetValue(null)).ToArray(),
			bools = boolFields.Select((FieldInfo f) => (bool)f.GetValue(null)).ToArray()
		};
	}

	private static void Apply(SettingsSnapshot state)
	{
		for (int i = 0; i < floatFields.Length; i++)
		{
			floatFields[i].SetValue(null, state.floats[i]);
		}
		for (int j = 0; j < intFields.Length; j++)
		{
			intFields[j].SetValue(null, state.ints[j]);
		}
		for (int k = 0; k < boolFields.Length; k++)
		{
			boolFields[k].SetValue(null, state.bools[k]);
		}
	}

	internal static IExposable SettingsState(Game game)
	{
		return SessionServices.SettingsForGame("fuu.bloodanimations.settings", game);
	}

	private static void SettingsBefore(out SettingsEdit __state)
	{
		__state = null;
		if (MP.IsInMultiplayer && Current.Game != null)
		{
			SessionServices.EnsureSettings(SettingsState(Current.Game));
			__state = new SettingsEdit
			{
				Before = Capture(),
				Game = Current.Game,
				Epoch = epoch,
				Enabled = GUI.enabled
			};
			GUI.enabled &= MP.IsHosting;
		}
	}

	private static Exception SettingsAfter(Exception __exception, SettingsEdit __state)
	{
		if (__state == null)
		{
			return __exception;
		}
		try
		{
			if (!MP.IsInMultiplayer || Current.Game != __state.Game || epoch != __state.Epoch)
			{
				return __exception;
			}
			SettingsSnapshot settingsSnapshot = Capture();
			Apply(__state.Before);
			if (__exception == null && MP.IsHosting && MP.InInterface && !settingsSnapshot.Same(__state.Before))
			{
				SyncSettings(settingsSnapshot.floats, settingsSnapshot.ints, settingsSnapshot.bools);
			}
			return __exception;
		}
		finally
		{
			GUI.enabled = __state.Enabled;
		}
	}

	private static void SyncSettings(float[] floats, int[] ints, bool[] bools)
	{
		SettingsSnapshot settingsSnapshot = new SettingsSnapshot
		{
			floats = floats,
			ints = ints,
			bools = bools
		};
		if (Current.Game != null && settingsSnapshot.Valid)
		{
			SessionServices.ApplySettings(SettingsState(Current.Game), settingsSnapshot);
		}
	}
}
