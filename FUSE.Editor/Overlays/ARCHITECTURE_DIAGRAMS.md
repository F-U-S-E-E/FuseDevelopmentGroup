# Overlay Selection System - Visual Architecture Guide

## System Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│ Editor Tools / Input Handlers / Scene View                      │
└────────────────────────────┬────────────────────────────────────┘
                             │
                       TrySelectPreviewAtMouse(mousePos)
                             │
┌────────────────────────────▼────────────────────────────────────┐
│ FuseOverlayManager (Singleton)                                  │
│                                                                 │
│  Properties:                                                    │
│  ├─ SelectionSystem                                             │
│  ├─ HandlerRegistry                                             │
│  └─ Renderer                                                    │
│                                                                 │
│  Methods:                                                       │
│  ├─ SetSelectionCamera(camera)                                  │
│  ├─ TrySelectPreviewAtMouse(mousePos)                            │
│  └─ InvokeSelectionCallback(preview, area)                      │
└────────────────────────────┬────────────────────────────────────┘
                             │
        ┌────────────────────┴────────────────────┐
        │                                         │
        ▼                                         ▼
┌───────────────────────────┐        ┌──────────────────────────┐
│ OverlaySelectionSystem    │        │ FuseOverlayRenderer      │
│                           │        │                          │
│ ├─ TrySelect()            │        │ ├─ _activePreviews      │
│ ├─ UpdateHoverFromRay()   │        │ ├─ _selectionSystem     │
│ ├─ TrySelectFromRay()     │        │ └─ _handlerRegistry     │
│ ├─ SetCamera()            │        │                          │
│ ├─ GetHoveredArea()       │        │ Methods:                 │
│ └─ Events:                │        │ ├─ RegisterPreview()    │
│    ├─ OnPreviewSelection  │        │ ├─ UpdatePreview()      │
│    │  Changed             │        │ ├─ UnregisterPreview()  │
│    ├─ OnPreviewHovered    │        │ ├─ RenderPreviews()     │
│    └─ OnPreviewUnhovered  │        │ └─ ApplyPreview<T>()    │
└────────────────────────┬──┘        └──────────────┬───────────┘
                         │                          │
         Emits selection event            Stores/manages previews
                         │                          │
                         └──────────────┬───────────┘
                                        │
                            ┌───────────▼────────────┐
                            │ OverlayPreviewData[]   │
                            │                        │
                            │ ├─ Entity Reference    │
                            │ ├─ SelectionAreas[]    │
                            │ ├─ PreviewTransform    │
                            │ ├─ Renderable          │
                            │ └─ Tint/Visibility     │
                            └────────────────────────┘
```

## Selection Flow Sequence Diagram

```
User                Editor Tool        Overlay System           Handler
  │                    │                    │                     │
  ├─ Click Mouse       │                    │                     │
  │                    │                    │                     │
  │    Mouse Event     │                    │                     │
  ├──────────────────►│                    │                     │
  │                    │                    │                     │
  │                    │ TrySelectPreviewAt│                    │
  │                    │  Mouse(mousePos)  │                     │
  │                    ├──────────────────►│                     │
  │                    │                    │                     │
  │                    │                    │ TrySelect(ray)     │
  │                    │                    ├─ Raycast against   │
  │                    │                    │   all preview areas│
  │                    │                    │ ─ Find closest hit │
  │                    │                    │                     │
  │                    │                    │ InvokeSelectionCB  │
  │                    │                    ├────────────────────►│
  │                    │                    │                     │
  │                    │                    │ OnPreviewSelected() │
  │                    │                    │ ◄────────────────────
  │                    │                    │                     │
  │                    │  return true       │                     │
  │                    │ ◄──────────────────┤                     │
  │                    │                    │                     │
  │ Event.Use()        │                    │                     │
  │ ◄──────────────────┤                    │                     │
  │                    │                    │                     │
