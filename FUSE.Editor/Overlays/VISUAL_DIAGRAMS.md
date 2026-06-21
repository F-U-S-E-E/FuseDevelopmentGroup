# FUSE Editor Overlay System - Visual Diagrams

## System Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         Editor Tools & UI                               │
│  (Move Tool, Rotate Tool, Node Selection, Building Placement, etc.)     │
└────────────────────────────┬────────────────────────────────────────────┘
                             │
                             ▼
                    ┌────────────────────┐
                    │   User initiates   │
                    │    editing action  │
                    └────────────┬───────┘
                                 │
                    ┌────────────▼──────────────┐
                    │  Register Preview with   │
                    │  FuseOverlayManager      │
                    └────────────┬──────────────┘
                                 │
              ┌──────────────────┘
              │
              ├──► Original object (unchanged)
              │
              ├──► Preview data (pos, rot, scale from edits)
              │
              └──► IOverlayRenderable adapter
                       │
                       ▼
         ┌──────────────────────────────┐
         │ FuseOverlayRenderer          │
         │ - Manages all previews       │
         │ - Creates/caches materials   │
         │ - Renders in OnPostRender()  │
         └──────────────────┬───────────┘
                            │
                ┌───────────┼───────────┐
                ▼           ▼           ▼
         Wireframe     Ghost         Custom
         Material      Material      Material
                │           │           │
                └───────────┼───────────┘
                            │
                            ▼
                   Graphics.DrawMesh()
                   (Rendering Layer 30)
                            │
                            ▼
         ┌────────────────────────────────┐
         │   Screen - Preview Visible     │
         │  (ghost/wireframe at new pos)  │
         └────────────────────────────────┘
```

---

## Data Flow During Editing

```
Start Editing
     │
     ▼
┌─────────────────────────────────────────┐
│ Register Preview:                       │
│ - ID: "node-123"                        │
│ - Original Object: nodeGameObject       │
│ - Preview Position: (10, 5, 20)         │
│ - Preview Rotation: (0, 90, 0)          │
│ - Adapter: TrackNodeOverlayAdapter      │
└──────────────┬──────────────────────────┘
               │
               ▼
       ┌───────────────┐
       │ Preview Added │
       │ Event Fired   │
       └───────────────┘
               │
        ┌──────┴──────┐
        │             │
        ▼             ▼
   [User Edits] [UI Updates]
        │             │
        └──────┬──────┘
               │
               ▼
    Update Preview (New Position)
        │
        ▼
┌─────────────────────────────────┐
│ Update Preview:                 │
│ - Position: (15, 5, 20)         │
│ - Rotation: (0, 90, 0)          │
│ - Fire OnPreviewUpdated event   │
└──────────────┬──────────────────┘
               │
        ┌──────┴──────┐
        │             │
    [More       [Re-Render]
     Edits]          │
        │            ▼
        │      Screen Updates
        │            │
        └──────┬─────┘
               │
        (repeat as needed)
               │
               ▼
      ┌────────────────┐
      │ User Confirms  │
      └────────────┬───┘
                   │
        ┌──────────┴──────────┐
        │                     │
        ▼                     ▼
    Apply Edits       UnregisterPreview
    (to actual          (clears preview)
    object)
        │                     │
        └──────────┬──────────┘
                   │
                   ▼
         ┌──────────────────┐
         │ Preview Removed  │
         │ Event Fired      │
         └──────────────────┘
                   │
                   ▼
          Editing Complete
```

---

## Gizmo + Overlay Workflow

```
User Selects Node
     │
     ▼
┌──────────────────────────────┐
│ Begin Move with Preview:     │
│ 1. Register Preview          │
│ 2. Create Gizmo Target       │
│ 3. Start Gizmo on Target     │
└──────────────┬───────────────┘
               │
     ┌─────────┴──────────┐
     │                    │
     ▼                    ▼
[Gizmo Active]      [Preview Visible]
     │                    │
     │  Update Loop:      │
     │  - Get gizmo pos   │
     │  - Update preview  │
     │  - Screen updates  │
     │                    │
     └─────────┬──────────┘
               │
               ▼
      Gizmo Callback: Final Position
               │
               ▼
    ┌──────────────────────────────┐
    │ On Move Completed:           │
    │ 1. Apply to actual object    │
    │ 2. Update preview final pos  │
    │ 3. Save to backend/history   │
    │ 4. Unregister preview        │
    │ 5. Clean up gizmo target     │
    └────────────┬─────────────────┘
                 │
                 ▼
         ┌──────────────┐
         │ Editing Done │
         └──────────────┘
