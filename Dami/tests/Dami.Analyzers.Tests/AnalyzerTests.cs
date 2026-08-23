using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Dami.Analyzers.Tests;

/// <summary>Each Dami rule fires on a violation and stays silent on compliant code.</summary>
public sealed class AnalyzerTests
{
    [Fact]
    public async Task RegionAnalyzer_Should_Report_A_Region_Directive()
    {
        const string code = """
            public class Sample
            {
                #region Things
                public int Value => 1;
                #endregion
            }
            """;

        var ids = await AnalyzerHarness.IdsAsync(new RegionAnalyzer(), code);

        Assert.Equal([DiagnosticIds.NO_REGIONS], ids);
    }

    [Fact]
    public async Task RegionAnalyzer_Should_Stay_Silent_Without_A_Region()
    {
        const string code = "public class Sample { public int Value => 1; }";

        var ids = await AnalyzerHarness.IdsAsync(new RegionAnalyzer(), code);

        Assert.Empty(ids);
    }

    [Fact]
    public async Task DynamicTypeAnalyzer_Should_Report_A_Dynamic_Declaration()
    {
        const string code = """
            public class Sample
            {
                public dynamic Loose() => 1;
            }
            """;

        var ids = await AnalyzerHarness.IdsAsync(new DynamicTypeAnalyzer(), code);

        Assert.Equal([DiagnosticIds.NO_DYNAMIC], ids);
    }

    [Fact]
    public async Task MethodLengthAnalyzer_Should_Report_A_Method_Over_Thirty_Lines()
    {
        var body = string.Join("\n", Enumerable.Range(0, 34).Select(index => $"        int v{index} = {index};"));
        var source = $"public class Sample\n{{\n    public void Long()\n    {{\n{body}\n    }}\n}}";

        var ids = await AnalyzerHarness.IdsAsync(new MethodLengthAnalyzer(), source);

        Assert.Equal([DiagnosticIds.METHOD_TOO_LONG], ids);
    }

    [Fact]
    public async Task MethodLengthAnalyzer_Should_Stay_Silent_On_A_Short_Method()
    {
        const string code = """
            public class Sample
            {
                public int Short()
                {
                    return 1;
                }
            }
            """;

        var ids = await AnalyzerHarness.IdsAsync(new MethodLengthAnalyzer(), code);

        Assert.Empty(ids);
    }

    [Fact]
    public async Task LoopNestingAnalyzer_Should_Report_Three_Nested_Loops()
    {
        const string code = """
            public class Sample
            {
                public void Deep()
                {
                    for (int a = 0; a < 1; a++)
                    {
                        foreach (var b in new int[0])
                        {
                            while (a > 0)
                            {
                                a--;
                            }
                        }
                    }
                }
            }
            """;

        var ids = await AnalyzerHarness.IdsAsync(new LoopNestingAnalyzer(), code);

        Assert.Equal([DiagnosticIds.LOOP_NESTING_TOO_DEEP], ids);
    }

    [Fact]
    public async Task LoopNestingAnalyzer_Should_Allow_Two_Levels()
    {
        const string code = """
            public class Sample
            {
                public void Fine()
                {
                    for (int a = 0; a < 1; a++)
                    {
                        foreach (var b in new int[0])
                        {
                            a += b;
                        }
                    }
                }
            }
            """;

        var ids = await AnalyzerHarness.IdsAsync(new LoopNestingAnalyzer(), code);

        Assert.Empty(ids);
    }

    [Fact]
    public async Task OptionalConstructorParameterAnalyzer_Should_Report_A_Defaulted_Dependency()
    {
        const string code = """
            public interface IClock { }
            public class Sample
            {
                public Sample(IClock? clock = null) { }
            }
            """;

        var ids = await AnalyzerHarness.IdsAsync(new OptionalConstructorParameterAnalyzer(), code);

        Assert.Equal([DiagnosticIds.OPTIONAL_CONSTRUCTOR_PARAMETER], ids);
    }

    [Fact]
    public async Task OptionalConstructorParameterAnalyzer_Should_Allow_A_Required_Dependency()
    {
        const string code = """
            public interface IClock { }
            public class Sample
            {
                public Sample(IClock clock) { }
            }
            """;

        var ids = await AnalyzerHarness.IdsAsync(new OptionalConstructorParameterAnalyzer(), code);

        Assert.Empty(ids);
    }

    [Fact]
    public async Task NotImplementedMemberAnalyzer_Should_Report_An_Unimplemented_Interface_Member()
    {
        const string code = """
            using System;
            public interface IThing { void Do(); }
            public class Sample : IThing
            {
                public void Do() => throw new NotImplementedException();
            }
            """;

        var ids = await AnalyzerHarness.IdsAsync(new NotImplementedMemberAnalyzer(), code);

        Assert.Equal([DiagnosticIds.NOT_IMPLEMENTED_MEMBER], ids);
    }

    [Fact]
    public async Task NotImplementedMemberAnalyzer_Should_Allow_It_On_A_Non_Interface_Member()
    {
        const string code = """
            using System;
            public class Sample
            {
                public void Scratch() => throw new NotImplementedException();
            }
            """;

        var ids = await AnalyzerHarness.IdsAsync(new NotImplementedMemberAnalyzer(), code);

        Assert.Empty(ids);
    }
}
