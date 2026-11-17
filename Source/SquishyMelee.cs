using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Multiplayer.Compat;

[MpCompatFor("local.gunrecoil.melee")]
public sealed class SquishyMelee
{
	internal sealed class AttackScope
	{
		internal AttackScope Previous;

		internal SquishyMeleeCombatComponent Component;

		internal Pawn Pawn;

		internal Thing Target;

		internal int Kind = -1;

		internal bool Attempted;
	}

	private const string HarmonyOwner = "local.multiplayer.squishy-melee";

	private static FieldInfo settings;

	private static FieldInfo enabled;

	private static FieldInfo shieldMotion;

	private static FieldInfo shieldGuard;

	private static FieldInfo shieldChance;

	private static FieldInfo shieldKinds;

	private static FieldInfo shieldBlockMul;

	private static FieldInfo shieldReflect;

	private static FieldInfo shieldStrength;

	private static FieldInfo raceTunes;

	private static FieldInfo raceName;

	private static FieldInfo raceEnabled;

	private static FieldInfo mapTicks;

	private static MethodInfo asyncTime;

	private static MethodInfo guardSpan;

	private static MethodInfo blockJolt;

	private static MethodInfo motionPlay;

	private static Type shieldKind;

	private static object hitLayer;

	private static Func<Pawn, Apparel> shieldOf;

	private static Func<bool> ticking;

	private static Func<bool> executing;

	private static Func<bool> simulationLongEvent;

	private static Action clearMotion;

	private static IDictionary authorGuards;

	private static IDictionary chains;

	private static IDictionary spacing;

	private static IDictionary engaged;

	[ThreadStatic]
	private static AttackScope attack;

	[ThreadStatic]
	private static bool reflecting;

	internal static bool Installed { get; private set; }

	private static bool InSimulation
	{
		get
		{
			if (MP.IsInMultiplayer && UnityData.IsInMainThread)
			{
				if (!ticking() && !executing())
				{
					return simulationLongEvent();
				}
				return true;
			}
			return false;
		}
	}

	private static SquishyMeleeCombatComponent Combat => Current.Game?.GetComponent<SquishyMeleeCombatComponent>();

	public SquishyMelee(ModContentPack mod)
	{
		if (Installed)
		{
			return;
		}
		SessionServices.Install();
		Harmony harmony = new Harmony("local.multiplayer.squishy-melee");
		try
		{
			Install(mod, harmony);
		}
		catch
		{
			harmony.UnpatchAll("local.multiplayer.squishy-melee");
			throw;
		}
	}

