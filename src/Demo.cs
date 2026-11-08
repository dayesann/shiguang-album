namespace Shiguang;

public static class Demo
{
    public static void Seed(Catalog catalog)
    {
        if (catalog.Count() > 0) return;
        var root = AppPaths.Data + "-素材"; Directory.CreateDirectory(root); catalog.AddRoot(root);
        var photos = new List<Photo>();
        var names = new[] { "海岸的早晨", "山间漫步", "日落时分", "旅途片刻", "小城假日", "远山与风", "夏日记忆", "散步途中", "旧日风景", "蓝色时刻", "慢慢生活", "光的形状", "周末出发", "路过春天", "好天气", "回家的路", "追逐落日", "晴空下", "旧时光", "第一次远行", "海边假期", "初春", "留念", "山里的人家" };
        for (int i = 0; i < names.Length; i++)
        {
            var path = Path.Combine(root, names[i] + ".jpg"); if (!File.Exists(path)) SelfTest.CreateImage(path, i + 1, true);
            var p = MediaIndexer.Skeleton(path, root); catalog.Upsert(p);
            int year = i < 12 ? 2026 : i < 18 ? 2020 : 2015;
            catalog.SetDate([p], new DateTime(year, 10, i < 12 ? 28 - i / 5 : 3 + i % 4)); photos.Add(p);
        }
        var duplicatePath = Path.Combine(root, "海岸的早晨-副本.jpg");
        if (!File.Exists(duplicatePath)) File.Copy(Path.Combine(root, names[0] + ".jpg"), duplicatePath);
        var duplicate = MediaIndexer.Skeleton(duplicatePath, root);
        catalog.Upsert(duplicate); catalog.SetDate([duplicate], new DateTime(2026, 10, 28)); photos.Insert(1, duplicate);
        var similarPath = Path.Combine(root, "海岸的早晨-低清副本.jpg");
        if (!File.Exists(similarPath))
        {
            var reduced = Imaging.Decode(Path.Combine(root, names[0] + ".jpg"), 320);
            var encoder = new System.Windows.Media.Imaging.JpegBitmapEncoder { QualityLevel = 42 };
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(reduced));
            using var output = File.Create(similarPath);
            encoder.Save(output);
        }
        var similar = MediaIndexer.Skeleton(similarPath, root);
        catalog.Upsert(similar); catalog.SetDate([similar], new DateTime(2026, 10, 28)); photos.Insert(2, similar);
        foreach (var entry in new[] { ("雪山全景.jpg", true), ("行程截图.png", false), ("路线截图.png", false) })
        {
            var path = Path.Combine(root, entry.Item1); if (!File.Exists(path)) ViewerTests.CreateSpecial(path, entry.Item2);
            var p = MediaIndexer.Skeleton(path, root); catalog.Upsert(p); catalog.SetDate([p], new DateTime(2026, 10, 28)); photos.Insert(entry.Item2 ? 4 : Math.Min(7, photos.Count), p);
        }
        var video = Path.Combine(AppContext.BaseDirectory, "Assets", "Demo", "flower.mp4");
        if (File.Exists(video))
        {
            var path = Path.Combine(root, "花间片刻.mp4"); if (!File.Exists(path)) File.Copy(video, path);
            var p = MediaIndexer.Skeleton(path, root); catalog.Upsert(p); catalog.SetDate([p], new DateTime(2026, 10, 28)); photos.Insert(1, p);
        }
        var album = catalog.CreateAlbum("家庭旅行"); catalog.AddToAlbum(album, photos.Take(15));
        var other = catalog.CreateAlbum("2015 · 最初的回忆"); catalog.AddToAlbum(other, photos.TakeLast(6));
        catalog.Favorite(photos.Where((_, i) => i % 4 == 0), true);
        PreviewPipeline.Enrich(catalog, new Progress<JobProgress>(), CancellationToken.None).GetAwaiter().GetResult();
    }
}
