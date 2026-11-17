using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using PipeSystem;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("oskarpotocki.vanillafactionsexpanded.core")]
internal class PipeSystem17
{
	public PipeSystem17(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(ApplyPatches);
	}

	private static void ApplyPatches()
	{
		MpCompat.harmony.Patch(AccessTools.Method(typeof(LinkedPipes), "GetOverlayFor"), new HarmonyMethod(typeof(PipeSystem17), "EnsureOverlay"));
		MpCompat.harmony.Patch(AccessTools.Method(typeof(LinkedPipes), "GetPipeFor"), new HarmonyMethod(typeof(PipeSystem17), "EnsurePipe"));
	}

	internal static bool ReindexIfPresent<TKey, TValue>(Dictionary<TKey, TValue> cache, TKey key)
	{
		if (cache.ContainsKey(key))
		{
			return true;
		}
		if (!cache.Keys.Any((TKey existing) => (object)existing == (object)key))
		{
			return false;
		}
		KeyValuePair<TKey, TValue>[] array = cache.ToArray();
		cache.Clear();
		KeyValuePair<TKey, TValue>[] array2 = array;
		for (int num = 0; num < array2.Length; num++)
		{
			KeyValuePair<TKey, TValue> keyValuePair = array2[num];
			cache[keyValuePair.Key] = keyValuePair.Value;
		}
		return cache.ContainsKey(key);
	}

	private static void EnsureOverlay(PipeNetDef resource)
	{
		//IL_005c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0066: Expected Obj, but got Unknown
		if (resource != null && !ReindexIfPresent(LinkedPipes.overlayLinked, resource))
		{
			OverlayOption overlayOptions = resource.overlayOptions;
			Graphic graphic = GraphicDatabase.Get<Graphic_Single>(overlayOptions.transmitterAtlas, ShaderDatabase.MetaOverlay);
			if (overlayOptions.transmitterAtlas == "Special/PSTransmitterAtlas")
			{
				graphic = graphic.GetColoredVersion(graphic.Shader, overlayOptions.overlayColor, overlayOptions.overlayColor);
			}
			LinkedPipes.overlayLinked[resource] = new Graphic_LinkedOverlayPipe(graphic, resource);
		}
	}

	private static void EnsurePipe(ThingDef pipe)
	{
		//IL_0070: Unknown result type (might be due to invalid IL or missing references)
		//IL_007a: Expected Obj, but got Unknown
		if (pipe != null && !ReindexIfPresent(LinkedPipes.pipesLinked, pipe))
		{
			PipeNetDef val = DefDatabase<PipeNetDef>.AllDefsListForReading.FirstOrDefault((PipeNetDef def) => def.pipeDefs?.Contains(pipe) ?? false);
			if (val != null)
			{
				Graphic graphic = GraphicDatabase.Get<Graphic_Single>(pipe.graphic.data.texPath, ShaderDatabase.Cutout);
				LinkedPipes.pipesLinked[pipe] = new Graphic_LinkedPipe(graphic, val);
			}
		}
	}
}
