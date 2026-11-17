using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;

namespace Multiplayer.Compat;

internal static class RimTalkMetadataCodec
{
	internal sealed class Persona
	{
		public int Schema = 1;

		public int PawnId;

		public long Revision;

		public string Personality;

		public float Weight;

		public SortedDictionary<string, int> Spoken = new SortedDictionary<string, int>(StringComparer.Ordinal);

		public List<Memory> Memories = new List<Memory>();
	}

	internal sealed class Memory
	{
		public int TargetPawnId;

		public int CreatedTick;

		public int LastRecalledTick;

		public int Perspective;

		public int Count;

		public int LastTick;

		public string TargetPawnName;

		public string EventKey;

		public string Note;

		public float BaseWeight;

		public bool IsDirective;

		public bool IsCoreTrauma;

		public bool IsMilestone;
	}

	internal const int MaxBytes = 32768;

	internal const int MaxMemories = 512;

	internal const int MaxSpoken = 2048;

	private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
	{
		IncludeFields = true,
		MaxDepth = 8,
		UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
	};

	internal static (string payload, string digest) Encode(Persona record)
	{
		Validate(record);
		string text = JsonSerializer.Serialize(record, Json);
		byte[] bytes = Encoding.UTF8.GetBytes(text);
		if (bytes.Length > 32768)
		{
			throw new InvalidDataException("RimTalk metadata exceeds its per-command budget");
		}
		return (payload: text, digest: Convert.ToHexString(SHA256.HashData(bytes)));
	}

	internal static Persona Decode(int pawnId, long revision, string payload, string digest)
	{
		if (pawnId <= 0 || revision <= 0 || payload == null || payload.Length > 32768 || digest == null || digest.Length != 64 || digest.Any((char c) =>
		{
			bool flag;
			switch (c)
			{
			case '0':
			case '1':
			case '2':
			case '3':
			case '4':
			case '5':
			case '6':
			case '7':
			case '8':
			case '9':
			case 'A':
			case 'B':
			case 'C':
			case 'D':
			case 'E':
			case 'F':
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			return !flag;
		}))
		{
			throw new InvalidDataException("Invalid RimTalk metadata envelope");
		}
		byte[] bytes = Encoding.UTF8.GetBytes(payload);
		if (bytes.Length > 32768 || Convert.ToHexString(SHA256.HashData(bytes)) != digest)
		{
			throw new InvalidDataException("RimTalk metadata checksum/budget differs");
		}
		using (JsonDocument jsonDocument = JsonDocument.Parse(bytes, new JsonDocumentOptions
		{
			MaxDepth = 8
		}))
		{
			JsonElement root = jsonDocument.RootElement;
			if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 7 || new string[7] { "Schema", "PawnId", "Revision", "Personality", "Weight", "Spoken", "Memories" }.Any((string name) => !root.TryGetProperty(name, out var _)))
			{
				throw new InvalidDataException("Missing RimTalk metadata schema fields");
			}
			UniqueProperties(root);
		}
		Persona persona = JsonSerializer.Deserialize<Persona>(bytes, Json) ?? throw new InvalidDataException("Missing RimTalk metadata");
		Validate(persona);
		if (persona.PawnId != pawnId || persona.Revision != revision)
		{
			throw new InvalidDataException("RimTalk metadata ID/revision differs");
		}
		return persona;
	}

	private static void UniqueProperties(JsonElement element)
	{
		if (element.ValueKind == JsonValueKind.Object)
		{
			HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
			{
				foreach (JsonProperty item in element.EnumerateObject())
				{
					if (!hashSet.Add(item.Name))
					{
						throw new InvalidDataException("Duplicate RimTalk metadata field");
					}
					UniqueProperties(item.Value);
				}
				return;
			}
		}
		if (element.ValueKind != JsonValueKind.Array)
		{
			return;
		}
		foreach (JsonElement item2 in element.EnumerateArray())
		{
			UniqueProperties(item2);
		}
	}

	private static void Validate(Persona value)
	{
		if (value == null || value.Schema != 1 || value.PawnId <= 0 || value.Revision < 0 || !float.IsFinite(value.Weight) || value.Weight < 0f || value.Spoken == null || value.Memories == null || value.Spoken.Count > 2048 || value.Memories.Count > 512)
		{
			throw new InvalidDataException("Invalid RimTalk metadata record");
		}
		Text(value.Personality, 8192);
		int num = value.Personality?.Length ?? 0;
		checked
		{
			foreach (KeyValuePair<string, int> item in value.Spoken)
			{
				Text(item.Key, 512, nullable: false);
				num += item.Key.Length;
			}
			foreach (Memory memory in value.Memories)
			{
				bool flag = memory == null || !float.IsFinite(memory.BaseWeight) || memory.Count < 1;
				if (!flag)
				{
					int perspective = memory.Perspective;
					bool flag2 = ((perspective < 0 || perspective > 2) ? true : false);
					flag = flag2;
				}
				if (flag)
				{
					throw new InvalidDataException("Invalid RimTalk memory record");
				}
				Text(memory.TargetPawnName, 1024);
				Text(memory.EventKey, 1024);
				Text(memory.Note, 8192);
				num = num + (memory.TargetPawnName?.Length ?? 0) + (memory.EventKey?.Length ?? 0) + (memory.Note?.Length ?? 0);
			}
			if (num > 24576)
			{
				throw new InvalidDataException("RimTalk metadata aggregate text exceeds its budget");
			}
		}
	}

	private static void Text(string text, int maximum, bool nullable = true)
	{
		if (text == null)
		{
			if (nullable)
			{
				return;
			}
			throw new InvalidDataException("Missing RimTalk metadata key");
		}
		if (text.Length > maximum)
		{
			throw new InvalidDataException("RimTalk metadata text exceeds its budget");
		}
		try
		{
			XmlConvert.VerifyXmlChars(text);
		}
		catch (XmlException innerException)
		{
			throw new InvalidDataException("RimTalk metadata text cannot be saved", innerException);
		}
	}
}
