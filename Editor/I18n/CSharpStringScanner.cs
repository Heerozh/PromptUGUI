using System.Collections.Generic;
using System.Text;

namespace PromptUGUI.Editor.I18n
{
    internal static class CSharpStringScanner
    {
        // Identifiers that look like methods (`name(...)`) but aren't, when used as the
        // last identifier before `{`. Filters keyword-led blocks like `if (x) {` or `while (x) {`
        // so we don't claim "in if()" as the enclosing method.
        private static readonly HashSet<string> NonMethodKeywords = new HashSet<string>
        {
            "if", "while", "for", "foreach", "switch", "using",
            "catch", "lock", "fixed", "do", "try",
            "return", "throw", "yield", "await",
            "sizeof", "typeof", "nameof",
            "default", "checked", "unchecked",
            "new", "delegate",
        };

        public static IEnumerable<ExtractedString> Scan(string source, string filePath)
        {
            var tokens = Tokenize(source);
            var results = new List<ExtractedString>();
            for (var i = 0; i < tokens.Count; i++)
            {
                var t = tokens[i];
                if (t.Kind != TK.Ident || t.Text != "Tr") continue;

                var openIdx = NextSig(tokens, i + 1);
                if (openIdx < 0 || tokens[openIdx].Kind != TK.OpenParen) continue;

                var closeIdx = MatchClose(tokens, openIdx);
                if (closeIdx < 0) continue;

                var args = SplitArgs(tokens, openIdx + 1, closeIdx);
                if (args.Count == 0) continue;

                var msgid = ArgAsLiteral(tokens, args[0]);
                if (msgid == null) continue;

                string ctx = null;
                var ctxInvalid = false;
                for (var a = 1; a < args.Count; a++)
                {
                    var arg = args[a];
                    var first = NextSig(tokens, arg.Start);
                    if (first < 0 || first > arg.End) continue;
                    var second = NextSig(tokens, first + 1);
                    if (second >= 0 && second <= arg.End &&
                        tokens[first].Kind == TK.Ident && tokens[first].Text == "ctx" &&
                        tokens[second].Kind == TK.Colon)
                    {
                        var ctxRange = new TokRange(second + 1, arg.End);
                        ctx = ArgAsLiteral(tokens, ctxRange);
                        if (ctx == null) { ctxInvalid = true; break; }
                    }
                }
                if (ctxInvalid) continue;

                var es = new ExtractedString
                {
                    Msgid = msgid,
                    Msgctxt = ctx,
                    LocalePartition = "_code",
                };
                var line = LineOf(source, t.Start);
                es.References.Add($"{filePath}:{line}");

                var methodName = FindEnclosingMethod(tokens, i);
                if (methodName != null) es.ExtractedComments.Add($"in {methodName}()");

                foreach (var c in CollectLeadingComments(tokens, i))
                    es.ExtractedComments.Add(c);

                if (TmpRichTextDetector.HasTmpTags(msgid))
                    es.ExtractedComments.Add(
                        "Contains TMP rich text tags. Preserve tags and attribute values verbatim.");

                results.Add(es);
            }
            return results;
        }

        private enum TK
        {
            Ident, StringLit, InterpolatedStr, RawStr, CharLit, Number,
            LineComment, BlockComment,
            OpenParen, CloseParen, OpenBrace, CloseBrace, OpenBracket, CloseBracket,
            Semi, Comma, Dot, Colon, Arrow,
            Other,
        }

        private struct Tok
        {
            public TK Kind;
            public string Text;     // ident text; line-comment body; parsed string-lit value
            public int Start;       // byte offset in source
        }

        private struct TokRange
        {
            public int Start;
            public int End;
            public TokRange(int s, int e) { Start = s; End = e; }
        }