```

## Handler-Based Preview Lifecycle

```
┌─────────────────────────────────────────────────────┐
│ Start: User has entity to preview                 │
└────────────┬────────────────────────────────────────┘
             │
             ▼
    ┌────────────────────┐
    │ Register Handler   │  (once per entity type)
    │                    │
    │ registry.Register  │
    │ Handler<MyEntity>()│
    └────────┬───────────┘
             │
             ▼
    ┌────────────────────┐
    │ Create Preview     │
    │                    │
    │ mgr.ApplyPreview   │
    │   <MyEntity>(obj)  │
    └────────┬───────────┘
             │
             ├─ Handler.CanHandle(obj)
             ├─ Handler.ExtractTransform(obj)
             ├─ Handler.GetSelectionAreas(obj) ◄─── NEW
             └─ Creates OverlayPreviewData
                  with SelectionAreas
             │
             ▼
    ┌────────────────────────┐
    │ Render & Allow Click   │
    │                        │
    │ Overlay renders mesh   │
    │ SelectionAreas active  │
    └────────┬───────────────┘
             │
             ├─ User clicks on preview
             │
             ▼
    ┌────────────────────────┐
    │ Selection Hit Test     │ ◄─── NEW
    │                        │
    │ Raycast against areas  │
    │ Find closest distance  │
    └────────┬───────────────┘
             │
             ▼
    ┌────────────────────────┐
    │ Invoke Handler         │ ◄─── NEW
    │ Callback               │
    │                        │
    │ Handler.OnPreviewSel   │
    │   ected(obj, area)     │
    └────────┬───────────────┘
             │
             ▼
    ┌────────────────────────┐
    │ Handler Does Work      │
    │                        │
    │ Registers selection    │
    │ Updates UI             │
    │ Performs action        │
    └────────┬───────────────┘
             │
             ▼
    ┌────────────────────┐
    │ Cleanup (optional) │
    │                    │
    │ mgr.Unregister     │
    │ Preview(id)        │
    └────────────────────┘
```

## Selection Area Bounds Diagram

```
                    World Space

            Preview Position (Px, Py, Pz)
                        ▲
                        │
                   (rotation)
                    │    │
                    │    │
         ┌──────────┼────┼──────────┐
         │          │    │          │
         │   (scale zone) │          │
         │          │    │          │
         │    ┌─────┼────┼─────┐    │
         │    │     │    │     │    │
         │    │  ┌──┼────┼──┐  │    │
         │    │  │  │Bounds│ │  │    │
         │    │  │  │ Size │ │  │    │
         │    │  │  └──┘   │  │    │
         │    │  └─────────┘  │    │
         │    └────────────────┘    │
         └──────────────────────────┘

         Transform = TRS(Position, Rotation, Scale)

         Resolution: OverlaySelectionArea.Raycast()
         ├─ Transform world point to local space
         ├─ Check against Bounds
         └─ Calculate hit distance
```

## Event Flow Diagram

```
        Mouse Movement
               │
               ▼
    UpdateHoverFromMouse()
               │
         ┌─────┴─────┐
         │           │
    ┌────▼─────┐  ┌──▼────────┐
    │ Hit Found │  │ No Hit    │
    └────┬──────┘  └──┬────────┘
         │            │
         ▼            ▼
    Different from   Current =
    Previous?        Null?
         │            │
         ├─Yes◄───────┤
         │            │
         ▼            ▼
    OnPreview    OnPreview
    Hovered      Unhovered
    (id, area)   ()
         ▲            ▲
         │            │
         └────────────┘


        Mouse Click
             │
             ▼
    TrySelectFromRay()
             │
        ┌────┴─────┐
        │          │
    ┌───▼──┐   ┌───▼──┐
    │ Hit  │   │ Miss │
    └───┬──┘   └───┬──┘
        │          │
        ▼          ▼
    OnPreview  (no event)
    Selection
    Changed
    (id, area)
        │
        ▼
    InvokeSelectionCallback()
        │
        ▼
    Handler.OnPreviewSelected()
        │
        ▼
    (Handler specific action)
