using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Multiplayer.Compat;

internal static class RimDarkState
{
	private static readonly HashSet<string> Records = new HashSet<string> { "Core40k.DecorationSettings", "Core40k.DecorationDrawData", "Core40k.DecorationDrawData+RotationalData", "Core40k.GeneVariantChoice" };

	internal static FieldInfo Field(Type type, string name)
	{
		return AccessTools.Field(type, name) ?? throw new MissingFieldException(type.FullName, name);
	}

	internal static string Capture(object value, string[] names)
	{
		JsonArray jsonArray = new JsonArray();
		foreach (string name in names)
		{
			jsonArray.Add(Encode(Field(value.GetType(), name).GetValue(value)));
		}
		return jsonArray.ToJsonString();
	}

	internal static void Apply(object value, string[] names, string text)
	{
		if (text != null && text.Length <= 262144)
		{
			JsonNode jsonNode = JsonNode.Parse(text);
			JsonArray array = jsonNode as JsonArray;
			if (array != null && array.Count == names.Length)
			{
				FieldInfo[] array2 = names.Select((string name) => Field(value.GetType(), name)).ToArray();
				object[] array3 = array2.Select((FieldInfo field, int index) => Decode(field.FieldType, array[index])).ToArray();
				for (int num = 0; num < array2.Length; num++)
				{
					array2[num].SetValue(value, array3[num]);
				}
				return;
			}
		}
		throw new ArgumentException("Invalid RimDark draft shape");
	}

