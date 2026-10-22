namespace SkillSamples.Uploads;

public sealed class PathOutsideRootException(string path) : IOException($"'{path}' is outside the storage root.");

/// <summary>
/// Joins a storage root and a relative key, and refuses any result outside the root. Path.Combine alone
/// doesn't: "../" climbs out, and a rooted second argument ("/app/x.dll", "C:\x") replaces the root
/// entirely.
/// </summary>
public static class SafePath
{
    private static readonly StringComparison Comparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static string Under(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root);
        var rootWithSeparator = Path.EndsInDirectorySeparator(fullRoot) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(fullRoot, relative));
        // The separator matters: "/data/photos-old" starts with "/data/photos" too.
        if (!full.StartsWith(rootWithSeparator, Comparison))
        {
            throw new PathOutsideRootException(relative);
        }

        return full;
    }
}
