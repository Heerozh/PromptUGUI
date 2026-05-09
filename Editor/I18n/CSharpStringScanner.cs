using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PromptUGUI.Editor.I18n
{
    internal static class CSharpStringScanner
    {
        public static IEnumerable<ExtractedString> Scan(string source, string filePath)
        {
            var tree = CSharpSyntaxTree.ParseText(source);
            var root = tree.GetRoot();
            var calls = root.DescendantNodes().OfType<InvocationExpressionSyntax>();

            foreach (var call in calls)
            {
                if (!IsUiTr(call)) continue;
                var args = call.ArgumentList.Arguments;
                if (args.Count == 0) continue;

                var msgid = AsLiteral(args[0].Expression);
                if (msgid == null) continue;       // dynamic — skip

                string ctx = null;
                var ctxArgInvalid = false;
                for (var i = 1; i < args.Count; i++)
                {
                    var a = args[i];
                    if (a.NameColon?.Name.Identifier.ValueText == "ctx")
                    {
                        ctx = AsLiteral(a.Expression);
                        if (ctx == null) { ctxArgInvalid = true; break; }
                    }
                }
                if (ctxArgInvalid) continue;

                var es = new ExtractedString
                {
                    Msgid = msgid,
                    Msgctxt = ctx,
                    LocalePartition = "_code",
                };
                var line = call.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                es.References.Add($"{filePath}:{line}");

                var enclosing = call.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
                if (enclosing != null)
                    es.ExtractedComments.Add($"in {enclosing.Identifier.ValueText}()");

                foreach (var c in CollectLeadingComments(call))
                    es.ExtractedComments.Add(c);

                if (TmpRichTextDetector.HasTmpTags(msgid))
                    es.ExtractedComments.Add(
                        "Contains TMP rich text tags. Preserve tags and attribute values verbatim.");

                yield return es;
            }
        }

        private static bool IsUiTr(InvocationExpressionSyntax call)
        {
            // Match `UI.Tr(...)` and `PromptUGUI.Application.UI.Tr(...)` and `Tr(...)` (looser).
            return call.Expression switch
            {
                MemberAccessExpressionSyntax m =>
                    m.Name.Identifier.ValueText == "Tr",
                IdentifierNameSyntax id =>
                    id.Identifier.ValueText == "Tr",
                _ => false,
            };
        }

        private static string AsLiteral(ExpressionSyntax e)
        {
            if (e is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
                return lit.Token.ValueText;
            return null;
        }

        private static IEnumerable<string> CollectLeadingComments(InvocationExpressionSyntax call)
        {
            var stmt = call.AncestorsAndSelf().OfType<StatementSyntax>().FirstOrDefault();
            if (stmt == null) yield break;
            foreach (var trivia in stmt.GetLeadingTrivia())
            {
                if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia))
                {
                    var text = trivia.ToString().TrimStart('/').Trim();
                    if (!string.IsNullOrEmpty(text)) yield return text;
                }
            }
        }
    }
}
