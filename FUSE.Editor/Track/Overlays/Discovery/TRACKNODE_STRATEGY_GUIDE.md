# TrackNodeDiscoveryStrategy - Implementation Guide

## Overview

The `TrackNodeDiscoveryStrategy` is an empty template for discovering `TrackNode` objects and making them available for overlay rendering.

**File**: `FUSE.Editor/Track/Overlays/Discovery/TrackNodeDiscoveryStrategy.cs`

## Quick Start

### 1. Basic Implementation

Find all TrackNodes in the scene:

```csharp
public IEnumerable<DiscoveredOverlayObject> DiscoverObjects()
{
    var trackNodes = Object.FindObjectsOfType<TrackNode>();

    foreach (var trackNode in trackNodes)
    {
        if (trackNode == null) continue;

        yield return new DiscoveredOverlayObject
        {
            Entity = trackNode,
            HasPendingEdits = false,
            ObjectId = $"tracknode_{trackNode.GetInstanceID()}",
            Priority = 1.0f,
            Distance = 0f,
            SourceStrategy = StrategyName
        };
    }
}
```

### 2. Distance-Based Discovery

Find TrackNodes near a focal point (e.g., camera):

```csharp
private Func<Vector3> _getFocalPoint;

public TrackNodeDiscoveryStrategy(Func<Vector3> getFocalPoint = null)
{
    _getFocalPoint = getFocalPoint ?? (() => Vector3.zero);
}

public IEnumerable<DiscoveredOverlayObject> DiscoverObjects()
{
    var focalPoint = _getFocalPoint();
    var trackNodes = Object.FindObjectsOfType<TrackNode>();
    const float searchRadius = 500f;

    foreach (var trackNode in trackNodes)
    {
        if (trackNode == null) continue;

        var distance = Vector3.Distance(trackNode.transform.position, focalPoint);
        if (distance > searchRadius) continue;  // Skip too far away

        yield return new DiscoveredOverlayObject
        {
            Entity = trackNode,
            HasPendingEdits = false,
            ObjectId = $"tracknode_{trackNode.GetInstanceID()}",
            Priority = 1.0f - (distance / searchRadius),  // Closer = higher priority
            Distance = distance,
            SourceStrategy = StrategyName
        };
    }
}
```

### 3. With Pending Edits

If TrackNodes have pending edits (FuseNode data):

```csharp
public IEnumerable<DiscoveredOverlayObject> DiscoverObjects()
{
    var trackNodes = Object.FindObjectsOfType<TrackNode>();

    foreach (var trackNode in trackNodes)
    {
        if (trackNode == null) continue;

        // Check if this TrackNode has pending edits
        var pendingData = GetPendingFuseNodeData(trackNode);

        yield return new DiscoveredOverlayObject
        {
            Entity = trackNode,
            HasPendingEdits = pendingData != null,
            PreviewData = pendingData,
            ObjectId = $"tracknode_{trackNode.GetInstanceID()}",
            Priority = pendingData != null ? 1.5f : 1.0f,  // Higher priority if pending
            Distance = 0f,
            SourceStrategy = StrategyName
        };
    }
}

private FuseNode GetPendingFuseNodeData(TrackNode trackNode)
{
    // TODO: Implement logic to find pending edits for this TrackNode
    // Return FuseNode if pending edits exist, null otherwise
    return null;
}
```

### 4. By Layer or Tag

Find TrackNodes in specific layers or tags:

```csharp
public IEnumerable<DiscoveredOverlayObject> DiscoverObjects()
{
    var trackNodes = Object.FindObjectsOfType<TrackNode>();

    foreach (var trackNode in trackNodes)
    {
        if (trackNode == null) continue;

        // Filter by layer
        if (trackNode.gameObject.layer != LayerMask.NameToLayer("Interactive"))
            continue;

        // Filter by tag
        if (!trackNode.gameObject.CompareTag("EditorSelectable"))
            continue;

        yield return new DiscoveredOverlayObject
        {
            Entity = trackNode,
            HasPendingEdits = false,
            ObjectId = $"tracknode_{trackNode.GetInstanceID()}",
            Priority = 1.0f,
            Distance = 0f,
            SourceStrategy = StrategyName
        };
    }
}
```

## DiscoveredOverlayObject Fields

When yielding discovered TrackNodes, populate these fields:

