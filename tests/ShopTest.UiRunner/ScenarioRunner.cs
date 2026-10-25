using System.Diagnostics;
using System.IO;
using System.Windows.Automation;
using ShopTest.UiRunner.Infrastructure;
using ShopTest.UiRunner.Ui;

namespace ShopTest.UiRunner;

internal sealed class ScenarioRunner
{
    private const int DefaultTimeoutMs = 10000;
    private const int AssertTimeoutMs = 5000;
    private const int LaunchTimeoutMs = 45000;

    private readonly RunnerOptions _options;
    private readonly AppSession _session = new();
    private AutomationElement? _window;
    private string _runId;

    public ScenarioRunner(RunnerOptions options)
    {
        _options = options;
        _runId = options.RunId;
    }

    private string Email => _options.Email ?? $"gauntlet+{_runId}@shop.local";

    private string DisplayName => _options.DisplayName ?? $"Gauntlet {_runId}";

    public ScenarioEvidence Run(ScenarioFile scenario)
    {
        var started = DateTime.UtcNow;
        // Each scenario gets its own run id, matching how the MCP evidence was named: two scenarios in
        // one iteration must not register the same email or create identically named items.
        _runId = $"{_options.RunId}-s{scenario.Id.Split('-')[0]}";

        var directoryName = $"{scenario.Id}-{_options.RunId}";
        var directory = Path.Combine(_options.OutputDirectory, directoryName);
        Directory.CreateDirectory(directory);

        var evidence = new ScenarioEvidence
        {
            Scenario = scenario.Id,
            Title = scenario.Title,
            RunId = _options.RunId,
            StartedAt = started.ToString("O"),
            Directory = directoryName,
        };

        var stopwatch = Stopwatch.StartNew();

        try
        {
            for (var index = 0; index < scenario.Steps.Count; index++)
            {
                var step = scenario.Steps[index];
                var stepNumber = index + 1;
                var stepEvidence = new StepEvidence { Index = stepNumber, Action = step.Action };
                var stepStopwatch = Stopwatch.StartNew();

                try
                {
                    var outcome = Execute(step, scenario, directory, stepNumber);
                    stepEvidence.Status = outcome.Status;
                    stepEvidence.Message = outcome.Message;
                    stepEvidence.Evidence = outcome.Evidence;
                }
                catch (Exception exception) when (exception is RunnerException or InvalidOperationException or NotSupportedException or ArgumentException or ElementNotAvailableException)
                {
                    stepStopwatch.Stop();
                    stepEvidence.Status = "fail";
                    stepEvidence.DurationMs = stepStopwatch.ElapsedMilliseconds;
                    stepEvidence.Message = exception.Message;
                    stepEvidence.Evidence = CaptureFailure(directory, stepNumber);
                    evidence.Steps.Add(stepEvidence);
                    evidence.Status = exception is RunnerException { Infrastructure: true } ? "error" : "fail";
                    break;
                }

                stepStopwatch.Stop();
                stepEvidence.DurationMs = stepStopwatch.ElapsedMilliseconds;
                evidence.Steps.Add(stepEvidence);

                Console.WriteLine(
                    $"    [{stepEvidence.Status,-7}] {stepNumber:00} {step.Action,-10} {stepEvidence.DurationMs,5}ms {stepEvidence.Message}");
            }
        }
        finally
        {
            _session.Dispose();
            _window = null;
            stopwatch.Stop();
        }

        evidence.FinishedAt = DateTime.UtcNow.ToString("O");
        evidence.DurationMs = stopwatch.ElapsedMilliseconds;

        if (evidence.Status != "error")
        {
            evidence.Status = evidence.Steps.Any(step => step.Status == "fail") ? "fail" : "pass";
        }

        var resultsPath = Path.Combine(directory, "results.json");
        EvidenceWriter.Write(resultsPath, evidence);

        return evidence;
    }

    private StepOutcome Execute(ScenarioStep step, ScenarioFile scenario, string directory, int stepNumber)
    {
        switch (step.Action.ToLowerInvariant())
        {
            case "launch":
                return Launch(step, scenario);
            case "waitfor":
                return WaitFor(step);
            case "click":
                return Click(step);
            case "type":
                return Type(step);
            case "shortcut":
                return Shortcut(step);
            case "scroll":
                return Scroll(step);
            case "asserttext":
                return AssertText(step);
            case "sqlassert":
                return AssertSql(step);
            case "screenshot":
                return Screenshot(step, directory, stepNumber);
            case "kill":
                return Kill(step);
            default:
                throw new RunnerException($"unsupported step action '{step.Action}'.");
        }
    }