	private static void Install(ModContentPack mod, Harmony harmony)
	{
		Assembly author = mod.assemblies.loadedAssemblies.Single((Assembly a) => a.GetName().Name == "SquishyMelee");
		settings = AuthorField("MeleeMod", "Settings");
		enabled = AuthorField("MeleeSettings", "enabled");
		shieldMotion = AuthorField("MeleeSettings", "shieldMotion");
		shieldGuard = AuthorField("MeleeSettings", "shieldGuard");
		shieldChance = AuthorField("MeleeSettings", "shieldChance");
		shieldKinds = AuthorField("MeleeSettings", "shieldKinds");
		shieldBlockMul = AuthorField("MeleeSettings", "shieldBlockMul");
		shieldReflect = AuthorField("MeleeSettings", "shieldReflect");
		shieldStrength = AuthorField("MeleeSettings", "shieldStrength");
		raceTunes = AuthorField("MeleeSettings", "raceTunes");
		raceName = AuthorField("MeleeRaceTune", "defName");
		raceEnabled = AuthorField("MeleeRaceTune", "enabled");
		shieldKind = AuthorType("ShieldKind");
		guardSpan = AuthorMethod("MotionGen", "ShieldGuardSpan", new Type[3]
		{
			shieldKind,
			typeof(int).MakeByRefType(),
			typeof(int).MakeByRefType()
		});
		shieldOf = AuthorMethod("ShieldSupport", "ShieldOf", new Type[1] { typeof(Pawn) }).CreateDelegate<Func<Pawn, Apparel>>();
		blockJolt = AuthorMethod("MotionGen", "BlockJolt", new Type[1] { typeof(float) });
		motionPlay = AuthorMethod("MotionEngine", "Play", new Type[9]
		{
			typeof(Pawn),
			AuthorType("MotionAnim"),
			AuthorType("AnimLayer"),
			typeof(UnityEngine.Vector2),
			typeof(int),
			typeof(float),
			typeof(bool),
			typeof(bool?),
			typeof(bool?)
		});
		hitLayer = Enum.Parse(AuthorType("AnimLayer"), "Hit");
		clearMotion = AuthorMethod("MotionEngine", "Clear", Type.EmptyTypes).CreateDelegate<Action>();
		authorGuards = (IDictionary)AuthorField("ShieldGuard", "guards").GetValue(null);
		chains = (IDictionary)AuthorField("Patch_Verb_MeleeAttack", "chains").GetValue(null);
		spacing = (IDictionary)AuthorField("DuelSpacing", "states").GetValue(null);
		engaged = (IDictionary)AuthorField("DuelSpacing", "engagedBy").GetValue(null);
		asyncTime = AccessTools.DeclaredMethod("Multiplayer.Client.Extensions:AsyncTime", new Type[1] { typeof(Map) }) ?? throw new MissingMethodException("Multiplayer map clock");
		mapTicks = AccessTools.Field(asyncTime.ReturnType, "mapTicks") ?? throw new MissingFieldException("Multiplayer map ticks");
		ticking = CoreFlag("Multiplayer.Client.Multiplayer:Ticking");
		executing = CoreFlag("Multiplayer.Client.Multiplayer:ExecutingCmds");
		simulationLongEvent = CoreFlag("Multiplayer.Client.Patches.SimLongEventContext:InSimulation");
		MethodInfo original = AccessTools.DeclaredMethod(typeof(Verb_MeleeAttack), "TryCastShot", Type.EmptyTypes) ?? throw new MissingMethodException("Verb_MeleeAttack.TryCastShot");
		HarmonyMethod prefix = new HarmonyMethod(typeof(SquishyMelee), "BeginAttack")
		{
			priority = 800
		};
		HarmonyMethod postfix = new HarmonyMethod(typeof(SquishyMelee), "FinishAttack")
		{
			priority = 800
		};
		harmony.Patch(original, prefix, postfix, null, Hook("EndAttack"));
		harmony.Patch(AuthorMethod("MeleeAttemptBridge", "Postfix", new Type[1] { typeof(Pawn) }), null, Hook("ObservedAttempt"));
		harmony.Patch(AuthorMethod("ShieldGuard", "Begin", new Type[4]
		{
			typeof(Pawn),
			typeof(Thing),
			typeof(int),
			typeof(int)
		}), Hook("SuppressPersonalGuard"));
		harmony.Patch(AuthorMethod("ShieldSupport", "PreApplyDamagePrefix", new Type[3]
		{
			typeof(Pawn),
			typeof(DamageInfo).MakeByRefType(),
			typeof(bool).MakeByRefType()
		}), Hook("BeforeDamage"));
		MethodInfo original2 = AuthorMethod("ShieldSupport", "Decide", new Type[3]
		{
			typeof(Pawn),
			settings.FieldType,
			shieldKind.MakeByRefType()
		});
		harmony.Patch(original2, Hook("BeforeVisualDecision"), null, Hook("CosmeticRandom"));
		MethodInfo[] array = new MethodInfo[3]
		{
			AuthorMethod("Patch_Verb_MeleeAttack", "Prefix", new Type[1] { typeof(Verb_MeleeAttack) }),
			AuthorMethod("Patch_Verb_MeleeAttack", "Postfix", new Type[2]
			{
				typeof(Verb_MeleeAttack),
				typeof(bool)
			}),
			AuthorMethod("Patch_Pawn_PostApplyDamage", "Postfix", new Type[3]
			{
				typeof(Pawn),
				typeof(DamageInfo),
				typeof(float)
			})
		};
		foreach (MethodInfo methodInfo in array)
		{
			harmony.Patch(methodInfo, Hook("BeginVisual"), null, finalizer: Hook("EndVisual"), transpiler: (methodInfo.DeclaringType == AuthorType("Patch_Pawn_PostApplyDamage")) ? Hook("CosmeticRandom") : null);
		}
		array = new MethodInfo[3]
		{
			AuthorMethod("MeleeLookup", "PickFlag", new Type[3]
			{
				typeof(int),
				typeof(int),
				typeof(int)
			}),
			AuthorMethod("MeleePlanner", "Pick"),
			AuthorMethod("Patch_Verb_MeleeAttack", "PlayDodge", new Type[5]
			{
				typeof(LocalTargetInfo),
				typeof(UnityEngine.Vector2),
				typeof(int),
				typeof(float),
				settings.FieldType
			})
		};
		foreach (MethodInfo original3 in array)
		{
			harmony.Patch(original3, null, null, Hook("CosmeticRandom"));
		}
		string[] array2 = new string[3] { "Patch_PawnRenderer_BodyAngle", "Patch_PawnRenderNodeWorker_Head_RotationFor", "Patch_PawnRenderNodeWorker_ScaleFor" };
		foreach (string type in array2)
		{
			harmony.Patch(AuthorMethod(type, "Postfix"), Hook("AllowRenderResult"));
		}
		SessionServices.RegisterLifecycle("local.gunrecoil.melee.presentation", null, ClearLocal);
		Installed = true;
		FieldInfo AuthorField(string text, string name)
		{
			return AccessTools.DeclaredField(AuthorType(text), name) ?? throw new MissingFieldException(text, name);
		}
		MethodInfo AuthorMethod(string text, string name, Type[] args = null)
		{
			return AccessTools.DeclaredMethod(AuthorType(text), name, args) ?? throw new MissingMethodException(text, name);
		}
		Type AuthorType(string name)
		{
			return author.GetType("SquishyMelee." + name, throwOnError: true);
		}
	}

