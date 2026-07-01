# FuseGizmoManager and Proxy GameObject Architecture

## Overview
The gizmo system uses temporary proxy GameObjects to manipulate editor handlers, completely decoupling the gizmo from actual GameObjects. This allows handlers to represent entities that don't have physical GameObjects (like data objects or abstract concepts). When a gizmo operation completes, transformations are synced back from the proxy to the handler.

## Multi-Handler Support
The FuseGizmoManager also supports operating on multiple EditorHandlerBase instances simultaneously. This allows users to move, rotate, and scale multiple objects together while maintaining their relative positions and orientations.

## Architecture: Proxy GameObject Pattern

### Why Proxy GameObjects?

The original approach directly manipulated GameObjects. However, some EditorHandlerBase implementations may represent entities without actual GameObjects:
- Data-only handlers (no visual representation)
- Abstract entities (not meant for scene editing)
- Derived handlers with custom visualization

The proxy pattern solves this by:
1. Creating a temporary GameObject for gizmo manipulation
2. Syncing the handler's transform to the proxy at start
3. After gizmo completes, syncing proxy transform back to handler
4. Automatically cleaning up the proxy when done

## Single-Handler Gizmos

### FuseGizmoHandler (Base Class)
**Location**: `FUSE.Editor/Gizmos/FuseGizmoHandler.cs`

Manages gizmo operations on a single EditorHandlerBase using a proxy GameObject.

**Key Properties**:
- `TransformGizmo` - The RLD gizmo instance
- `Handler` - The target EditorHandlerBase
- `GizmoTarget` - Temporary proxy GameObject  
- `InitialPosition/Rotation/Scale` - Captured at initialization

**Proxy Lifecycle**:
1. `Initialize()` creates proxy `GizmoTarget` GameObject
2. Proxy is named "FUSE_GizmoTarget" and hidden from hierarchy
3. Initial handler transform is copied to proxy
4. RLD gizmo manipulates the proxy
5. On completion, proxy transform is read and applied to handler via `Handler.SetPosition/Rotation/Scale()`
6. `Dispose()` calls `CleanupGizmoTarget()` to destroy proxy

**Public Methods**:
- `bool Initialize(EditorHandlerBase handler)` - Initialize with handler
- `void Cancel()` - Cancel and restore initial transforms
- `void Deactivate()` - Deactivate gizmo
- `void Dispose()` - Clean up all resources

**Protected Methods**:
- `abstract ObjectTransformGizmo CreateGizmo()` - Create specific gizmo type
- `virtual void ConfigureGizmo()` - Configure gizmo settings
- `virtual GameObject CreateGizmoTargetObject()` - Create proxy
- `virtual void CleanupGizmoTarget()` - Destroy proxy
- `abstract void OnGizmoCompleted(Vector3, Quaternion, Vector3)` - Handle completion

### Derived Classes

#### FuseMoveGizmoHandler
- Move single handler with position delta
- `OnMoveCompleted` event with final position
- `SetTransformSpace(GizmoSpace space)` for global/local

#### FuseRotateGizmoHandler
- Rotate single handler with rotation delta
- `OnRotateCompleted` event with final rotation
- `SetTransformSpace(GizmoSpace space)` for global/local

#### FuseScaleGizmoHandler
- Scale single handler with scale ratio
- `OnScaleCompleted` event with final scale
- `SetUniformScaling(bool uniform)` placeholder

## Multi-Handler Gizmos

### FuseMultiGizmoHandler (Base Class)
**Location**: `FUSE.Editor/Gizmos/FuseMultiGizmoHandler.cs`

Abstract base for managing multiple handlers with a single gizmo.

**Key Properties**:
- `TransformGizmo` - The RLD gizmo instance
- `Handlers` - Collection of target EditorHandlerBase instances
- `PrimaryHandler` - First handler (serves as visual anchor)
- `GizmoTarget` - Temporary proxy GameObject
- `InitialPositions/Rotations/Scales` - Captured for all handlers

**Proxy Lifecycle**:
1. `Initialize()` creates proxy matching primary handler's transform
2. Proxy is named "FUSE_MultiGizmoTarget"
3. User manipulates the proxy
4. RLD captures delta from proxy (compared to initial state)
5. `ApplyTransformToAllHandlers()` applies delta to all handlers preserving relative transforms
6. All handlers are synced via their `SetPosition/Rotation/Scale()` methods
7. Proxy is destroyed on `Dispose()`

