# 发布与软件更新

源码、Issue 和 Windows x64 便携包统一在 [主仓库](https://github.com/dayesann/shiguang-album)。从 0.2.14 起，应用只检查 [主仓库的正式 Release](https://github.com/dayesann/shiguang-album/releases/latest)。用户无需登录、令牌或管理员权限。把代码推到 GitHub 不会触发客户端更新；发布正式 Release 才会触发。

## 0.2.13 过渡

0.2.13 内置的更新地址是 `dayesann/shiguang-album-downloads`。应先在主仓库发布 0.2.14，再把**同一份** ZIP 和 SHA-256 校验文件放到旧仓库的 `v0.2.14` 正式 Release，并标为最新。之后保持旧仓库和过渡附件可访问；从 0.2.14 开始，后续版本只发主仓库。0.2.12 及以前仍须手动换到 0.2.14 或更高版本。

## 后续发版

1. 提高 `src/Shiguang.csproj` 的三段式版本号，补充 `docs/RELEASE-<版本>.md`，完成界面和回归验收。
2. 合并到 `main` 后，从主仓库 Actions 手动运行 **Publish Windows download**。流程构建、打包，并用实际 ZIP 执行核心自测，通过后在同一仓库发版。流程使用 GitHub Actions 自带令牌，无需配置跨仓库凭据。
3. 正式 Release 使用 `v<版本>`，附件必须为 `ShiguangAlbum-<版本>-win-x64.zip`。对照 GitHub 记录的 SHA-256 和发布附件；不要修改已发布安装包。修正发布请用更高版本。

## 排障与回退

- “设置 → 关于”可手动检查。渠道未发布、离线或包校验失败时，应用继续运行，可稍后重试。
- 便携目录必须完整且具有写权限。升级保留旧的 `运行程序-<旧版本>` 和并排的 `数据`，更新不会替换或编辑媒体库文件。手动启动时可以通过 `--skip-update-redirect` 临时跳过旧快捷方式的版本跳转；降级读取数据前应确认版本兼容，必要时使用更新前的备份。
- 更新信任官方下载仓库及 GitHub 返回的附件摘要。SHA-256 校验用于识别损坏或与发布记录不一致的文件，并非独立的代码签名。

## 0.2.13 验收（2026-11-15）

“设置 → 关于”使用设计系统的设置行、Fluent 更新图标和现有复选框。实际运行 WPF 后核对了 [更新前](images/updates-before-0.2.12.png)、[浅色和按钮焦点](images/updates-light-0.2.13.png)、[检查中](images/updates-checking-0.2.13.png)、[深色最小窗口和离线](images/updates-dark-offline-0.2.13.png) 以及 [跟随系统和发现新版](images/updates-system-0.2.13.png)。

Windows 构建、打包、20 项静态设计检查和 6 组核心回归通过。使用实际发布 ZIP 和模拟的 GitHub 响应执行 15 项更新检查，包含取消、错误摘要、异常解压路径、保留数据及旧程序。独立演示库的 WPF 设置验证通过 32 项，包括键盘焦点、三种主题和更新状态。没有测试物理键盘、所有 DPI 或在线跨电脑升级；公开下载仓库上线后仍需进行跨电脑验证。
