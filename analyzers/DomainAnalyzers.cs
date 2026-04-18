using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DomainAnalyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MagicStringAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "PT005";
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Magic string literal",
        "Use a domain constant instead of magic string '{0}'",
        "Domain",
        DiagnosticSeverity.Error,
        true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext Context)
    {
        Context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        Context.EnableConcurrentExecution();
        Context.RegisterSyntaxNodeAction(AnalyzeStringLiteral, SyntaxKind.StringLiteralExpression);
    }

    private static void AnalyzeStringLiteral(SyntaxNodeAnalysisContext Context)
    {
        var Literal = (LiteralExpressionSyntax)Context.Node;
        var Value = Literal.Token.ValueText;
        if (string.IsNullOrWhiteSpace(Value) || Value.Length <= 1)
            return;
        if (IsAllowedContext(Literal))
            return;
        Context.ReportDiagnostic(Diagnostic.Create(Rule, Literal.GetLocation(), Value.Length > 30 ? Value.Substring(0, 30) + "..." : Value));
    }

    private static bool IsAllowedContext(LiteralExpressionSyntax Literal)
    {
        var Parent = Literal.Parent;
        if (Parent is AttributeArgumentSyntax || Parent is ParameterSyntax)
            return true;
        if (Parent is EqualsValueClauseSyntax Equals && Equals.Parent is VariableDeclaratorSyntax Declarator)
        {
            var Field = Declarator.Parent?.Parent;
            if (Field is FieldDeclarationSyntax FieldDecl && FieldDecl.Modifiers.Any(M => M.IsKind(SyntaxKind.ConstKeyword) || M.IsKind(SyntaxKind.StaticKeyword)))
                return true;
            if (Field is LocalDeclarationStatementSyntax LocalDecl && LocalDecl.Modifiers.Any(M => M.IsKind(SyntaxKind.ConstKeyword)))
                return true;
        }
        if (Parent is ArgumentSyntax Arg && Arg.Parent is ArgumentListSyntax ArgList && ArgList.Parent is InvocationExpressionSyntax Invocation)
        {
            var MethodName = Invocation.Expression.ToString();
            if (MethodName.Contains("WriteLine") || MethodName.Contains("Error.WriteLine") || MethodName.Contains("AppendLine") || MethodName.Contains("Combine") || MethodName.Contains("GetFolderPath"))
                return true;
        }
        if (Parent is BinaryExpressionSyntax)
            return true;
        if (Parent is InitializerExpressionSyntax)
            return true;
        if (Parent is SwitchLabelSyntax || Parent is CasePatternSwitchLabelSyntax || Parent is ConstantPatternSyntax || Parent is CaseSwitchLabelSyntax)
            return true;
        if (Literal.Ancestors().OfType<SwitchSectionSyntax>().Any() && Parent is ConstantPatternSyntax or CaseSwitchLabelSyntax)
            return true;
        if (Parent is AssignmentExpressionSyntax Assignment && Assignment.Left is ElementAccessExpressionSyntax)
            return true;
        if (Parent is BracketedArgumentListSyntax || Literal.Ancestors().OfType<ElementAccessExpressionSyntax>().Any())
            return true;
        var PragmaParent = Literal.Ancestors().OfType<PragmaWarningDirectiveTriviaSyntax>().Any();
        if (PragmaParent)
            return true;
        return false;
    }
}

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MagicNumberAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "PT006";
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Magic number literal",
        "Use a domain constant instead of magic number '{0}'",
        "Domain",
        DiagnosticSeverity.Error,
        true);

    private static readonly ImmutableHashSet<int> AllowedIntegers = ImmutableHashSet.Create(
        -1, 0, 1, 2, 3, 4, 5, 8, 10, 15, 16, 50, 100, 150, 200, 300, 1000, 1024);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext Context)
    {
        Context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        Context.EnableConcurrentExecution();
        Context.RegisterSyntaxNodeAction(AnalyzeNumericLiteral, SyntaxKind.NumericLiteralExpression);
    }

    private static void AnalyzeNumericLiteral(SyntaxNodeAnalysisContext Context)
    {
        var Literal = (LiteralExpressionSyntax)Context.Node;
        var Token = Literal.Token;
        if (Token.Value is int IntVal && AllowedIntegers.Contains(IntVal))
            return;
        if (Token.Value is double DoubleVal && (DoubleVal == 0.0 || DoubleVal == 1.0))
            return;
        if (Token.Value is long LongVal && (LongVal == 0L || LongVal == 1L))
            return;
        var Parent = Literal.Parent;
        if (Parent is EqualsValueClauseSyntax Equals && Equals.Parent is VariableDeclaratorSyntax Declarator)
        {
            var Field = Declarator.Parent?.Parent;
            if (Field is FieldDeclarationSyntax FieldDecl && FieldDecl.Modifiers.Any(M => M.IsKind(SyntaxKind.ConstKeyword) || M.IsKind(SyntaxKind.StaticKeyword)))
                return;
            if (Field is LocalDeclarationStatementSyntax LocalDecl && LocalDecl.Modifiers.Any(M => M.IsKind(SyntaxKind.ConstKeyword)))
                return;
        }
        if (Parent is AttributeArgumentSyntax || Parent is ParameterSyntax)
            return;
        if (Parent is InitializerExpressionSyntax)
            return;
        if (Parent is CaseSwitchLabelSyntax || Parent is ConstantPatternSyntax)
            return;
        if (Parent is PrefixUnaryExpressionSyntax Prefix && Prefix.Parent is EqualsValueClauseSyntax)
            return;
        Context.ReportDiagnostic(Diagnostic.Create(Rule, Literal.GetLocation(), Token.ValueText));
    }
}

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InterpolationConstantAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "PT007";
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "String interpolation with inline literal",
        "Extract interpolated literal '{0}' to a domain constant",
        "Domain",
        DiagnosticSeverity.Error,
        true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext Context)
    {
        Context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        Context.EnableConcurrentExecution();
        Context.RegisterSyntaxNodeAction(AnalyzeInterpolation, SyntaxKind.InterpolatedStringExpression);
    }

    private static void AnalyzeInterpolation(SyntaxNodeAnalysisContext Context)
    {
        var Interpolated = (InterpolatedStringExpressionSyntax)Context.Node;
        foreach (var Content in Interpolated.Contents)
        {
            if (Content is InterpolatedStringTextSyntax Text)
            {
                var TextValue = Text.TextToken.ValueText.Trim();
                if (TextValue.Length > 1)
                    Context.ReportDiagnostic(Diagnostic.Create(Rule, Text.GetLocation(), TextValue.Length > 30 ? TextValue.Substring(0, 30) + "..." : TextValue));
            }
        }
    }
}

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FileLengthAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "PT008";
    private const int MaxLines = 300;
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "File too long",
        "File has {0} lines, maximum allowed is " + MaxLines,
        "Domain",
        DiagnosticSeverity.Error,
        true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext Context)
    {
        Context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        Context.EnableConcurrentExecution();
        Context.RegisterSyntaxTreeAction(AnalyzeTree);
    }

    private static void AnalyzeTree(SyntaxTreeAnalysisContext Context)
    {
        var Text = Context.Tree.GetText();
        var LineCount = Text.Lines.Count;
        if (LineCount > MaxLines)
            Context.ReportDiagnostic(Diagnostic.Create(Rule, Location.Create(Context.Tree, Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(0, 0)), LineCount));
    }
}