| Field | Required | Purpose |
|-------|----------|---------|
| `Entity` | ✅ | The TrackNode being discovered |
| `HasPendingEdits` | ✅ | Whether this node has unsaved edits |
| `PreviewData` | ❌ | FuseNode or other pending edit data (null if no edits) |
| `ObjectId` | ✅ | Unique string identifier (used as overlay ID) |
| `Priority` | ✅ | 0-1 range (1.0 = highest priority, render first) |
| `Distance` | ✅ | Distance from focal point for LOD/culling |
| `SourceStrategy` | ✅ | Set to `StrategyName` |

## Key Properties

```csharp
public string StrategyName => "TrackNodes";
// Change this if you want to distinguish multiple TrackNode strategies
// Example: "TrackNodes_Nearby", "TrackNodes_WithEdits", etc.

public int ExecutionOrder => 20;
// Determines order relative to other strategies (lower = earlier)
// 0-10: Built-in strategies run first
// 20: Default for custom strategies
// 30+: Run after other discovery
```

## Lifecycle Methods

### OnEnable()

Called when the strategy is registered. Use for:
- Caching data
- Subscribing to events
- Initializing caches

```csharp
public void OnEnable()
{
    // Example: Subscribe to TrackNode changes
    // TrackNodeManager.OnNodeAdded += HandleNodeAdded;
    // TrackNodeManager.OnNodeRemoved += HandleNodeRemoved;
}
```

### OnDisable()

Called when the strategy is unregistered. Use for:
- Cleanup
- Unsubscribing from events
- Clearing caches

```csharp
public void OnDisable()
{
    // Example: Unsubscribe from events
    // TrackNodeManager.OnNodeAdded -= HandleNodeAdded;
    // TrackNodeManager.OnNodeRemoved -= HandleNodeRemoved;
}
```

## Registration

To use this strategy, register it with the overlay manager:

```csharp
var manager = FuseOverlayManager.Instance;

var strategy = new TrackNodeDiscoveryStrategy(
    getFocalPoint: () => Camera.main.transform.position
);

manager.RegisterDiscoveryStrategy(strategy);
manager.EnableDiscovery();
```

## Common Patterns

### Pattern 1: All TrackNodes in Scene

```csharp
public IEnumerable<DiscoveredOverlayObject> DiscoverObjects()
{
    foreach (var trackNode in Object.FindObjectsOfType<TrackNode>())
    {
        if (trackNode != null)
        {
            yield return new DiscoveredOverlayObject
            {
                Entity = trackNode,
                ObjectId = $"tn_{trackNode.GetInstanceID()}",
                Priority = 1.0f,
            };
        }
    }
}
```

### Pattern 2: Nearby TrackNodes with Distance Priority

```csharp
private Vector3 _focalPoint;

public IEnumerable<DiscoveredOverlayObject> DiscoverObjects()
{
    _focalPoint = Camera.main?.transform.position ?? Vector3.zero;
    const float radius = 500f;

    foreach (var trackNode in Object.FindObjectsOfType<TrackNode>())
    {
        if (trackNode == null) continue;

        float distance = Vector3.Distance(trackNode.transform.position, _focalPoint);
        if (distance > radius) continue;

        yield return new DiscoveredOverlayObject
        {
            Entity = trackNode,
            ObjectId = $"tn_{trackNode.GetInstanceID()}",
            Priority = 1.0f - (distance / radius),
            Distance = distance,
        };
    }
}
```

### Pattern 3: TrackNodes by Category

```csharp
private TrackNodeCategory _category;

public TrackNodeDiscoveryStrategy(TrackNodeCategory category)
{
    _category = category;
}

public IEnumerable<DiscoveredOverlayObject> DiscoverObjects()
{
    foreach (var trackNode in Object.FindObjectsOfType<TrackNode>())
    {
        if (trackNode == null || trackNode.Category != _category) continue;

        yield return new DiscoveredOverlayObject
        {
            Entity = trackNode,
            ObjectId = $"tn_{trackNode.GetInstanceID()}",
            Priority = 1.0f,
        };
    }
}
```

## Performance Tips

1. **Cache FindObjectsOfType results** - It's expensive, do it rarely
   ```csharp
   private TrackNode[] _cachedNodes;

   public void OnEnable()
   {
       _cachedNodes = Object.FindObjectsOfType<TrackNode>();
   }

   public IEnumerable<DiscoveredOverlayObject> DiscoverObjects()
   {
       foreach (var node in _cachedNodes ?? System.Array.Empty<TrackNode>())
       {
           // ... yield discovered objects
       }
   }
   ```

