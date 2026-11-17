using System;
using Verse;

namespace Multiplayer.Compat;

public sealed class RimDarkShieldEvent : IExposable
{
	internal Pawn caster;

	internal Pawn target;

	private DamageDef damageDef;

	private float damageAmount;

	private float armorPenetration;

	private float maxKnockbackBodySize;

	private float impactDamage;

	private int stunTicks;

	private int knockbackCells;

	private FleckDef impactFleck;

	private SoundDef impactSound;

	private ThingDef weapon;

	private static readonly string[] BashFields = new string[10] { "damageDef", "damageAmount", "armorPenetration", "stunTicks", "knockbackCells", "maxKnockbackBodySize", "impactDamage", "impactFleck", "impactSound", "weapon" };

	internal static RimDarkShieldEvent Capture(object tuple)
	{
		RimDarkShieldEvent rimDarkShieldEvent = new RimDarkShieldEvent
		{
			caster = (Pawn)Read("Item1"),
			target = (Pawn)Read("Item2")
		};
		object obj = Read("Item3");
		string[] bashFields = BashFields;
		foreach (string name in bashFields)
		{
			RimDarkState.Field(typeof(RimDarkShieldEvent), name).SetValue(rimDarkShieldEvent, RimDarkState.Field(obj.GetType(), name).GetValue(obj));
		}
		return rimDarkShieldEvent;
		object Read(string name2)
		{
			return RimDarkState.Field(tuple.GetType(), name2).GetValue(tuple);
		}
	}

	internal object Create(Type tupleType)
	{
		Type type = tupleType.GetGenericArguments()[2];
		object obj = Activator.CreateInstance(type);
		string[] bashFields = BashFields;
		foreach (string name in bashFields)
		{
			RimDarkState.Field(type, name).SetValue(obj, RimDarkState.Field(typeof(RimDarkShieldEvent), name).GetValue(this));
		}
		return Activator.CreateInstance(tupleType, caster, target, obj);
	}

	public void ExposeData()
	{
		Scribe_References.Look(ref caster, "caster");
		Scribe_References.Look(ref target, "target");
		Scribe_Defs.Look(ref damageDef, "damageDef");
		Scribe_Defs.Look(ref impactFleck, "impactFleck");
		Scribe_Defs.Look(ref impactSound, "impactSound");
		Scribe_Defs.Look(ref weapon, "weapon");
		Scribe_Values.Look(ref damageAmount, "damageAmount", 0f);
		Scribe_Values.Look(ref armorPenetration, "armorPenetration", 0f);
		Scribe_Values.Look(ref stunTicks, "stunTicks", 0);
		Scribe_Values.Look(ref knockbackCells, "knockbackCells", 0);
		Scribe_Values.Look(ref maxKnockbackBodySize, "maxKnockbackBodySize", 0f);
		Scribe_Values.Look(ref impactDamage, "impactDamage", 0f);
	}
}
