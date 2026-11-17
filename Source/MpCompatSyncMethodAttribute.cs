using System;
using JetBrains.Annotations;
using Multiplayer.API;

namespace Multiplayer.Compat;

[AttributeUsage(AttributeTargets.Method)]
[MeansImplicitUse(ImplicitUseTargetFlags.WithMembers)]
public class MpCompatSyncMethodAttribute : Attribute
{
	public SyncContext context;

	public bool cancelIfAnyArgNull;

	public bool cancelIfNoSelectedMapObjects;

	public bool cancelIfNoSelectedWorldObjects;

	public bool debugOnly;

	public bool hostOnly;

	public int version;

	public int[] exposeParameters;

	public MpCompatSyncMethodAttribute(SyncContext context = SyncContext.None)
	{
		this.context = context;
	}
}
