using System.Windows;
using System.Windows.Media;

namespace Shiguang;

internal static class UiElements
{
    public static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); if (child is T t) yield return t;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
