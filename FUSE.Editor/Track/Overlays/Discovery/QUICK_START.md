# TrackNodeDiscoveryStrategy - Visual Quick Start

## 📁 Files Created

```
FUSE.Editor/Track/Overlays/Discovery/
├── 📄 TrackNodeDiscoveryStrategy.cs    (Empty template - YOU implement logic)
├── 📄 TRACKNODE_STRATEGY_GUIDE.md      (200+ lines of examples & patterns)
└── 📄 README.md                         (This file + summary)
```

## 🎯 What You Need to Do

### Step 1: Open the template
`FUSE.Editor/Track/Overlays/Discovery/TrackNodeDiscoveryStrategy.cs`

### Step 2: Find the TODO
```csharp
public IEnumerable<DiscoveredOverlayObject> DiscoverObjects()
{
    // TODO: Implement TrackNode discovery logic
    yield break;  // ← Replace this
}
```

### Step 3: Implement your logic
Choose a pattern from **TRACKNODE_STRATEGY_GUIDE.md** and replace the `yield break;`

**Example** (all TrackNodes):
```csharp
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
```

## 🔧 Setup Instructions

```csharp
// In your editor initialization:
var manager = FuseOverlayManager.Instance;

// Create your strategy
var strategy = new TrackNodeDiscoveryStrategy();

// Register it
manager.RegisterDiscoveryStrategy(strategy);

// Enable
manager.EnableDiscovery();

// ✅ Done!
```

## 📊 Discovery Flow

```
Your Implementation
        │
        ▼
DiscoverObjects()
 Loop through TrackNodes
 For each valid node:
   Create DiscoveredOverlayObject
   yield return it
        │
        ▼
Discovery System
 Receives objects
 Applies culling (distance, count, frustum)
 Sorts by priority
 Removes stale entries
        │
        ▼
Creates Overlays
 Uses TrackNodeOverlayHandler
 Renders on screen
        │
        ▼
Editor Display ✅
```

## 💡 Key Concepts

### DiscoveredOverlayObject Fields

```csharp
new DiscoveredOverlayObject
{
    Entity = trackNode,              // The TrackNode to discover
    HasPendingEdits = false,         // Does it have unsaved changes?
    PreviewData = null,              // FuseNode (if pending edits)
    ObjectId = "tn_12345",           // MUST be unique per object
    Priority = 1.0f,                 // 0-1: Higher = render first
    Distance = 50.5f,                // Distance from focal point
    SourceStrategy = StrategyName     // Track which strategy found it
}
```

### Priority Explained

```csharp
Priority = 1.5f  // Highest - render first, full detail
Priority = 1.0f  // Normal - render in order
Priority = 0.5f  // Low - might render with LOD
Priority = 0.1f  // Very low - might be culled if limit hit
```

### Distance Explained

```csharp
Distance = 0f    // At focal point (usually camera)
Distance = 100f  // 100 units away
Distance = 500f  // 500 units away (far)
```

## 🎨 Pattern Decision Tree

```
What TrackNodes do you want to discover?

├─ All in scene?
│  └─ Use: "All TrackNodes in Scene" pattern
│     See: TRACKNODE_STRATEGY_GUIDE.md, Pattern 1
│
├─ Nearby ones (radius-based)?
│  └─ Use: "Distance-Based" pattern
│     See: TRACKNODE_STRATEGY_GUIDE.md, Pattern 2
│
├─ Ones with pending edits?
│  └─ Use: "With Pending Edits" pattern
│     See: TRACKNODE_STRATEGY_GUIDE.md, Pattern 3
│
├─ Ones in a specific layer/tag?
│  └─ Use: "By Layer or Tag" pattern
│     See: TRACKNODE_STRATEGY_GUIDE.md, Pattern 4
│
└─ Ones in a specific category?
   └─ Use: "By Category" pattern
      See: TRACKNODE_STRATEGY_GUIDE.md, Pattern 3
```

## 📝 Skeleton to Copy-Paste