    private StepOutcome Launch(ScenarioStep step, ScenarioFile scenario)
    {
        var executable = _options.AppExecutable
            ?? Substitute(step.Exe)
            ?? Substitute(scenario.App?.Exe)
            ?? throw new RunnerException("launch step has no exe and --app-exe was not supplied.");

        if (!Path.IsPathRooted(executable))
        {
            executable = Path.GetFullPath(executable);
        }

        if (!File.Exists(executable))
        {
            throw new RunnerException($"app executable not found: {executable}", infrastructure: true);
        }

        AppSession.KillByName(Path.GetFileNameWithoutExtension(executable));

        var arguments = scenario.App?.Args ?? [];
        _session.Start(executable, arguments, Path.GetDirectoryName(executable));
        _window = _session.WaitReady(Substitute(scenario.App?.WindowTitle), Scale(LaunchTimeoutMs));

        return new StepOutcome("pass", $"launched {Path.GetFileName(executable)} (pid {_session.ProcessId})");
    }

    private StepOutcome WaitFor(ScenarioStep step)
    {
        var timeout = Scale(step.TimeoutMs ?? DefaultTimeoutMs);

        if (!string.IsNullOrWhiteSpace(step.Window))
        {
            var title = Substitute(step.Window);
            _window = _session.WaitReady(title, timeout);
            return new StepOutcome("pass", $"window '{title}' ready");
        }

        if (step.Target is not null)
        {
            var element = Resolve(step.Target, timeout, "waitFor");
            return new StepOutcome("pass", Uia.Describe(element));
        }

        throw new RunnerException("waitFor step needs 'window' or 'target'.");
    }

    private StepOutcome Click(ScenarioStep step)
    {
        var timeout = Scale(step.TimeoutMs ?? DefaultTimeoutMs);
        var element = Resolve(step.Target, timeout, "click");

        if (!Uia.WaitEnabled(element, timeout))
        {
            throw new RunnerException($"{step.Target} stayed disabled for {timeout.TotalSeconds:0.#}s.");
        }

        try
        {
            if (Uia.Click(element))
            {
                Uia.WaitForQuiescence(_session.ProcessId, Scale(3000));
                return new StepOutcome("pass", Uia.Describe(element));
            }
        }
        catch (Exception exception) when (exception is ElementNotAvailableException or InvalidOperationException or ElementNotEnabledException)
        {
            _window = _session.WaitReady(null, Scale(DefaultTimeoutMs));
            var retry = Resolve(step.Target, Scale(DefaultTimeoutMs), "click");
            if (Uia.Click(retry))
            {
                Uia.WaitForQuiescence(_session.ProcessId, Scale(3000));
                return new StepOutcome("pass", Uia.Describe(retry));
            }
        }

        throw new RunnerException($"could not activate {step.Target}.");
    }

    private StepOutcome Type(ScenarioStep step)
    {
        if (step.Text is null)
        {
            throw new RunnerException("type step has no text.");
        }

        var element = Resolve(step.Target, Scale(step.TimeoutMs ?? DefaultTimeoutMs), "type");
        var text = Substitute(step.Text) ?? string.Empty;

        if (!Uia.SetText(element, text, step.Clear ?? false))
        {
            throw new RunnerException($"could not type into {step.Target}.");
        }

        return new StepOutcome("pass", $"typed {text.Length} char(s) into {Uia.Describe(element)}");
    }

    private StepOutcome Shortcut(ScenarioStep step)
    {
        if (string.IsNullOrWhiteSpace(step.Keys))
        {
            throw new RunnerException("shortcut step has no keys.");
        }

        if (step.Target is not null)
        {
            var element = Resolve(step.Target, Scale(step.TimeoutMs ?? DefaultTimeoutMs), "shortcut");
            try
            {
                element.SetFocus();
            }
            catch (Exception exception) when (exception is ElementNotAvailableException or InvalidOperationException or ElementNotEnabledException)
            {
                // Focusing is best-effort; the shortcut is still sent to the foreground window.
            }
        }

        Uia.SendShortcut(step.Keys);
        return new StepOutcome("pass", step.Keys);
    }

    private StepOutcome Scroll(ScenarioStep step)
    {
        var element = Resolve(step.Target, Scale(step.TimeoutMs ?? DefaultTimeoutMs), "scroll");
        var notches = step.Amount ?? 1;

        Uia.Scroll(element, notches);
        return new StepOutcome("pass", $"scrolled {notches} notch(es) over {Uia.Describe(element)}");
    }

