using System.Windows;

namespace Shiguang;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        string? Option(string key) { int i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        try
        {
            bool traceLifecycle = args.Any(arg => arg.EndsWith("-smoke", StringComparison.Ordinal));
            var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            var home = Path.GetFileName(baseDir).StartsWith("运行程序", StringComparison.Ordinal) ? Path.GetDirectoryName(baseDir)! : baseDir;
            bool demo = args.Contains("--demo");
            var restartArgs = (string[])args.Clone();
            bool normalLaunch = true;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--settings") continue;
                if (args[i] is "--data" or "--exclude" && i + 1 < args.Length)
                {
                    i++;
                    restartArgs[i] = Path.GetFullPath(args[i]);
                }
                else normalLaunch = false;
            }
            bool portable = string.Equals(Path.GetFileName(baseDir), PortableUpdates.FolderName(Version.Parse(AppInfo.Version)), StringComparison.OrdinalIgnoreCase);
            if (normalLaunch && portable && PortableUpdates.ActiveExecutable(home, Version.Parse(AppInfo.Version)) is string newer)
            {
                try { PortableUpdates.Launch(newer, restartArgs); return 0; }
                catch (System.ComponentModel.Win32Exception) { /* Keep the working version available. */ }
                catch (IOException) { }
            }
            var data = Option("--data") ?? Path.Combine(home, demo ? "演示媒体库-v0.2" : "数据");
            AppPaths.Init(data, Option("--exclude") ?? (demo ? baseDir : home));
            bool activationProbe = args.Contains("--activation-probe");
            void ActivationTrace(string text) { if (activationProbe) File.AppendAllText(Path.Combine(AppPaths.Data, "activation-" + Environment.ProcessId + ".log"), text + Environment.NewLine); }
            ActivationTrace("Startup");
            void Lifecycle(string text) { if (traceLifecycle) File.AppendAllText(Path.Combine(AppPaths.Data, "lifecycle.log"), $"{DateTime.Now:O} pid={Environment.ProcessId} {text}\n"); }
            if (args.Contains("--worker-server")) return PreviewWorker.Serve();
            if (args.Contains("--safety-test")) return SafetyRegression.Run();
            if (args.Contains("--viewer-test")) return ViewerTests.Run().GetAwaiter().GetResult();
            if (args.Contains("--migration-test")) return MigrationTest.Run();
            if (args.Contains("--package-test")) return PackageTests.Run();
            if (args.Contains("--update-test")) return UpdateTests.Run(Option("--update-archive")).GetAwaiter().GetResult();
            if (args.Contains("--tray-verify")) return TrayTests.Verify();
            if (Option("--probe") is string probe)
            {
                var before = LibraryService.HashFile(probe);
                var photo = Imaging.Read(probe, Path.GetDirectoryName(probe)!);
                var after = LibraryService.HashFile(probe);
                File.WriteAllText(Path.Combine(AppPaths.Data, "decode-probe.json"), System.Text.Json.JsonSerializer.Serialize(new { Decoded = photo.Error.Length == 0, OriginalUnchanged = before == after, photo.Width, photo.Height, photo.DateSource, photo.Error }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                return before == after && photo.Error.Length == 0 ? 0 : 1;
            }
            if (args.Contains("--self-test")) return SelfTest.Run(Option("--test-root") ?? Path.Combine(AppPaths.Data, "test-run"));
            if (Option("--cross-test") is string cross) return ExtendedTests.CrossVolume(cross);
            if (args.Contains("--scale-test")) return ExtendedTests.Scale();
            if ((Option("--play-video") ?? Option("--video-smoke")) is string || Option("--video-request") != null)
            {
                VideoRequest request;
                if (Option("--video-request") is string requestFile)
                {
                    request = System.Text.Json.JsonSerializer.Deserialize<VideoRequest>(File.ReadAllText(requestFile)) ?? throw new IOException("播放请求无效");
                    File.Delete(requestFile);
                }
                else request = new(Option("--play-video") ?? Option("--video-smoke")!);
                var playerApp = new Application();
                playerApp.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/拾光照片;component/Resources.xaml", UriKind.Relative) });
                ThemeManager.Initialize();
                System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
                return SafeShutdown.Finish(playerApp.Run(new VideoPlayerWindow(request, Option("--player-pipe") ?? (args.Contains("--video-smoke") ? "shiguang-smoke-" + Guid.NewGuid().ToString("N") : null), args.Contains("--video-smoke"))));
            }
            FileStream sessionStream;
            try { sessionStream = new FileStream(Path.Combine(AppPaths.Data, "session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33)
            {
                ActivationTrace("Existing library lock: " + error.HResult);
                if (LibraryInstance.TryRestoreExisting(AppPaths.Data)) { ActivationTrace("Restore acknowledged"); return 0; }
                ActivationTrace("Restore not acknowledged");
                if (args.Contains("--activation-probe")) return 2;
                throw new IOException("此媒体库已在另一个窗口或旧版本中打开。请从系统托盘恢复窗口；旧版本需要先正常退出。", error);
            }
            using var session = sessionStream;
            ActivationTrace("New owner");
            using var instance = new LibraryInstance(AppPaths.Data);
            if (File.Exists(Path.Combine(AppPaths.Data, "library.sqlite"))) LibraryPackage.Validate(AppPaths.Data);
            LibraryPackage.RelocateIfNeeded(AppPaths.Data);
            using var catalog = new Catalog();
            if (demo) Demo.Seed(catalog);
            if (args.Contains("--recovery-smoke")) RecoverySmoke.Prepare(catalog);
            var app = new Application();
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            if (traceLifecycle) UiSmoke.EnableTrace();
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/拾光照片;component/Resources.xaml", UriKind.Relative) });
            ThemeManager.Initialize();
            app.DispatcherUnhandledException += (_, e) => { LibraryService.Log("界面", "", e.Exception); MessageBox.Show(e.Exception.Message, "拾光照片"); e.Handled = true; };
            AppDomain.CurrentDomain.UnhandledException += (_, e) => { try { File.AppendAllText(Path.Combine(AppPaths.Data, "fatal.log"), DateTime.Now.ToString("O") + " " + e.ExceptionObject + Environment.NewLine); } catch { } };
            var window = new MainWindow(catalog);
            window.ConfigureUpdates(home, baseDir, normalLaunch);
            window.SafeClose = code =>
            {
                Lifecycle("Final window closing; background work stopped");
                window.StopUpdates(); window.StopTray(); instance.Dispose();
                ThemeManager.Stop(); catalog.Dispose(); Lifecycle("Database closed");
                session.Dispose(); Lifecycle("Session closed; controlled process exit");
                if (window.RestartForUpdate is string updated)
                {
                    try { PortableUpdates.Launch(updated, restartArgs); }
                    catch (Exception e) { MessageBox.Show("新版未能启动。请从原快捷方式重试。\n\n" + e.Message, "拾光照片"); }
                }
                SafeShutdown.Finish(code);
            };
            window.Title = $"时光相册 {AppInfo.Version} · " + (demo ? "演示媒体库" : "本地媒体库");
            window.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/拾光照片;component/Assets/Shiguang.ico"));
            bool backgroundMode = !args.Any(arg => arg.EndsWith("-smoke", StringComparison.Ordinal)) || args.Contains("--tray-smoke");
            if (backgroundMode) window.Loaded += (_, _) => window.EnableTray();
            instance.OnRestore(window, window.RestoreFromTray);
            app.SessionEnding += (_, _) => window.PrepareSessionEnd();
            app.Exit += (_, _) => { window.StopTray(); instance.Dispose(); };
            if (args.Contains("--ui-smoke")) window.Loaded += async (_, _) => await UiSmoke.Run(window, catalog);
            if (args.Contains("--gallery-smoke")) window.Loaded += async (_, _) => await GalleryTests.Run(window, catalog);
            if (args.Contains("--slideshow-smoke")) window.Loaded += async (_, _) => await SlideshowTests.Run(window, catalog);
            if (args.Contains("--recovery-smoke")) window.Loaded += async (_, _) => await RecoverySmoke.Run(window, catalog);
            if (args.Contains("--components")) window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(() => new ComponentGalleryWindow { Owner = window }.ShowDialog());
            if (args.Contains("--component-smoke")) window.Loaded += async (_, _) => await ComponentAcceptance.Run(window, catalog);
            bool trayTestStarted = false;
            if (args.Contains("--tray-smoke")) window.Loaded += async (_, _) => { if (trayTestStarted) return; trayTestStarted = true; await TrayTests.Run(window, catalog); };
            if (args.Contains("--settings-smoke")) window.Loaded += async (_, _) => await SettingsSmoke.Run(window, catalog);
            if (args.Contains("--design-smoke")) window.Loaded += async (_, _) => await DesignAcceptance.Run(window, catalog);
            else if (args.Contains("--flow-smoke")) window.Loaded += async (_, _) => await FlowTests.Run(window, catalog);
            else if (args.Contains("--settings")) window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(() => new SettingsWindow(window, catalog) { Owner = window }.ShowDialog());
            int exitCode = app.Run(window);
            Lifecycle("Application.Run returned");
            catalog.Dispose();
            Lifecycle("Database closed");
            session.Dispose();
            Lifecycle("Session closed; Main returning");
            return SafeShutdown.Finish(exitCode);
        }
        catch (Exception e)
        {
            if (args.Contains("--activation-probe")) { File.WriteAllText(Path.Combine(AppPaths.Data, "activation-" + Environment.ProcessId + "-error.log"), e.ToString()); return 2; }
            if (args.Contains("--worker-server")) { try { File.AppendAllText(Path.Combine(AppPaths.Data, "worker-errors.log"), e.ToString() + Environment.NewLine); } catch { } return 1; }
            if (args.Contains("--self-test")) { File.WriteAllText(Path.Combine(AppPaths.Data, "self-test-fatal.txt"), e.ToString()); return 1; }
            MessageBox.Show("无法启动拾光照片。请检查是否已打开相同照片库，或数据目录是否可写。\n\n" + e.Message, "拾光照片", MessageBoxButton.OK, MessageBoxImage.Error); return 1;
        }
    }
}