	private static HarmonyMethod Hook(string name)
	{
		return new HarmonyMethod(typeof(SquishyMelee), name);
	}

	private static Func<bool> CoreFlag(string member)
	{
		return (AccessTools.PropertyGetter(member) ?? throw new MissingMemberException(member)).CreateDelegate<Func<bool>>();
	}

	internal static int MapTick(Map map)
	{
		object obj = ((map == null) ? null : asyncTime.Invoke(null, new object[1] { map }));
		if (obj != null)
		{
			return (int)mapTicks.GetValue(obj);
		}
		return -1;
	}

	private static void BeginAttack(Verb_MeleeAttack __instance, out AttackScope __state)
	{
		__state = null;
		if (!InSimulation || !Installed)
		{
			return;
		}
		SquishyMeleeCombatComponent combat = Combat;
		Pawn casterPawn = __instance.CasterPawn;
		Thing thing = __instance.CurrentTarget.Thing;
		SquishyMeleeCombatSettings squishyMeleeCombatSettings = combat?.Combat;
		if (squishyMeleeCombatSettings == null || !squishyMeleeCombatSettings.Active || casterPawn?.Map == null || thing == null || thing.MapHeld != casterPawn.Map)
		{
			return;
		}
		AttackScope attackScope = new AttackScope
		{
			Previous = attack,
			Component = combat,
			Pawn = casterPawn,
			Target = thing
		};
		if (!squishyMeleeCombatSettings.DisabledRaces.Contains(casterPawn.def.defName) && shieldOf(casterPawn) != null)
		{
			attackScope.Kind = PickKind(squishyMeleeCombatSettings, Rand.Value, (int count) => Rand.Range(0, count));
		}
		attack = (__state = attackScope);
	}

	internal static int PickKind(SquishyMeleeCombatSettings config, float roll, Func<int, int> pick)
	{
		if (roll >= config.Chance)
		{
			return -1;
		}
		int num = BitOperations.PopCount((uint)config.Kinds);
		if (num == 0)
		{
			return -1;
		}
		int num2 = pick(num);
		for (int i = 0; i < 3; i++)
		{
			if ((config.Kinds & (1 << i)) != 0 && num2-- == 0)
			{
				return i;
			}
		}
		return -1;
	}

	private static void ObservedAttempt(Pawn __0)
	{
		AttackScope attackScope = attack;
		if (attackScope != null && attackScope.Pawn == __0)
		{
			attackScope.Attempted = true;
		}
	}

	private static void FinishAttack(Verb_MeleeAttack __instance, bool __result, AttackScope __state)
	{
		if (__state == null || __state.Kind < 0)
		{
			return;
		}
		Pawn pawn = __state.Pawn;
		Thing target = __state.Target;
		Map map = pawn.Map;
		if (map != null && target.MapHeld == map && pawn.Spawned && !pawn.Downed && !pawn.Dead && (__result || __state.Attempted))
		{
			int num = MapTick(map);
			if (num >= 0)
			{
				object[] array = new object[3]
				{
					Enum.ToObject(shieldKind, __state.Kind),
					0,
					0
				};
				guardSpan.Invoke(null, array);
				__state.Component.Begin(pawn, target, num + (int)array[1], num + (int)array[2]);
			}
		}
	}

	private static Exception EndAttack(Exception __exception, AttackScope __state)
	{
		if (__state != null && attack == __state)
		{
			attack = __state.Previous;
		}
		return __exception;
	}