**Delta Application Logic**:
```
primaryDeltaPos = finalPos - initialPrimaryPos
primaryDeltaRot = finalRot * inverse(initialPrimaryRot)
primaryDeltaScale = finalScale / initialPrimaryScale

For each handler:
  newPos = initialPos + primaryDeltaPos
  newRot = primaryDeltaRot * initialRot
  newScale = initialScale * primaryDeltaScale
```

**Public Methods**:
- `bool Initialize(IEnumerable<EditorHandlerBase> handlers)` - Initialize with handlers
- `void Cancel()` - Cancel all and restore initial transforms
- `void Deactivate()` - Deactivate gizmo
- `void Dispose()` - Clean up all resources

**Protected Methods**:
- `abstract ObjectTransformGizmo CreateGizmo()` - Create specific gizmo type
- `virtual void ConfigureGizmo()` - Configure gizmo settings
- `virtual GameObject CreateGizmoTargetObject()` - Create proxy
- `virtual void CleanupGizmoTarget()` - Destroy proxy
- `virtual void ApplyTransformToAllHandlers(...)` - Apply delta to all handlers
- `abstract void OnGizmoCompleted(Vector3, Quaternion, Vector3)` - Handle completion

### Derived Classes

#### FuseMultiMoveGizmoHandler
- Move multiple handlers together
- Maintains relative positions
- `OnMoveCompleted` event with final position

#### FuseMultiRotateGizmoHandler
- Rotate multiple handlers together
- Maintains relative rotations around primary handler
- `OnRotateCompleted` event with final rotation

#### FuseMultiScaleGizmoHandler
- Scale multiple handlers together
- Maintains relative scales
- `OnScaleCompleted` event with final scale

## FuseGizmoManager

### Updated API

**Properties**:
```csharp
public FuseGizmoHandler ActiveHandler { get; }
public FuseMultiGizmoHandler ActiveMultiHandler { get; }
public bool HasActiveGizmo { get; }
```

**Single-Handler Methods**:
```csharp
public FuseMoveGizmoHandler BeginMove(
    EditorHandlerBase handler, 
    Action<Vector3> onCompleted = null)

public FuseRotateGizmoHandler BeginRotate(
    EditorHandlerBase handler, 
    Action<Quaternion> onCompleted = null)

public FuseScaleGizmoHandler BeginScale(
    EditorHandlerBase handler, 
    Action<Vector3> onCompleted = null)
```

**Multi-Handler Methods**:
```csharp
public FuseMultiMoveGizmoHandler BeginMoveMultiple(
    IEnumerable<EditorHandlerBase> handlers, 
    Action<Vector3> onCompleted = null)

public FuseMultiRotateGizmoHandler BeginRotateMultiple(
    IEnumerable<EditorHandlerBase> handlers, 
    Action<Quaternion> onCompleted = null)

public FuseMultiScaleGizmoHandler BeginScaleMultiple(
    IEnumerable<EditorHandlerBase> handlers, 
    Action<Vector3> onCompleted = null)
```

**Lifecycle Methods**:
```csharp
public void CancelCurrentGizmo()    // Cancel and restore
public void EndCurrentGizmo()       // End and accept
public void Dispose()               // Clean up all
```

## Usage Examples

### Single Handler Move
```csharp
var moveGizmo = gizmoManager.BeginMove(handler, finalPos =>
{
    Debug.Log($"Moved to {finalPos}");
});
```

### Multiple Handlers Rotate
```csharp
var handlers = selection.GetSelectedHandlers();
var rotateGizmo = gizmoManager.BeginRotateMultiple(handlers, finalRot =>
{
    Debug.Log($"Rotated: {finalRot}");
});
```

### Cancel Current Operation
```csharp
if (userPressedEscape)
{
    gizmoManager.CancelCurrentGizmo();  // Restores initial transforms
}
```

## Design Benefits

1. **No GameObject Requirement**: Handlers without GameObjects work seamlessly
2. **Decoupled**: Gizmo logic independent of handler implementation
3. **Safe Cleanup**: Proxy destruction guaranteed via IDisposable pattern
4. **Relative Transforms**: Multi-handler operations preserve object relationships
5. **Undo/Redo**: All changes go through handler methods with undo support
6. **Single Gizmo**: Only one gizmo active at a time, avoiding conflicts

## Build Status
✅ All changes compile successfully with no errors.

