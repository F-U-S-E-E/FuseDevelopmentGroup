# Overlay Selection System - Quick Navigation Guide

## 📋 Start Here Based on Your Goal

### 🚀 "I want to get started immediately"
**→ Read**: `QUICK_REFERENCE.md` + `SELECTION_INTEGRATION_GUIDE.md`

**Time**: 10 minutes  
**Outcome**: You'll have working code

```csharp
// Setup (copy this)
FuseOverlayManager.Instance.SetSelectionCamera(camera);

// In input handler
FuseOverlayManager.Instance.TrySelectPreviewAtMouse(mousePosition);

// In handler
public void OnPreviewSelected(MyEntity entity, OverlaySelectionArea area)
{
    // Handle selection here
}
```

---

### 📚 "I want to understand the system"
**→ Read in order**:
1. `FEATURE_SUMMARY.md` - 5-minute overview
2. `SELECTION_SYSTEM.md` - Detailed concepts
3. `ARCHITECTURE_DIAGRAMS.md` - Visual reference

**Time**: 20 minutes  
**Outcome**: Deep understanding of architecture

---

### 🔧 "I want to implement handlers"
**→ Read**:
1. `SELECTION_INTEGRATION_GUIDE.md` - Integration patterns
2. `TrackNodeOverlayHandler.cs` - Working example
3. `QUICK_REFERENCE.md` - API cheat sheet

**Time**: 15 minutes  
**Outcome**: Ready to write your handler

---

### 🐛 "I'm debugging an issue"
**→ Check**:
1. `SELECTION_INTEGRATION_GUIDE.md` → Troubleshooting section
2. `SELECTION_SYSTEM.md` → Troubleshooting section
3. `ARCHITECTURE_DIAGRAMS.md` → Data flow diagrams

**Time**: Variable  
**Outcome**: Problem solved

---

### 🎓 "I want the full story"
**→ Read in order**:
1. `COMPLETION_SUMMARY.md` - What was built
2. `FEATURE_SUMMARY.md` - Overview
3. `SELECTION_SYSTEM.md` - Detailed guide
4. `ARCHITECTURE_DIAGRAMS.md` - Visual deep-dive
5. `SELECTION_INTEGRATION_GUIDE.md` - Practical patterns

**Time**: 45 minutes  
**Outcome**: Expert knowledge

---

## 📁 File Organization

### 🔴 **Core Code** (Read if implementing)
- `OverlaySelectionSystem.cs` - Selection/raycasting logic (400 lines)
- `OverlaySelectionArea.cs` - Clickable region model (100 lines)
- `TrackNodeOverlayHandler.cs` - Working example (90 lines)

### 🟠 **Enhanced Existing** (Reference only)
- `IOverlayHandler.cs` - Added selection methods
- `FuseOverlayRenderer.cs` - Added selection system integration
- `FuseOverlayManager.cs` - Added selection public API
- `OverlayHandlerRegistry.cs` - Added selection dispatch
- `OverlayPreviewData.cs` - Added selection fields

### 🟡 **Quick References**
- `QUICK_REFERENCE.md` - API cheat sheet (2 min read)
- `FEATURE_SUMMARY.md` - Overview (5 min read)
- `COMPLETION_SUMMARY.md` - What was built (3 min read)

### 🟢 **Detailed Guides**
- `SELECTION_SYSTEM.md` - Complete feature guide (10 min read)
- `SELECTION_INTEGRATION_GUIDE.md` - Step-by-step patterns (15 min read)
- `ARCHITECTURE_DIAGRAMS.md` - Visual reference (10 min read)

### 🔵 **Implementation Details**
- `SELECTION_IMPLEMENTATION_SUMMARY.md` - Design decisions (5 min read)
- `README.md` - Main overlay system docs (updated)

---

## ⚡ Quick Command Reference

### Start Selection Support
```csharp
// One-time setup
FuseOverlayManager.Instance.SetSelectionCamera(camera);

// When you see mouse click
FuseOverlayManager.Instance.TrySelectPreviewAtMouse(Event.current.mousePosition);
```

### Create Selectable Preview
```csharp
// In handler
public OverlaySelectionArea[] GetSelectionAreas(...)
{
    return new[] { new OverlaySelectionArea { /* ... */ } };
}

// Handle click
public void OnPreviewSelected(Entity entity, OverlaySelectionArea area)
{
    // Do something
}
```

### Debug Hover State
```csharp
var hoveredArea = FuseOverlayManager.Instance.SelectionSystem.GetHoveredArea();
if (hoveredArea != null) Debug.Log($"Hovering over: {hoveredArea.AreaId}");
```

---

## 🎯 Decision Tree: Which Document?

