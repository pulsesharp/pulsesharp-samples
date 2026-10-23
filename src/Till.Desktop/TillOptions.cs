namespace Till.Desktop;

/// <summary>
/// Which till this is and where the cloud is. Start a second till with <c>--till till-2</c>.
/// </summary>
public sealed record TillOptions(string Name, string CloudAddress, string ApiKey, string DatabasePath)
{
    public static TillOptions FromArguments(string[] args)
    {
        var name = ValueOf(args, "--till") ?? "till-1";
        var cloud = ValueOf(args, "--cloud") ?? "http://localhost:5100";
        var key = ValueOf(args, "--key") ?? $"demo-key-{name}";
        var folder = ValueOf(args, "--data") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PulseSharpSamples");
        return new TillOptions(name, cloud.TrimEnd('/'), key, Path.Combine(folder, $"{name}.db"));
    }

    private static string? ValueOf(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
