# TOfont 工具箱化重构方案

> 目标：把 TOfont 从"单一字模提取应用"改造成"工具箱"，字模提取降级为其中一个工具。
> 首版范围：**工具框架 + 字模提取**（只迁移现有功能，不新增工具）。
> 界面：**沿用现有 NavigationView**。
> 状态：待确认后实施。

---

## 1. 现状分析

```
src/TOfont.WinUI/
├── MainWindow.xaml(.cs)      # 硬编码导航：主页/取模/设置
├── App.xaml.cs               # 启动入口
├── AppSettings.cs            # 静态设置类
├── Pages/
│   ├── HomePage              # 空壳
│   ├── ExtractionPage(.cs)   # 400+ 行代码后置：4 种模式逻辑 + 点阵预览渲染全混在一起
│   └── SettingsPage          # 扫描模式/位序/阴码设置
└── ViewModels/
    └── MainViewModel.cs      # 全局单例 VM，涵盖文字/图片取模全部状态
```

**问题**：
1. `ExtractionPage.xaml.cs` 是巨型代码后置（UI 事件 + 提取逻辑 + 位图渲染混合）
2. 导航菜单在 XAML 硬编码，加工具要改 `MainWindow.xaml` + `OnNavSelectionChanged` 两处
3. 未来每加一个工具，`MainWindow` 的 switch 就要膨胀一次
4. `MainViewModel` 名为 Main 实为 Extraction 的 VM，复用性差

---

## 2. 目标架构

```
src/TOfont.WinUI/
├── Framework/                    # 【新】工具宿主框架（与具体工具无关）
│   ├── ToolDescriptor.cs         # 工具描述：Id / Title / 图标 / Page 类型
│   ├── ToolCatalog.cs            # 工具注册表：静态只读列表，新工具在此登记
│   └── ToolPage.cs               # 可选基类（预留，暂不实现）
├── Tools/                        # 【新】各工具目录，一个工具一个子目录
│   └── FontExtraction/           # 字模提取（从 Pages/ExtractionPage 迁移）
│       ├── FontExtractionPage.xaml(.cs)
│       └── FontExtractionViewModel.cs   # 【新】从代码后置抽取逻辑
├── Pages/                        # 保留（宿主级页面）
│   ├── HomePage                  # 改造为"工具箱首页"：工具卡片网格
│   └── SettingsPage
├── MainWindow.xaml(.cs)          # 改造：从 ToolCatalog 动态生成导航
├── App.xaml.cs                   # 不变
└── AppSettings.cs                # 不变
```

**核心机制：工具注册驱动导航**

```csharp
// Framework/ToolDescriptor.cs
public class ToolDescriptor
{
    public string Id { get; init; } = "";          // "font-extraction"
    public string Title { get; init; } = "";       // "字模提取"
    public string Glyph { get; init; } = "";       // Segoe MDL2 图标码 "\uE943"
    public Type PageType { get; init; } = null!;   // 对应 Page 类型
    public bool ShowInHome { get; init; } = true;  // 是否显示在首页卡片
}

// Framework/ToolCatalog.cs
public static class ToolCatalog
{
    public static IReadOnlyList<ToolDescriptor> Tools { get; } =
    [
        new() { Id = "font-extraction", Title = "字模提取", Glyph = "\uE943",
                PageType = typeof(FontExtractionPage) },
        // 未来新工具在此追加一行即可
    ];

    public static ToolDescriptor? FindById(string id) =>
        Tools.FirstOrDefault(t => t.Id == id);
}
```

**MainWindow 改造**：
- 菜单项不再写死在 XAML，改为 `NavView.MenuItems` 在代码里从 `ToolCatalog` 生成
- `OnNavSelectionChanged` 用 `ToolCatalog.FindById(tag)` 拿到 Page 类型后 `Activator.CreateInstance` 创建（页面按需懒加载，用字典缓存实例）
- 结构：`主页 / ── 工具分组 ── / 设置`

**HomePage 改造**：显示工具卡片网格（Title + 图标），点击卡片 `MainWindow.NavigateTo(toolId)`。App 启动后落在主页，用户点卡片进工具。

