using UnityEngine;
using VanillaGravshipExpanded2;
using Verse;

namespace Multiplayer.Compat.Workshop17.Bodies;

public class CompProperties_EscapePod_Body
{
	public static Texture2D get_AutoRebuildGizmo(CompProperties_EscapePod __instance)
	{
		if ((Texture)(object)__instance.autoRebuildGizmo == (Texture)null)
		{
			if (!GenText.NullOrEmpty(__instance.autoRebuildGizmoIconPath))
			{
				__instance.autoRebuildGizmo = ContentFinder<Texture2D>.Get(__instance.autoRebuildGizmoIconPath, true);
			}
			if ((Texture)(object)__instance.autoRebuildGizmo == (Texture)null)
			{
				__instance.autoRebuildGizmo = BaseContent.BadTex;
			}
		}
		return __instance.autoRebuildGizmo;
	}

	public static Texture2D get_EvacuationGizmo(CompProperties_EscapePod __instance)
	{
		if ((Texture)(object)__instance.evacuationGizmo == (Texture)null)
		{
			if (!GenText.NullOrEmpty(__instance.evacuationGizmoIconPath))
			{
				__instance.evacuationGizmo = ContentFinder<Texture2D>.Get(__instance.evacuationGizmoIconPath, true);
			}
			if ((Texture)(object)__instance.evacuationGizmo == (Texture)null)
			{
				__instance.evacuationGizmo = BaseContent.BadTex;
			}
		}
		return __instance.evacuationGizmo;
	}
}
