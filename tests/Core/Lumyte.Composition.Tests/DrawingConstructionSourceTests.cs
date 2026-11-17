using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Lumyte.Composition.Tests;

/// <summary>Checks the actual separately compiled drawing consumer, not an illustrative string.</summary>
public sealed class DrawingConstructionSourceTests
{
    /// <summary>Checks all eleven primary slot expressions contain no object construction or intermediate node variables.</summary>
    [Fact]
    public void PublicDrawingConstructionUsesOnlyRealFactories()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Lumyte.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        string source = File.ReadAllText(Path.Combine(directory.FullName, "tests/Graphics/Lumyte.Resources.Graphics.AotSmoke/CompositionSmoke.cs"));
        SyntaxNode root = CSharpSyntaxTree.ParseText(source).GetRoot();
        VariableDeclaratorSyntax host = Assert.Single(root.DescendantNodes().OfType<VariableDeclaratorSyntax>(), variable => variable.Identifier.ValueText == "host");
        ExpressionSyntax construction = host.Initializer!.Value;
        Assert.Empty(construction.DescendantNodesAndSelf().OfType<ObjectCreationExpressionSyntax>());
        Assert.Empty(construction.DescendantNodesAndSelf().OfType<ImplicitObjectCreationExpressionSyntax>());
        Assert.Equal(11, construction.DescendantNodes().OfType<InvocationExpressionSyntax>().Count(invocation => invocation.Expression is MemberAccessExpressionSyntax member && member.Expression.ToString() == "Renderer"));
        Assert.Contains("RetentionLimits(maxFramesInFlight:", construction.ToString(), StringComparison.Ordinal);
        Assert.Contains("Paths(maxSegments: 4096)", construction.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("CreateDefault", construction.ToString(), StringComparison.Ordinal);
    }
}