```

---

## Class Interactions

```
┌──────────────────────────────────────┐
│     FuseOverlayManager               │
│     (Singleton)                      │
├──────────────────────────────────────┤
│ - Instance                           │
│ - IsEnabled                          │
│ - RegisterPreview()                  │
│ - UnregisterPreview()                │
│ - UpdatePreview()                    │
│ - GetPreview()                       │
└──────────┬──────────────────────────┘
           │ delegates to
           ▼
┌──────────────────────────────────────────┐
│     FuseOverlayRenderer                  │
├──────────────────────────────────────────┤
│ - _activePreviews: Dict<id, data>       │
│ - RegisterPreview()                     │
│ - UpdatePreview()                       │
│ - GetPreview()                          │
│ - RenderPreviews() [OnPostRender]       │
│ - GetMeshForPreview()                   │
│ - GetMaterialForPreview()               │
└──────────┬───────────────────────────────┘
           │ manages many
           ▼
┌──────────────────────────────────────────┐
│     OverlayPreviewData                   │
│     (per preview instance)               │
├──────────────────────────────────────────┤
│ + OriginalObject: GameObject             │
│ + ObjectId: string                       │
│ + ObjectType: string (tag)               │
│ + PreviewPosition: Vector3               │
│ + PreviewRotation: Quaternion            │
│ + PreviewScale: Vector3                  │
│ + IsVisible: bool                        │
│ + Tint: Color?                           │
│ + Renderable: IOverlayRenderable         │
│ + UpdatePreviewTransform()               │
│ + GetPreviewMatrix()                     │
└──────────┬───────────────────────────────┘
           │ optionally uses
           ▼
┌──────────────────────────────────┐
│   IOverlayRenderable             │
│   (Interface)                    │
├──────────────────────────────────┤
│ + GetOverlayMesh()               │
│ + GetOverlayMaterial()           │
│ + GetOriginalPosition()          │
│ + GetOriginalRotation()          │
│ + GetOriginalScale()             │
│ + GetObjectBounds()              │
└──────────┬──────────────────────┘
           │ implemented by
           ├──► TrackNodeOverlayAdapter
           ├──► BuildingOverlayAdapter
           ├──► BezierPointOverlayAdapter
           └──► (Your custom adapters)
```

---

## Object Lifecycle

```
Game Object (e.g., TrackNode)
     │
     │  Create Adapter
     │  ▼
     │  IOverlayRenderable
     │  ├─ GetOverlayMesh() → Mesh
     │  ├─ GetOverlayMaterial() → Material
     │  └─ GetOriginal*() → Transform data
     │
     │  Register Preview
     │  ▼
     │  OverlayPreviewData
     │  ├─ OriginalObject (game object ref)
     │  ├─ PreviewPosition (from edits)
     │  ├─ PreviewRotation (from edits)
     │  ├─ PreviewScale (from edits)
     │  ├─ Renderable (adapter ref)
     │  └─ IsVisible, Tint, etc.
     │
     │  During Update
     │  ▼
     │  UpdatePreviewTransform()
     │  └─ PreviewPosition = newPos
     │
     │  During Render (OnPostRender)
     │  ▼
     │  Graphics.DrawMesh(
     │    mesh = adapter.GetOverlayMesh(),
     │    matrix = preview.GetPreviewMatrix(),
     │    material = adapter.GetOverlayMaterial()
     │  )
     │
     │  On Confirm
     │  ▼
     │  gameObject.transform.position = previewData.PreviewPosition
     │  gameObject.transform.rotation = previewData.PreviewRotation
     │  gameObject.transform.localScale = previewData.PreviewScale
     │
     │  Unregister Preview
     │  ▼
     │  OverlayPreviewData destroyed
     │  (game object unchanged by overlay)
     │
     └─ Game Object continues normally
```

---

## Rendering Pipeline

```
OnPostRender() Hook Called
     │
     ▼
