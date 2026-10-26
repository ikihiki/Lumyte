using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Lumyte.Execution.Generators;

/// <summary>Generates explicit typed work and one-reference senders without runtime reflection.</summary>
[Generator(LanguageNames.CSharp)]
public sealed class ExecutionGenerator : IIncrementalGenerator
{
    private const string Prefix = "Lumyte.Execution.";
    private const string Execution = "global::Lumyte.Execution.";
    private const string Token = "global::System.Threading.CancellationToken";
    private static readonly DiagnosticDescriptor _error1 = new("LMEXEC001", "Invalid execution declaration", "{0}", "Lumyte.Execution", DiagnosticSeverity.Error, true);
    private static readonly DiagnosticDescriptor _error2 = new("LMEXEC002", "Invalid execution declaration", "{0}", "Lumyte.Execution", DiagnosticSeverity.Error, true);
    private static readonly DiagnosticDescriptor _error3 = new("LMEXEC003", "Invalid execution declaration", "{0}", "Lumyte.Execution", DiagnosticSeverity.Error, true);
    private static readonly DiagnosticDescriptor _error4 = new("LMEXEC004", "Invalid execution declaration", "{0}", "Lumyte.Execution", DiagnosticSeverity.Error, true);
    private static readonly DiagnosticDescriptor _error5 = new("LMEXEC005", "Invalid execution declaration", "{0}", "Lumyte.Execution", DiagnosticSeverity.Error, true);
    private static readonly DiagnosticDescriptor _error6 = new("LMEXEC006", "Invalid execution declaration", "{0}", "Lumyte.Execution", DiagnosticSeverity.Error, true);
    private static readonly DiagnosticDescriptor _error7 = new("LMEXEC007", "Invalid execution declaration", "{0}", "Lumyte.Execution", DiagnosticSeverity.Error, true);
    private static readonly DiagnosticDescriptor _error8 = new("LMEXEC008", "Invalid execution declaration", "{0}", "Lumyte.Execution", DiagnosticSeverity.Error, true);
    private static readonly DiagnosticDescriptor[] _errors = [_error1, _error2, _error3, _error4, _error5, _error6, _error7, _error8];

    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValueProvider<ImmutableArray<IMethodSymbol>> commands = Methods(context, "ExecutionCommandAttribute");
        IncrementalValueProvider<ImmutableArray<IMethodSymbol>> updates = Methods(context, "ExecutionUpdateAttribute");
        IncrementalValueProvider<ImmutableArray<IMethodSymbol>> ranges = Methods(context, "ExecutionRangeAttribute");
        IncrementalValueProvider<ImmutableArray<IMethodSymbol>> methods = commands.Combine(updates).Combine(ranges)
            .Select(static (pair, _) => pair.Left.Left.AddRange(pair.Left.Right).AddRange(pair.Right));
        context.RegisterSourceOutput(methods.Combine(context.CompilationProvider), static (output, pair) => Generate(output, pair.Left, pair.Right));
    }

    private static IncrementalValueProvider<ImmutableArray<IMethodSymbol>> Methods(IncrementalGeneratorInitializationContext context, string attribute) =>
        context.SyntaxProvider.ForAttributeWithMetadataName(Prefix + attribute, static (node, _) => node is MethodDeclarationSyntax, static (attributeContext, _) => (IMethodSymbol)attributeContext.TargetSymbol).Collect();

    private static void Generate(SourceProductionContext output, ImmutableArray<IMethodSymbol> methods, Compilation compilation)
    {
        var groups = new Dictionary<INamedTypeSymbol, List<IMethodSymbol>>(SymbolEqualityComparer.Default);
        foreach (IMethodSymbol method in methods)
        {
            if (!groups.TryGetValue(method.ContainingType, out List<IMethodSymbol>? group))
            {
                group = [];
                groups.Add(method.ContainingType, group);
            }

            if (!group.Contains(method, SymbolEqualityComparer.Default))
            {
                group.Add(method);
            }
        }

        foreach (KeyValuePair<INamedTypeSymbol, List<IMethodSymbol>> group in groups.OrderBy(group => group.Key.ToDisplayString(), StringComparer.Ordinal))
        {
            Emit(output, compilation, group.Key, group.Value);
        }
    }

    private static void Emit(SourceProductionContext output, Compilation compilation, INamedTypeSymbol type, List<IMethodSymbol> methods)
    {
        bool valid = true;
        void Error(int index, ISymbol symbol, string message)
        {
            output.ReportDiagnostic(Diagnostic.Create(_errors[index - 1], symbol.Locations.FirstOrDefault(), message));
            valid = false;
        }

        bool hasCommands = methods.Any(method => method.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == Prefix + "ExecutionCommandAttribute"));
        bool wrapper = type.TypeKind == TypeKind.Struct && type.IsReadOnly;
        if ((hasCommands && !wrapper) || (!wrapper && type.TypeKind != TypeKind.Class) || type.IsRecord || type.IsAbstract || type.IsStatic || type.Arity != 0 || type.ContainingType is not null
            || type.IsFileLocal || type.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal)
            || type.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax() is not TypeDeclarationSyntax declaration || !declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
            || type.GetMembers().Any(member => member.Name.StartsWith("__LumyteExecution", StringComparison.Ordinal)))
        {
            Error(1, type, "Commands require a namespace-level nongeneric readonly partial struct wrapper.");
        }

        IFieldSymbol[] fields = type.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsStatic).ToArray();
        string receiverField = string.Empty;
        if (hasCommands)
        {
            if (fields.Length != 1 || fields[0].IsImplicitlyDeclared || !fields[0].IsReadOnly || fields[0].Type.TypeKind is not (TypeKind.Class or TypeKind.Interface) || fields[0].NullableAnnotation == NullableAnnotation.Annotated)
            {
                Error(7, type, "A command wrapper must contain exactly one explicit readonly nonnullable reference field.");
            }
            else
            {
                receiverField = fields[0].Name;
            }
        }

        if (hasCommands)
        {
            foreach (SyntaxReference reference in type.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax() is not TypeDeclarationSyntax declaration)
                {
                    continue;
                }

                SemanticModel semantics = compilation.GetSemanticModel(declaration.SyntaxTree);
                bool capturesParameter = declaration.Members.Where(member => member is not FieldDeclarationSyntax and not ConstructorDeclarationSyntax)
                    .SelectMany(member => member.DescendantNodes().OfType<IdentifierNameSyntax>())
                    .Any(identifier => semantics.GetSymbolInfo(identifier).Symbol is IParameterSymbol parameter
                        && parameter.ContainingSymbol is IMethodSymbol { MethodKind: MethodKind.Constructor }
                        && SymbolEqualityComparer.Default.Equals(parameter.ContainingType, type));
                if (capturesParameter)
                {
                    Error(7, type, "Use the readonly reference field in members; capturing a primary constructor parameter adds hidden instance storage.");
                }
            }
        }

        INamedTypeSymbol? portType = compilation.GetTypeByMetadataName(Prefix + "CommandPort`2");
        if (portType?.GetMembers("TryEnqueue").OfType<IMethodSymbol>().Any(method => method.Parameters.Length == 3 && method.Parameters[2].RefKind == RefKind.Out) != true)
        {
            Error(8, type, "This generator requires the matching typed CommandPort.TryEnqueue runtime API.");
        }

        var models = new List<MethodModel>();
        var generatedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (IMethodSymbol method in methods.OrderBy(method => method.Name, StringComparer.Ordinal))
        {
            AttributeData[] attributes = method.GetAttributes().Where(attribute => attribute.AttributeClass?.ToDisplayString() is Prefix + "ExecutionCommandAttribute" or Prefix + "ExecutionUpdateAttribute" or Prefix + "ExecutionRangeAttribute").ToArray();
            if (attributes.Length != 1)
            {
                Error(4, method, "A method must have exactly one execution declaration attribute.");
                continue;
            }

            AttributeData attribute = attributes[0];
            string name = NamedString(attribute, "Name") ?? (method.Name.EndsWith("Core", StringComparison.Ordinal) && method.Name.Length > 4 ? method.Name.Substring(0, method.Name.Length - 4) : method.Name);
            string kind = attribute.AttributeClass!.Name;
            bool existing = NamedInt(attribute, "Input") == 1;
            bool allowAwaitable = NamedBool(attribute, "AllowAwaitableResult");
            bool hasContext = method.Parameters.Length > 0 && method.Parameters[method.Parameters.Length - 1].Type.ToDisplayString() == Prefix + "WorkContext";
            IParameterSymbol[] payload = method.Parameters.Take(method.Parameters.Length - (hasContext ? 1 : 0)).ToArray();
            var model = new MethodModel(method, name, kind, existing, allowAwaitable, hasContext, payload);
            model.ReceiverField = receiverField;
            models.Add(model);

            if (!Identifier(name) || !generatedNames.Add(name))
            {
                Error(4, method, "Execution names must be unique valid C# identifiers.");
            }

            if (method.MethodKind != MethodKind.Ordinary || method.IsStatic || method.IsAsync || method.IsVirtual || method.IsOverride || method.IsAbstract || method.IsExtern || method.Arity != 0
                || method.PartialDefinitionPart is not null || method.PartialImplementationPart is not null
                || method.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax() is MethodDeclarationSyntax syntax && (syntax.Modifiers.Any(SyntaxKind.PartialKeyword) || syntax.DescendantNodes().OfType<YieldStatementSyntax>().Any())))
            {
                Error(2, method, "Use a finite synchronous nonvirtual instance method; async, iterator, generic, and partial methods are unsupported.");
            }

            if (method.ReturnsByRef || method.ReturnsByRefReadonly || (!method.ReturnsVoid && !SupportedType(method.ReturnType, type)))
            {
                Error(3, method, "The result must be an accessible, nonpointer, non-ref-like data type.");
            }

            if (hasContext && method.Parameters[method.Parameters.Length - 1].RefKind != RefKind.In)
            {
                Error(5, method, "The trailing WorkContext must be an in parameter.");
            }

            if (payload.Any(parameter => !SupportedType(parameter.Type, type) || parameter.IsOptional || parameter.IsParams
                || (kind == "ExecutionCommandAttribute" && parameter.Name is "cancellationToken" or "context" or "batch" or "builder" or "options" or "receiver") || parameter.Name.StartsWith("__", StringComparison.Ordinal)))
            {
                Error(3, method, "Inputs must be accessible typed data without optional/params or reserved control argument names.");
            }

            if (payload.Any(parameter => parameter.Type.ToDisplayString() == Prefix + "WorkContext"))
            {
                Error(5, method, "WorkContext can only be injected as the trailing in parameter.");
            }

            if (kind == "ExecutionCommandAttribute")
            {
                if (NamedInt(attribute, "Input") is < 0 or > 1 || (existing ? payload.Length != 1 || payload[0].RefKind != RefKind.In || (!payload[0].Type.IsValueType || (payload[0].Type is INamedTypeSymbol inputType && inputType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)) : payload.Any(parameter => parameter.RefKind != RefKind.None)))
                {
                    Error(5, method, "Use packed value arguments or one in struct command.");
                }

                if (IsAwaitable(method.ReturnType) && !allowAwaitable)
                {
                    Error(6, method, "Awaitable results require explicit startup-only permission and cannot use an instance sender.");
                }

                if (allowAwaitable && !IsAwaitable(method.ReturnType))
                {
                    Error(6, method, "AllowAwaitableResult applies only to actual pending-operation results.");
                }
            }
            else if (kind == "ExecutionUpdateAttribute")
            {
                if (!method.ReturnsVoid || payload.Length != 1 || payload[0].RefKind != RefKind.In || payload[0].Type.ToDisplayString() != Prefix + "UpdateTick")
                {
                    Error(5, method, "An update takes in UpdateTick, optionally followed by in WorkContext, and returns void.");
                }
            }
            else
            {
                string? length = attribute.ConstructorArguments.FirstOrDefault().Value as string;
                IMethodSymbol[] lengths = type.GetMembers(length ?? string.Empty).OfType<IMethodSymbol>().ToArray();
                IMethodSymbol? lengthMethod = lengths.Length == 1 ? lengths[0] : null;
                if (hasContext || !method.ReturnsVoid || payload.Length != 3 || (payload[0].Type is INamedTypeSymbol rangeType && rangeType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T) || payload[0].RefKind != RefKind.In || !payload[0].Type.IsValueType || payload[1].RefKind != RefKind.None || payload[2].RefKind != RefKind.None
                    || payload[1].Type.SpecialType != SpecialType.System_Int32 || payload[2].Type.SpecialType != SpecialType.System_Int32
                    || lengthMethod is null || lengthMethod.IsStatic || lengthMethod.IsAsync || lengthMethod.IsVirtual || lengthMethod.IsOverride || lengthMethod.IsExtern || lengthMethod.ReturnsByRef || lengthMethod.ReturnsByRefReadonly || lengthMethod.Arity != 0 || lengthMethod.ReturnType.SpecialType != SpecialType.System_Int32
                    || lengthMethod.Parameters.Length != 1 || lengthMethod.Parameters[0].RefKind != RefKind.In || !SymbolEqualityComparer.Default.Equals(lengthMethod.Parameters[0].Type, payload[0].Type))
                {
                    Error(5, method, "A range needs (in struct context, int first, int endExclusive) and a synchronous int Length(in context). Use a nonreserved input name.");
                }

                model.LengthMethod = lengthMethod;
            }
        }

        var reserved = new HashSet<string>(StringComparer.Ordinal);
        foreach (MethodModel model in models)
        {
            string[] names = model.Kind == "ExecutionCommandAttribute"
                ? [model.Name + "Work", "Declare" + model.Name, model.Name, "Add" + model.Name + "To"]
                : [model.Name + "Work", "Declare" + model.Name];
            if (model.Kind == "ExecutionCommandAttribute")
            {
                names = names.Concat([model.Name + "Command"]).ToArray();
            }

            foreach (string name in names)
            {
                if (!reserved.Add(name) || name == type.Name || type.GetMembers(name).Length != 0)
                {
                    Error(1, model.Method, "Generated execution member collides with an existing or generated member: " + name);
                }
            }
        }

        if (!valid)
        {
            return;
        }

        var text = new StringBuilder("// <auto-generated/>\n#nullable enable\n");
        if (!type.ContainingNamespace.IsGlobalNamespace)
        {
            text.Append("namespace ").Append(type.ContainingNamespace.ToDisplayString()).AppendLine(";");
        }

        text.Append(type.DeclaredAccessibility == Accessibility.Public ? "public " : "internal ").Append(wrapper ? "readonly partial struct " : "partial class ").Append(Escape(type.Name)).AppendLine("\n{");
        foreach (MethodModel model in models.OrderBy(model => model.Name, StringComparer.Ordinal))
        {
            if (model.Kind == "ExecutionCommandAttribute")
            {
                EmitCommand(text, type, model, !model.Name.EndsWith("Async", StringComparison.Ordinal) && model.Name + "Async" != type.Name && !reserved.Contains(model.Name + "Async") && type.GetMembers(model.Name + "Async").Length == 0);
            }
            else
            {
                EmitDriven(text, type, model);
            }
        }

        text.AppendLine("}");
        output.AddSource(type.ToDisplayString().Replace('.', '_') + ".Execution.g.cs", SourceText.From(DocumentGeneratedMembers(text.ToString()), Encoding.UTF8));
    }

    private static void EmitCommand(StringBuilder text, INamedTypeSymbol type, MethodModel model, bool asyncAlias)
    {
        string command = model.Command;
        string result = model.Result;
        string queue = "__LumyteExecution" + model.Name + "Queue";
        string handler = "__LumyteExecution" + model.Name + "Handler";
        text.Append("public readonly struct ").Append(command).AppendLine("\n{");
        text.Append("public readonly ").Append(Type(type)).AppendLine(" __Target;");
        for (int i = 0; i < model.Payload.Length; i++)
        {
            text.Append("public readonly ").Append(Type(model.Payload[i].Type)).Append(" F").Append(i).AppendLine(";");
        }

        text.Append("public ").Append(command).Append('(').Append(Type(type)).Append(" receiver");
        if (model.Payload.Length > 0)
        {
            text.Append(", ").Append(Parameters(model.Payload));
        }

        text.AppendLine(")\n{\n__Target = receiver;");
        for (int i = 0; i < model.Payload.Length; i++)
        {
            text.Append('F').Append(i).Append(" = ").Append(Escape(model.Payload[i].Name)).AppendLine(";");
        }

        text.AppendLine("}\n}");
        text.Append("private static readonly ").Append(Execution).Append("CommandPort<").Append(command).Append(", ").Append(result).Append("> ").Append(queue).AppendLine(" = new();");
        text.Append("private readonly struct ").Append(handler).Append(" : ").Append(Execution).Append("ICommandHandler<").Append(command).Append(", ").Append(result).AppendLine(">\n{");
        text.Append("public ").Append(result).Append(" Execute(in ").Append(command).Append(" command, in ").Append(Execution).AppendLine("WorkContext context)\n{\ncontext.VerifyAccess();");
        if (!model.Method.ReturnsVoid)
        {
            text.Append("return ");
        }

        text.Append("command.__Target.").Append(Escape(model.Method.Name)).Append('(');
        text.Append(string.Join(", ", model.Payload.Select((parameter, index) => (parameter.RefKind == RefKind.In ? "in " : string.Empty) + "command.F" + index)));
        if (model.HasContext)
        {
            text.Append(model.Payload.Length > 0 ? ", in context" : "in context");
        }

        text.AppendLine(");");
        if (model.Method.ReturnsVoid)
        {
            text.AppendLine("return default;");
        }

        text.AppendLine("}\n}");
        text.Append("public static ").Append(model.Name).Append("Work Declare").Append(model.Name).Append('(').Append(Execution).Append("ModuleBuilder builder, ").Append(Execution).AppendLine("WorkOptions options)\n{");
        if (model.AllowAwaitable)
        {
            text.Append("if (options.Admission != ").Append(Execution).AppendLine("WorkAdmission.Startup) throw new global::System.ArgumentException(\"Pending operations require Startup admission.\", nameof(options));");
        }

        text.Append("return new(builder.Command<").Append(command).Append(", ").Append(result).Append(", ").Append(handler).Append(">(").Append(Literal(model.Name)).Append(", default, options, ").Append(queue).AppendLine("));\n}");
        text.Append("public readonly struct ").Append(model.Name).AppendLine("Work\n{");
        text.Append("internal ").Append(model.Name).Append("Work(").Append(Execution).Append("WorkKey<").Append(command).Append(", ").Append(result).AppendLine("> key) { Key = key; }");
        text.Append("public ").Append(Execution).Append("WorkKey<").Append(command).Append(", ").Append(result).AppendLine("> Key { get; }");
        EmitSend(text, model, "ExecuteAsync", model.Payload, "context.TryEnqueue(Key, in __command, cancellationToken, out var __completion)", Execution + "ModuleContext context, " + Type(type) + " receiver");
        text.AppendLine("}");
        EmitSend(text, model, Escape(model.Name), model.Payload, queue + ".TryEnqueue(in __command, cancellationToken, out var __completion)", null);
        if (asyncAlias)
        {
            EmitSend(text, model, Escape(model.Name + "Async"), model.Payload, queue + ".TryEnqueue(in __command, cancellationToken, out var __completion)", null);
        }

        EmitBatch(text, model, "Add" + model.Name + "To", model.Payload);
    }

    private static void EmitSend(StringBuilder text, MethodModel model, string name, IParameterSymbol[] parameters, string call, string? first)
    {
        text.Append("public ").Append("global::System.Threading.Tasks.ValueTask<").Append(model.Result).Append("> ").Append(name).Append('(');
        string prefix = string.Join(", ", new[] { first, Parameters(parameters) }.Where(value => !string.IsNullOrEmpty(value)));
        if (prefix.Length > 0)
        {
            text.Append(prefix).Append(", ");
        }

        text.Append(Token).AppendLine(" cancellationToken = default)\n{");
        EmitPayload(text, model, first is null ? "this" : "receiver");
        text.Append("var __status = ").Append(call).AppendLine(";");
        text.AppendLine("return __status switch\n{");
        text.Append(Execution).Append("CommandEnqueueStatus.Accepted => new global::System.Threading.Tasks.ValueTask<").Append(model.Result).AppendLine(">(__completion!, 0),");
        text.Append(Execution).Append("CommandEnqueueStatus.Canceled => global::System.Threading.Tasks.ValueTask.FromCanceled<").Append(model.Result).AppendLine(">(cancellationToken),");
        text.Append(Execution).Append("CommandEnqueueStatus.NotRunning => global::System.Threading.Tasks.ValueTask.FromException<").Append(model.Result).AppendLine(">(new global::System.InvalidOperationException(\"Command admission is closed.\")),");
        text.Append(Execution).Append("CommandEnqueueStatus.QueueFull => global::System.Threading.Tasks.ValueTask.FromException<").Append(model.Result).Append(">(new ").Append(Execution).AppendLine("ExecutionQueueFullException(\"Command capacity is exhausted.\")),");
        text.AppendLine("_ => throw new global::System.InvalidOperationException(\"Unknown enqueue status.\")\n};\n}");
    }

    private static void EmitBatch(StringBuilder text, MethodModel model, string name, IParameterSymbol[] parameters)
    {
        text.Append("public ").Append(Execution).Append("CommandResultKey<").Append(model.Result).Append("> ").Append(name).Append('(').Append(Execution).Append("CommandBatch batch");
        if (parameters.Length > 0)
        {
            text.Append(", ").Append(Parameters(parameters));
        }

        text.AppendLine(")\n{");
        EmitPayload(text, model, "this");
        text.Append("return batch.Add(__LumyteExecution").Append(model.Name).AppendLine("Queue, in __command);\n}");
    }

    private static void EmitPayload(StringBuilder text, MethodModel model, string receiver)
    {
        text.Append("if (").Append(receiver).Append('.').Append(Escape(model.ReceiverField)).AppendLine(" is null) throw new global::System.InvalidOperationException(\"Wrapper has no original instance.\");");
        text.Append("var __command = new ").Append(model.Command).Append('(').Append(receiver);
        if (model.Payload.Length > 0)
        {
            text.Append(", ").Append(string.Join(", ", model.Payload.Select(parameter => Escape(parameter.Name))));
        }

        text.AppendLine(");");
    }

    private static void EmitDriven(StringBuilder text, INamedTypeSymbol type, MethodModel model)
    {
        bool update = model.Kind == "ExecutionUpdateAttribute";
        string handler = "__LumyteExecution" + model.Name + "Handler";
        string key = update ? Execution + "UpdateKey" : Execution + "RangeWorkKey<" + Type(model.Payload[0].Type) + ">";
        text.Append("private sealed class ").Append(handler).Append('(').Append(Type(type)).Append(" state) : ").Append(Execution);
        text.Append(update ? "IUpdateHandler" : "IRangeWorkHandler<" + Type(model.Payload[0].Type) + ">").AppendLine("\n{");
        if (update)
        {
            text.Append("public void Update(in ").Append(Execution).Append("UpdateTick tick, in ").Append(Execution).AppendLine("WorkContext context)\n{\ncontext.VerifyAccess();");
            text.Append("state.").Append(Escape(model.Method.Name)).Append("(in tick").Append(model.HasContext ? ", in context" : string.Empty).AppendLine(");\n}");
        }
        else
        {
            string input = Type(model.Payload[0].Type);
            text.Append("public int GetLength(in ").Append(input).Append(" input) => state.").Append(Escape(model.LengthMethod!.Name)).AppendLine("(in input);");
            text.Append("public void Execute(in ").Append(input).AppendLine(" input, int first, int endExclusive)\n{");
            if (model.HasContext)
            {
                // Range handlers have no fixed owner context; explicit context injection is not part of the range primitive.
                text.AppendLine("throw new global::System.InvalidOperationException(\"Range context injection is unsupported.\");");
            }
            else
            {
                text.Append("state.").Append(Escape(model.Method.Name)).AppendLine("(in input, first, endExclusive);");
            }

            text.AppendLine("}");
        }

        text.AppendLine("}");
        text.Append("public ").Append(model.Name).Append("Work Declare").Append(model.Name).Append('(').Append(Execution).Append("ModuleBuilder builder");
        if (update)
        {
            text.Append(", ").Append(Execution).Append("OwnerKey owner, global::System.ReadOnlyMemory<").Append(Execution).Append("GateKey> requiredGates = default");
        }

        text.Append(") => new(builder.").Append(update ? "Update<" : "Range<" + Type(model.Payload[0].Type) + ", ").Append(handler).Append(">(").Append(Literal(model.Name)).Append(update ? ", owner, new(this), requiredGates" : ", new(this)").AppendLine("));");
        text.Append("public readonly struct ").Append(model.Name).AppendLine("Work\n{");
        text.Append("internal ").Append(model.Name).Append("Work(").Append(key).AppendLine(" key) { Key = key; }");
        text.Append("public ").Append(key).AppendLine(" Key { get; }\n}");
    }

    private static string DocumentGeneratedMembers(string source)
    {
        SyntaxNode root = CSharpSyntaxTree.ParseText(source).GetRoot();
        MemberDeclarationSyntax[] members = root.DescendantNodes().OfType<MemberDeclarationSyntax>().Where(member => member switch
        {
            BaseTypeDeclarationSyntax declaration => declaration.Modifiers.Any(SyntaxKind.PublicKeyword),
            BaseMethodDeclarationSyntax declaration => declaration.Modifiers.Any(SyntaxKind.PublicKeyword),
            BasePropertyDeclarationSyntax declaration => declaration.Modifiers.Any(SyntaxKind.PublicKeyword),
            BaseFieldDeclarationSyntax declaration => declaration.Modifiers.Any(SyntaxKind.PublicKeyword),
            _ => false,
        }).ToArray();
        return root.ReplaceNodes(members, static (original, rewritten) =>
        {
            var documentation = new StringBuilder("/// <summary>Generated typed execution declaration.</summary>\n");
            if (original is BaseMethodDeclarationSyntax method)
            {
                foreach (ParameterSyntax parameter in method.ParameterList.Parameters)
                {
                    documentation.Append("/// <param name=\"").Append(parameter.Identifier.ValueText).AppendLine("\">The typed input or execution context.</param>");
                }
            }

            return rewritten.WithLeadingTrivia(rewritten.GetLeadingTrivia().AddRange(SyntaxFactory.ParseLeadingTrivia(documentation.ToString())));
        }).ToFullString();
    }

    private static bool Identifier(string value) => value.Length > 0 && value[0] != '@' && SyntaxFacts.IsValidIdentifier(value) && SyntaxFacts.GetKeywordKind(value) == SyntaxKind.None;

    private static string Escape(string value) => "@" + value;

    private static string Type(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier | SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers));

    private static string Literal(string value) => SymbolDisplay.FormatLiteral(value, true);

    private static string Parameters(IEnumerable<IParameterSymbol> parameters) => string.Join(", ", parameters.Select(parameter => Type(parameter.Type) + " " + Escape(parameter.Name)));

    private static string? NamedString(AttributeData attribute, string name) => attribute.NamedArguments.FirstOrDefault(pair => pair.Key == name).Value.Value as string;

    private static int NamedInt(AttributeData attribute, string name) => attribute.NamedArguments.FirstOrDefault(pair => pair.Key == name).Value.Value is int value ? value : 0;

    private static bool NamedBool(AttributeData attribute, string name) => attribute.NamedArguments.FirstOrDefault(pair => pair.Key == name).Value.Value is true;

    private static bool SupportedType(ITypeSymbol type, INamedTypeSymbol container)
    {
        if (type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer or TypeKind.Dynamic or TypeKind.Delegate || type.IsRefLikeType)
        {
            return false;
        }

        if (type is IArrayTypeSymbol array)
        {
            return SupportedType(array.ElementType, container);
        }

        if (type is INamedTypeSymbol named)
        {
            if (named.Name == "Expression" && named.ContainingNamespace.ToDisplayString() == "System.Linq.Expressions")
            {
                return false;
            }

            for (INamedTypeSymbol? current = named; current is not null; current = current.ContainingType)
            {
                if (current.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal)
                    || (container.DeclaredAccessibility == Accessibility.Public && current.DeclaredAccessibility != Accessibility.Public))
                {
                    return false;
                }
            }

            return named.TypeArguments.All(argument => SupportedType(argument, container));
        }

        return true;
    }

    private static bool IsAwaitable(ITypeSymbol type) => type is INamedTypeSymbol named
        && ((named.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks" && named.Name is "Task" or "ValueTask") || named.GetMembers("GetAwaiter").Length > 0);

    private sealed class MethodModel(IMethodSymbol method, string name, string kind, bool existing, bool allowAwaitable, bool hasContext, IParameterSymbol[] payload)
    {
        internal IMethodSymbol Method { get; } = method;

        internal string Name { get; } = name;

        internal string Kind { get; } = kind;

        internal bool Existing { get; } = existing;

        internal bool AllowAwaitable { get; } = allowAwaitable;

        internal bool HasContext { get; } = hasContext;

        internal IParameterSymbol[] Payload { get; } = payload;

        internal string ReceiverField { get; set; } = string.Empty;

        internal IMethodSymbol? LengthMethod { get; set; }

        internal string Command => Name + "Command";

        internal string Result => Method.ReturnsVoid ? Execution + "Unit" : Type(Method.ReturnType);
    }
}
