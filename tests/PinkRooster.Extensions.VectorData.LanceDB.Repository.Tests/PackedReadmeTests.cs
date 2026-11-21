using System.Text.RegularExpressions;

namespace PinkRooster.Extensions.VectorData.LanceDB.Repository.Tests;

/// <summary>
/// Keeps the packed readmes (<c>docs/PinkRooster.*.md</c>, packed as each package's README) inside what NuGet.org renders: no relative link or
/// image, images only from its allow-list of hosts, every code fence tagged with a language. A readme cannot be fixed after a package is published,
/// so a violation has to fail here. The install lines of the root README and the packed readmes carry <c>--prerelease</c> while no stable version exists.
/// </summary>
public sealed partial class PackedReadmeTests
{
    // The hosts NuGet.org renders images and badges from; github.com only for a workflow badge. https://learn.microsoft.com/nuget/nuget-org/package-readme-on-nuget-org
    private static readonly string[] AllowedImageHosts =
    [
        "api.codacy.com", "api.codeclimate.com", "api.dependabot.com", "api.reuse.software", "api.travis-ci.com", "app.codacy.com", "app.deepsource.com",
        "avatars.githubusercontent.com", "badgen.net", "badges.gitter.im", "camo.githubusercontent.com", "caniuse.bitsofco.de", "cdn.jsdelivr.net",
        "cdn.syncfusion.com", "ci.appveyor.com", "circleci.com", "cloudback.it", "codecov.io", "codefactor.io", "coveralls.io", "dev.azure.com",
        "devpod.sh", "flat.badgen.net", "gitlab.com", "i.imgur.com", "img.shields.io", "infragistics.com", "isitmaintained.com",
        "media.githubusercontent.com", "opencollective.com", "raw.github.com", "raw.githubusercontent.com", "snyk.io", "sonarcloud.io",
        "travis-ci.com", "travis-ci.org", "user-images.githubusercontent.com",
    ];

    private static readonly string Root = RepositoryRoot.Path;

    public static IEnumerable<TheoryDataRow<string>> PackedReadmes() =>
        Directory.GetFiles(Path.Combine(Root, "docs"), "PinkRooster.*.md").Order(StringComparer.Ordinal).Select(file => new TheoryDataRow<string>(RelativePath(file)));

    [Theory]
    [MemberData(nameof(PackedReadmes))]
    public void PackedReadme_HasNoRelativeLinkOrImage(string file)
    {
        string[] offenders = [.. Targets(Read(file)).Where(target => !IsAbsolute(target) && !target.StartsWith('#')).Select(target => $"{file}: '{target}'")];

        Assert.True(offenders.Length == 0, $"NuGet.org shows a relative link or image as broken; use an absolute https://github.com/pinkroosterai/PinkRooster.Extensions.VectorData.LanceDB/blob/main/... URL:\n{string.Join("\n", offenders)}");
    }

    [Theory]
    [MemberData(nameof(PackedReadmes))]
    public void PackedReadme_ImagesComeFromAnAllowedHost(string file)
    {
        string[] offenders = [.. ImageOffenders(Read(file)).Select(offender => $"{file}: {offender}")];

        Assert.True(offenders.Length == 0, $"NuGet.org does not render these images:\n{string.Join("\n", offenders)}");
    }

    private static IEnumerable<string> ImageOffenders(string text)
    {
        string renderedText = ScanMarkdown(text).Text;
        foreach (Match image in HtmlImage().Matches(renderedText))
        {
            Match source = HtmlImageSource().Match(image.Value);
            if (!source.Success || !Uri.TryCreate(source.Groups["src"].Value, UriKind.Absolute, out Uri? uri) || !IsAllowedImage(uri))
            {
                yield return $"HTML image '{image.Value}'";
            }
        }

        foreach (Match image in MarkdownImage().Matches(renderedText))
        {
            string target = image.Groups["target"].Value;
            if (!Uri.TryCreate(target, UriKind.Absolute, out Uri? uri) || !IsAllowedImage(uri))
            {
                yield return $"image '{target}'";
            }
        }
    }

    [Theory]
    [MemberData(nameof(PackedReadmes))]
    public void PackedReadme_TagsEveryCodeFence(string file)
    {
        string[] untagged = [.. ScanMarkdown(Read(file)).Fences.Where(fence => fence.Info.Length == 0).Select(fence => $"{file}:{fence.Line}")];

        Assert.True(untagged.Length == 0, $"A code fence without a language is not highlighted on NuGet.org; tag it (text, csharp, json):\n{string.Join("\n", untagged)}");
    }

