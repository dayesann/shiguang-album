# 架构概览

- **入口与生命周期**：`Program.cs`、`MainWindow.Lifetime.cs`、`TrayController.cs`。同库 session.lock 防止多个写入者；注册窗口消息与库内短期请求用于恢复已有窗口。
- **界面与规范**：WPF / XAML，`Resources.xaml`、`Controls.xaml`、`UiMetrics.cs` 统一主题、状态和尺寸；`GalleryLayout.cs` 计算按原比例组成的照片行。
- **索引**：`MediaIndexer.cs` 递归枚举并分批提交；`Catalog` 系列负责 SQLite 数据。`NativeDb.cs` 使用 Windows 的 SQLite 接口。
- **预览**：`Imaging.cs`、`PreviewWorker.cs`、`ThumbnailImage.cs` 负责解码、后台预览和缓存。扫描不要求把全部原图加载进内存。
- **清理**：`LibraryService.cs` 负责完全重复校验与近似候选；清理默认移动到可恢复区，原图操作前显示范围。
- **相册与迁移**：相册保存媒体引用；`AlbumBackup.cs`、`LibraryPackage.cs`、`CatalogRelink.cs` 负责备份、完整迁移与路径重连。
- **播放器**：`VideoLauncher` / `VideoSession.cs` 与独立 WPF 播放窗口；格式支持取决于系统解码器。

当前核心实现与测试位于同一项目，通过命令行测试入口生成隔离素材。后续可逐步拆分测试项目，但必须保留文件安全、旧库升级、迁移和退出时持久化的回归覆盖。