	internal static object Copy(object value)
	{
		if (value == null || value is Def || value is string || value.GetType().IsValueType)
		{
			return value;
		}
		Type type = value.GetType();
		if (value is IDictionary dictionary)
		{
			IDictionary dictionary2 = (IDictionary)Activator.CreateInstance(type);
			{
				foreach (DictionaryEntry item in dictionary)
				{
					dictionary2.Add(Copy(item.Key), Copy(item.Value));
				}
				return dictionary2;
			}
		}
		if (value is IList list)
		{
			IList list2 = (IList)Activator.CreateInstance(type);
			{
				foreach (object item2 in list)
				{
					list2.Add(Copy(item2));
				}
				return list2;
			}
		}
		if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(HashSet<>))
		{
			object obj = Activator.CreateInstance(type);
			MethodInfo method = type.GetMethod("Add");
			{
				foreach (object item3 in (IEnumerable)value)
				{
					method.Invoke(obj, new object[1] { Copy(item3) });
				}
				return obj;
			}
		}
		if (!Records.Contains(type.FullName))
		{
			return value;
		}
		object obj2 = Activator.CreateInstance(type);
		FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
		foreach (FieldInfo fieldInfo in fields)
		{
			fieldInfo.SetValue(obj2, Copy(fieldInfo.GetValue(value)));
		}
		return obj2;
	}

	private static JsonNode Encode(object value)
	{
		if (value == null)
		{
			return null;
		}
		Type type = value.GetType();
		if (value is Def def)
		{
			InlineArray2<JsonNode> buffer = default;
			buffer[0] = JsonValue.Create(type.FullName);
			buffer[1] = JsonValue.Create(def.defName);
			return new JsonArray(buffer);
		}
		if (value is string value2)
		{
			return JsonValue.Create(value2);
		}
		if (value is bool value3)
		{
			return JsonValue.Create(value3);
		}
		if (value is int value4)
		{
			return JsonValue.Create(value4);
		}
		if (value is float value5)
		{
			return JsonValue.Create(value5);
		}
		if (type.IsEnum)
		{
			return JsonValue.Create(Convert.ToInt32(value));
		}
		if (value is Color color)
		{
			return Floats(color.r, color.g, color.b, color.a);
		}
		if (value is Vector3 vector)
		{
			return Floats(vector.x, vector.y, vector.z);
		}
		if (value is Rot4 { AsInt: var asInt })
		{
			return JsonValue.Create(asInt);
		}
		if (value is IntRange intRange)
		{
			InlineArray2<JsonNode> buffer2 = default;
			buffer2[0] = intRange.min;
			buffer2[1] = intRange.max;
			return new JsonArray(buffer2);
		}
		if (value is IDictionary dictionary)
		{
			JsonArray jsonArray = new JsonArray();
			List<DictionaryEntry> list = new List<DictionaryEntry>();
			foreach (DictionaryEntry item in dictionary)
			{
				list.Add(item);
			}
			{
				foreach (DictionaryEntry item2 in list.OrderBy((DictionaryEntry entry) => (!(entry.Key is Def def2)) ? Convert.ToString(entry.Key, CultureInfo.InvariantCulture) : def2.defName, StringComparer.Ordinal))
				{
					InlineArray2<JsonNode> buffer3 = default;
					buffer3[0] = Encode(item2.Key);
					buffer3[1] = Encode(item2.Value);
					jsonArray.Add(new JsonArray(buffer3));
				}
				return jsonArray;
			}
		}
		if (value is IList list2)
		{
			JsonArray jsonArray2 = new JsonArray();
			{
				foreach (object item3 in list2)
				{
					jsonArray2.Add(Encode(item3));
				}
				return jsonArray2;
			}
		}
		if (Records.Contains(type.FullName))
		{
			JsonArray jsonArray3 = new JsonArray();
			FieldInfo[] array = RecordFields(type);
			foreach (FieldInfo fieldInfo in array)
			{
				jsonArray3.Add(Encode(fieldInfo.GetValue(value)));
			}
			return jsonArray3;
		}
		throw new NotSupportedException("RimDark wire value: " + type.FullName);
	}

	private static object Decode(Type type, JsonNode value)
	{
		if (value == null)
		{
			if (!type.IsValueType || !(Nullable.GetUnderlyingType(type) == null))
			{
				return null;
			}
			throw new ArgumentException("Null RimDark value");
		}
		Type underlyingType = Nullable.GetUnderlyingType(type);
		if (underlyingType != null)
		{
			return Decode(underlyingType, value);
		}
		if (typeof(Def).IsAssignableFrom(type))
		{
			JsonArray jsonArray = Array(value, 2);
			Type type2 = AccessTools.TypeByName(jsonArray[0].GetValue<string>());
			if (type2 == null || !type.IsAssignableFrom(type2))
			{
				throw new ArgumentException("Wrong RimDark def type");
			}
			return GenDefDatabase.GetDef(type2, jsonArray[1].GetValue<string>(), errorOnFail: false) ?? throw new ArgumentException("Missing RimDark def");
		}
		if (type == typeof(string))
		{
			return value.GetValue<string>();
		}
		if (type == typeof(bool))
		{
			return value.GetValue<bool>();
		}
		if (type == typeof(int))
		{
			return value.GetValue<int>();
		}
		if (type == typeof(float))
		{
			float value2 = value.GetValue<float>();
			if (!float.IsFinite(value2))
			{
				throw new ArgumentException("Nonfinite RimDark value");
			}
			return value2;
		}
		if (type.IsEnum)
		{
			return Enum.ToObject(type, value.GetValue<int>());
		}
		if (type == typeof(Color))
		{
			JsonArray jsonArray2 = Array(value, 4);
			return new Color(Float(jsonArray2[0]), Float(jsonArray2[1]), Float(jsonArray2[2]), Float(jsonArray2[3]));
		}
		if (type == typeof(Vector3))
		{
			JsonArray jsonArray3 = Array(value, 3);
			return new Vector3(Float(jsonArray3[0]), Float(jsonArray3[1]), Float(jsonArray3[2]));
		}
		if (type == typeof(Rot4))
		{
			return new Rot4(value.GetValue<int>());
		}
		if (type == typeof(IntRange))
		{
			JsonArray jsonArray4 = Array(value, 2);
			return new IntRange(jsonArray4[0].GetValue<int>(), jsonArray4[1].GetValue<int>());
		}
		if (typeof(IDictionary).IsAssignableFrom(type))
		{
			IDictionary dictionary = (IDictionary)Activator.CreateInstance(type);
			Type[] genericArguments = type.GetGenericArguments();
			{
				foreach (JsonNode item in Array(value))
				{
					JsonArray jsonArray5 = Array(item, 2);
					dictionary.Add(Decode(genericArguments[0], jsonArray5[0]), Decode(genericArguments[1], jsonArray5[1]));
				}
				return dictionary;
			}
		}
		if (typeof(IList).IsAssignableFrom(type))
		{
			IList list = (IList)Activator.CreateInstance(type);
			{
				foreach (JsonNode item2 in Array(value))
				{
					list.Add(Decode(type.GetGenericArguments()[0], item2));
				}
				return list;
			}
		}
		if (Records.Contains(type.FullName))
		{
			FieldInfo[] array = RecordFields(type);
			JsonArray jsonArray6 = Array(value, array.Length);
			object obj = Activator.CreateInstance(type);
			for (int i = 0; i < array.Length; i++)
			{
				array[i].SetValue(obj, Decode(array[i].FieldType, jsonArray6[i]));
			}
			return obj;
		}
		throw new NotSupportedException("RimDark wire type: " + type.FullName);
	}

	private static FieldInfo[] RecordFields(Type type)
	{
		return type.GetFields(BindingFlags.Instance | BindingFlags.Public).OrderBy((FieldInfo field) => field.Name, StringComparer.Ordinal).ToArray();
	}

	private static JsonArray Array(JsonNode value, int count = -1)
	{
		if (!(value is JsonArray { Count: <=2048 } jsonArray) || (count >= 0 && jsonArray.Count != count))
		{
			throw new ArgumentException("Invalid RimDark array");
		}
		return jsonArray;
	}

	private static float Float(JsonNode value)
	{
		return (float)Decode(typeof(float), value);
	}

	private static JsonArray Floats(params float[] values)
	{
		JsonArray jsonArray = new JsonArray();
		foreach (float value in values)
		{
			jsonArray.Add(value);
		}
		return jsonArray;
	}
}
