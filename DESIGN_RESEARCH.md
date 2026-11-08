# 拾光照片设计规范调研

调研日期：2026-11-08

补充研究：[MyGo UI 设计调研](docs/design/MyGo-UI设计调研.md)。已查看官方文档和按钮、工具栏、表单、侧栏、删除提示的实际预览。建议采用紧凑控件、统一密度、工具栏溢出与就近反馈；保留 WPF 技术栈、Eagle 式照片自适应排版和媒体删除安全约定。此次研究未直接修改应用样式。

## 结论

GitHub 上有成熟且持续维护的软件设计体系，可以直接支撑拾光照片，但没有任何一个仓库可以原样套进 WPF：

- 采用 `microsoft/WinUI-Gallery` 和 Microsoft Fluent 2 作为 Windows 控件行为、视觉层级、自适应与无障碍依据。
- 采用 `microsoft/fluentui-system-icons` 作为唯一产品图标来源。
- 采用 `dotnet/wpf` 的官方 Fluent 主题与 WPF 资源机制作为原生实现参考。
- 采用 `microsoft/WPF-Samples` 只核对具体 WPF 写法，不把它当完整 UI 规范。其 README 明确提醒部分示例可能违反无障碍最佳实践。
- React Web 设计系统用于借鉴组件治理、令牌和验收方法，不能直接改变这个 WPF 产品的版式。

因此建议建立自己的项目设计系统，以 Fluent 为底座，把 Eagle 的内容优先思路和 iPhone 相册的时间浏览方式作为产品模式；不复制第三方商品界面或资产。

## 候选仓库评估

