namespace BoxLine.PerfTool;

/// <summary>A minimal command line: a command, an optional positional word, and --name value / --flag options.</summary>
public sealed class Args
{
    private readonly Dictionary<string, string?> options = new(StringComparer.OrdinalIgnoreCase);

    public string Command { get; private init; } = string.Empty;

    public string? Positional { get; private init; }

    public static Args Parse(string[] args)
    {
        var parsed = new Args { Command = args.FirstOrDefault() ?? string.Empty, Positional = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--")) };
        for (var i = 1; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--"))
            {
                continue;
            }

            var name = args[i][2..];
            var hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--");
            parsed.options[name] = hasValue ? args[++i] : null;
        }

        return parsed;
    }

    public bool Flag(string name) => options.ContainsKey(name);

    public string? Text(string name) => options.GetValueOrDefault(name);

    public int Int(string name, int fallback) =>
        options.TryGetValue(name, out var value) && int.TryParse(value, out var number) ? number
        : options.ContainsKey(name) ? throw new ArgumentException($"--{name} needs a whole number.") : fallback;
}
