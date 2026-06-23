# PaneWorks Technical Design

## 1. Goal

PaneWorks MVP is a Windows desktop layout editor for a single screen canvas.

In the first version, the product only solves one problem:

- Users can recursively split a screen canvas into rectangular regions.
- Users can drag splitters to adjust ratios.
- Users can delete a split and merge regions back.
- Users can save and load reusable layout templates as local JSON files.

The MVP does not yet manage real windows.

## 2. Recommended Stack

### App stack

- Language: `C#`
- Runtime: `.NET 8`
- UI framework: `WPF`
- Pattern: `MVVM`
- Serialization: `System.Text.Json`

### Why WPF first

- Mature for Windows desktop tooling.
- Excellent support for custom drawing, hit testing, and drag interactions.
- Easy to host a canvas-like editor surface.
- Smooth path to later integrate `Win32` window management APIs.
- Lower implementation friction than WinUI for an editor-heavy MVP.

If the long-term goal becomes a more modern shell experience, we can later evaluate WinUI, but for this editor-first MVP WPF is the faster and safer choice.

## 3. Architecture Overview

The app should be split into four layers:

1. `Presentation`
   - Windows, panels, commands, canvas rendering, selection visuals.
2. `Application`
   - Editor use cases such as split, resize, delete split, save, load.
3. `Domain`
   - Layout tree model, validation rules, layout computation.
4. `Infrastructure`
   - JSON storage, file system access, later Win32 integration.

Suggested solution shape:

```text
PaneWorks/
  src/
    PaneWorks.App/
    PaneWorks.Core/
    PaneWorks.Infrastructure/
  docs/
```

Suggested project responsibilities:

- `PaneWorks.App`
  - WPF app entry
  - views and view models
  - input and drag orchestration
- `PaneWorks.Core`
  - layout tree entities
  - editor operations
  - rectangle calculation
  - validation logic
- `PaneWorks.Infrastructure`
  - layout repository
  - JSON file persistence
  - app config paths

## 4. Core Domain Model

The persisted model is a split tree, not a flat rectangle list.

### Layout document

```json
{
  "version": 1,
  "name": "coding-layout",
  "root": {
    "id": "root",
    "type": "split",
    "direction": "vertical",
    "ratio": 0.35,
    "first": {
      "id": "left",
      "type": "leaf"
    },
    "second": {
      "id": "right",
      "type": "split",
      "direction": "horizontal",
      "ratio": 0.5,
      "first": {
        "id": "right-top",
        "type": "leaf"
      },
      "second": {
        "id": "right-bottom",
        "type": "leaf"
      }
    }
  }
}
```

### Domain types

```csharp
public enum SplitDirection
{
    Horizontal,
    Vertical
}

public abstract record LayoutNode(string Id);

public sealed record LeafNode(string Id) : LayoutNode(Id);

public sealed record SplitNode(
    string Id,
    SplitDirection Direction,
    double Ratio,
    LayoutNode First,
    LayoutNode Second) : LayoutNode(Id);

public sealed record LayoutDocument(
    int Version,
    string Name,
    LayoutNode Root);
```

### Runtime-only computed geometry

Persisted JSON should not store absolute rectangles.

At runtime we compute rectangles from:

- current canvas bounds
- split tree
- minimum region size
- splitter thickness

Suggested runtime model:

```csharp
public sealed record ComputedRegion(
    string NodeId,
    Rect Bounds);

public sealed record ComputedSplitter(
    string SplitNodeId,
    Rect Bounds,
    SplitDirection Direction);
```

## 5. Editor Rules

### Split

- Only a selected `leaf` can be split.
- A new split defaults to `0.5`.
- Splitting replaces one `leaf` with one `split node` and two child `leaf nodes`.

### Resize

- Dragging a splitter only updates one `SplitNode.Ratio`.
- Resize is constrained by minimum child size.
- Ratio should be clamped to a safe range computed from current canvas geometry.

### Delete split

- Deleting operates on a `split node`, not a leaf.
- Removing a split replaces that split node with a single `leaf`.
- The new leaf can reuse the deleted split node's id or receive a fresh id.

Recommendation:

- Reuse the deleted split node id for the merged leaf to simplify selection recovery.

### Validation

The domain layer should protect these invariants:

- Every non-leaf node has exactly two children.
- `Ratio` is always between `0` and `1`, exclusive.
- Layout fills the canvas completely.
- No overlapping regions.
- No empty gaps.
- No region smaller than minimum size after any committed operation.

## 6. Layout Computation

The editor needs a deterministic tree-to-rectangles computation pass.

Input:

- root node
- canvas rectangle
- splitter thickness

Output:

- computed leaf rectangles
- computed splitter rectangles
- parent-child mapping for hit testing and actions

Pseudo flow:

1. Start from root with the full canvas rect.
2. If node is a leaf, emit one region rect.
3. If node is a split:
   - calculate available size along split axis
   - compute first and second child rects using `ratio`
   - reserve splitter thickness in the middle
   - recurse into both children
   - emit one splitter rect

This computation should live in `PaneWorks.Core`, not in the view.

## 7. Interaction Model

### Selection

- Single click on a leaf region selects that leaf.
- Single click on a splitter selects that split.
- Selection state is independent from the persisted layout model.

Suggested editor state:

```csharp
public sealed class EditorState
{
    public string? SelectedNodeId { get; set; }
    public bool IsDirty { get; set; }
    public DragSession? ActiveDrag { get; set; }
}
```

