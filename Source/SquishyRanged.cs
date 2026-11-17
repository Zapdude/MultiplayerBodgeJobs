using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using UnityEngine;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("local.gunrecoil")]
public sealed class SquishyRanged
{
	public SquishyRanged(ModContentPack mod)
	{
		MethodInfo method = RequiredMethod(RequiredType("GunRecoil.CasingMapComponent"), "Eject", new Type[9]
		{
			typeof(Vector3),
			typeof(Vector3),
			typeof(int),
			typeof(bool),
			typeof(float),
			typeof(float),
			typeof(float),
			typeof(float),
			typeof(int)
		});
		Type[] arguments = new Type[2]
		{
			typeof(Pawn),
			typeof(float).MakeByRefType()
		};
		MethodInfo original = RequiredMethod(RequiredType("GunRecoil.Patch_PawnRenderer_BodyAngle"), "Postfix", arguments);
		MethodInfo original2 = RequiredMethod(RequiredType("GunRecoil.SquishyWalk_BodyAngle"), "Postfix", arguments);
		SessionServices.Install();
		IsolateEffect(method);
		HarmonyMethod prefix = new HarmonyMethod(typeof(SquishyRanged), "AllowVisualAngle");
		MpCompat.harmony.Patch(original, prefix);
		MpCompat.harmony.Patch(original2, prefix);
		Type type = AccessTools.TypeByName("MuzzleFlash.MuzzleFlashUtility");
		Type type2 = AccessTools.TypeByName("MuzzleFlash.MuzzleFlashDef");
		if (type != null && type2 != null)
		{
			IsolateEffect(RequiredMethod(type, "SpawnMuzzleFlash", new Type[7]
			{
				typeof(Map),
				type2,
				typeof(Vector3),
				typeof(Vector3),
				typeof(Vector3),
				typeof(Vector2),
				typeof(bool)
			}));
		}
	}

	private static Type RequiredType(string name)
	{
		return AccessTools.TypeByName(name) ?? throw new TypeLoadException(name);
	}

	private static MethodInfo RequiredMethod(Type type, string name, Type[] arguments)
	{
		MethodInfo methodInfo = AccessTools.DeclaredMethod(type, name, arguments);
		if ((object)methodInfo != null)
		{
			Type returnType = methodInfo.ReturnType;
			if (returnType == typeof(void))
			{
				return methodInfo;
			}
		}
		throw new MissingMethodException(type.FullName, name);
	}

	private static void IsolateEffect(MethodInfo method)
	{
		MpCompat.harmony.Patch(method, new HarmonyMethod(typeof(SquishyRanged), "BeginLocalEffect")
		{
			priority = 801
		}, null, null, new HarmonyMethod(typeof(SquishyRanged), "EndLocalEffect")
		{
			priority = 0
		});
	}

	private static void BeginLocalEffect(out long __state)
	{
		SessionServices.BeginLocalOnly(out __state);
	}

	private static void EndLocalEffect(long __state)
	{
		if (__state != 0L)
		{
			SessionServices.EndLocalOnly(__state);
		}
	}

	private static bool AllowVisualAngle()
	{
		if (MP.IsInMultiplayer)
		{
			return MP.InInterface;
		}
		return true;
	}
}
