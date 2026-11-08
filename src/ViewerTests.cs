using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Shiguang;

public static class ViewerTests
{
    public static async Task<int> Run()
    {
        var checks = new List<string>(); string? failure = null;
        void Check(bool value, string text) { if (!value) throw new IOException(text); checks.Add(text); }
        try
        {
            string root = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(AppPaths.Data)!, "source");
            if (Directory.Exists(root)) throw new IOException("测试需要新的独立目录"); Directory.CreateDirectory(root);
            var a = System.IO.Path.Combine(root, "IMG_20150615.png"); SelfTest.CreateImage(a, 1, false);
            var b = System.IO.Path.Combine(root, "IMG_20201003.png"); SelfTest.CreateImage(b, 2, false);
            var screenshot = System.IO.Path.Combine(root, "截图_20201004.png"); CreateSpecial(screenshot, false);
            var panorama = System.IO.Path.Combine(root, "全景_20201005.jpg"); CreateSpecial(panorama, true);
            var bad = System.IO.Path.Combine(root, "损坏_20140101.heic"); File.WriteAllText(bad, "synthetic decoder failure");
            var sample = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Demo", "flower.mp4");
            string? video = null;
            if (File.Exists(sample)) { video = System.IO.Path.Combine(root, "VID_20201003.mp4"); File.Copy(sample, video); }
            var originals = Directory.GetFiles(root).ToDictionary(x => x, LibraryService.HashFile);
            using var catalog = new Catalog(); var indexer = new MediaIndexer(catalog); var progress = new Progress<JobProgress>();
            var first = indexer.Scan(root, progress, default);
            Check(first.Added == originals.Count, "file-only indexing discovers images, videos, panoramas and screenshots");
            Check(catalog.PendingPreviews().Count == originals.Count, "index is usable before native thumbnail generation");
            Check(catalog.Browse(new(Year: "2015")).Total == 1, "year jump resolves filename date before decoding");
            Check(catalog.Browse(new(Year: "2020", Month: "10", Day: "04")).Total == 1, "precise year-month-day query");
            Check(catalog.Buckets(new(), false).Any(x => x.Key == "2015"), "year overview contains old memories");
            Check(catalog.Buckets(new(Year: "2020"), true).Single().Key == "2020-10", "month overview scopes to selected year");
            await PreviewPipeline.Enrich(catalog, progress, default);
            Check(catalog.ByPath(a)!.PreviewState == "ready" && File.Exists(catalog.ByPath(a)!.ThumbPath), "isolated image worker generates thumbnail");
            Check(catalog.ByPath(bad)!.PreviewState == "error", "corrupt HEIC leaves a recoverable preview-error state");
            var backfill = catalog.ByPath(b)!;
            backfill.VisualHash = "";
            catalog.Upsert(backfill);
            await PreviewPipeline.Enrich(catalog, progress, default);
            Check(catalog.ById(backfill.Id)!.VisualHash.Length == 16, "legacy-ready previews backfill a visual fingerprint without reopening the original");
            Check(catalog.Browse(new(Kind: "screenshot")).Total == 1, "screenshot view is independent from PNG format");
            Check(catalog.Browse(new(Kind: "panorama")).Items.Single().Path == panorama, "wide panorama is represented as its own view");
            Check(catalog.Browse(new(Kind: "image")).Total == 5, "image total includes screenshot and panorama views");
            if (video != null)
            {
                var videoPhoto = catalog.ByPath(video)!;
                Check(videoPhoto.IsVideo && videoPhoto.Duration > 0 && videoPhoto.VisualHash.Length == 16, "native video metadata contains duration and a visual fingerprint");
                Check(File.Exists(videoPhoto.ThumbPath), "video thumbnail extracted in worker process");
            }
            var same = indexer.Scan(root, progress, default); Check(same.Added == 0 && same.Skipped >= originals.Count - 1, "incremental scan reuses valid previews");
            var pA = catalog.ByPath(a)!; var pB = catalog.ByPath(b)!;
            string album = catalog.CreateAlbum("家庭旅行"); var media = catalog.AllActive().Where(p => p.Error.Length == 0).ToList(); catalog.AddToAlbum(album, media);
            Check(catalog.Browse(new("album", album)).Total == media.Count, "one virtual album holds mixed media");
            Check(catalog.Browse(new("album", album, "panorama")).Total == 1, "media filtering works inside mixed albums");
            catalog.RemoveFromAlbum(album, [pA]); Check(catalog.Browse(new("album", album)).Total == media.Count - 1 && File.Exists(a), "removing album item only removes reference");
            catalog.SetTags([pB], "国庆 旅行"); Check(catalog.Browse(new(Search: "国庆")).Total == 1, "tag search queries index only");
            catalog.SetKind([pB], "screenshot"); Check(catalog.ById(pB.Id)!.ManualKind, "manual classification is explicit");
            catalog.SetDate([pB], new DateTime(2014, 12, 31));
            var p = catalog.ById(pB.Id)!; p.PreviewState = "pending"; catalog.Upsert(p); await PreviewPipeline.Enrich(catalog, progress, default);
            Check(catalog.ById(pB.Id)!.Taken.StartsWith("2014-12-31") && catalog.ById(pB.Id)!.ContentKind == "screenshot", "manual date and classification survive metadata refresh");
            var stale = catalog.ById(pA.Id)!; catalog.SetTags([pA], "家人"); catalog.Favorite([pA], true); catalog.UpdatePreview(stale);
            Check(catalog.ById(pA.Id) is { Tags: "家人", Favorite: true }, "background preview updates preserve concurrent tags and favorites");
            var staleIndex = MediaIndexer.Skeleton(a, root, catalog.ById(pA.Id)); catalog.SetDate([pA], new DateTime(2015, 6, 16)); catalog.SaveIndexBatch([staleIndex]);
            Check(catalog.ById(pA.Id)!.Taken.StartsWith("2015-06-16"), "background index flush preserves a newly confirmed date");
            var large = await PreviewSession.Large(pA); Check(File.Exists(large) && AppPaths.Within(large, AppPaths.Data), "full-image preview is a cache, not a copied source directory");
            using (var session = new PreviewSession())
            {
                var failed = await session.Request(new("test-exit", pA), default); Check(!failed.Success, "unexpected decoder process exit is contained");
                var recovered = await session.Request(new("thumb", pA), default); Check(recovered.Success, "next file succeeds after worker restart");
            }
            catalog.DeleteAlbum(album); Check(catalog.Count() == originals.Count, "deleting an album retains every indexed original");
            Check(originals.All(x => File.Exists(x.Key) && LibraryService.HashFile(x.Key) == x.Value), "all original file paths and bytes remain unchanged end-to-end");
            Check(Directory.GetFiles(root).Length == originals.Count && !Directory.Exists(System.IO.Path.Combine(root, ".shiguang-trash")), "viewer creates no source folders, copies, or quarantine");
        }
        catch (Exception e) { failure = e.ToString(); }
        File.WriteAllText(System.IO.Path.Combine(AppPaths.Data, "viewer-test-results.json"), JsonSerializer.Serialize(new { Passed = failure == null, Count = checks.Count, Checks = checks, Failure = failure }, new JsonSerializerOptions { WriteIndented = true }));
        return failure == null ? 0 : 1;
    }
    public static void CreateSpecial(string path, bool panorama)
    {
        int width = panorama ? 3600 : 420, height = panorama ? 600 : 1000;
        var v = new DrawingVisual();
        using (var dc = v.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(115, 177, 215)), null, new Rect(0, 0, width, height));
            if (panorama)
            {
                dc.DrawEllipse(Brushes.LightGoldenrodYellow, null, new Point(3000, 130), 70, 70);
                for (int i = 0; i < 16; i++) dc.DrawEllipse(new SolidColorBrush(Color.FromRgb((byte)(70 + i * 4), (byte)(110 + i * 3), (byte)(115 + i * 2))), null, new Point(i * 250, 650), 390, 280 + i % 4 * 25);
            }
            else
            {
                dc.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(0, 0, width, height));
                var typeface = new Typeface("Microsoft YaHei UI");
                void Text(string s, int y, int size, Brush brush) => dc.DrawText(new FormattedText(s, System.Globalization.CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight, typeface, size, brush, 1), new Point(30, y));
                Text("旅行行程", 35, 32, Brushes.Black);
                for (int i = 0; i < 5; i++) { dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(228, 238, 247)), null, new Rect(20, 120 + i * 158, 380, 133)); Text($"第 {i + 1} 天", 137 + i * 158, 23, Brushes.DimGray); Text(i % 2 == 0 ? "湖边散步 · 记录风景" : "山间徒步 · 返程", 185 + i * 158, 19, Brushes.SlateGray); }
            }
        }
        var b = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); b.Render(v);
        BitmapEncoder enc = panorama ? new JpegBitmapEncoder { QualityLevel = 85 } : new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(b)); using var f = File.Create(path); enc.Save(f);
    }
}