	private static bool BeforeVisualDecision(Pawn __0, object[] __args, ref bool __result)
	{
		AttackScope attackScope = attack;
		if (attackScope == null || attackScope.Pawn != __0)
		{
			return true;
		}
		int num;
		if (attackScope.Kind >= 0)
		{
			object obj = __args[1];
			if (obj != null && (bool)shieldMotion.GetValue(obj))
			{
				num = (((float)shieldChance.GetValue(obj) > 0f) ? 1 : 0);
				goto IL_0051;
			}
		}
		num = 0;
		goto IL_0051;
		IL_0051:
		__result = (byte)num != 0;
		__args[2] = Enum.ToObject(shieldKind, Math.Max(0, attackScope.Kind));
		return false;
	}

	private static bool SuppressPersonalGuard()
	{
		if (Installed)
		{
			return !MP.IsInMultiplayer;
		}
		return true;
	}

	private static bool AllowRenderResult()
	{
		return !InSimulation;
	}

	private static bool BeforeDamage(Pawn __0, ref DamageInfo __1, ref bool __2, ref bool __result)
	{
		if (!Installed || !MP.IsInMultiplayer)
		{
			return true;
		}
		__result = true;
		if (!reflecting && InSimulation)
		{
			SquishyMeleeCombatComponent combat = Combat;
			if (combat != null)
			{
				__result = ApplyGuard(combat, __0, ref __1, ref __2);
			}
		}
		return false;
	}

	internal static bool ApplyGuard(SquishyMeleeCombatComponent component, Pawn pawn, ref DamageInfo damage, ref bool absorbed)
	{
		SquishyMeleeCombatSettings combat = component.Combat;
		if (reflecting || combat == null || !combat.Active || pawn?.Map == null || !component.TryGet(pawn, out var guard))
		{
			return true;
		}
		int num = MapTick(pawn.Map);
		if (num >= guard.From && num <= guard.To && damage.Instigator == guard.Opponent && damage.Instigator != pawn)
		{
			DamageDef def = damage.Def;
			if (def != null && def.harmsHealth && !(damage.Amount <= 0f))
			{
				ThingDef weapon = damage.Weapon;
				if (weapon == null || !weapon.IsRangedWeapon)
				{
					float amount = damage.Amount;
					float num2 = amount * (1f - combat.BlockMultiplier);
					if (num2 <= 0.01f)
					{
						return true;
					}
					PlayBlock(pawn, guard.Opponent);
					float num3 = combat.Reflect * num2;
					if (num3 >= 0.5f && guard.Opponent.Spawned)
					{
						reflecting = true;
						try
						{
							guard.Opponent.TakeDamage(new DamageInfo(DamageDefOf.Blunt, num3, 0f, -1f, pawn));
						}
						finally
						{
							reflecting = false;
						}
					}
					if (combat.BlockMultiplier <= 0.001f)
					{
						absorbed = true;
						return false;
					}
					damage.SetAmount(amount * combat.BlockMultiplier);
					return true;
				}
			}
		}
		return true;
	}