| 项目 | 定位 | 优点 | 对拾光照片的限制 | 结论 |
|---|---|---|---|---|
| [microsoft/WinUI-Gallery](https://github.com/microsoft/WinUI-Gallery) | WinUI 3 控件与 Fluent 示例应用 | 真实控件示例、自适应布局、设计与无障碍指南；MIT | 使用 WinUI 3，不是 WPF，不能直接复用代码 | 作为控件行为和视觉状态基准 |
| [microsoft/microsoft-ui-xaml](https://github.com/microsoft/microsoft-ui-xaml) | WinUI 框架实现 | 控件样式、设计和可访问性行为的底层参考；MIT | C++ 框架仓库，页面代码不可直接搬入 WPF | 作为 Fluent 行为的事实来源 |
| [microsoft/fluentui-system-icons](https://github.com/microsoft/fluentui-system-icons) | 官方 Fluent 图标 | 统一线性/填充语言、SVG 源、RTL 元数据；MIT | 只解决图标，不解决布局与按钮层级 | 直接采用 |
| [microsoft/fluentui](https://github.com/microsoft/fluentui) | Fluent UI React/Web | 成熟组件状态、令牌和文档 | TypeScript/React 实现，页面样式与原生桌面差异大 | 用作设计原则参考，不直接引入 |
| [microsoft/WPF-Samples](https://github.com/microsoft/WPF-Samples) | WPF API 示例 | 仓库已对齐 .NET 10，覆盖 WPF 控件与无障碍样例；MIT | README 明确说明很多示例不是无障碍最佳实践，也不是视觉规范 | 仅用于 WPF 技术验证 |
| [dotnet/wpf](https://github.com/dotnet/wpf) | WPF 框架源码 | .NET 10、官方主题与控件实现、MIT | 框架级代码，不提供产品级设计流程 | 作为 WPF Fluent 实现参照 |
| [primer/design](https://github.com/primer/design) | GitHub Primer 指南 | 工程与设计规范表达成熟；MIT | 面向 GitHub Web，仓库已归档 | 只借鉴治理方法 |
| [carbon-design-system/carbon](https://github.com/carbon-design-system/carbon) | IBM 企业设计系统 | 完整令牌、组件、无障碍流程；Apache-2.0 | React/Web 产品气质，与轻量媒体库不一致 | 借鉴流程，不采用视觉 |
| [adobe/spectrum-css](https://github.com/adobe/spectrum-css) | Adobe Spectrum CSS | 媒体产品经验丰富；Apache-2.0 | Web/CSS 实现且仓库已归档；不能替代 WPF | 借鉴内容与工具如何共存 |

## 采用依据

### Fluent 控件与按钮层级

Microsoft 的按钮指导要求按主要、次要和普通操作建立清晰层级，而不是给所有动作相同视觉重量。工具栏在常规宽度下应容纳最重要操作，额外动作进入溢出菜单。

- [Fluent 2 Button usage](https://fluent2.microsoft.design/components/web/react/core/button/usage)
- [Windows Command bar](https://learn.microsoft.com/windows/apps/develop/ui/controls/command-bar)

拾光照片将“添加目录”等当前页唯一主任务做成 Primary；详情、收藏、加入相册等常规动作使用透明 Tertiary 或图标操作。设置页的普通操作以行尾图标加文字呈现。

### 字体与间距

Windows 类型排布推荐中文界面正文至少 14–16 DIP、辅助信息至少 12 DIP，并按固定间距体系组织信息。拾光照片提高默认正文到 16 DIP，常用辅助信息不低于 13 DIP，只允许短徽标使用 12 DIP。

- [Windows typography](https://learn.microsoft.com/windows/apps/design/style/typography)
- [Windows layout and spacing](https://learn.microsoft.com/windows/apps/design/style/spacing)

### 图标

Fluent System Icons 提供常规和填充两套风格、多个尺寸与方向元数据，正好满足导航、工具栏、播放器和 RTL 准备。产品将停止使用 Unicode 符号补位，并统一图标尺寸与状态。

- [Fluent System Icons 仓库](https://github.com/microsoft/fluentui-system-icons)

### 可访问性

- [Windows accessibility overview](https://learn.microsoft.com/windows/accessibility)
- [WCAG 2.2 target size minimum](https://www.w3.org/WAI/WCAG22/Understanding/target-size-minimum.html)

项目采用桌面紧凑尺寸：常规按钮与图标热区 32 DIP，导航 42 DIP，分段切换 30 DIP，筛选 36 DIP；触摸模式使用 44 DIP。WCAG 2.5.8 的最低目标是 24×24 CSS 像素；32 DIP 保留桌面可操作性，同时避免整套界面呈现“老年模式”式的粗大块面。颜色对比与焦点必须有自动或人工证据，不能只凭截图感觉。

### WPF 实现

当前项目目标框架为 `net10.0-windows`，使用 WPF。`microsoft/WPF-Samples` 与 `dotnet/wpf` 都与该技术路线匹配。WPF 的矢量渲染和 DPI 支持适合媒体库，但需要为所有自定义按钮模板补齐键盘、焦点、高对比度和可访问状态。

参考：

- [microsoft/WPF-Samples](https://github.com/microsoft/WPF-Samples)
- [dotnet/wpf](https://github.com/dotnet/wpf)

## 对现有界面与代码的审查

这是设计治理缺口，不能只靠逐页润色解决。

1. `src/Resources.xaml` 同时保存语义颜色、隐式控件模板和 `PrimaryButton`、`IconButton`、`NavButton`、`SegmentButton`、`ChoiceCardButton` 等产品样式，缺少“基础令牌 / 控件组件 / 页面模式”分层。
2. `src/ThemeManager.cs` 在 C# 中维护浅色和深色颜色字典，但没有高对比度分支，也没有尺寸、排版和动效令牌。
3. `src/MainWindow.xaml` 中有 `#D3191D24`、`#FFCF6B` 等页面级裸色值；同一文件还存在多套边距、字号和控制高度。
4. `src/VideoPlayerWindow.xaml` 独立维护大量颜色、按钮尺寸与滑块样式，尚未完全接入全局令牌。
5. `src/SettingsWindow.cs` 通过代码生成界面，存在本地尺寸与 `Card` 拼装；后续若继续扩展，容易与 XAML 页面偏离。
6. `src/Dialogs.cs`、`src/PhotoViewer.cs` 又提供各自的按钮入口；同一个“按钮”的实际规则分散在多个实现点。
7. 扫描状态中的“暂停/停止”曾显式设置为 `MinHeight=29`；现已并入 32 DIP 桌面按钮规范。
8. 部分元数据、路径和重复照片信息使用 10–11 DIP，高 DPI 和中文环境下容易难读。
9. 主题选择使用 210 DIP 高卡片，这是有真实预览的合理特殊组件；普通操作不应效仿该视觉重量。
10. 当前测试覆盖了若干按钮尺寸和主题，但没有全仓扫描裸色值、过小热区、缺失提示/可访问名称或设计令牌遗漏。

## 建议的落地路线

1. 先按 `DESIGN_SYSTEM.md` 建立唯一规范，所有 UI 任务都引用该文档。
2. 将令牌和组件资源拆分，逐步替换裸色值与分散按钮工厂。
3. 选择主窗口、设置、弹窗、相册、图片预览、视频播放器作为六个基准模板。
4. 在六个基准模板全部通过截图、键盘、主题、DPI 和规模测试后，再扩展新界面。
5. 增加设计静态检查。违反强制规则时应在构建或交付脚本中报告，而不是等用户截图指出。

## 暂不采用的做法

- 不直接把 WinUI 3 控件库引入当前 WPF 项目，避免改变应用框架和现有交互。
- 不把 Fluent UI React 或 Carbon 的 CSS 视觉直接移植到 WPF。
- 不把每项操作都包成大卡片或实心按钮。
- 不用截图排版代替真实控件行为测试。
- 不在没有基准页面和回归证据的情况下继续堆叠新功能页面。


0.2.8 已实施 MyGo 紧凑控件规则，沿用 WPF 和 Fluent 图标，运行与视觉证据见 [验收记录](验证/MyGo-0.2.8/验收记录.md)。