    [Fact]
    public void InstallLines_ForPinkRoosterPackages_CarryPrerelease()
    {
        string[] files = ["README.md", .. Directory.GetFiles(Path.Combine(Root, "docs"), "PinkRooster.*.md").Order(StringComparer.Ordinal).Select(RelativePath)];
        string[] offenders =
        [
            .. files.SelectMany(file => Read(file).Split('\n').Select((line, index) => (file, line, number: index + 1)))
                .Where(entry => InstallLine().IsMatch(entry.line) && !entry.line.Contains("--prerelease", StringComparison.Ordinal))
                .Select(entry => $"{entry.file}:{entry.number}: {entry.line.Trim()}"),
        ];

        Assert.True(offenders.Length == 0, $"No stable version exists yet, so `dotnet add package` needs --prerelease:\n{string.Join("\n", offenders)}");
    }

    private static bool IsAbsolute(string target) =>
        target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);

    private static bool IsAllowedImage(Uri uri) =>
        string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
        && (AllowedImageHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase)
            || (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) && WorkflowBadge().IsMatch(uri.AbsolutePath)));

    /// <summary>Every link and image target outside code fences and code spans.</summary>
    private static IEnumerable<string> Targets(string text) =>
        LinkTarget().Matches(ScanMarkdown(text).Text).Select(match => match.Groups["target"].Value);

    private static (string Text, List<(int Line, string Info)> Fences) ScanMarkdown(string text)
    {
        string[] lines = text.ReplaceLineEndings("\n").Split('\n');
        List<(int Line, string Info)> fences = [];
        string? open = null;
        for (int index = 0; index < lines.Length; index++)
        {
            Match fence = Fence().Match(lines[index]);
            string marker = fence.Groups["marker"].Value;
            string info = fence.Groups["info"].Value.Trim();
            if (open is not null)
            {
                if (fence.Success && marker[0] == open[0] && marker.Length >= open.Length && info.Length == 0)
                {
                    open = null;
                }

                lines[index] = string.Empty;
            }
            else if (fence.Success && (marker[0] == '~' || !info.Contains('`')))
            {
                open = marker;
                fences.Add((index + 1, info));
                lines[index] = string.Empty;
            }
            else
            {
                lines[index] = CodeSpan().Replace(lines[index], string.Empty);
            }
        }

        return (string.Join('\n', lines), fences);
    }

    [Fact]
    public void MarkdownChecks_IgnoreExamplesAndResumeAfterMatchingFences()
    {
        const string text = """
              ````markdown
            ![hidden](hidden.png)
            ```
            <img src="hidden.png">
            ````not-a-close
            [hidden](hidden.md)
              ````
            ~~~
            ![also hidden](hidden.png)
            ~~~~
            `[inline](hidden.md)`
            [visible](visible.md)
            ![visible](visible.png)
            <img src="https://example.com/visible.png">
            """;

        Assert.Equal([(1, "markdown"), (8, "")], ScanMarkdown(text).Fences);
        Assert.Equal(["visible.md", "visible.png"], Targets(text));
        Assert.Equal(["image 'visible.png'", "HTML image '<img src=\"https://example.com/visible.png\">'"], ImageOffenders(text).OrderDescending(StringComparer.Ordinal));
    }

    [GeneratedRegex(@"^ {0,3}(?<marker>`{3,}|~{3,})(?<info>.*)$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Fence();

    private static string Read(string relativePath) => File.ReadAllText(Path.Combine(Root, relativePath)).ReplaceLineEndings("\n");

    private static string RelativePath(string file) => Path.GetRelativePath(Root, file).Replace('\\', '/');

    [GeneratedRegex(@"!?\[[^\]]*\]\((?<target>[^)\s]+)(?:\s+""[^""]*"")?\)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LinkTarget();

    [GeneratedRegex(@"!\[[^\]]*\]\((?<target>[^)\s]+)(?:\s+""[^""]*"")?\)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex MarkdownImage();

    [GeneratedRegex(@"<img\b[^>]*>", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HtmlImage();

    [GeneratedRegex(@"\bsrc\s*=\s*""(?<src>[^""]*)""", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HtmlImageSource();

    [GeneratedRegex("`[^`]*`", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CodeSpan();

    [GeneratedRegex(@"^/[^/]+/[^/]+/workflows/[^/]+/badge\.svg$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex WorkflowBadge();

    [GeneratedRegex(@"dotnet add package PinkRooster\.", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex InstallLine();

}
