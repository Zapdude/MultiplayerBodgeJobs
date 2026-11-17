using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("roolo.AlmostThere")]
[MpCompatFor("Chad.Almostthere1.5")]
public class AlmostThere
{
	public AlmostThere(ModContentPack mod)
	{
		MpCompat.RegisterLambdaMethod("AlmostThere.AlmostThereWorldObjectComp", "GetGizmos", 1, 3, 5);
	}
}
