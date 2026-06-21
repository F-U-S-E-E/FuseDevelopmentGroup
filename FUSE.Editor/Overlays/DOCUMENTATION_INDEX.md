# Overlay System - Dual-Type Handler Documentation Index

## 📋 Start Here

**New to dual-type handlers?** Start with these in order:
1. **`IMPLEMENTATION_BRIEF.md`** ← You are here (5 min read)
2. **`DUAL_TYPE_QUICK_REFERENCE.md`** (10 min read - Code examples)
3. **`DUAL_TYPE_HANDLER_MIGRATION.md`** (15 min read - Detailed guide)

## 📚 Complete Documentation

### For Learning the Architecture
- **`DUAL_TYPE_IMPLEMENTATION_SUMMARY.md`** (20 min)
  - Full architecture breakdown
  - Data flow diagrams
  - Design decisions explained
  - Comprehensive handler examples

- **`COMPLETION_REPORT.md`** (15 min)
  - What was changed
  - Build validation
  - File modifications summary
  - Troubleshooting guide

### For Quick Reference While Coding
- **`DUAL_TYPE_QUICK_REFERENCE.md`** 
  - API cheat sheet
  - Common patterns
  - Handler template
  - Parameter reference table

### For Migration Work
- **`DUAL_TYPE_HANDLER_MIGRATION.md`**
  - Before/after patterns
  - Migration checklist
  - Backward compatibility info
  - Troubleshooting FAQ

## 📁 Code Files

### New Files (Dual-Type Support)
```
FUSE.Editor/Overlays/
├── IOverlayHandler2.cs              ← New dual-type interface
├── OverlayHandlerRegistry2.cs       ← New dual-type registry
└── Examples/
    └── (Example files for reference)
```

### Updated Files (For Dual-Type Integration)
```
FUSE.Editor/Overlays/
├── OverlayPreviewData.cs            ← Now stores entity + preview data
├── OverlayHandlerRegistry.cs        ← Updated to accept fuseData
├── FuseOverlayRenderer.cs           ← Updated preview creation
├── FuseOverlayManager.cs            ← Propagates changes
└── Track/Overlays/
    ├── TrackNodeOverlayExample.cs   ← Uses FuseNode data
    └── TrackNodeGizmoOverlayIntegration.cs ← Uses FuseNode data
```

### Existing Files (Unchanged)
```
FUSE.Editor/Overlays/
├── IOverlayHandler.cs               ← Old interface (still works)
├── IOverlayRenderable.cs            ← Still used
├── OverlaySelectionArea.cs          ← Still used
├── OverlaySelectionSystem.cs        ← Still used
├── Track/Overlays/
│   ├── TrackNodeOverlayAdapter.cs   ← Still used
│   └── TrackNodeOverlayHandler.cs   ← Old single-type handler
└── README.md, START_HERE.md, etc.   ← Original docs
```

## 🎯 Documentation by Use Case

### "I want to understand what changed"
→ Read: `COMPLETION_REPORT.md`
→ Section: "What Was Accomplished"

### "I need to create a new entity type overlay"
→ Read: `DUAL_TYPE_QUICK_REFERENCE.md`
→ Section: "Checklist: Setting Up a New Entity Type"

### "I'm migrating existing code"
→ Read: `DUAL_TYPE_HANDLER_MIGRATION.md`
→ Section: "Migration Checklist"

### "I need code examples"
→ Read: `DUAL_TYPE_IMPLEMENTATION_SUMMARY.md`
→ Section: "Example: TrackNode Handler Migration"

### "I need API reference"
→ Read: `DUAL_TYPE_QUICK_REFERENCE.md`
→ Section: "API Reference"

### "Something isn't working"
→ Read: `DUAL_TYPE_QUICK_REFERENCE.md`
→ Section: "Troubleshooting"

## 🔑 Key Concepts

### Entity vs. Preview Data
- **Entity** (e.g., `TrackNode`) - The original game object
  - Read-only in handlers
  - Determines: renderable type, base color decisions, object context

- **Preview Data** (e.g., `FuseNode`) - Pending edits
  - Created and maintained by your code
  - Contains: pending position, rotation, other edit values
  - Passed to handler for visualization

### Handler Responsibilities
A dual-type handler `IOverlayHandler<TrackNode, FuseNode>` must provide:
1. **Entity validation** - `CanHandle(TrackNode)`
2. **ID extraction** - `GetEntityId(TrackNode)` 
3. **Transform extraction** - Read from `FuseNode`
4. **Rendering** - Choose mesh/material based on `TrackNode` type
5. **Color** - Determine from both `TrackNode` and `FuseNode`
6. **Selection** - Define clickable areas + handle clicks

