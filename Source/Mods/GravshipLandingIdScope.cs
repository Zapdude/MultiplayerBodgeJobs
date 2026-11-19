using Verse;

namespace Multiplayer.Compat
{
    [MpCompatFor("vanillaexpanded.gravship2")]
    public class GravshipLandingIdScopeVge2
    {
        public GravshipLandingIdScopeVge2(ModContentPack mod)
        {
            LongEventHandler.ExecuteWhenFinished(() =>
                VanillaGravshipGlobalIds.Install("VanillaGravshipExpanded2.LandingStructure_StructureSet"));
        }
    }
}
