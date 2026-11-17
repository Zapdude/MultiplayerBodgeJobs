using System.Collections.Generic;
using Verse;

namespace Multiplayer.Compat;

internal sealed class SquishyMeleeCombatSettings : IExposable
{
	internal bool Enabled;

	internal bool ShieldMotion;

	internal bool Guard;

	internal int Kinds;

	internal float Chance;

	internal float BlockMultiplier = 1f;

	internal float Reflect;

	internal List<string> DisabledRaces = new List<string>();

	internal bool Active
	{
		get
		{
			if (Enabled && ShieldMotion && Guard && Kinds != 0 && Chance > 0f)
			{
				return BlockMultiplier < 1f;
			}
			return false;
		}
	}

	internal bool Valid
	{
		get
		{
			int kinds = Kinds;
			if (kinds >= 0 && kinds <= 7)
			{
				float chance = Chance;
				if (chance >= 0f && chance <= 1f)
				{
					chance = BlockMultiplier;
					if (chance >= 0f && chance <= 1f)
					{
						chance = Reflect;
						if (chance >= 0f && chance <= 1f)
						{
							return DisabledRaces != null;
						}
					}
				}
			}
			return false;
		}
	}

	public void ExposeData()
	{
		Scribe_Values.Look(ref Enabled, "enabled", defaultValue: false);
		Scribe_Values.Look(ref ShieldMotion, "shieldMotion", defaultValue: false);
		Scribe_Values.Look(ref Guard, "guard", defaultValue: false);
		Scribe_Values.Look(ref Kinds, "kinds", 0);
		Scribe_Values.Look(ref Chance, "chance", 0f);
		Scribe_Values.Look(ref BlockMultiplier, "blockMultiplier", 1f);
		Scribe_Values.Look(ref Reflect, "reflect", 0f);
		Scribe_Collections.Look(ref DisabledRaces, "disabledRaces", LookMode.Value);
		if (Scribe.mode == LoadSaveMode.PostLoadInit && DisabledRaces == null)
		{
			DisabledRaces = new List<string>();
		}
	}
}
