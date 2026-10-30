using RoG.Core.Config;
using RoG.Linux.Platform;

namespace RoG.Linux.App;

/// <summary>Start-Argumente: Umgebung für die Entwicklung und ein Deep Link (<c>rog://…</c>).</summary>
public sealed record LaunchOptions(string? Environment, Uri? DeepLink)
{
    public static LaunchOptions Current { get; set; } = new(null, null);

    /// <summary>
    /// <c>--environment=staging</c> bzw. Umgebungsvariable <c>ROG_ENVIRONMENT</c> gehen den Einstellungen vor; unbekannte
    /// Namen werden ignoriert. Domains stehen nur in <c>environments.json</c> des Kerns.
    /// </summary>
    public static LaunchOptions Parse(IReadOnlyList<string> args, Func<string, string?> env)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(env);
        string? environment = null;
        Uri? link = null;
        foreach (var arg in args)
        {
            if (arg.StartsWith("--environment=", StringComparison.Ordinal))
            {
                environment = arg["--environment=".Length..];
            }
            else if (Uri.TryCreate(arg, UriKind.Absolute, out var uri) && uri.Scheme == AppIdentity.DeepLinkScheme)
            {
                link = uri;
            }
        }

        environment ??= env("ROG_ENVIRONMENT");
        return new LaunchOptions(environment is not null && RoGEnvironment.All.ContainsKey(environment) ? environment : null, link);
    }
}