	private static void PlayBlock(Pawn pawn, Thing opponent)
	{
		if (!pawn.Spawned || !SessionServices.BeginLocalOnly(out var token))
		{
			return;
		}
		try
		{
			object value = settings.GetValue(null);
			if (value != null && (bool)enabled.GetValue(value) && (bool)shieldMotion.GetValue(value))
			{
				MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, "SM_Blocked".Translate(), new Color(0.75f, 0.9f, 1f));
				DefDatabase<SoundDef>.GetNamedSilentFail("MetalHitImportant")?.PlayOneShot(new TargetInfo(pawn.Position, pawn.Map));
				UnityEngine.Vector3 vector = pawn.DrawPos - opponent.DrawPos;
				UnityEngine.Vector2 vector2 = new UnityEngine.Vector2(vector.x, vector.z);
				vector2 = ((!(vector2.sqrMagnitude > 0.0004f)) ? (pawn.Rotation.AsInt switch
				{
					0 => new UnityEngine.Vector2(0f, 1f), 
					1 => new UnityEngine.Vector2(1f, 0f), 
					2 => new UnityEngine.Vector2(0f, -1f), 
					_ => new UnityEngine.Vector2(-1f, 0f), 
				}) : vector2.normalized);
				object obj = blockJolt.Invoke(null, new object[1] { Mathf.Clamp((float)shieldStrength.GetValue(value), 0f, 2f) });
				if (obj != null)
				{
					motionPlay.Invoke(null, new object[9] { pawn, obj, hitLayer, vector2, 0, 1f, false, null, null });
				}
			}
		}
		catch (Exception value2)
		{
			Log.ErrorOnce($"MP Compat: SQUISHY shield presentation failed: {value2}", 84750003);
		}
		finally
		{
			SessionServices.EndLocalOnly(token);
		}
	}

	private static bool BeginVisual(out long __state)
	{
		__state = 0L;
		if (MP.IsInMultiplayer)
		{
			return SessionServices.BeginLocalOnly(out __state);
		}
		return true;
	}

	private static Exception EndVisual(Exception __exception, long __state)
	{
		SessionServices.EndLocalOnly(__state);
		return __exception;
	}

	private static float CosmeticValue()
	{
		if (!MP.IsInMultiplayer)
		{
			return UnityEngine.Random.value;
		}
		return System.Random.Shared.NextSingle();
	}

	private static float CosmeticFloat(float min, float max)
	{
		if (!MP.IsInMultiplayer)
		{
			return UnityEngine.Random.Range(min, max);
		}
		return min + (max - min) * System.Random.Shared.NextSingle();
	}

	private static int CosmeticInt(int min, int max)
	{
		if (!MP.IsInMultiplayer)
		{
			return UnityEngine.Random.Range(min, max);
		}
		return System.Random.Shared.Next(min, max);
	}

	private static IEnumerable<CodeInstruction> CosmeticRandom(IEnumerable<CodeInstruction> instructions, MethodBase original)
	{
		Type typeFromHandle = typeof(UnityEngine.Random);
		MethodInfo value = AccessTools.PropertyGetter(typeFromHandle, "value");
		MethodInfo floating = AccessTools.DeclaredMethod(typeFromHandle, "Range", new Type[2]
		{
			typeof(float),
			typeof(float)
		});
		MethodInfo integer = AccessTools.DeclaredMethod(typeFromHandle, "Range", new Type[2]
		{
			typeof(int),
			typeof(int)
		});
		int count = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			string text;
			if (instruction.Calls(value))
			{
				text = "CosmeticValue";
			}
			else if (instruction.Calls(floating))
			{
				text = "CosmeticFloat";
			}
			else
			{
				text = (instruction.Calls(integer) ? "CosmeticInt" : null);
			}
			if (text == null)
			{
				yield return instruction;
				continue;
			}
			count++;
			yield return new CodeInstruction(instruction)
			{
				opcode = OpCodes.Call,
				operand = AccessTools.DeclaredMethod(typeof(SquishyMelee), text)
			};
		}
		int num = ((!(original.Name == "Decide")) ? 1 : 2);
		if (count != num)
		{
			throw new InvalidOperationException($"SQUISHY Melee RNG callsites changed in {original.Name}: {count}/{num}");
		}
	}

	internal static SquishyMeleeCombatSettings CaptureSettings()
	{
		object value = settings.GetValue(null);
		if (value == null)
		{
			return new SquishyMeleeCombatSettings();
		}
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		HashSet<string> hashSet2 = new HashSet<string>(StringComparer.Ordinal);
		if (raceTunes.GetValue(value) is IEnumerable enumerable)
		{
			foreach (object item2 in enumerable)
			{
				if (item2 != null && raceName.GetValue(item2) is string item && hashSet2.Add(item) && !(bool)raceEnabled.GetValue(item2))
				{
					hashSet.Add(item);
				}
			}
		}
		return new SquishyMeleeCombatSettings
		{
			Enabled = (bool)enabled.GetValue(value),
			ShieldMotion = (bool)shieldMotion.GetValue(value),
			Guard = (bool)shieldGuard.GetValue(value),
			Chance = Unit((float)shieldChance.GetValue(value), 0f),
			Kinds = ((int)shieldKinds.GetValue(value) & 7),
			BlockMultiplier = Unit((float)shieldBlockMul.GetValue(value), 1f),
			Reflect = Unit((float)shieldReflect.GetValue(value), 0f),
			DisabledRaces = hashSet.OrderBy((string name) => name, StringComparer.Ordinal).ToList()
		};
	}

	private static float Unit(float value, float fallback)
	{
		if (!float.IsFinite(value))
		{
			return fallback;
		}
		return Mathf.Clamp01(value);
	}

	private static void ClearLocal()
	{
		attack = null;
		reflecting = false;
		clearMotion();
		chains.Clear();
		spacing.Clear();
		engaged.Clear();
		authorGuards.Clear();
	}
}
