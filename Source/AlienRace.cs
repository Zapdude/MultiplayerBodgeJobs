using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("erdelf.HumanoidAlienRaces")]
public class AlienRace
{
	private sealed class AcceptState
	{
		public Pawn pawn;

		public AlienCompData data;
	}

	public class AlienCompData
	{
		public class LinkData
		{
			public List<(string channel, int categoryIndex)> targetsOne = new List<(string, int)>();

			public List<(string channel, int categoryIndex)> targetsTwo = new List<(string, int)>();
		}

		public Dictionary<string, (Color, Color)> colorChannels = new Dictionary<string, (Color, Color)>();

		public List<int> addonVariants = new List<int>();

		public List<(Color? first, Color? second)> addonColors = new List<(Color?, Color?)>();

		public Dictionary<string, LinkData> colorChannelLinks = new Dictionary<string, LinkData>();
	}

	private sealed class StylingContext
	{
		internal object[] Fields;

		internal object[][] Collections;

		internal static StylingContext Capture()
		{
			return new StylingContext
			{
				Fields = stylingContextFields.Select((FieldInfo field) => field.GetValue(null)).ToArray(),
				Collections = stylingContextCollections.Select((FieldInfo field) =>
				{
					object value = field.GetValue(null);
					if (value is IDictionary dictionary)
					{
						List<object> list = new List<object>();
						foreach (DictionaryEntry item in dictionary)
						{
							list.Add(item);
						}
						return list.ToArray();
					}
					if (value is IList source)
					{
						return source.Cast<object>().ToArray();
					}
					throw new InvalidOperationException("Unsupported HAR styling collection " + field.Name);
				}).ToArray()
			};
		}

		internal void Restore()
		{
			for (int i = 0; i < stylingContextFields.Length; i++)
			{
				stylingContextFields[i].SetValue(null, Fields[i]);
			}
			for (int j = 0; j < stylingContextCollections.Length; j++)
			{
				object value = stylingContextCollections[j].GetValue(null);
				if (value is IDictionary dictionary)
				{
					dictionary.Clear();
					object[] array = Collections[j];
					for (int k = 0; k < array.Length; k++)
					{
						DictionaryEntry dictionaryEntry = (DictionaryEntry)array[k];
						dictionary.Add(dictionaryEntry.Key, dictionaryEntry.Value);
					}
				}
				else if (value is IList list)
				{
					list.Clear();
					object[] array = Collections[j];
					foreach (object value2 in array)
					{
						list.Add(value2);
					}
				}
			}
		}
	}

	private sealed class StylingContextScope
	{
		internal StylingContext Previous;

		internal Dialog_StylingStation PreviousDialog;

		internal Dialog_StylingStation Dialog;

		internal bool IsPreview;
	}

	private static Type alienCompType;

	private static FieldInfo originPawnField;

	private static FieldInfo[] copiedVariantFields;

	private static FieldInfo stylingDialogDrawingField;

	private static FieldInfo stylingDialogPawnField;

	private static FieldInfo addonVariantsField;

	private static FieldInfo addonColorsField;

	private static FieldInfo colorChannelsField;

	private static FieldInfo colorChannelLinksField;

	private static Type colorChannelLinkDataType;

	private static Type colorChannelLinkTargetDataType;

	private static FieldInfo linkOriginalChannelField;

	private static FieldInfo linkTargetsOneField;

	private static FieldInfo linkTargetsTwoField;

	private static FieldInfo linkTargetChannelField;

	private static FieldInfo linkTargetCategoryIndexField;

	private const string HarHarmonyOwner = "rimworld.erdelf.alien_race.main";

	private static FieldInfo[] stylingContextFields;

	private static FieldInfo[] stylingContextCollections;

	private static FieldInfo stylingContextPawnField;

	private static MethodInfo stylingContextAuthorConstructor;

	private static readonly ConditionalWeakTable<Dialog_StylingStation, StylingContext> stylingContexts = new ConditionalWeakTable<Dialog_StylingStation, StylingContext>();

	[ThreadStatic]
	private static Dialog_StylingStation activeStylingDialog;

	public AlienRace(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
	}