### Registration Pattern
```csharp
// Register handler with BOTH type parameters
HandlerRegistry2.RegisterHandler<TrackNode, FuseNode>(handler);

// Create and register preview with BOTH objects
RegisterPreview(id, gameObject, fuseNode);

// Handler automatically dispatches to correct methods
```

## 📊 File Organization

```
FUSE.Editor/
└── Overlays/
    ├── Core System
    │   ├── FuseOverlayManager.cs
    │   ├── FuseOverlayRenderer.cs
    │   └── OverlayPreviewData.cs
    │
    ├── Handlers (Old)
    │   ├── IOverlayHandler.cs
    │   ├── OverlayHandlerRegistry.cs
    │   └── IOverlayRenderable.cs
    │
    ├── Handlers (New - Dual-Type)
    │   ├── IOverlayHandler2.cs
    │   └── OverlayHandlerRegistry2.cs
    │
    ├── Selection System
    │   ├── OverlaySelectionArea.cs
    │   ├── OverlaySelectionSystem.cs
    │   └── SELECTION_SYSTEM.md
    │
    ├── Track Integration
    │   └── Track/Overlays/
    │       ├── TrackNodeOverlayHandler.cs (old)
    │       ├── TrackNodeOverlayAdapter.cs
    │       ├── TrackNodeOverlayExample.cs
    │       └── TrackNodeGizmoOverlayIntegration.cs
    │
    └── Documentation
        ├── IMPLEMENTATION_BRIEF.md ← START HERE
        ├── DUAL_TYPE_QUICK_REFERENCE.md
        ├── DUAL_TYPE_HANDLER_MIGRATION.md
        ├── DUAL_TYPE_IMPLEMENTATION_SUMMARY.md
        ├── COMPLETION_REPORT.md
        ├── README.md (original overlay docs)
        ├── START_HERE.md (original overlay docs)
        └── FEATURE_SUMMARY.md (original overlay docs)
```

## ✅ Build Status

**Current Build:** ✅ **SUCCESSFUL**
- 0 errors
- 0 warnings
- All new code integrated
- Backward compatible

## 🚀 Next Steps

1. **Understand the architecture**
   - Read `DUAL_TYPE_QUICK_REFERENCE.md` section on "Key Points"

2. **Create a concrete handler**
   - Follow template in `DUAL_TYPE_QUICK_REFERENCE.md`
   - Implement for `TrackNode` and `FuseNode`

3. **Test at runtime**
   - Register handler at editor startup
   - Create preview with both entity and preview data
   - Verify overlay renders at correct position
   - Test selection works

4. **Extend to other types**
   - Repeat process for `Building`, `Route`, etc.
   - Each gets its own preview data type

5. **Validate and optimize**
   - Performance test with many simultaneous previews
   - Finalize any edge cases

## 📞 Quick Lookup

| Question | Answer Document | Section |
|----------|-----------------|---------|
| How do I create a handler? | QUICK_REFERENCE | "Handler Implementation" |
| What changed from old API? | MIGRATION | "Key Changes" |
| How do I register a handler? | QUICK_REFERENCE | "Registration" |
| Where do I read the transform from? | MIGRATION | "Extract from preview data" |
| What files were modified? | COMPLETION_REPORT | "Files Changed" |
| Something's not working | QUICK_REFERENCE | "Troubleshooting" |
| I need code examples | IMPLEMENTATION_SUMMARY | "Handler Implementation Example" |
| What's the architecture? | IMPLEMENTATION_SUMMARY | "Architecture Pattern" |

## 🎓 Recommended Reading Order

### For Developers New to This System
1. `IMPLEMENTATION_BRIEF.md` (5 min)
2. `DUAL_TYPE_QUICK_REFERENCE.md` (10 min)
3. Code exploration (reading IOverlayHandler2.cs)
4. `DUAL_TYPE_HANDLER_MIGRATION.md` (15 min)

### For Code Review
1. `COMPLETION_REPORT.md` (15 min)
2. `DUAL_TYPE_IMPLEMENTATION_SUMMARY.md` (20 min)
3. File diffs (reading actual changes)

### For Integration
1. `DUAL_TYPE_QUICK_REFERENCE.md` sections: "Registration" and "Creating a Preview"
2. Code templates for your entity types
3. `DUAL_TYPE_HANDLER_MIGRATION.md` checklist

### For Troubleshooting
1. `DUAL_TYPE_QUICK_REFERENCE.md` section: "Troubleshooting"
2. `DUAL_TYPE_HANDLER_MIGRATION.md` section: "Troubleshooting"
3. `COMPLETION_REPORT.md` section: "Troubleshooting Guide"

---

**Status:** ✅ Implementation Complete

All documentation is current and validated against the working code.
