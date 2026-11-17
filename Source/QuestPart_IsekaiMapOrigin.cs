using RimWorld;
using Verse;

namespace Multiplayer.Compat;

public sealed class QuestPart_IsekaiMapOrigin : QuestPart
{
	public Map originMap;

	public bool world;

	public override void ExposeData()
	{
		base.ExposeData();
		Map refee = ((Scribe.mode == LoadSaveMode.Saving && (originMap == null || originMap.Disposed || !Find.Maps.Contains(originMap))) ? null : originMap);
		Scribe_References.Look(ref refee, "originMap");
		if (Scribe.mode != LoadSaveMode.Saving)
		{
			originMap = refee;
		}
		Scribe_Values.Look(ref world, "world", defaultValue: false);
	}
}
