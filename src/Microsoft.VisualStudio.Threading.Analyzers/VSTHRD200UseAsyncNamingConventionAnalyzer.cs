﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Microsoft.VisualStudio.Threading.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp, LanguageNames.VisualBasic)]
public class VSTHRD200UseAsyncNamingConventionAnalyzer : DiagnosticAnalyzer
{
    public const string Id = "VSTHRD200";

    internal const string NewNameKey = "NewName";

    internal const string MandatoryAsyncSuffix = "Async";

    internal static readonly DiagnosticDescriptor AddAsyncDescriptor = new DiagnosticDescriptor(
        id: Id,
        title: new LocalizableResourceString(nameof(Strings.VSTHRD200_Title), Strings.ResourceManager, typeof(Strings)),
        messageFormat: new LocalizableResourceString(nameof(Strings.VSTHRD200_AddAsync_MessageFormat), Strings.ResourceManager, typeof(Strings)),
        helpLinkUri: Utils.GetHelpLink(Id),
        category: "Style",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor RemoveAsyncDescriptor = new DiagnosticDescriptor(
        id: Id,
        title: new LocalizableResourceString(nameof(Strings.VSTHRD200_Title), Strings.ResourceManager, typeof(Strings)),
        messageFormat: new LocalizableResourceString(nameof(Strings.VSTHRD200_RemoveAsync_MessageFormat), Strings.ResourceManager, typeof(Strings)),
        helpLinkUri: Utils.GetHelpLink(Id),
        category: "Style",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
        AddAsyncDescriptor,
        RemoveAsyncDescriptor);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze);

        context.RegisterCompilationStartAction(context =>
        {
            CommonInterest.AwaitableTypeTester awaitableTypes = CommonInterest.CollectAwaitableTypes(context.Compilation, context.CancellationToken);
            context.RegisterSymbolAction(Utils.DebuggableWrapper(context => this.AnalyzeNode(context, awaitableTypes)), SymbolKind.Method);
            context.RegisterOperationAction(Utils.DebuggableWrapper(context => this.AnalyzeLocalFunction(context, awaitableTypes)), OperationKind.LocalFunction);
        });
    }

    private void AnalyzeLocalFunction(OperationAnalysisContext context, CommonInterest.AwaitableTypeTester awaitableTypes)
    {
        if (this.AnalyzeMethodSymbol(context.Compilation, ((ILocalFunctionOperation)context.Operation).Symbol, awaitableTypes, context.CancellationToken) is Diagnostic diagnostic)
        {
            context.ReportDiagnostic(diagnostic);
        }
    }

    private void AnalyzeNode(SymbolAnalysisContext context, CommonInterest.AwaitableTypeTester awaitableTypes)
    {
        if (this.AnalyzeMethodSymbol(context.Compilation, (IMethodSymbol)context.Symbol, awaitableTypes, context.CancellationToken) is Diagnostic diagnostic)
        {
            context.ReportDiagnostic(diagnostic);
        }
    }

    private Diagnostic? AnalyzeMethodSymbol(Compilation compilation, IMethodSymbol methodSymbol, CommonInterest.AwaitableTypeTester awaitableTypes, CancellationToken cancellationToken)
    {
        if (methodSymbol.AssociatedSymbol is IPropertySymbol)
        {
            // Skip accessor methods associated with properties.
            return null;
        }

        // Skip entrypoint methods since their name is non-negotiable.
        if (Utils.IsEntrypointMethod(methodSymbol, compilation, cancellationToken))
        {
            return null;
        }

        // Skip the method of the recommended dispose pattern.
        if (methodSymbol.Name == "DisposeAsyncCore")
        {
            return null;
        }

        bool hasAsyncFocusedReturnType = Utils.HasAsyncCompatibleReturnType(methodSymbol);

        bool actuallyEndsWithAsync = methodSymbol.Name.EndsWith(MandatoryAsyncSuffix, StringComparison.CurrentCulture);

        if (hasAsyncFocusedReturnType != actuallyEndsWithAsync)
        {
            // Now that we have done the cheap checks to find that this method may deserve a diagnostic,
            // Do deeper checks to skip over methods that implement API contracts that are controlled elsewhere.
            if (methodSymbol.FindInterfacesImplemented().Any() || methodSymbol.IsOverride)
            {
                return null;
            }

            if (hasAsyncFocusedReturnType)
            {
                // We actively encourage folks to use the Async keyword only for clearly async-focused types.
                // Not just any awaitable, since some stray extension method shouldn't change the world for everyone.
                ImmutableDictionary<string, string?>? properties = ImmutableDictionary<string, string?>.Empty
                    .Add(NewNameKey, methodSymbol.Name + MandatoryAsyncSuffix);
                return Diagnostic.Create(
                    AddAsyncDescriptor,
                    methodSymbol.Locations[0],
                    properties);
            }
            else if (!awaitableTypes.IsAwaitableType(methodSymbol.ReturnType))
            {
                // Only warn about abusing the Async suffix if the return type is not awaitable.
                ImmutableDictionary<string, string?>? properties = ImmutableDictionary<string, string?>.Empty
                    .Add(NewNameKey, methodSymbol.Name.Substring(0, methodSymbol.Name.Length - MandatoryAsyncSuffix.Length));
                return Diagnostic.Create(
                    RemoveAsyncDescriptor,
                    methodSymbol.Locations[0],
                    properties);
            }
        }

        return null;
    }
}
