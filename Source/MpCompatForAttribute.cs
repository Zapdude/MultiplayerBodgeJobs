using System;

namespace Multiplayer.Compat;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class MpCompatForAttribute : Attribute
{
	public string PackageId { get; }

	public override object TypeId => this;

	public MpCompatForAttribute(string packageId)
	{
		PackageId = packageId;
	}
}
