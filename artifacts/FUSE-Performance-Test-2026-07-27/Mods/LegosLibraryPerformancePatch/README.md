# Lego's Library Performance Patch

Standalone Unity Mod Manager compatibility patch for Railroader.

It suppresses the per-definition informational log storm produced by
`LegosLibraryOfStuff` while its `ContainerSerialization.Deserialize` patch
applies rolling-stock edits. Definition edits, clones, component groups, and
exception reporting are unchanged.

It also replaces Logos & Decorations' first-use PNG path with Unity's direct
PNG decoder. The original code decoded each file through System.Drawing,
re-encoded it into a new PNG in memory, and then asked Unity to decode it a
second time. Texture content, mipmaps, cache keys, materials, and component
behavior are unchanged.

Version 0.3 also limits Logos & Decorations prefab-model requests to four
starts per frame. A complex locomotive can contain dozens of these components;
the original starts every asset request from the car's single model-completion
frame. All components still load, but the bundle I/O and continuation wave is
spread across frames. Destroyed controllers now cancel their outstanding load,
dispose retained asset references, and release the runtime materials they own.

Version 0.4 guards two Logos & Decorations failure paths that previously sent
Unity through its synchronous crash reporter. A decal helper placed on building
scenery no longer dereferences a nonexistent parent car; the decal stays
visible, but its car-only culling hook is skipped. Missing optional prefab
assets now skip only the affected decoration instead of escaping from the
mod's `async void` configure method.

Measured on a 204-car save, the snapshot hitch fell from 4,982 ms and 59
full-GC cycles to 937 ms and 3 full-GC cycles. All 204 cars restored.

This mod does not depend on FUSE. Remove the
`Mods/LegosLibraryPerformancePatch` folder when Lego's Library fixes the issue
upstream.
