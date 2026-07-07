namespace Yarpa.Agent;

/// <summary>
/// Parsed CLI arguments for the Agent.
/// Registered as a singleton so any service can query the run mode.
/// </summary>
public sealed class CliArgs
{
    /// <summary>Collect and send once, then exit (default mode).</summary>
    public bool Once { get; init; } = true;

    /// <summary>Write the collected JSON to this file path instead of sending it.</summary>
    public string? OutputPath { get; init; }

    /// <summary>Collect and print the JSON to the log; do not send or write to disk.</summary>
    public bool DryRun { get; init; }

    /// <summary>
    /// Run as a long-lived Windows Service that collects and sends on a configurable
    /// schedule. Mutually exclusive with the one-shot modes.
    /// </summary>
    public bool Service { get; init; }

    /// <summary>
    /// Parses the command-line <paramref name="args"/> array into a <see cref="CliArgs"/> instance.
    /// Unknown arguments are silently ignored.
    /// </summary>
    public static CliArgs Parse(string[] args)
    {
        bool dryRun = false;
        bool service = false;
        string? outputPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--dry-run":
                    dryRun = true;
                    break;

                case "--output":
                    if (i + 1 < args.Length)
                        outputPath = args[++i];
                    break;

                case "--service":
                    service = true;
                    break;

                // --once is the default; accept it but no special handling needed
                case "--once":
                    break;
            }
        }

        return new CliArgs
        {
            // Service mode is long-running; one-shot modes keep Once semantics.
            Once = !service,
            DryRun = dryRun,
            Service = service,
            OutputPath = outputPath
        };
    }
}
