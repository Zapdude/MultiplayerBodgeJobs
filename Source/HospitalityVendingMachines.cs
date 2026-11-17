using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Adamas.VendingMachines")]
public class HospitalityVendingMachines
{
	public HospitalityVendingMachines(ModContentPack mod)
	{
		HospitalityCasino.InitializeGizmos("VendingMachines");
	}
}