    private StepOutcome AssertText(ScenarioStep step)
    {
        if (string.IsNullOrWhiteSpace(step.Text))
        {
            throw new RunnerException("assertText step has no text.");
        }

        var text = Substitute(step.Text)!;
        var timeout = Scale(step.TimeoutMs ?? AssertTimeoutMs);
        var deadline = DateTime.UtcNow + timeout;

        while (true)
        {
            if (Uia.ContainsText(_session.ProcessId, text))
            {
                return new StepOutcome("pass", $"'{text}' visible");
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new RunnerException($"text '{text}' was not visible within {timeout.TotalSeconds:0.#}s.");
            }

            Thread.Sleep(150);
        }
    }

    private StepOutcome AssertSql(ScenarioStep step)
    {
        if (string.IsNullOrWhiteSpace(step.Query) || step.Expect is null)
        {
            throw new RunnerException("sqlAssert step needs query and expect.");
        }

        var query = Substitute(step.Query)!;
        var expect = Substitute(step.Expect)!.Trim();
        var timeout = Scale(step.TimeoutMs ?? AssertTimeoutMs);
        var deadline = DateTime.UtcNow + timeout;
        var actual = string.Empty;

        // Persistence happens on the app's background thread, so the row can lag the click by a moment.
        // Polling keeps the expectation strict without turning a race into a flaky failure.
        while (true)
        {
            actual = SqlProbe.Scalar(query);

            if (string.Equals(actual, expect, StringComparison.Ordinal))
            {
                return new StepOutcome("pass", $"{step.Description ?? "sql"} = {actual}");
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new RunnerException(
                    $"sql assertion failed: expected '{expect}' but got '{actual}' ({step.Description ?? query}).");
            }

            Thread.Sleep(200);
        }
    }

    private StepOutcome Screenshot(ScenarioStep step, string directory, int stepNumber)
    {
        var label = Sanitize(Substitute(step.Label) ?? "screenshot");
        var path = Path.Combine(directory, $"step-{stepNumber:00}-{label}.png");

        if (Capture.TrySave(_window, path, out var note))
        {
            return new StepOutcome("pass", note, EvidenceWriter.Relative(directory, path));
        }

        return new StepOutcome("skipped", note);
    }

    private StepOutcome Kill(ScenarioStep step)
    {
        var processName = Substitute(step.Process) ?? "ShopTest.App";
        AppSession.KillByName(Path.GetFileNameWithoutExtension(processName));
        _session.Kill();
        _window = null;

        return new StepOutcome("pass", $"stopped {processName}");
    }

    private AutomationElement Resolve(StepTarget? target, TimeSpan timeout, string action)
    {
        if (target is null)
        {
            throw new RunnerException($"{action} step has no target.");
        }

        var window = _window ?? throw new RunnerException("no application window is available yet.");
        var resolved = SubstituteTarget(target);

        return Uia.WaitForTarget(window, resolved, timeout)
            ?? throw new RunnerException($"could not find {resolved} within {timeout.TotalSeconds:0.#}s ({action}).");
    }

    private StepTarget SubstituteTarget(StepTarget target) => new()
    {
        AutomationId = Substitute(target.AutomationId),
        Text = Substitute(target.Text),
        Label = target.Label,
    };

    private string? CaptureFailure(string directory, int stepNumber)
    {
        try
        {
            var png = Path.Combine(directory, $"step-{stepNumber:00}-failure.png");
            var dump = Path.Combine(directory, $"step-{stepNumber:00}-failure.txt");

            var captured = Capture.TrySave(_window, png, out _);

            if (_session.IsRunning)
            {
                try
                {
                    File.WriteAllText(dump, Uia.DumpTree(_session.ProcessId));
                }
                catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // Process gone.
                }
            }

            var reference = captured ? png : dump;
            return File.Exists(reference) ? EvidenceWriter.Relative(directory, reference) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private TimeSpan Scale(int milliseconds) =>
        TimeSpan.FromMilliseconds(Math.Max(1, milliseconds) * Math.Max(0.1, _options.TimeoutScale));

    private string? Substitute(string? value) =>
        value?
            .Replace("{{runId}}", _runId, StringComparison.Ordinal)
            .Replace("{{run.email}}", Email, StringComparison.Ordinal)
            .Replace("{{run.name}}", DisplayName, StringComparison.Ordinal);

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var characters = value.Select(c => invalid.Contains(c) ? '-' : c).ToArray();
        return new string(characters);
    }

    private readonly record struct StepOutcome(string Status, string? Message, string? Evidence = null);
}
