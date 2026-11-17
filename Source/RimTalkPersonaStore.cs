using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Multiplayer.Compat;

internal sealed class RimTalkPersonaStore
{
	private readonly Dictionary<int, RimTalkPersonaRecord> records = new Dictionary<int, RimTalkPersonaRecord>();

	private readonly Dictionary<int, RimTalkPersonaRecord> committed = new Dictionary<int, RimTalkPersonaRecord>();

	private readonly Dictionary<Pawn, object> native = new Dictionary<Pawn, object>();

	internal object Get(Pawn pawn)
	{
		if (native.TryGetValue(pawn, out var value))
		{
			RimTalk.StateRefreshAge(pawn, value);
			RimTalk.MarkMetadataDirty(pawn);
			return value;
		}
		if (!records.TryGetValue(pawn.thingIDNumber, out var value2))
		{
			value2 = (records[pawn.thingIDNumber] = RimTalk.StateInitialRecord(pawn));
		}
		value = RimTalk.StateMakePersona(pawn, value2);
		RimTalk.StateRefreshAge(pawn, value);
		native.Add(pawn, value);
		RimTalk.MarkMetadataDirty(pawn);
		return value;
	}

	internal List<RimTalkPersonaRecord> Capture()
	{
		foreach (KeyValuePair<Pawn, object> item in native)
		{
			if (item.Key.thingIDNumber > 0)
			{
				records[item.Key.thingIDNumber] = RimTalk.StateCapturePersona(item.Key.thingIDNumber, item.Value);
			}
		}
		return (from r in records.Values
			where r.PawnId > 0
			orderby r.PawnId
			select r).ToList();
	}

	internal void Restore(List<RimTalkPersonaRecord> snapshot)
	{
		native.Clear();
		records.Clear();
		committed.Clear();
		foreach (RimTalkPersonaRecord item in snapshot ?? new List<RimTalkPersonaRecord>())
		{
			if (item != null && item.PawnId > 0)
			{
				records[item.PawnId] = RimTalk.StateCloneRecord(item);
				committed[item.PawnId] = RimTalk.StateCloneRecord(item);
			}
		}
	}

	internal List<RimTalkPersonaRecord> CaptureCommitted()
	{
		return committed.Values.OrderBy((RimTalkPersonaRecord r) => r.PawnId).Select(RimTalk.StateCloneRecord).ToList();
	}

	internal void CommitInitialBaseline()
	{
		committed.Clear();
		foreach (RimTalkPersonaRecord item in Capture())
		{
			committed[item.PawnId] = RimTalk.StateCloneRecord(item);
		}
	}

	internal long Revision(int pawnId)
	{
		if (!committed.TryGetValue(pawnId, out var value))
		{
			return 0L;
		}
		return value.Revision;
	}

	internal RimTalkPersonaRecord Committed(int pawnId)
	{
		if (!committed.TryGetValue(pawnId, out var value))
		{
			return null;
		}
		return RimTalk.StateCloneRecord(value);
	}

	internal bool Accept(RimTalkPersonaRecord record)
	{
		if (record.Revision != checked(Revision(record.PawnId) + 1))
		{
			return false;
		}
		committed[record.PawnId] = RimTalk.StateCloneRecord(record);
		if (!RimTalk.MetadataPhysicalHost)
		{
			records[record.PawnId] = RimTalk.StateCloneRecord(record);
			Pawn[] array = native.Keys.Where((Pawn p) => p.thingIDNumber == record.PawnId).ToArray();
			foreach (Pawn key in array)
			{
				native.Remove(key);
			}
		}
		return true;
	}

	internal void ApplyOrderedFields(Pawn pawn, string personality, float weight, byte fields)
	{
		RimTalkPersonaRecord rimTalkPersonaRecord = Committed(pawn.thingIDNumber) ?? RimTalk.StateInitialRecord(pawn);
		checked
		{
			rimTalkPersonaRecord.Revision++;
			if ((fields & 1) != 0)
			{
				rimTalkPersonaRecord.Personality = personality;
			}
			if ((fields & 2) != 0)
			{
				rimTalkPersonaRecord.Weight = weight;
			}
			committed[pawn.thingIDNumber] = rimTalkPersonaRecord;
			if (!RimTalk.MetadataPhysicalHost)
			{
				records[pawn.thingIDNumber] = RimTalk.StateCloneRecord(rimTalkPersonaRecord);
				native.Remove(pawn);
			}
		}
	}
}
