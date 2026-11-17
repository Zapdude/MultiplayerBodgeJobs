using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("syrchalis.processor.framework")]
internal class ProcessorFramework
{
	private static AccessTools.FieldRef<object, IDictionary> compProcessorEnabledProcessesField;

	private static AccessTools.FieldRef<object, IList> compProcessorActiveProcessesField;

	private static AccessTools.FieldRef<object, ThingComp> activeProcessProcessorField;

	public ProcessorFramework(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
	}

	public void LatePatch()
	{
		Type type = AccessTools.TypeByName("ProcessorFramework.CompProcessor");
		compProcessorEnabledProcessesField = AccessTools.FieldRefAccess<IDictionary>(type, "enabledProcesses");
		compProcessorActiveProcessesField = AccessTools.FieldRefAccess<IList>(type, "activeProcesses");
		MP.RegisterSyncMethod(type, "EnableAllProcesses");
		MP.RegisterSyncMethod(type, "ToggleProcess");
		MP.RegisterSyncMethod(type, "ToggleIngredient");
		type = AccessTools.TypeByName("ProcessorFramework.ProcessorFramework_Utility");
		MP.RegisterSyncMethod(type, "SetEmptyNow").SetContext(SyncContext.MapSelected);
		type = AccessTools.TypeByName("ProcessorFramework.Command_Quality");
		MP.RegisterSyncMethod(type, "ChangeQuality").SetContext(SyncContext.MapSelected);
		type = AccessTools.TypeByName("ProcessorFramework.ITab_ProcessorContents");
		MpCompat.RegisterLambdaDelegate(type, "GetProgressQualityDropdowns", default(int));
		type = AccessTools.TypeByName("ProcessorFramework.ActiveProcess");
		activeProcessProcessorField = AccessTools.FieldRefAccess<ThingComp>(type, "processor");
		MP.RegisterSyncWorker<object>(SyncActiveProcess, type);
		type = AccessTools.TypeByName("ProcessorFramework.ITab_ProcessSelection");
		MP.RegisterSyncMethod(typeof(ProcessorFramework), "SyncedClear");
		MpCompat.harmony.Patch(AccessTools.Method(type, "FillTab"), new HarmonyMethod(typeof(ProcessorFramework), "PreFillTab"), new HarmonyMethod(typeof(ProcessorFramework), "PostFillTab"));
	}

	private static void SyncActiveProcess(SyncWorker sync, ref object process)
	{
		if (sync.isWriting)
		{
			ThingComp thingComp = activeProcessProcessorField(process);
			int num = compProcessorActiveProcessesField(thingComp).IndexOf(process);
			sync.Write(num);
			if (num >= 0)
			{
				sync.Write(thingComp);
			}
		}
		else
		{
			int num2 = sync.Read<int>();
			if (num2 >= 0)
			{
				process = compProcessorActiveProcessesField(sync.Read<ThingComp>())[num2];
			}
		}
	}

	private static void PreFillTab(IEnumerable ___processorComps, ref DictionaryEntry[][] __state)
	{
		if (___processorComps == null)
		{
			__state = null;
			return;
		}
		object[] array = (___processorComps as object[]) ?? ___processorComps.Cast<object>().ToArray();
		__state = new DictionaryEntry[array.Length][];
		for (int i = 0; i < array.Length; i++)
		{
			IDictionary dictionary = compProcessorEnabledProcessesField(array[i]);
			if (dictionary == null)
			{
				__state[i] = Array.Empty<DictionaryEntry>();
				continue;
			}
			__state[i] = new DictionaryEntry[dictionary.Count];
			dictionary.CopyTo(__state[i], 0);
		}
	}

	private static void PostFillTab(IEnumerable ___processorComps, ref DictionaryEntry[][] __state)
	{
		if (__state == null)
		{
			return;
		}
		ThingComp[] array = (___processorComps as ThingComp[]) ?? ___processorComps.Cast<ThingComp>().ToArray();
		for (int i = 0; i < array.Length; i++)
		{
			if (__state[i].Length == 0 || compProcessorEnabledProcessesField(array[i]).Count != 0)
			{
				continue;
			}
			for (i = 0; i < array.Length; i++)
			{
				IDictionary dictionary = compProcessorEnabledProcessesField(array[i]);
				dictionary.Clear();
				DictionaryEntry[] array2 = __state[i];
				for (int j = 0; j < array2.Length; j++)
				{
					DictionaryEntry dictionaryEntry = array2[j];
					dictionary.Add(dictionaryEntry.Key, dictionaryEntry.Value);
				}
			}
			SyncedClear(array);
			break;
		}
	}

	private static void SyncedClear(ThingComp[] comps)
	{
		foreach (ThingComp instance in comps)
		{
			compProcessorEnabledProcessesField(instance).Clear();
		}
	}
}
