# Merge ignore list (1.6 → 1.7)

Commits on `main` (1.6) that must **not** be folded into the `1.7` branch.

| Commit | Subject | Why ignore |
| --- | --- | --- |
| `7f18e3a` | 1.6: sync Unbound dialog close; give VGE2 landing crew global IDs | The VGE2 `forceGlobalIds` scope is 1.6-only. In 1.6 the raid crew is generated only inside `CaptureGravshipCoroutine`, so suppressing the preview would remove it; the module forces global IDs during `GeneratePawns`/`Impact` instead. The 1.7 branch suppresses the preview through `VanillaGravshipPreview` and its VGE2 build generates the crew outside the preview. Folding the 1.6 scope would duplicate or conflict with the 1.7 implementation. |

The Unbound dialog-close part of `7f18e3a` **is** shared; it is already on 1.7 separately. Only the
VGE2 global-ID scope is ignored.
