# PaneWorks 技术方案

## 1. 目标

PaneWorks 的 MVP 是一个面向 Windows 的单屏布局编辑器。

第一版只解决一件事：

- 用户可以把一个屏幕画布递归切分成多个矩形区域。
- 用户可以拖拽分割线调整比例。
- 用户可以删除某次分割并把区域合并回去。
- 用户可以将布局保存为本地 JSON 模板，并再次加载使用。

第一版暂时不管理真实窗口。

## 2. 推荐技术栈

### 应用栈

- 语言：`C#`
- 运行时：`.NET 8`
- UI 框架：`WPF`
- 架构模式：`MVVM`
- 序列化：`System.Text.Json`

### 为什么第一版先用 WPF

- 它是成熟的 Windows 桌面开发方案。
- 对自定义绘制、命中测试、拖拽交互支持很好。
- 很适合承载一个“可编辑画布”式的界面。
- 后续接入 `Win32` 窗口管理 API 的路径也很顺。
- 相比 WinUI，更适合快速把编辑器型 MVP 做出来。

如果后期产品目标更偏现代化壳层体验，可以再评估 WinUI；但对当前这个“先把布局编辑器做稳”的阶段，WPF 是更快也更稳的选择。

## 3. 整体架构

建议把应用拆成四层：

1. `Presentation`
   - 窗口、面板、命令、画布渲染、选中态视觉表现。
2. `Application`
   - 编辑器用例，例如分割、拖拽调整、删除分割、保存、加载。
3. `Domain`
   - 布局树模型、校验规则、布局计算逻辑。
4. `Infrastructure`
   - JSON 存储、文件系统访问、后续 Win32 集成。

建议的解决方案结构：

```text
PaneWorks/
  src/
    PaneWorks.App/
    PaneWorks.Core/
    PaneWorks.Infrastructure/
  docs/
```

各项目职责建议如下：

- `PaneWorks.App`
  - WPF 应用入口
  - View 和 ViewModel
  - 输入处理与拖拽协调
- `PaneWorks.Core`
  - 布局树实体
  - 编辑操作
  - 矩形计算
  - 校验逻辑
- `PaneWorks.Infrastructure`
  - 布局仓储
  - JSON 文件持久化
  - 应用配置路径

## 4. 核心领域模型

布局的持久化模型采用“分割树”，而不是“扁平矩形列表”。

### 布局文档

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

### 领域类型

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

### 仅运行时存在的计算结果

保存到 JSON 里的布局不应该包含绝对矩形坐标。

运行时再根据下面这些信息计算：

- 当前画布边界
- 分割树结构
- 最小区域尺寸
- 分割线厚度

建议的运行时模型：

```csharp
public sealed record ComputedRegion(
    string NodeId,
    Rect Bounds);

public sealed record ComputedSplitter(
    string SplitNodeId,
    Rect Bounds,
    SplitDirection Direction);
```

## 5. 编辑规则

### 分割

- 只有被选中的 `leaf` 节点才能继续分割。
- 新分割默认比例为 `0.5`。
- 分割操作会把一个 `leaf` 替换为一个 `split node`，同时生成两个新的 `leaf` 子节点。

### 调整大小

- 拖拽分割线本质上只修改一个 `SplitNode.Ratio`。
- 调整时必须受最小子区域尺寸限制。
- `ratio` 应根据当前画布几何信息被夹紧到安全范围。

### 删除分割

- 删除操作作用于 `split node`，不是作用于 `leaf`。
- 删除后，这个 `split node` 会被替换成一个新的单独 `leaf`。
- 合并后的这个叶子可以复用原分割节点的 id，也可以生成新 id。

建议：

- 合并后复用被删除分割节点的 id，这样更方便恢复选中状态。

### 校验约束

领域层要保证这些不变量：

- 每个非叶子节点必须且只能有两个子节点。
- `Ratio` 必须严格在 `0` 和 `1` 之间。
- 布局必须完整铺满画布。
- 区域之间不能重叠。
- 区域之间不能留空。
- 任意一次提交后的区域都不能小于最小尺寸。

## 6. 布局计算

编辑器需要一套确定性的“树转矩形”计算逻辑。

输入：

- 根节点
- 画布矩形
- 分割线厚度

输出：

- 所有叶子区域的矩形
- 所有分割线的矩形
- 便于命中测试和操作的父子关系映射

伪流程：

1. 从根节点开始，拿整块画布作为初始矩形。
2. 如果当前节点是叶子，则输出一个区域矩形。
3. 如果当前节点是分割节点：
   - 按分割方向计算可用长度
   - 根据 `ratio` 算出 first 和 second 子区域矩形
   - 中间预留分割线厚度
   - 递归计算两个子节点
   - 输出一个分割线矩形

这套计算应该放在 `PaneWorks.Core` 中，而不是写在 View 层里。

## 7. 交互模型

### 选中逻辑

- 单击叶子区域时，选中该叶子。
- 单击分割线时，选中对应的分割节点。
- 选中状态独立于布局持久化模型存在。

建议的编辑器状态：

```csharp
public sealed class EditorState
{
    public string? SelectedNodeId { get; set; }
    public bool IsDirty { get; set; }
    public DragSession? ActiveDrag { get; set; }
}
```

### 拖拽生命周期

1. 在分割线上按下鼠标。
2. 建立拖拽会话，记录：
   - 目标分割节点 id
   - 初始 ratio
   - 初始按下位置
3. 鼠标移动时：
   - 把当前位置换算为候选 ratio
   - 根据最小尺寸限制进行夹紧
   - 重新计算预览布局
4. 鼠标抬起时：
   - 提交最终 ratio
   - 标记文档已修改

