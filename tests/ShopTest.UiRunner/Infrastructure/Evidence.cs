using System.IO;
using System.Text.Json;

namespace ShopTest.UiRunner.Infrastructure;

internal sealed class StepEvidence
{
    public int Index { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Status { get; set; } = "pass";
    public long DurationMs { get; set; }
    public string? Evidence { get; set; }
    public string? Message { get; set; }
}

internal sealed class ScenarioEvidence
{
    public string Scenario { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Tool { get; set; } = "shoptest-uia-runner";
    public string RunId { get; set; } = string.Empty;
    public string Status { get; set; } = "pass";
    public string StartedAt { get; set; } = string.Empty;
    public string FinishedAt { get; set; } = string.Empty;
    public long DurationMs { get; set; }
    public string Directory { get; set; } = string.Empty;
    public List<StepEvidence> Steps { get; set; } = [];
}

internal sealed class ScenarioSummary
{
    public string Scenario { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public long DurationMs { get; set; }
    public int FailedStep { get; set; }
    public string Directory { get; set; } = string.Empty;
}

internal sealed class RunEvidence
{
    public string Tool { get; set; } = "shoptest-uia-runner";
    public string RunId { get; set; } = string.Empty;
    public string Environment { get; set; } = "host";
    public string StartedAt { get; set; } = string.Empty;
    public string FinishedAt { get; set; } = string.Empty;
    public long DurationMs { get; set; }
    public string Result { get; set; } = "pass";
    public int ScenarioCount { get; set; }
    public int FailedCount { get; set; }
    public int SkippedCount { get; set; }
    public List<ScenarioSummary> Scenarios { get; set; } = [];
    public List<string> Notes { get; set; } = [];
}

internal static class EvidenceWriter
{
    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, ScenarioLoader.JsonOptions));
    }

    public static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');
}