```

## Data Structure: OverlaySelectionArea

```
OverlaySelectionArea
│
├─ AreaId: string
│  └─ Unique identifier within preview (e.g., "node_123")
│
├─ PreviewId: string
│  └─ ID of owning preview (e.g., "track_node_456")
│
├─ Bounds: Bounds
│  └─ Local space bounds (center: origin, size: dimension)
│
├─ Transform: Matrix4x4
│  └─ World transform (position, rotation, scale)
│
├─ IsSelectable: bool
│  └─ Can this area be clicked?
│
├─ SelectionData: object
│  └─ Handler-specific metadata
│  │  Examples:
│  │  ├─ entity reference
│  │  ├─ control point index
│  │  └─ custom data
│
├─ HighlightColor: Color
│  └─ Optional visual feedback color
│
├─ SelectionMesh: Mesh (optional)
│  └─ Debug wireframe rendering
│
└─ Methods:
   ├─ ContainsPoint(Vector3 worldPoint): bool
   └─ Raycast(Ray ray, out float distance): bool
```

## Handler Selection Method Signatures

```csharp
// Define selectable regions
public OverlaySelectionArea[] GetSelectionAreas(
    T entity,                           // The entity being previewed
    Vector3 previewPosition,            // Current preview world position
    Quaternion previewRotation,         // Current preview world rotation
    Vector3 previewScale               // Current preview world scale
)
    → Returns: array of OverlaySelectionArea
             or null/empty if not selectable

// Handle selection callback
public void OnPreviewSelected(
    T entity,                           // The entity that was selected
    OverlaySelectionArea selectionArea  // Which area was clicked
)
    → Performs: selection registration, UI updates, etc.
```

## Memory Layout: Preview with Selection

```
OverlayPreviewData (heap object)
├─ objectId: string (24 bytes + str data)
├─ gameObject: GameObject (8 bytes reference)
├─ previewPosition: Vector3 (12 bytes)
├─ previewRotation: Quaternion (16 bytes)
├─ previewScale: Vector3 (12 bytes)
├─ tint: Color (16 bytes)
├─ isVisible: bool (1 byte)
├─ objectType: string (24 bytes + str data)
├─ renderable: IOverlayRenderable (8 bytes ref)
├─ SelectionAreas: OverlaySelectionArea[] (NEW)
│  ├─ Length: int (4 bytes)
│  └─ [0..n]: OverlaySelectionArea[] (80-120 bytes each)
├─ IsSelected: bool (NEW, 1 byte)
└─ Entity: object (NEW, 8 bytes reference)

Total per preview: ~100-200 bytes base
+ ~100 bytes per selection area
```

## State Machine: Selection States

```
                    ┌─────────────┐
                    │   No Click  │
                    └──────┬──────┘
                           │
              ┌────────────┴────────────┐
              │                         │
              ▼                         │
        ┌──────────────┐               │
        │ User Hovers  │              │
        │ Preview      │              │
        └──────┬───────┘              │
               │                      │
    (hovers)   │         (leaves)     │
    OnHovered  │                      │
               ├────────────┬─────────┤
               │            │         │
               ▼            ▼         │
        ┌────────────┐ ┌───────────┐  │
        │  Hovered   │ │  Unhovered│──┤
        │   State    │ │   State   │  │
        └──────┬─────┘ └─┬─────────┘  │
               │         │            │
    (clicks)   │         │ (mouse     │
    OnSelect   │         │  leaves)   │
               │         └────────┬───┘
               ▼                  │
        ┌────────────┐            │
        │  Selected  │            │
        │   State    │         (no click)
        └──────┬─────┘            │
               │                  │
               │◄───────────────────┘
               └──ConfirmOrCancel──
                    Handler
```

This architecture provides a clean, extensible, type-safe selection system that integrates seamlessly with the existing overlay rendering infrastructure.
