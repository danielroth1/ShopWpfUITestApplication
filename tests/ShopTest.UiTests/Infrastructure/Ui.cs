using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace ShopTest.UiTests.Infrastructure;

/// <summary>
/// In-process UI driver: finds controls by <c>AutomationId</c> and drives them through the same
/// automation peers that UI Automation (and therefore Windows-MCP) uses over the wire.
/// </summary>
/// <remarks>
/// Because the peers run in-process, an interaction costs microseconds instead of the seconds a
/// cross-process accessibility call plus an agent round-trip costs. No window is created, so nothing
/// appears on screen and no desktop lease is taken.
/// </remarks>
internal static class Ui
{
    public static readonly Size Screen = new(1280, 900);

    /// <summary>
    /// Applies <paramref name="dataContext"/>, runs a full layout pass and returns the element.
    /// No <c>Window</c> or HWND is involved.
    /// </summary>
    public static T Show<T>(T view, object? dataContext = null) where T : FrameworkElement
    {
        // Only override when a context is supplied: content hosted inside MainWindow must keep the
        // DataContext it inherits from the window.
        if (dataContext is not null)
        {
            view.DataContext = dataContext;
        }

        view.Measure(Screen);
        view.Arrange(new Rect(new Point(0, 0), Screen));
        view.UpdateLayout();
        return view;
    }

    /// <summary>Re-runs layout after a change. Call after mutating state if you need fresh bounds.</summary>
    public static void Relayout(FrameworkElement view)
    {
        view.Measure(Screen);
        view.Arrange(new Rect(new Point(0, 0), Screen));
        view.UpdateLayout();
    }

    public static IEnumerable<FrameworkElement> Descendants(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement element)
            {
                yield return element;
            }

            foreach (FrameworkElement descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>Controls the automation contract requires an <c>AutomationId</c> on.</summary>
    public static IEnumerable<FrameworkElement> Interactives(FrameworkElement root) =>
        Descendants(root).Where(IsInteractive);

    public static bool IsInteractive(FrameworkElement element) => element switch
    {
        ButtonBase => true,      // Button, ToggleButton, CheckBox, RadioButton, RepeatButton
        TextBoxBase => true,     // TextBox, RichTextBox
        PasswordBox => true,
        // The app's lists are ItemsControls; Selector, ListBox, ListView, ComboBox, TabControl and
        // DataGrid all derive from it, so this one arm covers every list-shaped control.
        ItemsControl => true,
        Expander => true,
        Slider => true,
        DatePicker => true,
        Calendar => true,
        _ => false,
    };

    public static FrameworkElement? Find(FrameworkElement root, string automationId) =>
        Descendants(root).FirstOrDefault(e => AutomationProperties.GetAutomationId(e) == automationId);

    public static FrameworkElement Require(FrameworkElement root, string automationId) =>
        Find(root, automationId)
        ?? throw new InvalidOperationException(
            $"No element with AutomationId '{automationId}' under {root.GetType().Name}.");

    /// <summary>Click by <c>AutomationId</c>, using Invoke (or Toggle for toggles).</summary>
    public static void Click(FrameworkElement root, string automationId)
    {
        FrameworkElement element = Require(root, automationId);
        AutomationPeer peer = PeerFor(element);

        if (peer is IToggleProvider toggle)
        {
            toggle.Toggle();
            return;
        }

        var invoke = peer.GetPattern(PatternInterface.Invoke) as IInvokeProvider
            ?? throw new InvalidOperationException(
                $"Element '{automationId}' ({element.GetType().Name}) exposes neither Invoke nor Toggle.");

        invoke.Invoke();
    }

    /// <summary>Types into a text box by <c>AutomationId</c> through the value pattern.</summary>
    public static void SetText(FrameworkElement root, string automationId, string value)
    {
        FrameworkElement element = Require(root, automationId);
        var provider = PeerFor(element).GetPattern(PatternInterface.Value) as IValueProvider
            ?? throw new InvalidOperationException(
                $"Element '{automationId}' ({element.GetType().Name}) does not expose the value pattern.");

        provider.SetValue(value);
    }

    /// <summary>Visible text of a text block, button or input, for assertions.</summary>
    public static string Text(FrameworkElement root, string automationId)
    {
        FrameworkElement element = Require(root, automationId);
        return element switch
        {
            TextBlock block => block.Text,
            TextBox box => box.Text,
            PasswordBox box => box.Password,
            ContentControl { Content: string content } => content,
            ContentControl control => control.Content?.ToString() ?? string.Empty,
            _ => throw new InvalidOperationException(
                $"Element '{automationId}' ({element.GetType().Name}) has no text."),
        };
    }

    public static bool IsEnabled(FrameworkElement root, string automationId) =>
        Require(root, automationId).IsEnabled;

    /// <summary>
    /// Whether the element and every ancestor are <see cref="Visibility.Visible"/>.
    /// Uses the visibility chain rather than <see cref="UIElement.IsVisible"/>, which is always false
    /// for elements that are not attached to a presentation source - i.e. for everything here.
    /// </summary>
    public static bool IsEffectivelyVisible(FrameworkElement root, string automationId)
    {
        DependencyObject? node = Require(root, automationId);
        while (node is not null)
        {
            if (node is UIElement { Visibility: not Visibility.Visible })
            {
                return false;
            }

            node = VisualTreeHelper.GetParent(node);
        }

        return true;
    }

    private static AutomationPeer PeerFor(FrameworkElement element) =>
        UIElementAutomationPeer.CreatePeerForElement(element)
        ?? new FrameworkElementAutomationPeer(element);
}
