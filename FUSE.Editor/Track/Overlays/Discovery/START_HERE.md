# 🎯 TrackNodeDiscoveryStrategy - START HERE

Welcome! You have an empty discovery strategy template ready to implement.

## 📍 What You Have

**File**: `FUSE.Editor/Track/Overlays/Discovery/TrackNodeDiscoveryStrategy.cs`

An empty strategy that you'll implement to discover TrackNode objects for overlay rendering.

## 🚀 5-Minute Quick Start

### 1. Open the template file
`FUSE.Editor/Track/Overlays/Discovery/TrackNodeDiscoveryStrategy.cs`

### 2. Find the TODO section
```csharp
public IEnumerable<DiscoveredOverlayObject> DiscoverObjects()
{
    // TODO: Implement your discovery logic
    yield break;  // ← Replace this
}
```

### 3. Pick a simple pattern

**Simplest - All TrackNodes:**
```csharp
foreach (var node in Object.FindObjectsOfType<TrackNode>())
    if (node != null)
        yield return new DiscoveredOverlayObject
        {
            Entity = node,
            ObjectId = $"tn_{node.GetInstanceID()}",
            Priority = 1.0f,
        };
```

**With Distance:**
```csharp
var focalPoint = Camera.main.transform.position;
foreach (var node in Object.FindObjectsOfType<TrackNode>())
{
    if (node == null) continue;
    var distance = Vector3.Distance(node.transform.position, focalPoint);
    if (distance > 500f) continue;

    yield return new DiscoveredOverlayObject
    {
        Entity = node,
        ObjectId = $"tn_{node.GetInstanceID()}",
        Priority = 1.0f - (distance / 500f),
        Distance = distance,
    };
}
```

### 4. Replace the code
Replace `yield break;` with one of the patterns above.

### 5. Use it
```csharp
var manager = FuseOverlayManager.Instance;
manager.RegisterDiscoveryStrategy(new TrackNodeDiscoveryStrategy());
manager.EnableDiscovery();
```

### ✅ Done!
TrackNodes are now auto-discovered.

## 📚 Documentation Files

### 🏃 I need this NOW (5 min)
→ Read this file + copy-paste one pattern above

### 🎨 I like visuals (10 min)
→ Open `QUICK_START.md` - has diagrams and flow charts

### 💡 I want examples (15 min)
→ Open `README.md` - has 3 quick examples

### 📖 I want details (30 min)
→ Open `TRACKNODE_STRATEGY_GUIDE.md` - has 5+ patterns + guidance

### 🔍 I want everything (complete)
→ Open `SUMMARY.md` - full technical reference

## 🎯 What Goes in DiscoverObjects()?

The method should:
1. **Find TrackNodes** - Use whatever logic you need
2. **For each valid node**, yield a `DiscoveredOverlayObject` with:
   - `Entity` = the TrackNode
   - `ObjectId` = unique string identifier
   - `Priority` = 0-1 (higher = render first)
   - Other optional fields

That's it!

## 💻 Bare Minimum Implementation

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

This finds all TrackNodes in the scene. That's a complete, working implementation!

## 🎓 Decision Guide

**What TrackNodes should I discover?**

```
├─ All TrackNodes in scene?
│  → Use bare minimum above ✓
│
├─ Only nearby ones (e.g., 500 units from camera)?
│  → Add distance check
│  → See TRACKNODE_STRATEGY_GUIDE.md "Nearby TrackNodes" pattern
│
├─ Only ones with pending edits?
│  → Check for pending data
│  → Set HasPendingEdits = true
│  → See TRACKNODE_STRATEGY_GUIDE.md "With Pending Edits" pattern
│
└─ Specific ones (by layer, tag, property)?
   → Add filtering logic
   → See TRACKNODE_STRATEGY_GUIDE.md "By Layer or Tag" pattern
```

## 🔧 Setup (When You're Done Implementing)

```csharp
// Create your strategy
var strategy = new TrackNodeDiscoveryStrategy();

// Register it with the overlay manager
var manager = FuseOverlayManager.Instance;
manager.RegisterDiscoveryStrategy(strategy);

// Enable discovery
manager.EnableDiscovery();

// Check it's working
var metrics = manager.GetDiscoveryMetrics();
Debug.Log($"Found {metrics.ObjectsDiscovered} TrackNodes!");
```

## 🎨 Understanding DiscoveredOverlayObject

```csharp
new DiscoveredOverlayObject
{
    // MUST SET:
    Entity = trackNode,                        // Your TrackNode
    ObjectId = $"tn_{trackNode.GetInstanceID()}", // Must be unique
    Priority = 1.0f,                          // 1.0 = highest priority
    Distance = distanceFromCamera,            // For sorting/culling

    // OPTIONAL:
    HasPendingEdits = false,                  // Any unsaved changes?
    PreviewData = null,                       // If pending edits, data here
    SourceStrategy = StrategyName              // Tracks which strategy found it
}
```

**Priority Guide:**
- `1.5f` = Very important (render first, highest detail)
- `1.0f` = Normal (default)
- `0.5f` = Less important (might use LOD)
- `0.1f` = Low (might be culled if limit hit)

## 📊 Example: Distance-Based Priority

```csharp
var focalPoint = Camera.main.transform.position;
var distance = Vector3.Distance(node.transform.position, focalPoint);

// Closer = higher priority (closer objects rendered first)
var priority = 1.0f - (distance / 500f);  // At 500 units away = 0.0

yield return new DiscoveredOverlayObject
{
    Entity = node,
    ObjectId = $"tn_{node.GetInstanceID()}",
    Priority = priority,
    Distance = distance,
};
```

## ✅ Completion Checklist

- [ ] Open `TrackNodeDiscoveryStrategy.cs`
- [ ] Replace `yield break;` with your logic
- [ ] Verify it compiles (should show no errors)
- [ ] Create strategy instance
- [ ] Register with manager
- [ ] Enable discovery
- [ ] Verify TrackNodes appear (check metrics)

## 🆘 Need Help?

| Question | Answer |
|----------|--------|
| Where's the template? | `FUSE.Editor/Track/Overlays/Discovery/TrackNodeDiscoveryStrategy.cs` |
| What do I implement? | The `DiscoverObjects()` method |
| Got examples? | Yes, see 3 quick ones above |
| More examples? | Yes, 5+ patterns in `TRACKNODE_STRATEGY_GUIDE.md` |
| How do I use it? | Register with manager, enable discovery |
| Is it working? | Use `GetDiscoveryMetrics()` to check |
| How do I optimize? | See performance tips in guide |

## 🚀 You're Ready!

Everything is set up. Just implement `DiscoverObjects()` and you're done!

**Next step**: Pick one of the patterns above and copy it into the template. Done! ✅

---

Questions? See the full guide: `TRACKNODE_STRATEGY_GUIDE.md`