FuseOverlayRenderer.RenderPreviews()
     │
     ├─► For each OverlayPreviewData:
     │   │
     │   ├─► Check IsVisible
     │   │   (skip if false)
     │   │
     │   ├─► Get Mesh via:
     │   │   - Renderable.GetOverlayMesh()  [if custom]
     │   │   - Or game object's MeshFilter
     │   │
     │   ├─► Get Material via:
     │   │   - Renderable.GetOverlayMaterial()  [if custom]
     │   │   - Or default wireframe/ghost
     │   │
     │   ├─► Build Matrix4x4:
     │   │   Matrix4x4.TRS(
     │   │     position: PreviewPosition,
     │   │     rotation: PreviewRotation,
     │   │     scale: PreviewScale
     │   │   )
     │   │
     │   ├─► Apply Tint (if set):
     │   │   material.SetColor("_Color", Tint)
     │   │
     │   └─► Render:
     │       Graphics.DrawMesh(mesh, matrix, material, layer=30)
     │
     └─► All previews rendered in batch
           (efficient!)
```

---

## Memory Layout

```
FuseOverlayManager (singleton)
│
└─ FuseOverlayRenderer
   │
   ├─ _wireframeMaterial: Material
   │
   ├─ _ghostMaterial: Material
   │
   └─ _activePreviews: Dictionary<string, OverlayPreviewData>
      │
      ├─ "node-1"
      │  └─ OverlayPreviewData
      │     ├─ OriginalObject: GameObject reference
      │     ├─ ObjectId: "node-1" (string)
      │     ├─ ObjectType: "TrackNode" (string)
      │     ├─ PreviewPosition: Vector3 (12 bytes)
      │     ├─ PreviewRotation: Quaternion (16 bytes)
      │     ├─ PreviewScale: Vector3 (12 bytes)
      │     ├─ IsVisible: bool (1 byte)
      │     ├─ Tint: Color? (nullable, 16 bytes)
      │     └─ Renderable: IOverlayRenderable (reference)
      │
      ├─ "node-2"
      │  └─ OverlayPreviewData (same structure)
      │
      ├─ "building-1"
      │  └─ OverlayPreviewData (same structure)
      │
      └─ ... (more previews)

Total per preview: ~64 bytes + string references
= Very lightweight!
```

---

## Integration with Existing Systems

```
Existing Systems                  Overlay System
─────────────────────────────────────────────────

    FuseGizmoManager                │
         │                          │
         ├─ BeginMove()             │
         │  └─ Callback             │
         │     │                    │
         │     └─► Overlay:         │
         │         UpdatePreview()  │
         │                          │
         └─ IsActive prop           │
            │                       │
            └─► Check during        │
                Update() to feed    │
                preview coords      

    FuseNodeMarker                 │
         │                          │
         ├─ OnClick()               │
         │  └─► UI feedback         │
         │      │                   │
         │      └─► Overlay:        │
         │          RegisterPreview()
         │                          │
         └─ BeginMove/Rotate        │
            └─► Handled separately  
                from overlay        

    FuseNodeEditorController       │
         │                          │
         └─ SelectMarker()          │
            └─► Could trigger       │
                RegisterPreview()   
```

---

## State Transitions

```
[EMPTY] (no preview registered)
     │
     │ RegisterPreview()
     ▼
[REGISTERED, VISIBLE] (showing preview)
     │
     ├─ UpdatePreview() ─────┐
     │  (position changes)   │
     └────────────────────────┘
     │
     ├─ preview.IsVisible = false
     │  ▼
     │  [REGISTERED, HIDDEN] (not rendering)
     │  │
     │  └─ preview.IsVisible = true
     │     ▼ (back to REGISTERED, VISIBLE)
     │
     └─ UnregisterPreview()
        │ / ClearAllPreviews()
        ▼
        [EMPTY]
```

---

## Performance Profile

```
Per Frame (OnPostRender):

  For each active preview:
  ├─ Check IsVisible: O(1) ~0.01ms
  ├─ Get Mesh: O(1) ~0.02ms
  ├─ Get Material: O(1) ~0.02ms
  ├─ Build Matrix: O(1) ~0.01ms
  └─ Graphics.DrawMesh(): Variable ~0.05-0.2ms per preview

  Dictionary iteration: O(n) where n = preview count

  Total for 100 previews: ~5-20ms (mostly rendering)
  Typical (10 previews): ~0.5-2ms

Memory:
  Per preview: ~64 bytes
  100 previews: ~6.4 KB
  Per material: ~few KB (cached, not per preview)
```

---

These diagrams show:
- **System Architecture** - How components connect
- **Data Flow** - What happens during editing
- **Gizmo Integration** - How it works with gizmo system
- **Class Structure** - Relationships and responsibilities
- **Memory Layout** - How data is organized
- **Rendering Pipeline** - What happens OnPostRender
- **State Transitions** - Preview lifecycle
- **Performance** - CPU and memory costs