2. **Use Physics queries** - More efficient than FindObjectsOfType
   ```csharp
   var nearby = Physics.OverlapSphere(focalPoint, searchRadius);
   foreach (var collider in nearby)
   {
       var trackNode = collider.GetComponent<TrackNode>();
       if (trackNode != null)
       {
           // ... yield discovered object
       }
   }
   ```

3. **Reduce Priority calculation** - Keep math simple
   ```csharp
   Priority = 1.0f - (distance / searchRadius);  // Simple and fast
   ```

4. **Filter early** - Skip objects before creating the wrapper
   ```csharp
   if (trackNode == null) continue;
   if (!ShouldDiscover(trackNode)) continue;  // Check before yield
   yield return new DiscoveredOverlayObject { ... };
   ```

## Integration with Handlers

The existing `TrackNodeOverlayHandler` handles rendering. Your strategy just needs to find them:

```csharp
yield return new DiscoveredOverlayObject
{
    Entity = trackNode,  // Handler expects TrackNode
    HasPendingEdits = true,
    PreviewData = fuseNode,  // Handler expects FuseNode
    ObjectId = $"tn_{trackNode.GetInstanceID()}",
    Priority = 1.5f,  // Pending edits = higher priority
};
```

The manager automatically calls the handler to render it.

## Troubleshooting

### Issue: TrackNodes not appearing

**Checklist:**
- [ ] Strategy is registered? `manager.RegisterDiscoveryStrategy(strategy)`
- [ ] Discovery is enabled? `manager.EnableDiscovery()`
- [ ] `DiscoverObjects()` is returning objects? Debug log in the method
- [ ] ObjectId is unique? Each tracknode should have different ID
- [ ] TrackNodes exist in scene? Use `Object.FindObjectsOfType<TrackNode>()`

### Issue: Only some TrackNodes appear

**Checklist:**
- [ ] Filtering logic correct? Check distance, layer, tag conditions
- [ ] Priority calculation correct? Closer/more important should be > 1.0
- [ ] Max overlay limit? Check `config.MaxOverlayCount`
- [ ] Distance culling? Check `config.MaxDiscoveryDistance`

### Issue: Performance drops when discovering

**Solutions:**
- [ ] Increase `DiscoveryUpdateInterval` to update less frequently
- [ ] Reduce search radius
- [ ] Use Physics queries instead of FindObjectsOfType
- [ ] Cache results where possible
- [ ] Filter objects early

## Complete Example

```csharp
using FUSE.Editor.Overlays.Discovery;
using System;
using System.Collections.Generic;
using Track;
using UnityEngine;

namespace FUSE.Editor.Track.Overlays.Discovery
{
    public class TrackNodeDiscoveryStrategy : IOverlayDiscoveryStrategy
    {
        private readonly Func<Vector3> _getFocalPoint;
        private readonly float _searchRadius;
        private TrackNode[] _cachedNodes;

        public string StrategyName => "TrackNodes";
        public int ExecutionOrder => 20;

        public TrackNodeDiscoveryStrategy(
            Func<Vector3> getFocalPoint = null,
            float searchRadius = 500f)
        {
            _getFocalPoint = getFocalPoint ?? (() => Vector3.zero);
            _searchRadius = searchRadius;
        }

        public IEnumerable<DiscoveredOverlayObject> DiscoverObjects()
        {
            var focalPoint = _getFocalPoint();

            foreach (var trackNode in _cachedNodes ?? System.Array.Empty<TrackNode>())
            {
                if (trackNode == null || trackNode.gameObject == null) continue;

                var distance = Vector3.Distance(
                    trackNode.transform.position, 
                    focalPoint
                );

                if (distance > _searchRadius) continue;

                yield return new DiscoveredOverlayObject
                {
                    Entity = trackNode,
                    HasPendingEdits = false,
                    ObjectId = $"tracknode_{trackNode.GetInstanceID()}",
                    Priority = 1.0f - (distance / _searchRadius),
                    Distance = distance,
                    SourceStrategy = StrategyName
                };
            }
        }

        public void OnEnable()
        {
            _cachedNodes = Object.FindObjectsOfType<TrackNode>();
        }

        public void OnDisable()
        {
            _cachedNodes = null;
        }
    }
}
```

## Next Steps

1. Copy one of the patterns above
2. Adjust to your needs
3. Test with `FuseOverlayManager.Instance.GetDiscoveryMetrics()`
4. Monitor performance and tune as needed
