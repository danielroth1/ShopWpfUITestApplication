using System.Globalization;
using System.IO;

namespace ShopTest.UiRunner.Infrastructure;

internal sealed class RunnerOptions
{
    public string ScenarioDirectory { get; set; } = Path.Combine("ui-tests", "scenarios");
    public string OutputDirectory { get; set; } = Path.Combine("artifacts", "ui-runner");
    public string? AppExecutable { get; set; }
    public string RunId { get; set; } = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public HashSet<string> Only { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Skip { get; } = new(StringComparer.OrdinalIgnoreCase);
    public double TimeoutScale { get; set; } = 1.0;
    public bool FailFast { get; set; }
    public bool ListOnly { get; set; }
    public bool ShowHelp { get; set; }
    public string EnvironmentTag { get; set; } = "host";

    public static RunnerOptions Parse(string[] args)
    {
        var options = new RunnerOptions();
        var tokens = Expand(args);

        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];

            string Value()
            {
                if (index + 1 >= tokens.Count)
                {
                    throw new ArgumentException($"'{token}' requires a value.");
                }

                return tokens[++index];
            }

            switch (token.ToLowerInvariant())
            {
                case "--scenarios":
                    options.ScenarioDirectory = Value();
                    break;
                case "--out":
                case "--output":
                    options.OutputDirectory = Value();
                    break;
                case "--app-exe":
                    options.AppExecutable = Value();
                    break;
                case "--run-id":
                    options.RunId = Value();
                    break;
                case "--email":
                    options.Email = Value();
                    break;
                case "--name":
                    options.DisplayName = Value();
                    break;
                case "--only":
                    foreach (var id in Value().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        options.Only.Add(id);
                    }

                    break;
                case "--skip":
                    foreach (var id in Value().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        options.Skip.Add(id);
                    }

                    break;
                case "--timeout-scale":
                    options.TimeoutScale = double.Parse(Value(), CultureInfo.InvariantCulture);
                    break;
                case "--fail-fast":
                    options.FailFast = true;
                    break;
                case "--list":
                    options.ListOnly = true;
                    break;
                case "--sandbox":
                    options.EnvironmentTag = "sandbox";
                    break;
                case "--help":
                case "-h":
                case "/?":
                    options.ShowHelp = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{token}'. Use --help for usage.");
            }
        }

        return options;
    }

    public static string Usage => string.Join(Environment.NewLine,
        "ShopTest.UiRunner - replays ui-tests/scenarios/*.json through UI Automation.",
        string.Empty,
        "  --scenarios <dir>       scenario directory (default ui-tests/scenarios)",
        "  --out <dir>             evidence directory (default artifacts/ui-runner)",
        "  --app-exe <path>        override the scenario's launch exe (used by the sandbox payload)",
        "  --run-id <id>           run id used for {{runId}}/{{run.email}}/{{run.name}}",
        "  --email <address>       override {{run.email}}",
        "  --name <text>           override {{run.name}}",
        "  --only <id[,id]>        run only these scenarios",
        "  --skip <id[,id]>        skip these scenarios",
        "  --timeout-scale <n>     multiply every step timeout (default 1.0)",
        "  --fail-fast             stop after the first failing scenario",
        "  --sandbox               mark evidence as produced inside Windows Sandbox",
        "  --list                  print scenario ids and exit",
        string.Empty,
        "Exit codes: 0 all green, 1 scenario failure, 2 infrastructure failure.");

    private static List<string> Expand(string[] args)
    {
        var tokens = new List<string>();

        foreach (var argument in args)
        {
            if (argument.StartsWith("--", StringComparison.Ordinal) && argument.Contains('=', StringComparison.Ordinal))
            {
                var split = argument.Split('=', 2);
                tokens.Add(split[0]);
                tokens.Add(split[1]);
            }
            else
            {
                tokens.Add(argument);
            }
        }

        return tokens;
    }
}
