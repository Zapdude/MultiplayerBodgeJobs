using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace Multiplayer.Compat;

internal static class StartupDefFingerprint
{
	private sealed class Entry
	{
		internal int Schema;

		internal int Hash;
	}

	private static readonly SortedDictionary<string, Entry> Entries = new SortedDictionary<string, Entry>(StringComparer.Ordinal);

	private static bool installed;

	internal static void Register(string key, Func<int> checksum, int schema = 1)
	{
		if (Entries.ContainsKey(key))
		{
			return;
		}
		Entries.Add(key, new Entry
		{
			Schema = schema,
			Hash = checksum()
		});
		if (!installed)
		{
			MethodInfo methodInfo = AccessTools.Method(AccessTools.TypeByName("Multiplayer.Client.MultiplayerData"), "CollectDefInfos");
			if (methodInfo == null)
			{
				throw new MissingMethodException("MultiplayerData.CollectDefInfos");
			}
			MpCompat.harmony.Patch(methodInfo, null, new HarmonyMethod(typeof(StartupDefFingerprint), "Apply"));
			installed = true;
		}
		Apply();
	}

	private static void Apply()
	{
		if (!(AccessTools.Field(AccessTools.TypeByName("Multiplayer.Client.MultiplayerData"), "localDefInfos").GetValue(null) is IDictionary dictionary))
		{
			return;
		}
		Type type = AccessTools.TypeByName("Multiplayer.Common.DefInfo") ?? throw new TypeLoadException("Multiplayer.Common.DefInfo");
		foreach (KeyValuePair<string, Entry> entry in Entries)
		{
			object obj = Activator.CreateInstance(type);
			AccessTools.Field(type, "count").SetValue(obj, entry.Value.Schema);
			AccessTools.Field(type, "hash").SetValue(obj, entry.Value.Hash);
			dictionary[entry.Key] = obj;
		}
	}

	internal static int Hash(object value)
	{
		uint hash = 2166136261u;
		Visit(value, 0);
		return (int)hash;
		void Token(string text)
		{
			foreach (char c in text)
			{
				hash = (hash ^ (byte)c) * 16777619;
				hash = (hash ^ (byte)((int)c >> 8)) * 16777619;
			}
			hash = (hash ^ 0xFF) * 16777619;
		}
		void Visit(object item, int depth)
		{
			if (item == null)
			{
				Token("null");
			}
			else
			{
				Type type = item.GetType();
				Token(type.FullName);
				if (item is Def { defName: var text })
				{
					if (text == null)
					{
						text = "";
					}
					Token(text);
				}
				else if (item is Type type2)
				{
					Token(type2.FullName);
				}
				else if (item is string text2)
				{
					Token(text2);
				}
				else if (type.IsPrimitive || type.IsEnum || item is decimal)
				{
					Token(Convert.ToString(item, CultureInfo.InvariantCulture));
				}
				else
				{
					if (depth > 24)
					{
						throw new InvalidOperationException("Startup Def fingerprint graph is too deep: " + type.FullName);
					}
					if (item is IngredientCount ingredientCount)
					{
						Visit(ingredientCount.GetBaseCount(), depth + 1);
						Visit(ingredientCount.filter.AllowedThingDefs.Select((ThingDef d) => d.defName).OrderBy((string n) => n, StringComparer.Ordinal).ToArray(), depth + 1);
					}
					else if (item is IDictionary dictionary)
					{
						foreach (object key in dictionary.Keys.Cast<object>().OrderBy((object k) => Convert.ToString(k, CultureInfo.InvariantCulture), StringComparer.Ordinal))
						{
							Visit(key, depth + 1);
							Visit(dictionary[key], depth + 1);
						}
						Token("end");
					}
					else
					{
						if (!(item is IEnumerable enumerable))
						{
							foreach (FieldInfo item2 in type.GetFields(BindingFlags.Instance | BindingFlags.Public).OrderBy((FieldInfo f) => f.Name, StringComparer.Ordinal))
							{
								bool flag = typeof(Delegate).IsAssignableFrom(item2.FieldType);
								if (!flag)
								{
									bool flag2;
									switch (item2.Name)
									{
									case "defaultDesc":
									case "description":
									case "label":
									case "descriptionHyperlinks":
									case "unlockedString":
									case "defaultLabel":
									case "renderNodeProperties":
										flag2 = true;
										break;
									default:
										flag2 = false;
										break;
									}
									flag = flag2;
								}
								if (!flag && !item2.GetCustomAttributesData().Any((CustomAttributeData a) => a.AttributeType.Name.StartsWith("MustTranslate", StringComparison.Ordinal)))
								{
									Token(item2.Name);
									Visit(item2.GetValue(item), depth + 1);
								}
							}
							return;
						}
						foreach (object item3 in enumerable)
						{
							Visit(item3, depth + 1);
						}
						Token("end");
					}
				}
			}
		}
	}
}
