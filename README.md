# 时光相册 · Shiguang Album

Windows 本地照片与视频媒体库，当前版本 **0.2.9**。通过索引统一浏览多个来源文件夹，保留原有媒体位置。相册保存引用，同一张照片可以加入多个相册。

## 已有功能

- 年、月、日时间线与自适应照片排版。
- 照片、HEIC、视频、全景图与截图分类。
- 虚拟相册、标签、收藏与搜索。
- 完全重复校验、近似照片人工复核、可恢复删除与回收站。
- 图片预览、图形化视频播放器、浅色／深色／跟随系统主题。
- 完整媒体库迁移、相册资料备份与来源重新连接。
- 默认关闭到系统托盘，任务继续；再次启动同库恢复原窗口。

本项目目前只有 Windows 桌面端，没有手机应用或手机传输服务。HEIC、HEVC 等格式依赖 Windows 解码组件。仍在开发中，清理操作应先用测试副本验证。

## 开发与构建

需要 Windows 10/11 x64、[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)（Windows x64）与 PowerShell 7。没有第三方 NuGet 依赖，`NuGet.Config` 禁用了外部包源。

```powershell
git clone https://github.com/dayesann/shiguang-album.git
cd shiguang-album
pwsh -File .\构建.ps1
```

构建脚本优先使用 `.tools/dotnet/dotnet.exe`，其次使用 PATH 中的 `dotnet`，也可通过 `-DotnetPath` 指定 SDK 程序。生成目录为 `成品/运行程序-<版本>/`，包含私有运行时；分发时应保留整个目录。

```powershell
# 独立演示库，不扫描正式来源
& '.\成品\运行程序-0.2.9\拾光照片.exe' --demo

# 无交互核心检查，适合 CI
pwsh -File .\自测.ps1 -CoreOnly

# 完整 13 组检查，需要可交互 Windows 桌面
pwsh -File .\自测.ps1
```

日常直接启动不带 `--demo` 的程序。打包目录位于 `成品` 下时，正式媒体库默认在 `成品/数据/`；通过 `--data <目录>` 可使用独立媒体库。媒体文件依然位于用户选择的来源目录。

## 项目结构

| 路径 | 用途 |
|---|---|
| `src/` | .NET 10 / WPF 应用、SQLite 索引及隔离测试 |
| `src/Assets/` | Fluent 图标、内置 AI 风景和许可明确的演示素材 |
| `工具/设计规范检查.ps1` | 静态设计检查 |
| `DESIGN_SYSTEM.md` | 统一设计规范 |
| `DESIGN_RESEARCH.md` | 设计依据与参考资料 |
| `docs/` | 架构、验证范围与第三方素材说明 |
| `.github/workflows/` | Windows 构建、静态检查和核心回归 |

照片、视频原件、数据库、缩略图、SDK、成品和本地验收截图不进入 Git。仓库只保留程序必需且来源明确的内置素材。工作区中的旧设计草案及完整验收证据由本地独立保留。

## 继续开发

先阅读 [开发约定](AGENTS.md)、[设计规范](DESIGN_SYSTEM.md) 和 [贡献说明](CONTRIBUTING.md)。功能说明见 [使用说明](使用说明.md)，当前验证基线见 [验证范围](docs/VALIDATION.md)。CI 的核心检查不能替代实际 WPF 窗口、键盘、浅深主题和文件安全验收。

代码尚未指定开源许可证；托管到 GitHub 不表示已授予开源使用许可。第三方素材仍遵循各自许可证，详见 [第三方说明](docs/THIRD_PARTY_NOTICES.md)。