        private static List<Tok> Tokenize(string src)
        {
            var toks = new List<Tok>();
            var i = 0;
            while (i < src.Length)
            {
                var ch = src[i];

                if (char.IsWhiteSpace(ch)) { i++; continue; }

                if (ch == '/' && i + 1 < src.Length)
                {
                    if (src[i + 1] == '/')
                    {
                        var start = i;
                        i += 2;
                        var sb = new StringBuilder();
                        while (i < src.Length && src[i] != '\n' && src[i] != '\r')
                        {
                            sb.Append(src[i]);
                            i++;
                        }
                        toks.Add(new Tok { Kind = TK.LineComment, Text = sb.ToString(), Start = start });
                        continue;
                    }
                    if (src[i + 1] == '*')
                    {
                        var start = i;
                        i += 2;
                        while (i + 1 < src.Length && !(src[i] == '*' && src[i + 1] == '/')) i++;
                        if (i + 1 < src.Length) i += 2;
                        else i = src.Length;
                        toks.Add(new Tok { Kind = TK.BlockComment, Start = start });
                        continue;
                    }
                }

                if (ch == '"')
                {
                    if (i + 2 < src.Length && src[i + 1] == '"' && src[i + 2] == '"')
                    {
                        var start = i;
                        var run = 0;
                        while (i < src.Length && src[i] == '"') { run++; i++; }
                        while (i < src.Length)
                        {
                            if (src[i] == '"')
                            {
                                var close = 0;
                                var j = i;
                                while (j < src.Length && src[j] == '"') { close++; j++; }
                                if (close >= run) { i = j; break; }
                                i = j;
                            }
                            else i++;
                        }
                        toks.Add(new Tok { Kind = TK.RawStr, Start = start });
                        continue;
                    }
                    var sStart = i;
                    i++;
                    var sb = new StringBuilder();
                    var bad = false;
                    while (i < src.Length && src[i] != '"')
                    {
                        if (src[i] == '\\' && i + 1 < src.Length)
                        {
                            var esc = src[i + 1];
                            switch (esc)
                            {
                                case '"': sb.Append('"'); i += 2; break;
                                case '\\': sb.Append('\\'); i += 2; break;
                                case 'n': sb.Append('\n'); i += 2; break;
                                case 't': sb.Append('\t'); i += 2; break;
                                case 'r': sb.Append('\r'); i += 2; break;
                                case '0': sb.Append('\0'); i += 2; break;
                                case 'a': sb.Append('\a'); i += 2; break;
                                case 'b': sb.Append('\b'); i += 2; break;
                                case 'f': sb.Append('\f'); i += 2; break;
                                case 'v': sb.Append('\v'); i += 2; break;
                                case '\'': sb.Append('\''); i += 2; break;
                                case 'u':
                                    if (TryHex(src, i + 2, 4, out var u4)) { sb.Append((char)u4); i += 6; }
                                    else { sb.Append(esc); i += 2; }
                                    break;
                                case 'x':
                                    {
                                        var h = 0;
                                        var read = 0;
                                        var p = i + 2;
                                        while (read < 4 && p < src.Length && IsHex(src[p]))
                                        {
                                            h = (h << 4) | HexVal(src[p]);
                                            p++; read++;
                                        }
                                        if (read > 0) { sb.Append((char)h); i = p; }
                                        else { sb.Append(esc); i += 2; }
                                        break;
                                    }
                                default: sb.Append(esc); i += 2; break;
                            }
                        }
                        else if (src[i] == '\n' || src[i] == '\r')
                        {
                            bad = true;
                            break;
                        }
                        else
                        {
                            sb.Append(src[i]);
                            i++;
                        }
                    }
                    if (i < src.Length && src[i] == '"') i++;
                    toks.Add(new Tok
                    {
                        Kind = bad ? TK.Other : TK.StringLit,
                        Text = bad ? null : sb.ToString(),
                        Start = sStart,
                    });
                    continue;
                }

                if (ch == '@' && i + 1 < src.Length && src[i + 1] == '"')
                {
                    var start = i;
                    i += 2;
                    var sb = new StringBuilder();
                    while (i < src.Length)
                    {
                        if (src[i] == '"')
                        {
                            if (i + 1 < src.Length && src[i + 1] == '"') { sb.Append('"'); i += 2; continue; }
                            i++;
                            break;
                        }
                        sb.Append(src[i]);
                        i++;
                    }
                    toks.Add(new Tok { Kind = TK.StringLit, Text = sb.ToString(), Start = start });
                    continue;
                }

                if (ch == '$' || (ch == '@' && i + 1 < src.Length && src[i + 1] == '$'))
                {
                    var start = i;
                    var verbatim = false;
                    if (ch == '@') { verbatim = true; i++; }
                    if (i < src.Length && src[i] == '$') i++;
                    if (i < src.Length && src[i] == '@') { verbatim = true; i++; }
                    if (i >= src.Length || src[i] != '"')
                    {
                        toks.Add(new Tok { Kind = TK.Other, Start = start });
                        continue;
                    }
                    i++;
                    var depth = 0;
                    while (i < src.Length)
                    {
                        var c = src[i];
                        if (c == '{')
                        {
                            if (i + 1 < src.Length && src[i + 1] == '{') { i += 2; continue; }
                            depth++; i++; continue;
                        }
                        if (c == '}')
                        {
                            if (i + 1 < src.Length && src[i + 1] == '}') { i += 2; continue; }
                            if (depth > 0) { depth--; i++; continue; }
                            i++; continue;
                        }
                        if (depth == 0 && c == '"')
                        {
                            if (verbatim && i + 1 < src.Length && src[i + 1] == '"') { i += 2; continue; }
                            i++;
                            break;
                        }
                        if (!verbatim && c == '\\' && i + 1 < src.Length) { i += 2; continue; }
                        if (!verbatim && (c == '\n' || c == '\r')) break;
                        i++;
                    }
                    toks.Add(new Tok { Kind = TK.InterpolatedStr, Start = start });
                    continue;
                }

                // `@<letter>` — verbatim identifier (`@class`, `@Tr` etc). Strip the `@`.
                if (ch == '@' && i + 1 < src.Length && (char.IsLetter(src[i + 1]) || src[i + 1] == '_'))
                {
                    var start = i;
                    i++;
                    var nameStart = i;
                    while (i < src.Length && (char.IsLetterOrDigit(src[i]) || src[i] == '_')) i++;
                    toks.Add(new Tok { Kind = TK.Ident, Text = src.Substring(nameStart, i - nameStart), Start = start });
                    continue;
                }

                if (ch == '\'')
                {
                    var start = i;
                    i++;
                    while (i < src.Length && src[i] != '\'')
                    {
                        if (src[i] == '\\' && i + 1 < src.Length) i += 2;
                        else i++;
                    }
                    if (i < src.Length) i++;
                    toks.Add(new Tok { Kind = TK.CharLit, Start = start });
                    continue;
                }

                if (char.IsLetter(ch) || ch == '_')
                {
                    var start = i;
                    while (i < src.Length && (char.IsLetterOrDigit(src[i]) || src[i] == '_')) i++;
                    toks.Add(new Tok { Kind = TK.Ident, Text = src.Substring(start, i - start), Start = start });
                    continue;
                }

                if (char.IsDigit(ch))
                {
                    var start = i;
                    while (i < src.Length && (char.IsLetterOrDigit(src[i]) || src[i] == '.' || src[i] == '_')) i++;
                    toks.Add(new Tok { Kind = TK.Number, Start = start });
                    continue;
                }

                switch (ch)
                {
                    case '(': toks.Add(new Tok { Kind = TK.OpenParen, Start = i }); i++; break;
                    case ')': toks.Add(new Tok { Kind = TK.CloseParen, Start = i }); i++; break;
                    case '{': toks.Add(new Tok { Kind = TK.OpenBrace, Start = i }); i++; break;
                    case '}': toks.Add(new Tok { Kind = TK.CloseBrace, Start = i }); i++; break;
                    case '[': toks.Add(new Tok { Kind = TK.OpenBracket, Start = i }); i++; break;
                    case ']': toks.Add(new Tok { Kind = TK.CloseBracket, Start = i }); i++; break;
                    case ';': toks.Add(new Tok { Kind = TK.Semi, Start = i }); i++; break;
                    case ',': toks.Add(new Tok { Kind = TK.Comma, Start = i }); i++; break;
                    case '.': toks.Add(new Tok { Kind = TK.Dot, Start = i }); i++; break;
                    case ':': toks.Add(new Tok { Kind = TK.Colon, Start = i }); i++; break;
                    case '=':
                        if (i + 1 < src.Length && src[i + 1] == '>')
                        {
                            toks.Add(new Tok { Kind = TK.Arrow, Start = i });
                            i += 2;
                        }
                        else
                        {
                            toks.Add(new Tok { Kind = TK.Other, Start = i });
                            i++;
                        }
                        break;
                    default:
                        toks.Add(new Tok { Kind = TK.Other, Start = i });
                        i++;
                        break;
                }
            }
            return toks;
        }

