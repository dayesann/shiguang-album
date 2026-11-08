using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;

namespace Shiguang;

public sealed class SegmentChoice : Button
{
    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(SegmentChoice), new PropertyMetadata(false));
    public bool IsSelected { get => (bool)GetValue(IsSelectedProperty); set => SetValue(IsSelectedProperty, value); }
    protected override AutomationPeer OnCreateAutomationPeer() => new ChoicePeer(this);
    sealed class ChoicePeer(SegmentChoice owner) : ButtonAutomationPeer(owner), ISelectionItemProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.RadioButton;
        public override object? GetPattern(PatternInterface patternInterface) => patternInterface == PatternInterface.SelectionItem ? this : base.GetPattern(patternInterface);
        bool ISelectionItemProvider.IsSelected => owner.IsSelected;
        IRawElementProviderSimple? ISelectionItemProvider.SelectionContainer => null;
        void ISelectionItemProvider.Select() => owner.Dispatcher.Invoke(() => owner.RaiseEvent(new RoutedEventArgs(ClickEvent)));
        void ISelectionItemProvider.AddToSelection() => ((ISelectionItemProvider)this).Select();
        void ISelectionItemProvider.RemoveFromSelection() { if (owner.IsSelected) throw new InvalidOperationException("请选择另一个时间视图。"); }
    }
}
