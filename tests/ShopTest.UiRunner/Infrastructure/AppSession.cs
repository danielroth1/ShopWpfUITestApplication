using System.Diagnostics;
using System.IO;
using System.Windows.Automation;
using ShopTest.UiRunner.Ui;

namespace ShopTest.UiRunner.Infrastructure;

internal sealed class RunnerException : Exception
{
    public RunnerException(string message, bool infrastructure = false, Exception? inner = null)
        : base(message, inner)
    {
        Infrastructure = infrastructure;
    }

    public bool Infrastructure { get; }
}

/// <summary>Owns the WPF process under test: launch, readiness probe, teardown.</summary>
internal sealed class AppSession : IDisposable
{
    private Process? _process;

    public int ProcessId => _process?.Id ?? throw new InvalidOperationException("App is not running.");

    public bool IsRunning => _process is { HasExited: false };

    public void Start(string executable, IEnumerable<string> arguments, string? workingDirectory)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                ? Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory
                : workingDirectory,
        };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        try
        {
            _process = Process.Start(info);
        }
        catch (Exception exception)
        {
            throw new RunnerException($"could not launch '{executable}': {exception.Message}", infrastructure: true, exception);
        }

        if (_process is null)
        {
            throw new RunnerException($"could not launch '{executable}': no process was created.", infrastructure: true);
        }
    }

    public AutomationElement WaitReady(string? windowTitle, TimeSpan timeout)
    {
        if (_process is null)
        {
            throw new InvalidOperationException("App was not started.");
        }

        var deadline = DateTime.UtcNow + timeout;
        var processId = _process.Id;

        while (DateTime.UtcNow < deadline)
        {
            if (_process.HasExited)
            {
                throw new RunnerException(
                    $"the app exited during startup with code {_process.ExitCode}. " +
                    "Most likely the database was unreachable (see SHOP_DB_CONNECTION) - run the database gate first.",
                    infrastructure: true);
            }

            var windows = Uia.TopWindows(processId);

            foreach (var window in windows)
            {
                if (IsDialog(window))
                {
                    var text = DialogText(window);
                    throw new RunnerException($"the app showed a startup dialog: {text}", infrastructure: true);
                }
            }

            var match = windows.FirstOrDefault(window => Matches(window, windowTitle));
            if (match is not null)
            {
                return match;
            }

            Thread.Sleep(TimeSpan.FromMilliseconds(200));
        }

        throw new RunnerException(
            $"window '{windowTitle ?? "<any>"}' did not appear within {timeout.TotalSeconds:0.#}s.",
            infrastructure: true);
    }

    public void Kill()
    {
        if (_process is null)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(5000);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Already gone.
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }

    public static void KillByName(string processName)
    {
        foreach (var process in Process.GetProcessesByName(processName))
        {
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Already gone.
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    public void Dispose() => Kill();

    private static bool Matches(AutomationElement window, string? title)
    {
        if (title is null)
        {
            return true;
        }

        var name = Uia.Name(window);
        if (name is not null && name.Contains(title, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(Uia.AutomationId(window), "app.window", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDialog(AutomationElement window)
    {
        try
        {
            return string.Equals(window.Current.ClassName, "#32770", StringComparison.Ordinal)
                || (window.Current.ControlType == ControlType.Window
                    && Uia.AutomationId(window) is null
                    && Uia.Name(window)?.Contains("could not start", StringComparison.OrdinalIgnoreCase) == true);
        }
        catch (Exception exception) when (exception is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }

    private static string DialogText(AutomationElement dialog)
    {
        try
        {
            var condition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text);
            var texts = dialog.FindAll(TreeScope.Descendants, condition);
            var parts = new List<string>();

            foreach (AutomationElement text in texts)
            {
                var name = Uia.Name(text);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    parts.Add(name);
                }
            }

            if (parts.Count > 0)
            {
                return string.Join(" ", parts);
            }

            return Uia.Name(dialog) ?? "<no text>";
        }
        catch (Exception exception) when (exception is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return "<dialog text unavailable>";
        }
    }
}
