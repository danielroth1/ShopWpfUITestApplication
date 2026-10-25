using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShopTest.UiRunner;

/// <summary>
/// The declarative scenario contract (ui-tests/schema/scenario.schema.json). The runner is deliberately
/// schema-faithful: the same files that Windows-MCP replays are replayed here, with no per-executor forks.
/// </summary>
internal sealed class ScenarioFile
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public AppSpec? App { get; set; }
    public Preconditions? Preconditions { get; set; }
    public List<ScenarioStep> Steps { get; set; } = [];
}

internal sealed class AppSpec
{
    public string? Exe { get; set; }
    public string[] Args { get; set; } = [];
    public string? WindowTitle { get; set; }
}

internal sealed class Preconditions
{
    public bool DbReset { get; set; }
    public bool SeededAdmin { get; set; }
    public bool LoggedOut { get; set; }
}

internal sealed class StepTarget
{
    public string? AutomationId { get; set; }
    public string? Text { get; set; }

    /// <summary>Windows-MCP snapshot element id. Not replayable outside MCP; kept so scenarios stay shared.</summary>
    public int? Label { get; set; }

    public override string ToString() =>
        AutomationId is not null ? $"automationId={AutomationId}"
        : Text is not null ? $"text={Text}"
        : Label is not null ? $"label={Label}"
        : "target=empty";
}

internal sealed class ScenarioStep
{
    public string Action { get; set; } = string.Empty;
    public string? Exe { get; set; }
    public string? Window { get; set; }
    public StepTarget? Target { get; set; }
    public string? Text { get; set; }
    public bool? Clear { get; set; }
    public string? Keys { get; set; }
    public int? Amount { get; set; }
    public int? TimeoutMs { get; set; }
    public string? Query { get; set; }
    public string? Expect { get; set; }
    public string? Description { get; set; }

    /// <summary>`screenshot` step label (a file name, not an element id).</summary>
    public string? Label { get; set; }

    public string? Process { get; set; }
}

internal static class ScenarioLoader
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static IReadOnlyList<ScenarioFile> Load(string directory)
    {
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Scenario directory not found: {directory}");
        }

        var scenarios = new List<ScenarioFile>();

        foreach (var path in Directory.EnumerateFiles(directory, "*.json").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            // ui-tests/schema/scenario.schema.json describes the contract; it is not a scenario. Skipping it
            // by name keeps the strict "has no steps" check for real scenario files.
            if (Path.GetFileName(path).EndsWith(".schema.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var scenario = JsonSerializer.Deserialize<ScenarioFile>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidDataException($"Scenario '{path}' deserialized to null.");

            if (scenario.Steps.Count == 0)
            {
                throw new InvalidDataException($"Scenario '{path}' has no steps.");
            }

            scenarios.Add(scenario);
        }

        return scenarios;
    }
}