	private static void LatePatch()
	{
		alienCompType = AccessTools.TypeByName("AlienRace.AlienPartGenerator+AlienComp");
		if (!(alienCompType == null))
		{
			originPawnField = AccessTools.Field(AccessTools.TypeByName("Multiplayer.Client.Patches.StylingDialog_DummyPawn"), "origPawn");
			stylingDialogPawnField = AccessTools.DeclaredField(typeof(Dialog_StylingStation), "pawn");
			stylingDialogDrawingField = AccessTools.Field(AccessTools.TypeByName("Multiplayer.Client.Patches.StylingDialog_Marker"), "drawing");
			copiedVariantFields = new string[5] { "bodyVariant", "headVariant", "bodyMaskVariant", "headMaskVariant", "lastAlienMeatIngestedTick" }.Select((string name) => AccessTools.Field(alienCompType, name)).ToArray();
			addonVariantsField = AccessTools.Field(alienCompType, "addonVariants");
			addonColorsField = AccessTools.Field(alienCompType, "addonColors");
			colorChannelsField = AccessTools.Field(alienCompType, "colorChannels");
			colorChannelLinksField = AccessTools.Field(alienCompType, "colorChannelLinks");
			colorChannelLinkDataType = AccessTools.TypeByName("AlienRace.AlienPartGenerator+AlienComp+ColorChannelLinkData");
			colorChannelLinkTargetDataType = AccessTools.TypeByName("AlienRace.AlienPartGenerator+AlienComp+ColorChannelLinkData+ColorChannelLinkTargetData");
			linkOriginalChannelField = AccessTools.Field(colorChannelLinkDataType, "originalChannel");
			linkTargetsOneField = AccessTools.Field(colorChannelLinkDataType, "targetsChannelOne");
			linkTargetsTwoField = AccessTools.Field(colorChannelLinkDataType, "targetsChannelTwo");
			linkTargetChannelField = AccessTools.Field(colorChannelLinkTargetDataType, "targetChannel");
			linkTargetCategoryIndexField = AccessTools.Field(colorChannelLinkTargetDataType, "categoryIndex");
			MpCompat.harmony.Patch(AccessTools.Constructor(typeof(Dialog_StylingStation), new Type[2]
			{
				typeof(Pawn),
				typeof(Thing)
			}), new HarmonyMethod(typeof(AlienRace), "StylingStationCtorPrefix"));
			MethodInfo original = AccessTools.Method(AccessTools.TypeByName("Multiplayer.Client.Patches.StylingDialog_HandleAccept"), "OnAccept");
			MpCompat.harmony.Patch(original, new HarmonyMethod(typeof(AlienRace), "OnAcceptPrefix"), new HarmonyMethod(typeof(AlienRace), "OnAcceptPostfix"));
			InitializeStylingContext();
			MP.RegisterSyncWorker<AlienCompData>(SyncAlienCompData);
			MP.RegisterSyncMethod(typeof(AlienRace), "SyncAlienData");
		}
	}

	private static void StylingStationCtorPrefix(Pawn pawn)
	{
		FieldInfo fieldInfo = originPawnField;
		if ((object)fieldInfo == null || fieldInfo.DeclaringType?.IsInstanceOfType(pawn) != true || (pawn.AllComps != null && pawn.AllComps.Any((ThingComp c) => c.GetType() == alienCompType)))
		{
			return;
		}
		pawn.InitializeComps();
		ThingComp thingComp = pawn.AllComps?.FirstOrDefault((ThingComp c) => c.GetType() == alienCompType);
		if (thingComp != null && originPawnField.GetValue(pawn) is Pawn { AllComps: var allComps })
		{
			ThingComp thingComp2 = allComps?.FirstOrDefault((ThingComp c) => c.GetType() == alienCompType);
			if (thingComp2 != null)
			{
				DirectCopyAlienComp(thingComp2, thingComp);
			}
		}
	}