建议的拖拽会话：

```csharp
public sealed record DragSession(
    string SplitNodeId,
    double InitialRatio,
    Point DragStartPoint);
```

### 命令

ViewModel 层建议暴露这些命令：

- `NewLayout`
- `LoadLayout`
- `SaveLayout`
- `SaveAsLayout`
- `RenameLayout`
- `DeleteLayout`
- `SplitHorizontal`
- `SplitVertical`
- `DeleteSelectedSplit`

## 8. UI 结构

### 主窗口

建议采用三栏结构：

- 左侧：布局列表
- 中间：编辑画布
- 右侧或顶部：操作区与布局元信息

### 建议视图

- `MainWindow`
- `LayoutLibraryView`
- `EditorCanvasView`
- `EditorToolbarView`

### 画布视觉表现

- 区域要有明确边框。
- 当前选中区域或分割线要有高亮。
- 分割线悬停时要有 hover 态。
- 拖拽时要能实时预览。

MVP 阶段建议的渲染方式：

- 用一个 `Canvas` 或自定义 `FrameworkElement` 负责绘制。
- 根据运行时计算模型统一渲染。
- 命中测试尽量显式处理，不依赖很多层嵌套的 WPF 控件。

这样能避免“一个区域一个控件”的复杂视图树，后面布局树频繁变化时也更稳。

## 9. 持久化设计

布局建议采用“一布局一 JSON 文件”的方式存储。

建议的本地路径：

```text
%AppData%\PaneWorks\Layouts\
```

建议的文件规则：

- 文件名尽量可读，必要时做 slug 化。
- 布局的内部名称仍然以 JSON 中的 `name` 为准。

建议的仓储接口：

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

`LayoutListItem` 只需要包含轻量信息：

- id
- name
- 文件路径
- 最后修改时间

## 10. 脏状态与未保存修改

未保存变更一定要从第一版就明确处理。

以下操作都应把文档标记为已修改：

- 新建分割
- 调整 ratio
- 删除分割
- 修改布局名称

以下场景需要弹提醒：

- 加载另一个布局前
- 新建布局前
- 删除当前布局前
- 关闭应用前

## 11. 为未来功能预留兼容性

虽然 MVP 暂时不管理窗口，但设计上要给后续扩展留口子。

后面可以新增一个 `WorkspaceApplyService`，负责：

- 枚举当前桌面窗口
- 根据叶子节点计算目标区域矩形
- 把窗口移动到对应区域

届时它会依赖这些 Win32 API：

- `GetWindowRect`
- `SetWindowPos`
- `EnumWindows`
- `MonitorFromWindow`
- `GetMonitorInfo`

这也是为什么布局模型一定要独立于 View 层。

## 12. 建议里程碑

### 里程碑 1：项目骨架

- 创建解决方案和项目
- 搭好 MVVM 结构
- 渲染一个静态的整块区域

### 里程碑 2：分割树渲染

- 实现核心布局树类型
- 完成叶子区域与分割线几何计算
- 渲染递归分割结果

### 里程碑 3：编辑操作

- 选中叶子区域
- 横向与纵向分割
- 删除已选中的分割

### 里程碑 4：拖拽调整

- 分割线命中测试
- 拖拽会话管理
- 最小尺寸夹紧
- 实时预览

### 里程碑 5：持久化

- 保存布局 JSON
- 加载布局 JSON
- 布局列表
- 重命名与删除
- 未保存提醒

### 里程碑 6：打磨

- 快捷键
- 更好的空状态提示
- 错误提示
- 可选的撤销与重做

## 13. 推荐实现顺序

如果现在就开始写代码，最顺的顺序是：

1. 创建 `PaneWorks.Core`，先把节点模型搭起来。
2. 实现“分割树转矩形”的计算逻辑。
3. 先做一个能渲染叶子区域和分割线的画布。
4. 加入区域选中。
5. 加入横切和竖切命令。
6. 加入删除分割。
7. 加入带最小尺寸限制的拖拽调整。
8. 最后接 JSON 持久化和布局列表。

这个顺序的好处是每一步都能看到可验证的结果。

## 14. 关键技术风险

### 风险 1：UI 逻辑和领域逻辑混在一起

应对：

- 把分割、拖拽调整、删除、校验都放进 `PaneWorks.Core`。

### 风险 2：WPF 视图树变得过于复杂

应对：

- 用一个统一的自定义绘制表面，基于计算几何渲染，而不是堆很多嵌套控件。

### 风险 3：拖拽比例计算不稳定

应对：

- 把拖拽统一抽象为 ratio 更新。
- 把夹紧逻辑集中到一个服务里。
- 尽早补几何计算单元测试。

### 风险 4：持久化模型和运行时模型逐渐偏离

应对：

- 持久化层只保存树结构。
- 所有矩形都在运行时重新计算。

## 15. MVP 验收标准

第一版完成的标准是：

- 用户可以新建一个空布局。
- 画布初始只有一个区域。
- 用户可以对任意叶子区域做横切或竖切。
- 新分割默认是等比例。
- 用户可以拖拽分割线并实时看到变化。
- 用户不能把任一区域拖到小于最小尺寸。
- 用户可以删除某次分割并合并回一个区域。
- 用户可以把布局保存为本地 JSON。
- 用户可以加载、重命名、删除已保存布局。
- 用户在丢失未保存修改前会收到提醒。

## 16. 下一步

下一步最值得做的还不是窗口管理，而是：

- 搭解决方案骨架
- 实现 `PaneWorks.Core`
- 渲染第一版可编辑画布

只要这三件事起来，后面的 MVP 就会顺很多。
