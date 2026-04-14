using System.Runtime.CompilerServices;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Discovery.Tests;

internal static class TestProjectEnvironment
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        Directory.SetCurrentDirectory(ProjectRootPath);
    }

    internal static string ProjectRootPath { get; } = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}