```
START: "What do I need?"
│
├─ "Quick answer to a question"
│  └─► QUICK_REFERENCE.md
│
├─ "I'm integrating into my tool"
│  ├─ "Show me step-by-step examples"
│  │  └─► SELECTION_INTEGRATION_GUIDE.md
│  ├─ "Show me working code"
│  │  └─► TrackNodeOverlayHandler.cs
│  └─ "API reference"
│     └─► QUICK_REFERENCE.md
│
├─ "I'm implementing a handler"
│  ├─ "How do I implement selection?"
│  │  └─► SELECTION_SYSTEM.md → Handler Implementation sections
│  ├─ "Show me an example"
│  │  └─► TrackNodeOverlayHandler.cs
│  └─ "What's the full API?"
│     └─► QUICK_REFERENCE.md
│
├─ "I'm debugging"
│  ├─ "Selection not working"
│  │  └─► SELECTION_SYSTEM.md → Troubleshooting
│  ├─ "Performance issues"
│  │  └─► SELECTION_SYSTEM.md → Performance Considerations
│  ├─ "Where's the code?"
│  │  └─► ARCHITECTURE_DIAGRAMS.md → Data Flow
│  └─ "Is it even compiled?"
│     └─► run_build in terminal
│
├─ "I want to understand this"
│  ├─ "5-minute overview"
│  │  └─► FEATURE_SUMMARY.md
│  ├─ "10-minute deep dive"
│  │  └─► SELECTION_SYSTEM.md
│  ├─ "Show me visually"
│  │  └─► ARCHITECTURE_DIAGRAMS.md
│  └─ "Everything!"
│     └─► Read all guides in order
│
└─ "What was implemented?"
   └─► COMPLETION_SUMMARY.md
```

---

## 📊 Document Sizing & Time

| Document | Size | Read Time | Purpose |
|----------|------|-----------|---------|
| QUICK_REFERENCE.md | 2KB | 2 min | Fast lookups |
| COMPLETION_SUMMARY.md | 3KB | 3 min | What was built |
| FEATURE_SUMMARY.md | 5KB | 5 min | System overview |
| SELECTION_IMPLEMENTATION_SUMMARY.md | 3KB | 5 min | Design details |
| SELECTION_SYSTEM.md | 4KB | 10 min | Complete guide |
| SELECTION_INTEGRATION_GUIDE.md | 8KB | 15 min | Patterns & examples |
| ARCHITECTURE_DIAGRAMS.md | 6KB | 10 min | Visual reference |
| Updated README.md | +2KB | Part of system | Main overlay docs |

**Total**: ~33KB documentation, ~45 minutes to read everything

---

## 🎬 Five Common Scenarios

### Scenario 1: "I just want it to work"
1. Copy-paste setup from QUICK_REFERENCE.md
2. Implement OnPreviewSelected() in your handler
3. Done in 5 minutes

### Scenario 2: "I need multiple selection areas"
1. Read "Multiple Selection Areas" in SELECTION_SYSTEM.md
2. Look at Workflow 3 in SELECTION_INTEGRATION_GUIDE.md
3. Copy example, adapt for your entity

### Scenario 3: "Selection isn't working"
1. Check SetSelectionCamera() is called → QUICK_REFERENCE.md
2. Check TrySelectPreviewAtMouse() is wired → SELECTION_INTEGRATION_GUIDE.md
3. Check handler returns areas → SELECTION_SYSTEM.md → Troubleshooting

### Scenario 4: "I want hover feedback"
1. Read Event System section in SELECTION_SYSTEM.md
2. Subscribe to OnPreviewHovered event
3. Implement visual feedback in handlers

### Scenario 5: "Tell me everything"
1. Read COMPLETION_SUMMARY.md (3 min)
2. Read ARCHITECTURE_DIAGRAMS.md (10 min)
3. Browse working code TrackNodeOverlayHandler.cs (5 min)
4. Read SELECTION_INTEGRATION_GUIDE.md patterns (15 min)

---

## ✅ Build Status

```
✅ Build: SUCCESSFUL
✅ Errors: 0
✅ Warnings: 0
✅ Ready: NOW
```

All code compiles. All documentation is current. Ready to integrate!

---

## 🔗 Cross-References

**From**: QUICK_REFERENCE.md  
**Use**: For fast API lookups, handler examples

**From**: SELECTION_SYSTEM.md  
**Use**: For feature understanding, troubleshooting, concepts

**From**: SELECTION_INTEGRATION_GUIDE.md  
**Use**: For step-by-step integration, complete examples

**From**: ARCHITECTURE_DIAGRAMS.md  
**Use**: For understanding data flow, visual reference

**From**: TrackNodeOverlayHandler.cs  
**Use**: For working code example, implementation reference

---

## 💡 Pro Tips

1. **Start Small**: Read FEATURE_SUMMARY.md first (5 min)
2. **Copy Examples**: SELECTION_INTEGRATION_GUIDE.md has copy-paste code
3. **Visual Learner?**: Check ARCHITECTURE_DIAGRAMS.md
4. **Need Details?** SELECTION_SYSTEM.md has all the concepts
5. **Stuck?**: Check Troubleshooting in relevant document

---

**Choose your path above and start reading!** 🚀
