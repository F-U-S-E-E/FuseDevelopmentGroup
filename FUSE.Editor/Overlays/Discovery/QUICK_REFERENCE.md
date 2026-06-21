# Overlay Discovery System - Quick Reference

## One-Minute Setup

```csharp
var manager = FuseOverlayManager.Instance;

// Configure
var config = new OverlayDiscoveryCullingConfig
{
    DiscoveryUpdateInterval = 1.0f,
    MaxOverlayCount = 100,
    MaxDiscoveryDistance = 500f,
    EnableFrustumCulling = true,
};
manager.ConfigureDiscoveryCulling(config);

// Register strategies
manager.RegisterDiscoveryStrategy(
    new NearbyGameObjectDiscoveryStrategy(
        () => Camera.main.transform.position,
        searchRadius: 500f
    )
);

// Enable
manager.EnableDiscovery();
```

## Key Classes

| Class | Purpose |
|-------|---------|
| `IOverlayDiscoveryStrategy` | Interface for custom discovery logic |
| `OverlayDiscoverySystem` | Main orchestrator (throttling, culling, LOD) |
| `OverlayDiscoveryCullingConfig` | Configuration (update interval, max overlays, etc) |
| `DiscoveredOverlayObject` | Entity wrapper (GameObject or Component) |
| `DiscoveryPerformanceMetrics` | Performance data |

## Built-in Strategies

```csharp
// Find nearby GameObjects (colliders)
new NearbyGameObjectDiscoveryStrategy(
    () => Camera.main.transform.position,
    searchRadius: 500f
);

// Find nearby components
new NearbyComponentDiscoveryStrategy<Renderer>(
    () => Camera.main.transform.position,
    searchRadius: 500f
);

// Find by tag
new TagBasedDiscoveryStrategy(
    "EditorSelectable",
    () => Camera.main.transform.position,
    maxDistance: 500f
);

// Find by layer
new LayerBasedDiscoveryStrategy(
    LayerMask.GetMask("Interactive"),
    () => Camera.main.transform.position,
    searchRadius: 500f
);
```

## Manager API

```csharp
// Discovery control
manager.EnableDiscovery();
manager.DisableDiscovery();
manager.IsDiscoveryEnabled

// Strategy management
manager.RegisterDiscoveryStrategy(strategy);
manager.UnregisterDiscoveryStrategy("StrategyName");

// Configuration
manager.ConfigureDiscoveryCulling(config);
manager.GetDiscoveryCullingConfig();

// Metrics
var metrics = manager.GetDiscoveryMetrics();
Console.WriteLine($"{metrics.ObjectsDiscovered} objects");
Console.WriteLine($"{metrics.LastDiscoveryTime * 1000}ms");

// Manual update
manager.ManuallyUpdateDiscovery();

// Direct access
var system = manager.DiscoverySystem;
```

## Critical Settings

| Setting | Impact | Typical Value |
|---------|--------|---------------|
| `DiscoveryUpdateInterval` | CPU load vs responsiveness | 0.5 - 2.0 seconds |
| `MaxOverlayCount` | Max overlays rendered | 50 - 200 |
| `MaxDiscoveryDistance` | Search radius | 200 - 1000 units |
| `EnableFrustumCulling` | Skip off-screen overlays | true |
| `LODPriorityThreshold` | 0 = disabled, >0 = enabled | 0.5 - 1.5 |

## Custom Strategy Template

```csharp
public class MyDiscoveryStrategy : IOverlayDiscoveryStrategy
{
    public string StrategyName => "MyStrategy";
    public int ExecutionOrder => 50;

    public IEnumerable<DiscoveredOverlayObject> DiscoverObjects()
    {
        foreach (var obj in FindMyObjects())
        {
            yield return new DiscoveredOverlayObject
            {
                Entity = obj,
                HasPendingEdits = IsPending(obj),
                PreviewData = GetPreviewData(obj),
                ObjectId = $"MY_{obj.id}",
                Priority = CalculatePriority(obj),
                Distance = CalculateDistance(obj),
            };
        }
    }

    public void OnEnable() { }
    public void OnDisable() { }
}

manager.RegisterDiscoveryStrategy(new MyDiscoveryStrategy());
```

## Performance Tips

1. **Increase update interval** → Lower CPU, less responsive
   ```csharp
   config.DiscoveryUpdateInterval = 2.0f;
   ```

2. **Reduce max overlays** → Fewer to render
   ```csharp
   config.MaxOverlayCount = 50;
   ```

3. **Enable frustum culling** → Skip off-screen
   ```csharp
   config.EnableFrustumCulling = true;
   ```

4. **Enable LOD** → Lower detail for distant objects
   ```csharp
   config.LODPriorityThreshold = 1.0f;
   ```

5. **Use layer filtering** → Fewer objects to check
   ```csharp
   new NearbyGameObjectDiscoveryStrategy(
       () => Camera.main.transform.position,
       layerMask: LayerMask.GetMask("Interactive")
   );
   ```

## Metrics Breakdown

```csharp
var metrics = manager.GetDiscoveryMetrics();

// Timing
metrics.LastDiscoveryTime        // Time spent in discovery (seconds)
metrics.UpdateInterval           // Configured interval
metrics.ThrottledUntilNext       // Seconds until next update

// Counts
metrics.ObjectsDiscovered        // Found in last update
metrics.StrategiesActive         // Registered strategies
metrics.PreviewsTracked          // Overlays tracking staleness
```

## Common Issues

| Issue | Solution |
|-------|----------|
| Overlays too late | `↓ DiscoveryUpdateInterval` |
| Too many overlays | `↓ MaxOverlayCount` or `↓ MaxDiscoveryDistance` |
| Poor performance | `↑ UpdateInterval` or enable LOD |
| Overlays not found | Check strategy registration, search radius, colliders |
| Stuck overlays | Check `MaxPreviewStaleness > 0` |

## Integration with Handlers

Objects with pending edits automatically use the handler system:

```csharp
yield return new DiscoveredOverlayObject
{
    Entity = entity,
    HasPendingEdits = true,        // ← Uses handler
    PreviewData = pendingData,
    ObjectId = "ID",
};

// Manager automatically calls:
// handler.ApplyPreview(entity, pendingData);
```

Selection-only overlays use simple rendering:

```csharp
yield return new DiscoveredOverlayObject
{
    Entity = gameObject,
    HasPendingEdits = false,       // ← Simple overlay
    PreviewData = null,
    ObjectId = "ID",
};
```
