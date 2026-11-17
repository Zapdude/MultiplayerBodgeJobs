using System;
using UnityEngine;
using Verse;

namespace Multiplayer.Compat;

public sealed class BloodAnimationsParticle : IExposable
{
	internal object Value;

	private Thing source;

	internal bool Valid
	{
		get
		{
			if (Value != null)
			{
				return ((FleckStatic)Get("baseData")).def != null;
			}
			return false;
		}
	}

	private object Get(string name)
	{
		return BloodAnimations.ParticleFields[name].GetValue(Value);
	}

	private void Set(string name, object value)
	{
		BloodAnimations.ParticleFields[name].SetValue(Value, value);
	}

	private void Float(string name)
	{
		float value = (float)Get(name);
		Scribe_Values.Look(ref value, name, 0f, forceSave: true);
		if (Scribe.mode == LoadSaveMode.LoadingVars)
		{
			Set(name, value);
		}
	}

	private void Vector(string name)
	{
		Vector3 value = (Vector3)Get(name);
		Scribe_Values.Look(ref value, name, default, forceSave: true);
		if (Scribe.mode == LoadSaveMode.LoadingVars)
		{
			Set(name, value);
		}
	}

	public void ExposeData()
	{
		if (Value == null)
		{
			Value = Activator.CreateInstance(BloodAnimations.ParticleType);
		}
		FleckStatic fleckStatic = (FleckStatic)Get("baseData");
		Scribe_Defs.Look(ref fleckStatic.def, "def");
		Scribe_Values.Look(ref fleckStatic.position.worldPosition, "position", default, forceSave: true);
		Scribe_Values.Look(ref fleckStatic.position.height, "height", 0f, forceSave: true);
		Scribe_Values.Look(ref fleckStatic.position.anchorOffset, "anchorOffset", default, forceSave: true);
		Scribe_Values.Look(ref fleckStatic.position.unattachedDrawOffset, "unattachedDrawOffset", default, forceSave: true);
		Scribe_Values.Look(ref fleckStatic.position.attachedOffset, "attachedOffset", default, forceSave: true);
		Scribe_Values.Look(ref fleckStatic.exactRotation, "exactRotation", 0f, forceSave: true);
		Scribe_Values.Look(ref fleckStatic.originalScale, "originalScale", default, forceSave: true);
		Scribe_Values.Look(ref fleckStatic.linearScale, "linearScale", default, forceSave: true);
		Scribe_Values.Look(ref fleckStatic.curvedScale, "curvedScale", default, forceSave: true);
		Scribe_Values.Look(ref fleckStatic.instanceColor, "instanceColor", default, forceSave: true);
		Scribe_Values.Look(ref fleckStatic.solidTimeOverride, "solidTimeOverride", 0f, forceSave: true);
		Scribe_Values.Look(ref fleckStatic.ageSecs, "ageSecs", 0f, forceSave: true);
		Scribe_Values.Look(ref fleckStatic.ageTicks, "ageTicks", 0, forceSave: true);
		Scribe_Values.Look(ref fleckStatic.setupTick, "setupTick", 0, forceSave: true);
		Scribe_Values.Look(ref fleckStatic.spawnPosition, "spawnPosition", default, forceSave: true);
		Scribe_Values.Look(ref fleckStatic.skidSpeedMultiplierPerTick, "baseSkid", 0f, forceSave: true);
		if (Scribe.mode == LoadSaveMode.LoadingVars)
		{
			Set("baseData", fleckStatic);
		}
		string[] array = new string[8] { "airTimeLeft", "rotationRate", "orbitSpeed", "archHeight", "orbitSnapStrength", "orbitDistance", "orbitAccum", "skidSpeedMultiplierPerTick" };
		foreach (string name in array)
		{
			Float(name);
		}
		Vector("velocity");
		Vector("attacheeLastPosition");
		float? value = (float?)Get("archDuration");
		Scribe_Values.Look(ref value, "archDuration", null, forceSave: true);
		if (Scribe.mode == LoadSaveMode.LoadingVars)
		{
			Set("archDuration", value);
		}
		FleckAttachLink fleckAttachLink = (FleckAttachLink)Get("link");
		if (Scribe.mode == LoadSaveMode.Saving)
		{
			source = fleckAttachLink.Target.Thing;
		}
		Scribe_References.Look(ref source, "source", saveDestroyedThings: true);
		Scribe_Values.Look(ref fleckAttachLink.detachAfterTicks, "detachAfterTicks", -1, forceSave: true);
		Scribe_Values.Look(ref fleckAttachLink.lastDrawPosInt, "lastDrawPos", default, forceSave: true);
		if (Scribe.mode != LoadSaveMode.Saving)
		{
			fleckAttachLink.targetInt = ((source == null) ? TargetInfo.Invalid : new TargetInfo(source));
			Set("link", fleckAttachLink);
		}
	}
}
