# FUSE Gizmo System

A clean, event-driven gizmo handling system for the FUSE editor that manages RLD gizmo initialization, interaction tracking, and completion callbacks.

## Architecture

### Core Classes

- **`FuseGizmoHandler`**: Abstract base class that handles gizmo lifecycle, event registration, and provides completion callbacks.
- **`FuseMoveGizmoHandler`**: Handles move gizmos, invokes callback with final position.
- **`FuseRotateGizmoHandler`**: Handles rotate gizmos, invokes callback with final rotation.
- **`FuseScaleGizmoHandler`**: Handles scale gizmos, invokes callback with final scale.
- **`FuseGizmoManager`**: Manages active gizmos and ensures only one is active at a time.

## Usage Examples

### Basic Move Operation

```csharp
using FUSE.Editor.Gizmos;
using UnityEngine;

public class Example
{
    private FuseGizmoManager _gizmoManager = new FuseGizmoManager();

    public void StartMovingObject(GameObject target)
    {
        _gizmoManager.BeginMove(target, OnMoveCompleted);
    }

    private void OnMoveCompleted(Vector3 newPosition)
    {
        Debug.Log($"Object moved to: {newPosition}");

        // Persist the change, update backend, etc.
        SavePositionToBackend(newPosition);
    }
}
```

### Rotate Operation with Cancellation

```csharp
public class RotateExample
{
    private FuseGizmoManager _gizmoManager = new FuseGizmoManager();

    public void StartRotatingObject(GameObject target)
    {
        _gizmoManager.BeginRotate(target, OnRotateCompleted);
    }

    private void OnRotateCompleted(Quaternion newRotation)
    {
        Debug.Log($"Object rotated to: {newRotation.eulerAngles}");
        SaveRotationToBackend(newRotation);
    }

    public void CancelRotation()
    {
        // This will restore the original rotation
        _gizmoManager.CancelCurrentGizmo();
    }
}
```

### Scale Operation

```csharp
public class ScaleExample
{
    private FuseGizmoManager _gizmoManager = new FuseGizmoManager();

    public void StartScalingObject(GameObject target)
    {
        var handler = _gizmoManager.BeginScale(target, OnScaleCompleted);

        // Optional: configure uniform scaling
        handler?.SetUniformScaling(true);
    }

    private void OnScaleCompleted(Vector3 newScale)
    {
        Debug.Log($"Object scaled to: {newScale}");
        SaveScaleToBackend(newScale);
    }
}
```

### Advanced: Custom Configuration

```csharp
public class AdvancedExample
{
    private FuseGizmoManager _gizmoManager = new FuseGizmoManager();

    public void StartCustomMove(GameObject target)
    {
        var handler = _gizmoManager.BeginMove(target, OnMoveCompleted);

        if (handler != null)
        {
            // Configure transform space (Global or Local)
            handler.SetTransformSpace(RLD.GizmoSpace.Local);
        }
    }

    private void OnMoveCompleted(Vector3 newPosition)
    {
        // Custom validation
        if (IsPositionValid(newPosition))
        {
            SavePositionToBackend(newPosition);
        }
        else
        {
            Debug.LogWarning("Invalid position, reverting");
            // The original position was already captured, so we can handle this
        }
    }

    private bool IsPositionValid(Vector3 position)
    {
        // Custom validation logic
        return position.y >= 0; // Example: ensure above ground
    }
}
```

### Integration with Node Markers

```csharp
using FUSE.Editor.Gizmos;
using UnityEngine;

public class FuseNodeMarker : MonoBehaviour
{
    private static FuseGizmoManager _gizmoManager = new FuseGizmoManager();
    private GameObject _gizmoTarget;

    public void BeginMove()
    {
        // Create a temporary target GameObject for the gizmo
        _gizmoTarget = new GameObject("NodeGizmoTarget");
        _gizmoTarget.transform.position = transform.position;
        _gizmoTarget.transform.rotation = transform.rotation;

        // Start the move operation
        _gizmoManager.BeginMove(_gizmoTarget, OnMoveCompleted);
    }

    private void OnMoveCompleted(Vector3 newPosition)
    {
        // Update the actual node position
        transform.position = newPosition;

        // Persist to backend
        PersistNodePosition(newPosition);

        // Clean up the temporary target
        if (_gizmoTarget != null)
        {
            Destroy(_gizmoTarget);
            _gizmoTarget = null;
        }
    }

    public void BeginRotate()
    {
        _gizmoTarget = new GameObject("NodeGizmoTarget");
        _gizmoTarget.transform.position = transform.position;
        _gizmoTarget.transform.rotation = transform.rotation;

        _gizmoManager.BeginRotate(_gizmoTarget, OnRotateCompleted);
    }

    private void OnRotateCompleted(Quaternion newRotation)
    {
        transform.rotation = newRotation;
        PersistNodeRotation(newRotation);

        if (_gizmoTarget != null)
        {
            Destroy(_gizmoTarget);
            _gizmoTarget = null;
        }
    }

    private void OnDestroy()
    {
        // Clean up if marker is destroyed while gizmo is active
        if (_gizmoTarget != null)
        {
            Destroy(_gizmoTarget);
        }
    }
}
```

## Key Features

1. **Automatic Cleanup**: Handlers properly unregister events and dispose of resources.
2. **Single Active Gizmo**: The manager ensures only one gizmo is active at a time.
3. **Cancellation Support**: Cancel operations and restore original transforms.
4. **Type-Safe Callbacks**: Each handler provides strongly-typed completion events.
5. **Initial State Tracking**: Original transform is captured and can be restored.
6. **Error Handling**: Graceful degradation when RLD components aren't available.

## Lifecycle

```
1. BeginMove/Rotate/Scale() called
   ↓
2. Handler created and initialized
   ↓
3. RLD gizmo created and configured
   ↓
4. Target object set on gizmo
   ↓
5. User drags gizmo (OnDragUpdate called continuously)
   ↓
6. User releases gizmo (OnDragEnd called)
   ↓
7. Completion callback invoked with final transform
   ↓
8. Handler can be disposed or reused
```

## Thread Safety

All gizmo operations must be called from the Unity main thread. The handlers are not thread-safe.

## Memory Management

- Handlers implement `IDisposable` and should be disposed when no longer needed.
- The `FuseGizmoManager` automatically disposes old handlers when starting new operations.
- Remember to call `Dispose()` on the manager when your component is destroyed.

## Error Handling

All operations that can fail return `null` and log errors via `FuseLog`. Always check return values:

```csharp
var handler = _gizmoManager.BeginMove(target, OnMoveCompleted);
if (handler == null)
{
    Debug.LogError("Failed to start move operation");
    return;
}
```