	private static void DirectCopyAlienComp(object src, object dst)
	{
		SetAlienCompData(dst, SnapshotAlienComp(src));
		FieldInfo[] array = copiedVariantFields;
		foreach (FieldInfo fieldInfo in array)
		{
			fieldInfo.SetValue(dst, fieldInfo.GetValue(src));
		}
		IDictionary dictionary = (IDictionary)colorChannelLinksField.GetValue(src);
		IDictionary dictionary2 = (IDictionary)colorChannelLinksField.GetValue(dst);
		if (dictionary == null)
		{
			return;
		}
		foreach (DictionaryEntry item in dictionary)
		{
			linkOriginalChannelField.SetValue(dictionary2[item.Key], linkOriginalChannelField.GetValue(item.Value));
		}
	}

	private static void OnAcceptPrefix(out AcceptState __state)
	{
		__state = null;
		Pawn pawn = ((!(stylingDialogDrawingField?.GetValue(null) is Dialog_StylingStation obj)) ? null : (stylingDialogPawnField.GetValue(obj) as Pawn));
		FieldInfo fieldInfo = originPawnField;
		if ((object)fieldInfo != null && fieldInfo.DeclaringType?.IsInstanceOfType(pawn) == true)
		{
			ThingComp thingComp = pawn.AllComps?.FirstOrDefault((ThingComp c) => c.GetType() == alienCompType);
			if (thingComp != null && originPawnField.GetValue(pawn) is Pawn pawn2)
			{
				__state = new AcceptState
				{
					pawn = pawn2,
					data = SnapshotAlienComp(thingComp)
				};
			}
		}
	}

	private static void OnAcceptPostfix(AcceptState __state)
	{
		if (__state != null)
		{
			SyncAlienData(__state.pawn, __state.data);
		}
	}

	private static void SyncAlienData(Pawn pawn, AlienCompData data)
	{
		ApplyAlienCompData(pawn, data);
	}

	private static void ApplyAlienCompData(Pawn pawn, AlienCompData data)
	{
		ThingComp thingComp = pawn.AllComps?.FirstOrDefault((ThingComp c) => c.GetType() == alienCompType);
		if (thingComp != null)
		{
			SetAlienCompData(thingComp, data);
			pawn.Drawer?.renderer?.SetAllGraphicsDirty();
		}
	}

	private static void SetAlienCompData(object comp, AlienCompData data)
	{
		IDictionary dictionary = (IDictionary)Activator.CreateInstance(colorChannelsField.FieldType);
		Type type = colorChannelsField.FieldType.GetGenericArguments()[1];
		foreach (KeyValuePair<string, (Color, Color)> colorChannel in data.colorChannels)
		{
			dictionary[colorChannel.Key] = Activator.CreateInstance(type, colorChannel.Value.Item1, colorChannel.Value.Item2);
		}
		List<int> value = data.addonVariants.ToList();
		IList list = (IList)Activator.CreateInstance(addonColorsField.FieldType);
		foreach (var (first, second) in data.addonColors)
		{
			list.Add(CreateNullableColorTuple(first, second));
		}
		IDictionary dictionary2 = (IDictionary)Activator.CreateInstance(colorChannelLinksField.FieldType);
		foreach (KeyValuePair<string, AlienCompData.LinkData> colorChannelLink in data.colorChannelLinks)
		{
			object obj = Activator.CreateInstance(colorChannelLinkDataType);
			linkOriginalChannelField.SetValue(obj, colorChannelLink.Key);
			object value2 = linkTargetsOneField.GetValue(obj);
			object value3 = linkTargetsTwoField.GetValue(obj);
			foreach (var (channel, categoryIndex) in colorChannelLink.Value.targetsOne)
			{
				AddToCollection(value2, channel, categoryIndex);
			}
			foreach (var (channel2, categoryIndex2) in colorChannelLink.Value.targetsTwo)
			{
				AddToCollection(value3, channel2, categoryIndex2);
			}
			dictionary2[colorChannelLink.Key] = obj;
		}
		colorChannelsField.SetValue(comp, dictionary);
		addonVariantsField.SetValue(comp, value);
		addonColorsField.SetValue(comp, list);
		colorChannelLinksField.SetValue(comp, dictionary2);
	}

	private static void AddToCollection(object col, string channel, int categoryIndex)
	{
		object obj = Activator.CreateInstance(colorChannelLinkTargetDataType);
		linkTargetChannelField.SetValue(obj, channel);
		linkTargetCategoryIndexField.SetValue(obj, categoryIndex);
		col.GetType().GetMethod("Add").Invoke(col, new object[1] { obj });
	}

