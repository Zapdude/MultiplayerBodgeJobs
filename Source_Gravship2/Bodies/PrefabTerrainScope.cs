using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Multiplayer.Compat.Workshop17.Bodies;

public class PrefabTerrainScope : IDisposable
{
	private readonly Dictionary<PrefabDef, List<PrefabTerrainData>> backups = new Dictionary<PrefabDef, List<PrefabTerrainData>>();

	internal PrefabTerrainScope(IEnumerable<PrefabDef> defs, TerrainDef excluded)
	{
		try
		{
			foreach (PrefabDef def in defs)
			{
				if (def.terrain != null && GenCollection.Any<PrefabTerrainData>(def.terrain, (Predicate<PrefabTerrainData>)((PrefabTerrainData t) => t.def == excluded)))
				{
					backups.Add(def, new List<PrefabTerrainData>(def.terrain));
					def.terrain.RemoveAll((PrefabTerrainData t) => t.def == excluded);
				}
			}
		}
		catch
		{
			Dispose();
			throw;
		}
	}

	public void Dispose()
	{
		foreach (KeyValuePair<PrefabDef, List<PrefabTerrainData>> backup in backups)
		{
			backup.Key.terrain.Clear();
			backup.Key.terrain.AddRange(backup.Value);
		}
		backups.Clear();
	}
}