---

## 3. 迁移步骤（每步可独立验证、可回滚）

### 阶段 0：准备
- [ ] 建分支 `refactor/toolbox`（git 天然回滚点）
- [ ] 记录当前行为基线：4 种模式取模输出、设置项联动

### 阶段 1：搭工具框架（纯增量，不动现有功能）
- [ ] 新增 `Framework/ToolDescriptor.cs`、`Framework/ToolCatalog.cs`
- [ ] 改 `MainWindow`：导航从 `ToolCatalog` 生成（先只登记"字模提取"）
- [ ] 改 `HomePage`：渲染工具卡片，点击跳转
- **验证**：`dotnet build` 通过；启动应用，导航/首页/取模功能与重构前一致
- **回滚**：`git checkout` 丢弃阶段 1 改动

### 阶段 2：迁移字模提取为独立工具
- [ ] 新建 `Tools/FontExtraction/FontExtractionPage.xaml(.cs)`，**纯搬移** `ExtractionPage` 代码（改命名空间、重命名类，逻辑零改动）
- [ ] `ToolCatalog` 登记新页面，删除旧 `Pages/ExtractionPage.*`
- **验证**：构建通过；4 种模式取模结果与基线一致（可用 CLI 对比同一文字的字节输出）
- **回滚**：恢复 `ExtractionPage`，`ToolCatalog` 注销新页

### 阶段 3（可选，建议）抽取 ViewModel
- [ ] 从 `FontExtractionPage.xaml.cs` 抽出状态与提取逻辑到 `FontExtractionViewModel`（实现 `INotifyPropertyChanged`）
- [ ] 点阵预览渲染（bitmap 绘制）先留在 code-behind，只抽业务逻辑
- **验证**：功能不变，页面代码量明显下降
- **说明**：此步若风险高可整体推迟，先交付框架 + 迁移

### 阶段 4：收尾
- [ ] 删除/停用 `ViewModels/MainViewModel.cs`（被工具 VM 取代）
- [ ] 全面构建：`TOfont.sln` + WinUI（x64）
- [ ] 手动验收：启动 → 首页 → 进入字模提取 → 文字/图片/字体库/导入 4 模式 → 设置联动
- [ ] 合并回 `main`，打 tag

---

## 4. 关键设计决策

| 决策点 | 选择 | 理由 |
|---|---|---|
| 工具注册方式 | 静态 `ToolCatalog` 列表 | 工具少、无依赖注入需求，最简；将来要插件化可平滑升级为 DI 扫描 |
| 页面创建 | 懒加载 + 字典缓存实例 | 与现有 `_homePage ??= new HomePage()` 模式一致 |
| 视图模型 | 每工具一个 VM，不用全局单例 | 解耦；`MainViewModel` 退役 |
| 代码后置 vs MVVM | 先搬移后抽取（阶段 2→3 分离） | 控制风险，先保证行为一致 |
| 设置 | `AppSettings` 静态类暂不变 | 首版不引入设置持久化重构，避免范围膨胀 |
| Core 层 | **零改动** | 已是干净分层，提取/格式化/解析逻辑不动 |

## 5. 明确不做（首版范围外）

- ❌ 不新增任何工具（只搭框架）
- ❌ 不做插件热加载 / 动态扫描
- ❌ 不引入 DI 容器（Microsoft.Extensions.DependencyInjection 等）
- ❌ 不改 Core 层接口
- ❌ 不改设置持久化（暂存 `AppSettings` 静态类）

## 6. 验收清单

- [ ] 启动即见"工具箱首页"，字模提取卡片可点击进入
- [ ] 导航动态生成：菜单项来自 `ToolCatalog`，`MainWindow.xaml` 无硬编码工具项
- [ ] 字模提取 4 种模式输出与重构前逐字节一致（CLI 对比验证）
- [ ] 设置页联动正常（扫描模式/位序/阴码/十六进制）
- [ ] `dotnet build TOfont.sln` 与 WinUI（x64）0 错误