	private static object CreateNullableColorTuple(Color? first, Color? second)
	{
		return Activator.CreateInstance(addonColorsField.FieldType.GetGenericArguments()[0], first, second);
	}

	private static AlienCompData SnapshotAlienComp(object comp)
	{
		AlienCompData alienCompData = new AlienCompData();
		IDictionary dictionary = (IDictionary)colorChannelsField.GetValue(comp);
		if (dictionary != null)
		{
			foreach (DictionaryEntry item5 in dictionary)
			{
				object value = item5.Value;
				Color item = (Color)value.GetType().GetField("first").GetValue(value);
				Color item2 = (Color)value.GetType().GetField("second").GetValue(value);
				alienCompData.colorChannels[(string)item5.Key] = (item, item2);
			}
		}
		IEnumerable<int> obj = addonVariantsField.GetValue(comp) as IEnumerable<int>;
		alienCompData.addonVariants = ((obj != null) ? obj.ToList() : null) ?? new List<int>();
		if (addonColorsField.GetValue(comp) is IEnumerable enumerable)
		{
			foreach (object item6 in enumerable)
			{
				Color? item3 = (Color?)item6.GetType().GetField("first").GetValue(item6);
				Color? item4 = (Color?)item6.GetType().GetField("second").GetValue(item6);
				alienCompData.addonColors.Add((item3, item4));
			}
		}
		IDictionary dictionary2 = (IDictionary)colorChannelLinksField.GetValue(comp);
		if (dictionary2 != null)
		{
			foreach (DictionaryEntry item7 in dictionary2)
			{
				AlienCompData.LinkData linkData = new AlienCompData.LinkData();
				foreach (object item8 in (IEnumerable)linkTargetsOneField.GetValue(item7.Value))
				{
					linkData.targetsOne.Add(((string)linkTargetChannelField.GetValue(item8), (int)linkTargetCategoryIndexField.GetValue(item8)));
				}
				foreach (object item9 in (IEnumerable)linkTargetsTwoField.GetValue(item7.Value))
				{
					linkData.targetsTwo.Add(((string)linkTargetChannelField.GetValue(item9), (int)linkTargetCategoryIndexField.GetValue(item9)));
				}
				alienCompData.colorChannelLinks[(string)item7.Key] = linkData;
			}
		}
		return alienCompData;
	}

	private static void SyncAlienCompData(SyncWorker sync, ref AlienCompData data)
	{
		if (sync.isWriting)
		{
			sync.Write(data.colorChannels.Count);
			foreach (KeyValuePair<string, (Color, Color)> colorChannel in data.colorChannels)
			{
				sync.Write(colorChannel.Key);
				sync.Write(colorChannel.Value.Item1);
				sync.Write(colorChannel.Value.Item2);
			}
			sync.Write(data.addonVariants.Count);
			foreach (int addonVariant in data.addonVariants)
			{
				sync.Write(addonVariant);
			}
			sync.Write(data.addonColors.Count);
			foreach (var (color, color2) in data.addonColors)
			{
				sync.Write(color.HasValue);
				if (color.HasValue)
				{
					sync.Write(color.Value);
				}
				sync.Write(color2.HasValue);
				if (color2.HasValue)
				{
					sync.Write(color2.Value);
				}
			}
			sync.Write(data.colorChannelLinks.Count);
			{
				foreach (KeyValuePair<string, AlienCompData.LinkData> colorChannelLink in data.colorChannelLinks)
				{
					sync.Write(colorChannelLink.Key);
					WriteTargets(sync, colorChannelLink.Value.targetsOne);
					WriteTargets(sync, colorChannelLink.Value.targetsTwo);
				}
				return;
			}
		}
		data = new AlienCompData();
		int num = sync.Read<int>();
		for (int i = 0; i < num; i++)
		{
			string key = sync.Read<string>();
			Color item = sync.Read<Color>();
			Color item2 = sync.Read<Color>();
			data.colorChannels[key] = (item, item2);
		}
		num = sync.Read<int>();
		for (int j = 0; j < num; j++)
		{
			data.addonVariants.Add(sync.Read<int>());
		}
		num = sync.Read<int>();
		for (int k = 0; k < num; k++)
		{
			Color? item3 = (sync.Read<bool>() ? new Color?(sync.Read<Color>()) : ((Color?)null));
			Color? item4 = (sync.Read<bool>() ? new Color?(sync.Read<Color>()) : ((Color?)null));
			data.addonColors.Add((item3, item4));
		}
		num = sync.Read<int>();
		for (int l = 0; l < num; l++)
		{
			string key2 = sync.Read<string>();
			AlienCompData.LinkData linkData = new AlienCompData.LinkData();
			linkData.targetsOne = ReadTargets(sync);
			linkData.targetsTwo = ReadTargets(sync);
			data.colorChannelLinks[key2] = linkData;
		}
	}

