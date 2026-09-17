using System;
using System.Collections.Generic;
using System.IO;
using PromptUGUI.IR;
using PromptUGUI.Parser;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// One lint run over any number of entry files: read, parse, prefetch the Import closure, walk
    /// with <see cref="DocumentLinter"/>, spell each finding the one way (<c>file:line: [CODE] msg
    /// (via …)</c>, see <see cref="SourceLocation"/>) and fold duplicates ACROSS files. The UIXmlLint
    /// CLI and the Editor menu (<c>Tools › PromptUGUI › Lint All UI XML</c>) are two front ends over
    /// this: they differ in where the files come from, how a src is resolved, and where a line is
    /// printed — nothing else, so what a rule says reads identically in a terminal and in the
    /// Console.
    ///
    /// <para>Findings are deduplicated across entry files, not just within one. Linting a directory
    /// walks both a library and the document that imports it, and the expanded pass attributes a
    /// finding to where it was written — so the same defect would otherwise be printed once per
    /// entry file that reaches it. Origin + line is what makes that identity precise enough to
    /// fold.</para>
    /// </summary>
    public sealed class LintRun
    {
        public enum Kind
        {
            /// <summary>The document cannot be built at all: unreadable, unparseable, or expansion failed.</summary>
            Error,
            /// <summary>A rule finding — the runtime would warn about (or silently absorb) this.</summary>
            Issue,
            /// <summary>Information only: the expanded pass was skipped. Not counted.</summary>
            Note,
        }

        public readonly struct Finding
        {
            public Kind Kind { get; }
            /// <summary>The file to open: where the markup was written, or the entry when it has no origin.</summary>
            public string File { get; }
            /// <summary>The whole line, exactly as the CLI prints it.</summary>
            public string Text { get; }

            public Finding(Kind kind, string file, string text)
            {
                Kind = kind;
                File = file;
                Text = text;
            }
        }

        private readonly HashSet<string> _reported = new HashSet<string>();

        /// <summary>Entry files linted so far.</summary>
        public int Files { get; private set; }

        /// <summary>Errors + issues so far — what the CLI exits non-zero on. Notes do not count.</summary>
        public int Issues { get; private set; }

        /// <param name="read">Path → XML text. May throw; that is reported as an <see cref="Kind.Error"/>.</param>
        /// <param name="resolve">
        /// <c>(src, importingPath) → path</c> for every <c>&lt;Import&gt;</c>, null when unknown —
        /// see <see cref="ImportClosure.TryLoad"/>. An unresolvable Import is not an error: the
        /// expanded pass is skipped with a <see cref="Kind.Note"/> and the raw rules still apply.
        /// </param>
        public List<Finding> Lint(string path, Func<string, string> read, Func<string, string, string> resolve)
        {
            Files++;
            var findings = new List<Finding>();

            var doc = TryParse(path, read, findings);
            if (doc == null) return findings;

            var closure = ImportClosure.TryLoad(path, doc, resolve, read, out var unresolved);
            if (closure == null)
            {
                findings.Add(new Finding(Kind.Note, path,
                    $"{path}: skipping expanded pass - cannot resolve <Import src=\"{unresolved}\"> " +
                    "on disk (Addressables / custom resolver?). Raw-IR rules still applied."));
            }

            foreach (var issue in DocumentLinter.Walk(doc, SrcKeyOf(path),
                                                      closure == null ? null : s => Lookup(closure, s)))
            {
                // Origin is the file the markup was WRITTEN in — for a finding inside an imported
                // Template body that is the library, not the entry document that invoked it.
                // "file:line:" is the shape editors and terminals turn into a jump. SourceLocation
                // spells it, so a runtime "  at <Tag> file:line" greps the same as this line.
                var file = issue.Origin ?? path;
                var where = SourceLocation.Format(file, issue.Line);
                // The declaration site stays primary — that is where the edit goes. The invocation
                // is context, and only worth printing when it names a different place.
                var via = SourceLocation.Via(where, issue.Via);
                if (!_reported.Add(where + via + "|" + issue.Code + "|" + issue.Message)) continue;

                // Expansion failing means UI.Open() throws on this document — not a warning.
                var kind = issue.Code == DocumentLinter.ExpansionCode ? Kind.Error : Kind.Issue;
                Add(findings, kind, file, $"{where}: [{issue.Code}] {issue.Message}{via}");
            }
            return findings;
        }

        private void Add(List<Finding> findings, Kind kind, string file, string text)
        {
            findings.Add(new Finding(kind, file, text));
            Issues++;
        }

        private static UIDocument Lookup(Dictionary<string, UIDocument> closure, string src)
            => closure.TryGetValue(src, out var d) ? d : null;

        private UIDocument TryParse(string path, Func<string, string> read, List<Finding> findings)
        {
            string xml;
            try
            {
                xml = read(path);
            }
            catch (Exception ex)
            {
                Add(findings, Kind.Error, path, $"{path}: read failed: {ex.Message}");
                return null;
            }

            try
            {
                return UIDocumentParser.Parse(xml, path);
            }
            catch (ParseException ex)
            {
                Add(findings, Kind.Error, path, $"{path}: parse error: {ex.Message}");
                return null;
            }
            catch (System.Xml.XmlException ex)
            {
                Add(findings, Kind.Error, path,
                    $"{path}: xml error (line {ex.LineNumber}, pos {ex.LinePosition}): {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// A stable identity for the entry document. It is only ever compared against
        /// <c>&lt;Import src&gt;</c> values, which are resolver keys, so a full path cannot collide.
        /// </summary>
        private static string SrcKeyOf(string path) => Path.GetFullPath(path);
    }
}
