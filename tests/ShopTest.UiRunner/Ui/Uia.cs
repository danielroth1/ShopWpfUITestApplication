using System.Windows.Automation;
using ShopTest.UiRunner.Interop;

namespace ShopTest.UiRunner.Ui;

/// <summary>
/// Thin UIA3 wrapper: resolve targets the way the scenarios declare them, then act on them with
/// patterns where possible and injected input only as a fallback. When this runs inside a sandbox the
/// injected input is confined to the guest desktop.
/// </summary>
internal static class Uia
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(150);

    public static IReadOnlyList<AutomationElement> TopWindows(int processId)
    {
        var windows = new List<AutomationElement>();

        try
        {
            var condition = new PropertyCondition(AutomationElement.ProcessIdProperty, processId);
            var found = AutomationElement.RootElement.FindAll(TreeScope.Children, condition);

            foreach (AutomationElement window in found)
            {
                windows.Add(window);
            }
        }
        catch (Exception exception) when (exception is ElementNotAvailableException or InvalidOperationException)
        {
            // Process went away mid-query.
        }

        return windows;
    }

    public static string? Name(AutomationElement element)
    {
        try
        {
            return element.Current.Name;
        }
        catch (Exception exception) when (IsTransient(exception))
        {
            return null;
        }
    }

    public static string? AutomationId(AutomationElement element)
    {
        try
        {
            return element.Current.AutomationId;
        }
        catch (Exception exception) when (IsTransient(exception))
        {
            return null;
        }
    }

    public static bool IsVisible(AutomationElement element)
    {
        try
        {
            return !element.Current.IsOffscreen;
        }
        catch (Exception exception) when (IsTransient(exception))
        {
            return false;
        }
    }

    public static string Describe(AutomationElement element)
    {
        try
        {
            var type = element.Current.ControlType?.ProgrammaticName?.Replace("ControlType.", string.Empty, StringComparison.Ordinal);
            return $"{type}#{element.Current.AutomationId}('{element.Current.Name}')";
        }
        catch (Exception exception) when (IsTransient(exception))
        {
            return "<unavailable element>";
        }
    }

    public static AutomationElement? WaitForWindow(int processId, string? titleContains, TimeSpan timeout)
    {
        return WaitUntil(
            () => TopWindows(processId).FirstOrDefault(window =>
                IsWindowMatch(window, titleContains)),
            timeout);
    }

    private static bool IsWindowMatch(AutomationElement window, string? titleContains)
    {
        if (titleContains is null)
        {
            return true;
        }

        var name = Name(window);
        if (name is not null && name.Contains(titleContains, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(AutomationId(window), "app.window", StringComparison.OrdinalIgnoreCase);
    }

    public static AutomationElement? WaitForTarget(AutomationElement root, StepTarget target, TimeSpan timeout)
    {
        return WaitUntil(() => Find(root, target), timeout);
    }

    public static bool WaitEnabled(AutomationElement element, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (true)
        {
            try
            {
                if (element.Current.IsEnabled)
                {
                    return true;
                }
            }
            catch (Exception exception) when (IsTransient(exception))
            {
                return false;
            }

            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            Thread.Sleep(100);
        }
    }

    /// <summary>
    /// Waits until the visible tree stops changing. Screens load their data asynchronously, so a view can
    /// exist (and even expose its buttons) before its rows arrive; clicking in that window hits an
    /// empty state. The MCP pass was implicitly paced by its per-step latency - this makes the pace explicit.
    /// </summary>
    public static void WaitForQuiescence(int processId, TimeSpan maxWait, int quietMs = 200)
    {
        var deadline = DateTime.UtcNow + maxWait;
        var last = Signature(processId);
        var stableSince = DateTime.UtcNow;

        while (DateTime.UtcNow < deadline)
        {
            Thread.Sleep(120);
            var current = Signature(processId);

            if (string.Equals(current, last, StringComparison.Ordinal))
            {
                if ((DateTime.UtcNow - stableSince).TotalMilliseconds >= quietMs)
                {
                    return;
                }
            }
            else
            {
                last = current;
                stableSince = DateTime.UtcNow;
            }
        }
    }

    private static string Signature(int processId)
    {
        var builder = new System.Text.StringBuilder();

        foreach (var window in TopWindows(processId))
        {
            builder.Append(AutomationId(window)).Append('|').Append(Name(window)).Append(';');

            foreach (var element in Descendants(window))
            {
                if (!IsVisible(element))
                {
                    continue;
                }

                builder.Append(AutomationId(element)).Append(':').Append(Name(element)).Append(';');
            }
        }

        return builder.ToString();
    }

    public static AutomationElement? Find(AutomationElement root, StepTarget target)
    {
        if (!string.IsNullOrWhiteSpace(target.AutomationId))
        {
            if (string.Equals(AutomationId(root), target.AutomationId, StringComparison.OrdinalIgnoreCase))
            {
                return root;
            }

            var condition = new PropertyCondition(AutomationElement.AutomationIdProperty, target.AutomationId);

            try
            {
                return root.FindFirst(TreeScope.Descendants, condition);
            }
            catch (Exception exception) when (IsTransient(exception))
            {
                return null;
            }
        }

        if (!string.IsNullOrWhiteSpace(target.Text))
        {
            var exact = default(AutomationElement);
            var partial = default(AutomationElement);

            foreach (var element in Descendants(root).Prepend(root))
            {
                if (!IsVisible(element))
                {
                    continue;
                }

                var name = Name(element);
                if (name is null)
                {
                    continue;
                }

                if (string.Equals(name, target.Text, StringComparison.OrdinalIgnoreCase))
                {
                    exact ??= element;
                }
                else if (name.Contains(target.Text, StringComparison.OrdinalIgnoreCase))
                {
                    partial ??= element;
                }
            }

            return exact ?? partial;
        }

        if (target.Label is not null)
        {
            throw new NotSupportedException(
                "A label-only target comes from a Windows-MCP snapshot and cannot be resolved outside MCP. " +
                "Add automationId or text to the scenario step.");
        }

        throw new InvalidOperationException("Step target has no automationId, text or label.");
    }

    public static bool Click(AutomationElement element)
    {
        if (TryPattern<InvokePattern>(element, InvokePattern.Pattern, out var invoke))
        {
            invoke.Invoke();
            return true;
        }

        if (TryPattern<TogglePattern>(element, TogglePattern.Pattern, out var toggle))
        {
            toggle.Toggle();
            return true;
        }

        if (TryPattern<SelectionItemPattern>(element, SelectionItemPattern.Pattern, out var selection))
        {
            selection.Select();
            return true;
        }

        if (TryPattern<ExpandCollapsePattern>(element, ExpandCollapsePattern.Pattern, out var expand))
        {
            expand.Expand();
            return true;
        }

        if (TryPattern<ScrollItemPattern>(element, ScrollItemPattern.Pattern, out var scrollItem))
        {
            scrollItem.ScrollIntoView();
        }

        return ClickCenter(element);
    }

    public static bool ClickCenter(AutomationElement element)
    {
        try
        {
            var bounds = element.Current.BoundingRectangle;
            if (bounds.IsEmpty)
            {
                return false;
            }

            NativeMethods.MoveTo((int)(bounds.X + (bounds.Width / 2)), (int)(bounds.Y + (bounds.Height / 2)));
            NativeMethods.ClickLeft();
            return true;
        }
        catch (Exception exception) when (IsTransient(exception))
        {
            return false;
        }
    }

    public static bool SetText(AutomationElement element, string text, bool clear)
    {
        if (TryPattern<ValuePattern>(element, ValuePattern.Pattern, out var value))
        {
            value.SetValue(text);
            return true;
        }

        try
        {
            element.SetFocus();
        }
        catch (Exception exception) when (IsTransient(exception))
        {
            return false;
        }

        if (clear)
        {
            SendShortcut("Ctrl+A");
            SendVirtualKey(0x08);
        }

        NativeMethods.SendUnicodeText(text);
        return true;
    }

    public static void Scroll(AutomationElement element, int notches)
    {
        if (notches == 0)
        {
            return;
        }

        try
        {
            var bounds = element.Current.BoundingRectangle;
            if (!bounds.IsEmpty)
            {
                NativeMethods.MoveTo((int)(bounds.X + (bounds.Width / 2)), (int)(bounds.Y + (bounds.Height / 2)));
            }
        }
        catch (Exception exception) when (IsTransient(exception))
        {
            // Fall back to the current cursor position.
        }

        NativeMethods.Wheel(notches);
    }

    public static void SendShortcut(string keys)
    {
        var parts = keys.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            throw new ArgumentException($"Shortcut '{keys}' is empty.", nameof(keys));
        }

        var modifiers = new List<ushort>();
        ushort? key = null;

        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers.Add(0x11);
                    break;
                case "shift":
                    modifiers.Add(0x10);
                    break;
                case "alt":
                    modifiers.Add(0x12);
                    break;
                case "win":
                    modifiers.Add(0x5B);
                    break;
                default:
                    key = VirtualKey(part);
                    break;
            }
        }

        if (key is null)
        {
            throw new ArgumentException($"Shortcut '{keys}' has no key.", nameof(keys));
        }

        foreach (var modifier in modifiers)
        {
            NativeMethods.SendKeyDown(modifier, keyUp: false);
        }

        NativeMethods.SendKeyDown(key.Value, keyUp: false);
        NativeMethods.SendKeyDown(key.Value, keyUp: true);

        for (var index = modifiers.Count - 1; index >= 0; index--)
        {
            NativeMethods.SendKeyDown(modifiers[index], keyUp: true);
        }
    }

    private static void SendVirtualKey(ushort virtualKey)
    {
        NativeMethods.SendKeyDown(virtualKey, keyUp: false);
        NativeMethods.SendKeyDown(virtualKey, keyUp: true);
    }

    private static ushort VirtualKey(string name)
    {
        if (name.Length == 1)
        {
            var upper = char.ToUpperInvariant(name[0]);
            if (char.IsLetterOrDigit(upper))
            {
                return upper;
            }
        }

        return name.ToLowerInvariant() switch
        {
            "enter" or "return" => 0x0D,
            "tab" => 0x09,
            "escape" or "esc" => 0x1B,
            "space" => 0x20,
            "back" or "backspace" => 0x08,
            "delete" or "del" => 0x2E,
            "up" => 0x26,
            "down" => 0x28,
            "left" => 0x25,
            "right" => 0x27,
            "home" => 0x24,
            "end" => 0x23,
            "pageup" => 0x21,
            "pagedown" => 0x22,
            _ when name.Length >= 2 && (name[0] is 'f' or 'F') && int.TryParse(name.AsSpan(1), out var fn) && fn is >= 1 and <= 12
                => (ushort)(0x70 + fn - 1),
            _ => throw new ArgumentException($"Unsupported key '{name}' in a shortcut.", nameof(name)),
        };
    }

    /// <summary>True when any visible element name or non-password value contains the text.</summary>
    public static bool ContainsText(int processId, string text)
    {
        foreach (var window in TopWindows(processId))
        {
            var windowName = Name(window);
            if (windowName is not null && windowName.Contains(text, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (var element in Descendants(window))
            {
                if (!IsVisible(element))
                {
                    continue;
                }

                var name = Name(element);
                if (name is not null && name.Contains(text, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (TryPattern<ValuePattern>(element, ValuePattern.Pattern, out var value) && !IsPassword(element))
                {
                    var current = value.Current.Value;
                    if (!string.IsNullOrEmpty(current) && current.Contains(text, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    public static string DumpTree(int processId)
    {
        var lines = new List<string> { "controlType\tautomationId\tname\toffscreen" };

        foreach (var window in TopWindows(processId))
        {
            lines.Add(FormatRow(window));

            foreach (var element in Descendants(window))
            {
                lines.Add(FormatRow(element));
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatRow(AutomationElement element)
    {
        try
        {
            var type = element.Current.ControlType?.ProgrammaticName?.Replace("ControlType.", string.Empty, StringComparison.Ordinal);
            return $"{type}\t{element.Current.AutomationId}\t{element.Current.Name}\t{element.Current.IsOffscreen}";
        }
        catch (Exception exception) when (IsTransient(exception))
        {
            return "<unavailable>";
        }
    }

    private static bool IsPassword(AutomationElement element)
    {
        try
        {
            return element.Current.IsPassword;
        }
        catch (Exception exception) when (IsTransient(exception))
        {
            return false;
        }
    }

    private static List<AutomationElement> Descendants(AutomationElement root)
    {
        var elements = new List<AutomationElement>();

        try
        {
            var found = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);

            foreach (AutomationElement element in found)
            {
                elements.Add(element);
            }
        }
        catch (Exception exception) when (IsTransient(exception))
        {
            // Tree disappeared mid-walk; treat as empty.
        }

        return elements;
    }

    private static bool TryPattern<T>(AutomationElement element, AutomationPattern pattern, out T value)
        where T : class
    {
        value = null!;

        try
        {
            if (element.TryGetCurrentPattern(pattern, out var candidate) && candidate is T typed)
            {
                value = typed;
                return true;
            }
        }
        catch (Exception exception) when (IsTransient(exception))
        {
            return false;
        }

        return false;
    }

    private static T? WaitUntil<T>(Func<T?> probe, TimeSpan timeout)
        where T : class
    {
        var deadline = DateTime.UtcNow + timeout;
        var result = probe();

        while (result is null && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(PollInterval);
            result = probe();
        }

        return result;
    }

    private static bool IsTransient(Exception exception) =>
        exception is ElementNotAvailableException
            or InvalidOperationException
            or System.Runtime.InteropServices.COMException
            or UnauthorizedAccessException;
}