### Drag lifecycle

1. Pointer down on a splitter.
2. Capture drag session:
   - target split node id
   - original ratio
   - original splitter bounds
3. Pointer move:
   - convert pointer position into candidate ratio
   - clamp by min size
   - recompute preview layout
4. Pointer up:
   - commit final ratio
   - mark document dirty

Suggested drag session:

```csharp
public sealed record DragSession(
    string SplitNodeId,
    double InitialRatio,
    Point DragStartPoint);
```

### Commands

The ViewModel layer should expose commands such as:

- `NewLayout`
- `LoadLayout`
- `SaveLayout`
- `SaveAsLayout`
- `RenameLayout`
- `DeleteLayout`
- `SplitHorizontal`
- `SplitVertical`
- `DeleteSelectedSplit`

## 8. UI Composition

### Main window

Three-panel layout:

- Left: layout list
- Center: editor canvas
- Right or top: actions and layout metadata

### Suggested views

- `MainWindow`
- `LayoutLibraryView`
- `EditorCanvasView`
- `EditorToolbarView`

### Canvas visuals

- Draw regions with clear borders.
- Highlight selected region or split.
- Show splitter hover state.
- Show drag preview in real time.

Recommended rendering path for MVP:

- Use a `Canvas` or custom `FrameworkElement` for drawing.
- Render geometry from the computed runtime model.
- Keep hit testing explicit instead of relying on many nested WPF controls.

This avoids building one WPF control per region and stays simpler as the tree changes.

## 9. Persistence Design

Layouts should be stored as one JSON file per layout.

Suggested local path:

```text
%AppData%\PaneWorks\Layouts\
```

Suggested file conventions:

- Human-readable file name, slug-based if needed.
- Internal `name` still comes from JSON.

Repository interface:

```csharp
public interface ILayoutRepository
{
    Task<IReadOnlyList<LayoutListItem>> ListAsync(CancellationToken ct);
    Task<LayoutDocument> LoadAsync(string id, CancellationToken ct);
    Task SaveAsync(LayoutDocument document, CancellationToken ct);
    Task DeleteAsync(string id, CancellationToken ct);
    Task RenameAsync(string id, string newName, CancellationToken ct);
}
```

`LayoutListItem` should contain only lightweight list data:

- id
- name
- file path
- last modified time

## 10. Dirty State and Unsaved Changes

Unsaved change tracking should be explicit from day one.

Mark the document dirty when:

- split created
- ratio changed
- split deleted
- layout name changed

Prompt the user before:

- loading another layout
- creating a new layout
- deleting the current layout
- closing the app

## 11. Future Compatibility

Even though the MVP does not manage windows, the design should leave room for it.

Prepare for a future `WorkspaceApplyService` that will:

- enumerate desktop windows
- compute target rectangles from leaf nodes
- move windows into leaf regions

That later service can depend on:

- `GetWindowRect`
- `SetWindowPos`
- `EnumWindows`
- `MonitorFromWindow`
- `GetMonitorInfo`

This is why keeping the layout model independent from the view is important.

## 12. Suggested Milestones

### Milestone 1: project skeleton

- create solution and projects
- set up MVVM structure
- render one static full-canvas region

### Milestone 2: split tree rendering

- implement core layout tree types
- compute leaf and splitter geometry
- render recursive split results

### Milestone 3: editing operations

- select leaf
- horizontal and vertical split
- delete selected split

### Milestone 4: drag resizing

- splitter hit testing
- drag session management
- min-size clamping
- live preview

### Milestone 5: persistence

- save layout JSON
- load layout JSON
- layout list
- rename and delete
- dirty state prompts

### Milestone 6: polish

- keyboard shortcuts
- better empty states
- error messages
- optional undo and redo

## 13. Recommended First Build Order

If we start implementation now, the best order is:

1. Create `PaneWorks.Core` with the node model.
2. Implement tree-to-rect computation.
3. Build a canvas that renders leaf rects and splitters.
4. Add region selection.
5. Add split commands.
6. Add delete split.
7. Add drag resize with min-size rules.
8. Add JSON persistence and layout library.

This order keeps the editor testable at every stage.

## 14. Key Technical Risks

### Risk 1: UI and domain logic get mixed together

Mitigation:

- Keep split, resize, delete, and validation in `PaneWorks.Core`.

### Risk 2: WPF visual tree becomes too complex

Mitigation:

- Use one custom drawing surface with computed geometry rather than many nested controls.

### Risk 3: Resize math becomes unstable

Mitigation:

- Convert drag motion into ratio updates only.
- Centralize clamping logic in one service.
- Write geometry unit tests early.

### Risk 4: persistence model drifts from runtime model

Mitigation:

- Persist only the tree.
- Compute rectangles only at runtime.

## 15. MVP Acceptance Criteria

The first version is done when:

- User can create a new blank layout.
- The canvas starts with one region.
- User can split any leaf horizontally or vertically.
- New splits default to equal proportions.
- User can drag a splitter and the layout updates live.
- User cannot resize a region below minimum size.
- User can delete a split and merge back into one region.
- User can save layouts locally as JSON.
- User can load, rename, and delete saved layouts.
- User is warned before losing unsaved changes.

## 16. Next Step

The next practical step is not window management.

It is:

- scaffold the solution
- implement `PaneWorks.Core`
- render the first editable canvas

Once that is in place, the rest of the MVP becomes straightforward.