        private static int NextSig(List<Tok> toks, int from)
        {
            for (var i = from; i < toks.Count; i++)
            {
                var k = toks[i].Kind;
                if (k == TK.LineComment || k == TK.BlockComment) continue;
                return i;
            }
            return -1;
        }

        private static int MatchClose(List<Tok> toks, int openIdx)
        {
            var depth = 1;
            for (var i = openIdx + 1; i < toks.Count; i++)
            {
                var k = toks[i].Kind;
                if (k == TK.OpenParen) depth++;
                else if (k == TK.CloseParen) { depth--; if (depth == 0) return i; }
            }
            return -1;
        }

        private static List<TokRange> SplitArgs(List<Tok> toks, int start, int end)
        {
            var args = new List<TokRange>();
            var hasContent = false;
            for (var i = start; i < end; i++)
            {
                var k = toks[i].Kind;
                if (k != TK.LineComment && k != TK.BlockComment) { hasContent = true; break; }
            }
            if (!hasContent) return args;

            var s = start;
            var paren = 0; var br = 0; var sq = 0;
            for (var i = start; i < end; i++)
            {
                var k = toks[i].Kind;
                if (k == TK.OpenParen) paren++;
                else if (k == TK.CloseParen) paren--;
                else if (k == TK.OpenBrace) br++;
                else if (k == TK.CloseBrace) br--;
                else if (k == TK.OpenBracket) sq++;
                else if (k == TK.CloseBracket) sq--;
                else if (k == TK.Comma && paren == 0 && br == 0 && sq == 0)
                {
                    args.Add(new TokRange(s, i - 1));
                    s = i + 1;
                }
            }
            args.Add(new TokRange(s, end - 1));
            return args;
        }