```csharp
public IEnumerable<DiscoveredOverlayObject> DiscoverObjects()
{
    // Get focal point if needed
    var focalPoint = /* camera position or similar */;

    // Get TrackNodes (your logic here)
    var trackNodes = GetTrackNodesToDiscover();

    // For each valid node, yield it
    foreach (var trackNode in trackNodes)
    {
        if (trackNode == null) continue;  // Skip invalid nodes

        // Calculate distance if needed
        var distance = Vector3.Distance(
            trackNode.transform.position, 
            focalPoint
        );

        // Calculate priority (closer = higher)
        var priority = 1.0f;  // Your calculation

        // Yield the discovered object
        yield return new DiscoveredOverlayObject
        {
            Entity = trackNode,
            HasPendingEdits = false,           // Your logic
            PreviewData = null,                // Your logic
            ObjectId = $"tn_{trackNode.GetInstanceID()}",
            Priority = priority,
            Distance = distance,
            SourceStrategy = StrategyName
        };
    }
}
```

## 🚀 Implementation Checklist

- [ ] Open `TrackNodeDiscoveryStrategy.cs`
- [ ] Replace `yield break;` with your logic
- [ ] Test: Create instance and register with manager
- [ ] Verify: Use `GetDiscoveryMetrics()` to check objects found
- [ ] Monitor: Check performance metrics
- [ ] Optimize: Adjust update interval if needed

## ⚡ Performance Tips

| Situation | Solution |
|-----------|----------|
| Too slow | Increase update interval: `config.DiscoveryUpdateInterval = 2.0f` |
| Too many overlays | Reduce count: `config.MaxOverlayCount = 50` |
| Missing distant nodes | Increase distance: `config.MaxDiscoveryDistance = 1000f` |
| Lots of clutter | Enable frustum: `config.EnableFrustumCulling = true` |

## 🎓 Example Implementations

### Simplest (1 minute)
```csharp
public IEnumerable<DiscoveredOverlayObject> DiscoverObjects()
{
    foreach (var n in Object.FindObjectsOfType<TrackNode>())
        if (n != null)
            yield return new DiscoveredOverlayObject
            {
                Entity = n,
                ObjectId = $"tn_{n.GetInstanceID()}",
                Priority = 1.0f
            };
}
```

### With Distance (2 minutes)
```csharp
public IEnumerable<DiscoveredOverlayObject> DiscoverObjects()
{
    var fp = Camera.main.transform.position;
    foreach (var n in Object.FindObjectsOfType<TrackNode>())
    {
        if (n == null) continue;
        var d = Vector3.Distance(n.transform.position, fp);
        if (d > 500) continue;
        yield return new DiscoveredOverlayObject
        {
            Entity = n,
            ObjectId = $"tn_{n.GetInstanceID()}",
            Priority = 1.0f - (d / 500f),
            Distance = d
        };
    }
}
```

### Production Ready (3-5 minutes)
See `TRACKNODE_STRATEGY_GUIDE.md` "Complete Example" section

## 📖 Where to Find Guidance

| Need | File | Lines |
|------|------|-------|
| Examples | TRACKNODE_STRATEGY_GUIDE.md | Patterns 1-5 |
| Performance | TRACKNODE_STRATEGY_GUIDE.md | Performance Tips |
| Troubleshooting | TRACKNODE_STRATEGY_GUIDE.md | Troubleshooting |
| Full guide | TRACKNODE_STRATEGY_GUIDE.md | Complete (200+ lines) |
| Quick summary | README.md | Quick ref |
| Visual guide | QUICK_START.md | This file |

## ✅ Success Criteria

- [x] File created and compiles ✓
- [x] Comments explain what to do ✓
- [x] Multiple patterns available ✓
- [ ] You implement `DiscoverObjects()` ← Your turn!
- [ ] You register with manager ← Your turn!
- [ ] TrackNodes appear as overlays ← You'll see this!

## 🆘 Stuck?

1. **Can't find patterns?** → See `TRACKNODE_STRATEGY_GUIDE.md`
2. **Don't understand the structure?** → Compare with `NearbyGameObjectDiscoveryStrategy`
3. **Want a complete example?** → Search for "Complete Example" in the guide
4. **Performance issues?** → See performance tips above or in guide

## 🎉 You're Ready!

Your empty template is ready to go. Choose a pattern, implement it, and you're done!

---

**Next**: Open `TRACKNODE_STRATEGY_GUIDE.md` and pick a pattern! 🚀
