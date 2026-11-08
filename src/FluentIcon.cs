using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;

namespace Shiguang;
public sealed class FluentIcon : FrameworkElement
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(string), typeof(FluentIcon), new FrameworkPropertyMetadata("image", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ForegroundProperty = Control.ForegroundProperty.AddOwner(typeof(FluentIcon), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));
    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    static readonly Dictionary<string, Geometry> Icons = [];
    public FluentIcon() { Width = Height = 22; SnapsToDevicePixels = true; }
    protected override void OnRender(DrawingContext dc)
    {
        if (!Icons.TryGetValue(Kind, out var geometry))
        {
            using var file = Assembly.GetExecutingAssembly().GetManifestResourceStream("Shiguang.Assets.Fluent." + Kind + ".svg");
            if (file == null) return;
            var xml = XDocument.Load(file); var group = new GeometryGroup();
            foreach (var p in xml.Descendants().Where(e => e.Name.LocalName == "path"))
            {
                if (p.Attribute("d") is not { } data) continue;
                var path = PathGeometry.CreateFromGeometry(Geometry.Parse(data.Value));
                path.FillRule = p.Attribute("fill-rule")?.Value == "evenodd" ? FillRule.EvenOdd : FillRule.Nonzero; group.Children.Add(path);
            }
            group.Freeze(); geometry = group; Icons[Kind] = geometry;
        }
        dc.PushTransform(new ScaleTransform(ActualWidth / 24, ActualHeight / 24)); dc.DrawGeometry(Foreground, null, geometry); dc.Pop();
    }
}
