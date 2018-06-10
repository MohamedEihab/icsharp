using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace iCSharp.Kernel.Shell
{
    [ExportCompletionProvider(name: nameof(CustomCompletionProvider), language: LanguageNames.CSharp)]//, Shared]
    internal class CustomCompletionProvider : CompletionProvider
    {
        private const string Receiver = nameof(Receiver);
        private const string Description = nameof(Description);

        public override bool ShouldTriggerCompletion(SourceText text, int caretPosition, CompletionTrigger trigger, OptionSet options)
        {
            switch (trigger.Kind)
            {
                case CompletionTriggerKind.Insertion:
                    return ShouldTriggerCompletion(text, caretPosition);

                default:
                    return false;
            }
        }

        private static bool ShouldTriggerCompletion(SourceText text, int position)
        {
            // Provide completion if user typed "." after a whitespace/tab/newline char.
            var insertedCharacterPosition = position - 1;
            if (insertedCharacterPosition <= 0)
            {
                return false;
            }

            var ch = text[insertedCharacterPosition];
            var previousCh = text[insertedCharacterPosition - 1];
            return ch == '.' &&
                (char.IsWhiteSpace(previousCh) || previousCh == '\t' || previousCh == '\r' || previousCh == '\n');
        }

        public async override Task ProvideCompletionsAsync(CompletionContext context)
        {
            var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
            var text = await model.SyntaxTree.GetTextAsync(context.CancellationToken).ConfigureAwait(false);
            if (!ShouldTriggerCompletion(text, context.Position))
            {
                return;
            }

            // Only provide completion in method body.
            var enclosingMethod = model.GetEnclosingSymbol(context.Position, context.CancellationToken) as IMethodSymbol;
            if (enclosingMethod == null)
            {
                return;
            }

            // Get all accessible members in this and base types.
            var membersToSuggest = GetAccessibleMembersInThisAndBaseTypes(
                enclosingMethod.ContainingType,
                isStatic: enclosingMethod.IsStatic,
                position: context.Position - 1,
                model: model);

            // Add completion for each member.
            foreach (var member in membersToSuggest)
            {
                // Ignore constructors
                if ((member as IMethodSymbol)?.MethodKind == MethodKind.Constructor)
                {
                    continue;
                }

                // Add receiver and description properties.
                var receiver = enclosingMethod.IsStatic ? member.ContainingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) : "this";
                var description = member.ToMinimalDisplayString(model, context.Position - 1);

                var properties = ImmutableDictionary<string, string>.Empty
                    .Add(Receiver, receiver)
                    .Add(Description, description);

                // Compute completion tags to display.
                var tags = GetCompletionTags(member).ToImmutableArray();

                // Add completion item.
                var item = CompletionItem.Create(member.Name, properties: properties, tags: tags);
                context.AddItem(item);
            }
        }

        private static ImmutableArray<ISymbol> GetAccessibleMembersInThisAndBaseTypes(ITypeSymbol containingType, bool isStatic, int position, SemanticModel model)
        {
            var types = GetBaseTypesAndThis(containingType);
            return types.SelectMany(x => x.GetMembers().Where(m => m.IsStatic == isStatic && model.IsAccessible(position, m)))
                        .ToImmutableArray();
        }

        private static IEnumerable<ITypeSymbol> GetBaseTypesAndThis(ITypeSymbol type)
        {
            var current = type;
            while (current != null)
            {
                yield return current;
                current = current.BaseType;
            }
        }

        public override Task<CompletionDescription> GetDescriptionAsync(Document document, CompletionItem item, CancellationToken cancellationToken)
        {
            return Task.FromResult(CompletionDescription.FromText(item.Properties[Description]));
        }

        public override Task<CompletionChange> GetChangeAsync(Document document, CompletionItem item, char? commitKey, CancellationToken cancellationToken)
        {
            // Get new text replacement and span.
            var receiver = item.Properties[Receiver];
            var newText = $"{receiver}.{item.DisplayText}";
            var newSpan = new TextSpan(item.Span.Start - 1, 1);

            // Return the completion change with the new text change.
            var textChange = new TextChange(newSpan, newText);
            return Task.FromResult(CompletionChange.Create(textChange));
        }

        private IEnumerable<string> GetCompletionTags(ISymbol symbol)
        {
            // Get completion tags based on symbol accessiblity and symbol kind.
            switch (symbol.DeclaredAccessibility)
            {
                case Accessibility.Private:
                    yield return CompletionTags.Private;
                    break;

                case Accessibility.Public:
                    yield return CompletionTags.Public;
                    break;

                case Accessibility.Protected:
                    yield return CompletionTags.Protected;
                    break;

                case Accessibility.Internal:
                case Accessibility.ProtectedOrInternal:
                    yield return CompletionTags.Internal;
                    break;

                case Accessibility.ProtectedAndInternal:
                    yield return CompletionTags.Protected;
                    yield return CompletionTags.Internal;
                    break;
            }

            switch (symbol.Kind)
            {
                case SymbolKind.NamedType:
                    switch (((INamedTypeSymbol)symbol).TypeKind)
                    {
                        case TypeKind.Class:
                            yield return CompletionTags.Class;
                            break;

                        case TypeKind.Enum:
                            yield return CompletionTags.Enum;
                            break;

                        case TypeKind.Interface:
                            yield return CompletionTags.Interface;
                            break;

                        case TypeKind.Struct:
                            yield return CompletionTags.Structure;
                            break;
                    }

                    break;

                case SymbolKind.Method:
                    yield return CompletionTags.Method;
                    break;

                case SymbolKind.Field:
                    yield return CompletionTags.Field;
                    break;

                case SymbolKind.Property:
                    yield return CompletionTags.Property;
                    break;

                case SymbolKind.Event:
                    yield return CompletionTags.Event;
                    break;
            }
        }
    }
}