	private static void WriteTargets(SyncWorker sync, List<(string channel, int categoryIndex)> targets)
	{
		sync.Write(targets.Count);
		foreach (var (obj, obj2) in targets)
		{
			sync.Write(obj);
			sync.Write(obj2);
		}
	}

	private static List<(string channel, int categoryIndex)> ReadTargets(SyncWorker sync)
	{
		List<(string, int)> list = new List<(string, int)>();
		int num = sync.Read<int>();
		for (int i = 0; i < num; i++)
		{
			list.Add((sync.Read<string>(), sync.Read<int>()));
		}
		return list;
	}

	private static void InitializeStylingContext()
	{
		Type type = AccessTools.TypeByName("AlienRace.StylingStation") ?? throw new TypeLoadException("HAR StylingStation is missing");
		stylingContextFields = new string[17]
		{
			"curMainTab", "curRaceTab", "instance", "pawn", "alienComp", "alienRaceDef", "addonVariants", "addonColors", "colorChannels", "selectedIndexAddons",
			"addonsScrollPos", "variantsScrollPos", "editingFirstColor", "colorsScrollPos", "selectedIndexChannels", "channelsScrollPos", "channelColorViewRectHeight"
		}.Select((string name) => AccessTools.DeclaredField(type, name) ?? throw new MissingFieldException(type.FullName, name)).ToArray();
		stylingContextCollections = new string[3] { "mainTabs", "raceTabs", "availableColorsCache" }.Select((string name) => AccessTools.DeclaredField(type, name) ?? throw new MissingFieldException(type.FullName, name)).ToArray();
		if (stylingContextFields.Any((FieldInfo field) => !field.IsStatic || field.IsInitOnly) || stylingContextCollections.Any((FieldInfo field) => !field.IsStatic || !field.IsInitOnly))
		{
			throw new InvalidOperationException("HAR styling context field contract changed");
		}
		stylingContextAuthorConstructor = AccessTools.DeclaredMethod(type, "ConstructorPostfix", new Type[1] { typeof(Pawn) }) ?? throw new MissingMethodException(type.FullName, "ConstructorPostfix");
		stylingContextPawnField = AccessTools.DeclaredField(typeof(Dialog_StylingStation), "pawn") ?? throw new MissingFieldException(typeof(Dialog_StylingStation).FullName, "pawn");
		MpCompat.harmony.Patch(stylingContextAuthorConstructor, new HarmonyMethod(typeof(AlienRace), "StylingContextAuthorCtorPrefix"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, "ResetPostfix", new Type[1] { typeof(bool) }) ?? throw new MissingMethodException(type.FullName, "ResetPostfix"), new HarmonyMethod(typeof(AlienRace), "StylingContextAuthorResetPrefix"));
		MpCompat.harmony.Patch(AccessTools.Constructor(typeof(Dialog_StylingStation), new Type[2]
		{
			typeof(Pawn),
			typeof(Thing)
		}), new HarmonyMethod(typeof(AlienRace), "StylingContextCtorPrefix"), new HarmonyMethod(typeof(AlienRace), "StylingContextCtorPostfix"), null, new HarmonyMethod(typeof(AlienRace), "StylingContextCtorFinalizer"));
		MethodInfo[] array = new MethodInfo[2]
		{
			AccessTools.DeclaredMethod(typeof(Dialog_StylingStation), "DoWindowContents"),
			AccessTools.DeclaredMethod(typeof(Dialog_StylingStation), "Reset")
		};
		foreach (MethodInfo original in array)
		{
			MpCompat.harmony.Patch(original, new HarmonyMethod(typeof(AlienRace), "StylingContextEnter"), null, null, new HarmonyMethod(typeof(AlienRace), "StylingContextExit"));
		}
	}

