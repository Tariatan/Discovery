using System.IO;

namespace Discovery.Tests;

public sealed class ProjectRootLocatorTests
{
    [Fact]
    public void ResolveFromBaseDirectory_ReturnsAncestorContainingSolution()
    {
        using var workspace = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(workspace.Path, "Discovery.sln"), string.Empty);

        var baseDirectory = Directory.CreateDirectory(Path.Combine(workspace.Path, "Discovery", "bin", "Debug")).FullName;

        var result = ProjectRootLocator.ResolveFromBaseDirectory(baseDirectory);

        Assert.Equal(workspace.Path, result);
    }

    [Fact]
    public void ResolveFromBaseDirectory_ThrowsWhenSolutionCannotBeFound()
    {
        using var workspace = new TemporaryDirectory();

        var exception = Assert.Throws<DirectoryNotFoundException>(() =>
            ProjectRootLocator.ResolveFromBaseDirectory(workspace.Path));

        Assert.Contains("Could not locate the project root", exception.Message);
    }
}
