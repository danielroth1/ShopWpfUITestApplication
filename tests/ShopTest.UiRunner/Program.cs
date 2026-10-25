using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using ShopTest.UiRunner;
using ShopTest.UiRunner.Infrastructure;
using ShopTest.UiRunner.Interop;

namespace ShopTest.UiRunner;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        NativeMethods.EnablePerMonitorDpiAwareness();

        RunnerOptions options;

        try
        {
            options = RunnerOptions.Parse(args);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }

        if (options.ShowHelp)
        {
            Console.WriteLine(RunnerOptions.Usage);
            return 0;
        }

        IReadOnlyList<ScenarioFile> all;

        try
        {
            all = ScenarioLoader.Load(options.ScenarioDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            Console.Error.WriteLine($"could not load scenarios from '{options.ScenarioDirectory}': {exception.Message}");
            return 2;
        }

        var selected = all
            .Where(scenario => options.Only.Count == 0 || options.Only.Contains(scenario.Id))
            .Where(scenario => !options.Skip.Contains(scenario.Id))
            .ToList();

        if (selected.Count == 0)
        {
            Console.Error.WriteLine("no scenarios selected.");
            return 2;
        }

        if (options.ListOnly)
        {
            foreach (var scenario in selected)
            {
                Console.WriteLine(scenario.Id);
            }

            return 0;
        }

        Directory.CreateDirectory(options.OutputDirectory);

        var startedAt = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var summaries = new List<ScenarioSummary>();
        var notes = new List<string>();
        var exitCode = 0;

        Console.WriteLine($"ShopTest.UiRunner - {selected.Count} scenario(s), run {options.RunId}, environment {options.EnvironmentTag}");

        foreach (var scenario in selected)
        {
            Console.WriteLine($"{scenario.Id} - {scenario.Title}");

            var evidence = new ScenarioRunner(options).Run(scenario);

            summaries.Add(new ScenarioSummary
            {
                Scenario = evidence.Scenario,
                Status = evidence.Status,
                DurationMs = evidence.DurationMs,
                FailedStep = evidence.Steps.FirstOrDefault(step => step.Status == "fail")?.Index ?? 0,
                Directory = evidence.Directory,
            });

            if (evidence.Status == "error")
            {
                exitCode = 2;
                notes.Add($"{scenario.Id}: infrastructure failure - {evidence.Steps.LastOrDefault()?.Message}");
                break;
            }

            if (evidence.Status != "pass")
            {
                exitCode = exitCode == 2 ? 2 : 1;

                if (options.FailFast)
                {
                    break;
                }
            }
        }

        stopwatch.Stop();
        var finishedAt = DateTime.UtcNow;

        var run = new RunEvidence
        {
            RunId = options.RunId,
            Environment = options.EnvironmentTag,
            StartedAt = startedAt.ToString("O", CultureInfo.InvariantCulture),
            FinishedAt = finishedAt.ToString("O", CultureInfo.InvariantCulture),
            DurationMs = stopwatch.ElapsedMilliseconds,
            Result = exitCode switch { 0 => "pass", 1 => "fail", _ => "error" },
            ScenarioCount = summaries.Count,
            FailedCount = summaries.Count(summary => summary.Status == "fail"),
            SkippedCount = selected.Count - summaries.Count,
            Scenarios = summaries,
            Notes = notes,
        };

        EvidenceWriter.Write(Path.Combine(options.OutputDirectory, "summary.json"), run);
        EvidenceWriter.Write(
            Path.Combine(options.OutputDirectory, "_done.json"),
            new { exitCode, result = run.Result, finishedAt = run.FinishedAt, runId = options.RunId });

        Console.WriteLine(
            $"{run.Result}: {summaries.Count(s => s.Status == "pass")}/{summaries.Count} scenario(s) green in {run.DurationMs} ms -> {Path.GetFullPath(options.OutputDirectory)}");

        return exitCode;
    }
}
