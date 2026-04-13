using System.IO;

namespace Discovery.Tests;

public sealed class ProjectRootLocatorTests
{
    [Fact]
    public void ResolveFromBaseDirectory_BaseDirectoryContainsSolutionInAncestor_ReturnsProjectRoot()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(workspace.Path, "Discovery.sln"), string.Empty);
        var baseDirectory = Directory.CreateDirectory(Path.Combine(workspace.Path, "Discovery", "bin", "Debug")).FullName;

        // Act
        var result = ProjectRootLocator.ResolveFromBaseDirectory(baseDirectory);

        // Assert
        Assert.Equal(workspace.Path, result);
    }

    [Fact]
    public void ResolveFromBaseDirectory_SolutionDoesNotExist_ThrowsDirectoryNotFoundException()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();

        // Act
        var exception = Assert.Throws<DirectoryNotFoundException>(() =>
            ProjectRootLocator.ResolveFromBaseDirectory(workspace.Path));

        // Assert
        Assert.Contains("Could not locate the project root", exception.Message);
    }
}
