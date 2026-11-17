using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Adamas.HospitalitySpa")]
public class HospitalitySpa
{
	public HospitalitySpa(ModContentPack mod)
	{
		HospitalityCasino.InitializeGizmos("HospitalitySpa");
	}
}
