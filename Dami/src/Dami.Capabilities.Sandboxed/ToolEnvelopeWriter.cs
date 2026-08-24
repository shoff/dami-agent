using System.Text;
using Dami.Contracts.ToolStaging;

namespace Dami.Capabilities.Sandboxed;

/// <summary>Writes proposal bytes into the one trusted package-free project shape.</summary>
public sealed class ToolEnvelopeWriter
{
    private static readonly UTF8Encoding utf8 = new(
        encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Creates a new fixed build-and-test envelope.</summary>
    public async Task WriteAsync(
        ToolProposalArtifact artifact,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        string root = Path.GetFullPath(destinationDirectory);
        EnsureNewOrEmpty(root);
        await WriteFixedFilesAsync(root, cancellationToken).ConfigureAwait(false);
        await WriteProposalFilesAsync(
            root, "Source", artifact.SourceFiles, cancellationToken).ConfigureAwait(false);
        await WriteProposalFilesAsync(
            root, "Tests", artifact.TestFiles, cancellationToken).ConfigureAwait(false);
    }

    private static void EnsureNewOrEmpty(string root)
    {
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
        {
            throw new IOException("A tool envelope destination must be new or empty.");
        }

        Directory.CreateDirectory(root);
    }

    private static async Task WriteFixedFilesAsync(
        string root,
        CancellationToken cancellationToken)
    {
        await WriteAsync(root, "Tool.csproj", PROJECT, cancellationToken).ConfigureAwait(false);
        await WriteAsync(root, "NuGet.Config", NUGET_CONFIG, cancellationToken).ConfigureAwait(false);
        await WriteAsync(root, "DamiSandboxContracts.cs", CONTRACTS, cancellationToken)
            .ConfigureAwait(false);
        await WriteAsync(root, "Program.cs", PROGRAM, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteProposalFilesAsync(
        string root,
        string set,
        IReadOnlyDictionary<string, string> files,
        CancellationToken cancellationToken)
    {
        foreach (KeyValuePair<string, string> file in files)
        {
            string relativePath = Path.Combine("Proposal", set, file.Key);
            string path = Path.Combine(root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, file.Value, utf8, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static Task WriteAsync(
        string root,
        string relativePath,
        string content,
        CancellationToken cancellationToken)
    {
        return File.WriteAllTextAsync(
            Path.Combine(root, relativePath), content, utf8, cancellationToken);
    }

    private const string PROJECT = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <OutputType>Exe</OutputType>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
            <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
            <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
            <StartupObject>Dami.Sandbox.Program</StartupObject>
          </PropertyGroup>
          <ItemGroup>
            <Compile Include="DamiSandboxContracts.cs" />
            <Compile Include="Program.cs" />
            <Compile Include="Proposal/**/*.cs" />
          </ItemGroup>
        </Project>
        """;

    private const string NUGET_CONFIG = """
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <packageSources>
            <clear />
          </packageSources>
        </configuration>
        """;

    private const string CONTRACTS = """
        namespace Dami.Sandbox;

        public interface ISandboxedTool
        {
            ValueTask<string> ExecuteAsync(string inputJson, CancellationToken cancellationToken);
        }

        public interface ISandboxedToolTest
        {
            ValueTask RunAsync(CancellationToken cancellationToken);
        }
        """;

    private const string PROGRAM = """
        using System.Reflection;

        namespace Dami.Sandbox;

        internal static class Program
        {
            public static async Task<int> Main(string[] args)
            {
                Type[] types = Assembly.GetExecutingAssembly().GetTypes();
                if (args is ["--test"])
                {
                    Type[] tests = ConcreteImplementations<ISandboxedToolTest>(types);
                    if (tests.Length == 0)
                    {
                        throw new InvalidOperationException("No sandboxed tool tests were supplied.");
                    }

                    foreach (Type type in tests)
                    {
                        var test = (ISandboxedToolTest)Activator.CreateInstance(type)!;
                        await test.RunAsync(CancellationToken.None).ConfigureAwait(false);
                    }

                    await Console.Out.WriteAsync($"tests_passed={tests.Length}").ConfigureAwait(false);
                    return 0;
                }

                if (args.Length != 0)
                {
                    throw new ArgumentException("The sandbox accepts only the fixed --test mode.");
                }

                Type toolType = ConcreteImplementations<ISandboxedTool>(types) switch
                {
                    [Type only] => only,
                    _ => throw new InvalidOperationException("Exactly one sandboxed tool is required."),
                };
                var tool = (ISandboxedTool)Activator.CreateInstance(toolType)!;
                string input = await Console.In.ReadToEndAsync().ConfigureAwait(false);
                string output = await tool.ExecuteAsync(input, CancellationToken.None)
                    .ConfigureAwait(false);
                await Console.Out.WriteAsync(output).ConfigureAwait(false);
                return 0;
            }

            private static Type[] ConcreteImplementations<TContract>(IEnumerable<Type> types)
            {
                return types.Where(type =>
                        typeof(TContract).IsAssignableFrom(type)
                        && type is { IsAbstract: false, IsInterface: false })
                    .OrderBy(type => type.FullName, StringComparer.Ordinal)
                    .ToArray();
            }
        }
        """;
}
