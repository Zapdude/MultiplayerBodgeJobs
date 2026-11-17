using System;
using System.Collections;
using System.Collections.Generic;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

public sealed class BloodAnimationsMapComponent : MapComponent
{
	private FleckSystem system;

	private List<BloodAnimationsParticle> pending;

	internal FleckSystem ExistingSystem => system;

	internal FleckSystem System
	{
		get
		{
			if (system == null)
			{
				system = (FleckSystem)Activator.CreateInstance(BloodAnimations.SystemType, map.flecks);
			}
			if (pending != null)
			{
				IList list = (IList)BloodAnimations.ParticleData.GetValue(system);
				foreach (BloodAnimationsParticle item in pending)
				{
					if (item.Valid)
					{
						list.Add(item.Value);
					}
				}
				pending = null;
			}
			return system;
		}
	}

	public BloodAnimationsMapComponent(Map map)
		: base(map)
	{
	}

	public override void MapComponentTick()
	{
		if (BloodAnimations.Installed && MP.IsInMultiplayer)
		{
			Restore();
			if (system != null)
			{
				BloodAnimations.Tick(system, map);
			}
		}
	}

	public override void FinalizeInit()
	{
		if (BloodAnimations.Installed && MP.IsInMultiplayer)
		{
			Restore();
		}
	}

	private void Restore()
	{
		if (pending != null && map.flecks != null)
		{
			_ = System;
		}
	}

	public override void ExposeData()
	{
		if (!BloodAnimations.Installed)
		{
			return;
		}
		if (Scribe.mode == LoadSaveMode.Saving && system != null)
		{
			pending = new List<BloodAnimationsParticle>();
			foreach (object item in (IList)BloodAnimations.ParticleData.GetValue(system))
			{
				if ((float)BloodAnimations.ParticleFields["airTimeLeft"].GetValue(item) > 0f)
				{
					pending.Add(new BloodAnimationsParticle
					{
						Value = item
					});
				}
			}
		}
		Scribe_Collections.Look(ref pending, "mpBloodAnimationsPendingFilth", LookMode.Deep);
		if (Scribe.mode == LoadSaveMode.Saving)
		{
			pending = null;
		}
	}
}