        private static string ArgAsLiteral(List<Tok> toks, TokRange r)
        {
            var first = NextSig(toks, r.Start);
            if (first < 0 || first > r.End) return null;
            if (toks[first].Kind != TK.StringLit) return null;
            var next = NextSig(toks, first + 1);
            if (next >= 0 && next <= r.End) return null;
            return toks[first].Text;
        }

        private static IEnumerable<string> CollectLeadingComments(List<Tok> toks, int callIdx)
        {
            var boundary = -1;
            for (var i = callIdx - 1; i >= 0; i--)
            {
                var k = toks[i].Kind;
                if (k == TK.Semi || k == TK.OpenBrace || k == TK.CloseBrace) { boundary = i; break; }
            }
            for (var i = boundary + 1; i < callIdx; i++)
            {
                if (toks[i].Kind != TK.LineComment) continue;
                var t = (toks[i].Text ?? "").TrimStart('/').Trim();
                if (!string.IsNullOrEmpty(t)) yield return t;
            }
        }

        private static string FindEnclosingMethod(List<Tok> toks, int callIdx)
        {
            var depth = 0;
            for (var i = callIdx - 1; i >= 0; i--)
            {
                var k = toks[i].Kind;
                if (k == TK.CloseBrace) depth++;
                else if (k == TK.OpenBrace)
                {
                    if (depth > 0) depth--;
                    else
                    {
                        var name = IdentBeforeBrace(toks, i);
                        if (name != null && !NonMethodKeywords.Contains(name)) return name;
                    }
                }
            }
            return null;
        }

        private static string IdentBeforeBrace(List<Tok> toks, int braceIdx)
        {
            var i = braceIdx - 1;
            while (i >= 0 && (toks[i].Kind == TK.LineComment || toks[i].Kind == TK.BlockComment)) i--;
            if (i < 0 || toks[i].Kind != TK.CloseParen) return null;
            var depth = 1;
            i--;
            while (i >= 0 && depth > 0)
            {
                if (toks[i].Kind == TK.CloseParen) depth++;
                else if (toks[i].Kind == TK.OpenParen) depth--;
                i--;
            }
            if (depth != 0) return null;
            while (i >= 0 && (toks[i].Kind == TK.LineComment || toks[i].Kind == TK.BlockComment)) i--;
            if (i >= 0 && toks[i].Kind == TK.Ident) return toks[i].Text;
            return null;
        }

        private static int LineOf(string src, int offset)
        {
            var line = 1;
            var n = offset < src.Length ? offset : src.Length;
            for (var i = 0; i < n; i++) if (src[i] == '\n') line++;
            return line;
        }

        private static bool IsHex(char c) =>
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

        private static int HexVal(char c) =>
            c <= '9' ? c - '0' : ((c | 0x20) - 'a' + 10);

        private static bool TryHex(string s, int start, int len, out int val)
        {
            val = 0;
            for (var i = 0; i < len; i++)
            {
                if (start + i >= s.Length) return false;
                var c = s[start + i];
                if (!IsHex(c)) return false;
                val = (val << 4) | HexVal(c);
            }
            return true;
        }
    }
}
