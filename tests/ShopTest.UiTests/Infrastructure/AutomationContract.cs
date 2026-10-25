using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace ShopTest.UiTests.Infrastructure;

/// <summary>
/// The AutomationId rules from <c>.github/instructions/wpf-ui-automation.instructions.md</c>, as a
/// reusable check over one laid-out view.
/// </summary>
internal static class AutomationContract
{
    public static ContractReport Check(string viewName, FrameworkElement view)
    {
        var violations = new List<string>();
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        int authored = 0;
        int templateParts = 0;

        foreach (FrameworkElement element in Ui.Interactives(view))
        {
            if (IsControlTemplatePart(element))
            {
                templateParts++;
                continue;
            }

            authored++;
            string type = element.GetType().Name;
            string id = AutomationProperties.GetAutomationId(element);

            if (string.IsNullOrWhiteSpace(id))
            {
                violations.Add($"{viewName}: {type} has no AutomationProperties.AutomationId.");
                continue;
            }

            if (owners.TryGetValue(id, out string? firstOwner))
            {
                // With two or more items in a list this is how a template that forgot to include the
                // item key in its id shows up: the id repeats.
                violations.Add($"{viewName}: AutomationId '{id}' is reused by {type} (already used by {firstOwner}).");
            }
            else
            {
                owners[id] = type;
            }
        }

        if (authored == 0)
        {
            violations.Add($"{viewName}: no authored interactive controls found, so this check proves nothing. " +
                           "Either the view genuinely has none, or the layout/walk is broken.");
        }

        return new ContractReport(authored, templateParts, violations);
    }

    /// <summary>
    /// Whether the element was created by a control's own <c>ControlTemplate</c> - a ComboBox's drop-down
    /// ToggleButton, an Expander's header toggle, a ScrollBar's repeat buttons - rather than by the app.
    /// UI Automation exposes the parent control in that case, so the part carries no contract.
    /// </summary>
    /// <remarks>
    /// Content inside a <c>DataTemplate</c> also has a templated parent, but it is the item's
    /// <see cref="ContentPresenter"/>. That content *is* authored by the app, so excluding everything with
    /// a templated parent would silently skip every item template.
    /// </remarks>
    private static bool IsControlTemplatePart(FrameworkElement element) =>
        element.TemplatedParent is not null and not ContentPresenter;
}

internal readonly record struct ContractReport(int Authored, int TemplateParts, IReadOnlyList<string> Violations);
