# 设置中的快捷键说明

2026-11-15 · 0.2.15。对应 [Issue #6](https://github.com/dayesann/shiguang-album/issues/6)。

打开「设置 → 快捷键」，按媒体网格、多选模式、图片预览和回收站查找操作。每行左侧是操作说明，右侧是对应按键；键帽是只读提示。媒体网格的 Ctrl+A 只勾选已加载项目，回收站的 Ctrl+A 勾选列表中的所有项目。

样式与交互见 [设计系统](../DESIGN_SYSTEM.md#快捷键说明)。使用现有语义色、字号、导航和焦点样式。

已实际运行 WPF，核对 [原「关于」页](images/shortcuts-before.png)、[新版浅色](images/shortcuts-light.png)、[深色最小窗口](images/shortcuts-dark.png) 和 [深色滚动到底](images/shortcuts-bottom.png)，并检查跟随系统。用 Tab、End、方向键、Enter 打开快捷键页，确认焦点顺序；键帽只有说明文字。

Windows 构建、20 项静态设计检查、6 组核心回归和 41 项设置页检查通过。测试覆盖最小窗口滚动、返回页面时回到首组、快捷键文案和源媒体哈希。未使用实际系统高对比度、所有 DPI 或读屏软件逐项验收。
