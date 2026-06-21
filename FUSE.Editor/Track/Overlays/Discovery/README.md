# TrackNodeDiscoveryStrategy - Summary

## What You Got

An empty, well-documented template for discovering TrackNode objects:

**File**: `FUSE.Editor/Track/Overlays/Discovery/TrackNodeDiscoveryStrategy.cs`

```csharp
public class TrackNodeDiscoveryStrategy : IOverlayDiscoveryStrategy
{
    public string StrategyName => "TrackNodes";
    public int ExecutionOrder => 20;

    public IEnumerable<DiscoveredOverlayObject> DiscoverObjects()
    {
        // TODO: Implement your discovery logic
        yield break;  // Replace with your implementation
    }

    public void OnEnable() { }
    public void OnDisable() { }
}
```

## Quick Implementation (Pick One)

### Option 1: All TrackNodes in Scene (1 minute)
```csharp
public IEnumerable<DiscoveredOverlayObject> DiscoverObjects()
{
    foreach (var node in Object.FindObjectsOfType<TrackNode>())
    {
        if (node != null)
            yield return new DiscoveredOverlayObject
            {
                Entity = node,
                ObjectId = $"tn_{node.GetInstanceID()}",
                Priority = 1.0f,
            };
    }
}
```

### Option 2: Nearby TrackNodes with Distance Priority (2 minutes)
```csharp
private Func<Vector3> _getFocalPoint;

public TrackNodeDiscoveryStrategy(Func<Vector3> getFocalPoint)
{
    _getFocalPoint = getFocalPoint;
}

public IEnumerable<DiscoveredOverlayObject> DiscoverObjects()
{
    var focalPoint = _getFocalPoint();
    const float radius = 500f;

    foreach (var node in Object.FindObjectsOfType<TrackNode>())
    {
        if (node == null) continue;
        var dist = Vector3.Distance(node.transform.position, focalPoint);
        if (dist > radius) continue;

        yield return new DiscoveredOverlayObject
        {
            Entity = node,
            ObjectId = $"tn_{node.GetInstanceID()}",
            Priority = 1.0f - (dist / radius),
            Distance = dist,
        };
    }
}
```

### Option 3: With Pending Edits Support (3 minutes)
```csharp
public IEnumerable<DiscoveredOverlayObject> DiscoverObjects()
{
    foreach (var node in Object.FindObjectsOfType<TrackNode>())
    {
        if (node == null) continue;

        var pendingData = GetPendingData(node);  // Your logic

        yield return new DiscoveredOverlayObject
        {
            Entity = node,
            HasPendingEdits = pendingData != null,
            PreviewData = pendingData,
            ObjectId = $"tn_{node.GetInstanceID()}",
            Priority = pendingData != null ? 1.5f : 1.0f,
        };
    }
}

private FuseNode GetPendingData(TrackNode node)
{
    // TODO: Your logic to find pending edits
    return null;
}
```

## Documentation

Comprehensive guide available in:
- **TRACKNODE_STRATEGY_GUIDE.md** - 200+ lines with examples, patterns, tips
- **In-code comments** - Detailed TODOs and examples in the template file

## How to Use

```csharp
// 1. Implement your discovery logic in DiscoverObjects()
// See TRACKNODE_STRATEGY_GUIDE.md for examples

// 2. Create instance
var strategy = new TrackNodeDiscoveryStrategy();

// 3. Register with manager
var manager = FuseOverlayManager.Instance;
manager.RegisterDiscoveryStrategy(strategy);
manager.EnableDiscovery();

// ✅ Done! TrackNodes are now auto-discovered
```

## Key Points

✅ **Empty template** - You decide what TrackNodes to discover  
✅ **Fully documented** - Examples and patterns provided  
✅ **Integration ready** - Works with existing TrackNodeOverlayHandler  
✅ **Performance optimized** - Caching tips included  
✅ **Flexible** - Find all, find nearby, find by property, find by category, etc.  

## Common Questions

**Q: What do I need to implement?**  
A: The `DiscoverObjects()` method. Find TrackNodes and yield them as `DiscoveredOverlayObject`.

**Q: Where do I find patterns?**  
A: See **TRACKNODE_STRATEGY_GUIDE.md** for 5+ example patterns.

**Q: How do I make it find nearby nodes?**  
A: Use the constructor to accept focal point (usually camera), then filter by distance.

**Q: How do I integrate with pending edits?**  
A: Set `HasPendingEdits = true` and `PreviewData = fuseNode` when appropriate.

**Q: What about performance?**  
A: See performance tips in TRACKNODE_STRATEGY_GUIDE.md - caching, physics queries, filtering.

## Files Created

```
FUSE.Editor/Track/Overlays/Discovery/
├── TrackNodeDiscoveryStrategy.cs      ← Template (implement this)
└── TRACKNODE_STRATEGY_GUIDE.md        ← 200+ lines of guidance
```

## Next Steps

1. **Read** the guide: `TRACKNODE_STRATEGY_GUIDE.md`
2. **Choose** a pattern that fits your use case
3. **Implement** `DiscoverObjects()` 
4. **Register** with the manager
5. **Test** with metrics: `manager.GetDiscoveryMetrics()`

---

**Status**: ✅ Ready for implementation