	private static void StoreStylingContext(Dialog_StylingStation dialog)
	{
		stylingContexts.Remove(dialog);
		stylingContexts.Add(dialog, StylingContext.Capture());
	}

	private static bool IsStylingPreview(Pawn pawn)
	{
		FieldInfo fieldInfo = originPawnField;
		if ((object)fieldInfo == null)
		{
			return false;
		}
		return fieldInfo.DeclaringType?.IsInstanceOfType(pawn) == true;
	}

	[HarmonyPriority(800)]
	private static bool StylingContextAuthorCtorPrefix(Pawn pawn)
	{
		if (MP.IsInMultiplayer)
		{
			return IsStylingPreview(pawn);
		}
		return true;
	}

	[HarmonyPriority(800)]
	private static bool StylingContextAuthorResetPrefix()
	{
		if (MP.IsInMultiplayer)
		{
			if (activeStylingDialog != null)
			{
				return IsStylingPreview(stylingContextPawnField.GetValue(activeStylingDialog) as Pawn);
			}
			return false;
		}
		return true;
	}

	[HarmonyPriority(800)]
	[HarmonyBefore(new string[] { "rimworld.erdelf.alien_race.main" })]
	private static void StylingContextCtorPrefix(ref StylingContextScope __state)
	{
		if (MP.IsInMultiplayer)
		{
			__state = new StylingContextScope
			{
				Previous = StylingContext.Capture(),
				PreviousDialog = activeStylingDialog
			};
		}
	}

	[HarmonyPriority(0)]
	[HarmonyAfter(new string[] { "rimworld.erdelf.alien_race.main" })]
	private static void StylingContextCtorPostfix(Dialog_StylingStation __instance, StylingContextScope __state)
	{
		if (__state != null && IsStylingPreview(stylingContextPawnField.GetValue(__instance) as Pawn))
		{
			StoreStylingContext(__instance);
		}
	}

	[HarmonyPriority(0)]
	[HarmonyAfter(new string[] { "rimworld.erdelf.alien_race.main" })]
	private static void StylingContextCtorFinalizer(StylingContextScope __state)
	{
		if (__state != null)
		{
			__state.Previous.Restore();
			activeStylingDialog = __state.PreviousDialog;
		}
	}

	[HarmonyPriority(800)]
	[HarmonyBefore(new string[] { "rimworld.erdelf.alien_race.main" })]
	private static void StylingContextEnter(Dialog_StylingStation __instance, ref StylingContextScope __state)
	{
		if (!MP.IsInMultiplayer || activeStylingDialog == __instance)
		{
			return;
		}
		__state = new StylingContextScope
		{
			Previous = StylingContext.Capture(),
			PreviousDialog = activeStylingDialog,
			Dialog = __instance,
			IsPreview = IsStylingPreview(stylingContextPawnField.GetValue(__instance) as Pawn)
		};
		activeStylingDialog = __instance;
		if (!__state.IsPreview)
		{
			return;
		}
		if (!stylingContexts.TryGetValue(__instance, out var value))
		{
			stylingContextAuthorConstructor.Invoke(null, new object[1] { stylingContextPawnField.GetValue(__instance) });
			StoreStylingContext(__instance);
			value = stylingContexts.GetValue(__instance, (Dialog_StylingStation _) =>
			{
				throw new InvalidOperationException();
			});
		}
		value.Restore();
	}

	[HarmonyPriority(0)]
	[HarmonyAfter(new string[] { "rimworld.erdelf.alien_race.main" })]
	private static void StylingContextExit(StylingContextScope __state)
	{
		if (__state == null)
		{
			return;
		}
		try
		{
			if (__state.IsPreview)
			{
				StoreStylingContext(__state.Dialog);
			}
		}
		finally
		{
			__state.Previous.Restore();
			activeStylingDialog = __state.PreviousDialog;
		}
	}
}
