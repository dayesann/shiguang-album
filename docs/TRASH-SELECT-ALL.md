# 回收站全选

2026-11-15 · 0.2.14 开发分支，尚未发布。对应 [Issue #5](https://github.com/dayesann/shiguang-album/issues/5)。

回收站顶部的「全选」和 Ctrl+A 会勾选当前回收站中的所有项目，包括屏幕下方的条目；「取消全选」清除勾选。底部继续显示已选数量和总大小。全选不会恢复或删除任何项目。点击「永久删除」仍打开原来的范围确认，默认焦点为「取消」。取消后保留勾选，文件保持原状。

使用 [设计系统](../DESIGN_SYSTEM.md#回收站全选) 的轻量按钮和现有行内复选框。实际运行 WPF 后核对了 [改动前](images/trash-select-before.png)、[浅色全选](images/trash-select-light.png)、[深色最小窗口](images/trash-select-dark.png) 和 [永久删除确认](images/trash-select-confirm.png)，也检查了跟随系统和空回收站。

Windows 构建、20 项设计静态检查、6 组核心回归和 18 项回收站 WPF 检查通过。使用生成的演示素材验证了全部勾选、逐项取消、快捷键、空列表、取消确认后保留选择和媒体哈希不变。在隔离演示窗口实际按 Ctrl+A，确认从零项选中到 12 项。未切换系统高对比度或覆盖全部 DPI／读屏软件，未执行永久删除。
