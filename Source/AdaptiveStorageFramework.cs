using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("adaptive.storage.framework")]
internal class AdaptiveStorageFramework
{
	private static readonly MethodInfo IsInMainThreadGetter = AccessTools.PropertyGetter(typeof(UnityData), "IsInMainThread");

	public AdaptiveStorageFramework(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(() =>
		{
			MpCompatPatchLoader.LoadPatch(this);
		});
	}

	[MpCompatTranspiler("AdaptiveStorage.StorageRenderer", "SetPrintDataDirty", MethodType.Normal)]
	private static IEnumerable<CodeInstruction> SetPrintDataDirty_Transpiler(IEnumerable<CodeInstruction> insts)
	{
		foreach (CodeInstruction inst in insts)
		{
			if (inst.Calls(IsInMainThreadGetter))
			{
				yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(AdaptiveStorageFramework), "IsInMainThreadAndNotMp"));
			}
			else
			{
				yield return inst;
			}
		}
	}

	private static bool IsInMainThreadAndNotMp()
	{
		if (UnityData.IsInMainThread)
		{
			return !MP.enabled;
		}
		return false;
	}
}
