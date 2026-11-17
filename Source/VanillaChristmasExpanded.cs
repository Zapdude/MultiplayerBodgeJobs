using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("VE.VanillaChristmasExpanded")]
public class VanillaChristmasExpanded
{
	public VanillaChristmasExpanded(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
		MpCompat.RegisterLambdaMethod("VanillaChristmasExpanded.GrandFestiveTree", "GetGizmos", default(int));
		MpCompat.RegisterLambdaMethod("VanillaChristmasExpanded.CompSnowSpewer", "CompGetGizmosExtra", 0, 1);
		PatchVisualSprayer();
		PatchPresentGraphics();
		PatchWorldCalendar();
		VanillaChristmasDistribution.Install();
		PatchGiftDiagnostics();
	}

	internal static void PatchGiftDiagnostics()
	{
		MethodInfo original = AccessTools.DeclaredMethod("VanillaChristmasExpanded.VanillaChristmasExpanded_FactionGiftUtility_GiveGift_DropPods_Patch:Prefix");
		MpCompat.harmony.Patch(original, null, null, new HarmonyMethod(typeof(VanillaChristmasExpanded), "GiftDiagnosticIl"));
	}

	private static IEnumerable<CodeInstruction> GiftDiagnosticIl(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo message = AccessTools.DeclaredMethod(typeof(Log), "Message", new Type[1] { typeof(string) });
		MethodInfo replacement = AccessTools.DeclaredMethod(typeof(VanillaChristmasExpanded), "GiftItemDiagnostic");
		int count = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (!instruction.Calls(message))
			{
				yield return instruction;
				continue;
			}
			count++;
			yield return new CodeInstruction(instruction)
			{
				opcode = OpCodes.Call,
				operand = replacement
			};
		}
		if (count != 1)
		{
			throw new InvalidOperationException($"VCE gift-item diagnostic call changed: {count}");
		}
	}

	private static void GiftItemDiagnostic(string text)
	{
		if (!MP.IsInMultiplayer)
		{
			Log.Message(text);
		}
	}

	private static void LatePatch()
	{
		MpCompatPatchLoader.LoadPatch<VanillaChristmasExpanded>();
	}

	internal static void PatchWorldCalendar()
	{
		PatchingUtilities.SetupAsyncTime();
		MpCompat.harmony.Patch(AccessTools.PropertyGetter(AccessTools.TypeByName("VanillaChristmasExpanded.FestiveFavorManager"), "DaysUntilDecembrary10"), new HarmonyMethod(typeof(VanillaChristmasExpanded), "BeforeWorldCalendar"), null, null, new HarmonyMethod(typeof(VanillaChristmasExpanded), "AfterWorldCalendar"));
	}

	private static void BeforeWorldCalendar(out PatchingUtilities.TimeSnapshot? __state)
	{
		__state = ((MP.IsInMultiplayer && PatchingUtilities.IsAsyncTime) ? PatchingUtilities.TimeSnapshot.GetAndSetFromWorld() : ((PatchingUtilities.TimeSnapshot?)null));
	}

	private static void AfterWorldCalendar(PatchingUtilities.TimeSnapshot? __state)
	{
		__state?.Set();
	}

	internal static void PatchVisualSprayer()
	{
		MethodInfo methodInfo = AccessTools.DeclaredMethod("VanillaChristmasExpanded.SnowSpewer_SteamSprayer:SteamSprayerTick");
		if (!(methodInfo == null))
		{
			MpCompat.harmony.Patch(methodInfo, new HarmonyMethod(typeof(VanillaChristmasExpanded), "PushVisualRand")
			{
				priority = 800
			}, null, null, new HarmonyMethod(typeof(VanillaChristmasExpanded), "PopVisualRand")
			{
				priority = 0
			});
		}
	}

	internal static void PatchPresentGraphics()
	{
		SessionServices.Install();
		MethodInfo original = AccessTools.DeclaredMethod("VanillaChristmasExpanded.FestivePresent:StoreGraphics");
		MpCompat.harmony.Patch(original, new HarmonyMethod(typeof(VanillaChristmasExpanded), "BeforePresentGraphics")
		{
			priority = 0
		}, null, null, new HarmonyMethod(typeof(VanillaChristmasExpanded), "AfterPresentGraphics")
		{
			priority = 800
		});
	}

	private static void BeforePresentGraphics(Thing __instance, out (bool RandPushed, IDisposable LocalScope) __state)
	{
		__state = default;
		if (!MP.IsInMultiplayer)
		{
			return;
		}
		Rand.PushState(Gen.HashCombineInt(__instance.thingIDNumber, 1447249232));
		__state.RandPushed = true;
		try
		{
			__state.LocalScope = SessionServices.EnterLocalOnly();
		}
		catch
		{
			Rand.PopState();
			__state = default;
			throw;
		}
	}

	private static Exception AfterPresentGraphics(Exception __exception, (bool RandPushed, IDisposable LocalScope) __state)
	{
		try
		{
			__state.LocalScope?.Dispose();
		}
		finally
		{
			if (__state.RandPushed)
			{
				Rand.PopState();
			}
		}
		return __exception;
	}

	private static void PushVisualRand(out bool __state)
	{
		__state = false;
		Rand.PushState();
		__state = true;
	}

	private static void PopVisualRand(bool __state)
	{
		if (__state)
		{
			Rand.PopState();
		}
	}

	private static int ModifyScreenWidthValue(int screenWidth)
	{
		if (MP.IsInMultiplayer)
		{
			return screenWidth - 116;
		}
		return screenWidth;
	}

	[MpCompatTranspiler("VanillaChristmasExpanded.FestiveFavorManager", "DrawUI", MethodType.Normal)]
	private static IEnumerable<CodeInstruction> MoveFestiveOverlayPosition(IEnumerable<CodeInstruction> instr, MethodBase baseMethod)
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
		if (replacedCount != 2)
		{
			string value = ((baseMethod.DeclaringType?.Namespace).NullOrEmpty() ? baseMethod.Name : (baseMethod.DeclaringType.Name + ":" + baseMethod.Name));
			Log.Warning($"Patched incorrect number of UI.screenWidth reads (patched {replacedCount}, expected {2}) for method {value}");
		}
	}
}
