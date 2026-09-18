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
            /// <summary>
            /// The file to open: where the markup was written, or the entry when it has no origin.
            /// Null for a finding about the run's configuration (a malformed common-library row).
            /// </summary>
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

        // The commons closure is resolved and parsed once per run and shared by every entry; what
        // could not be resolved, and which rows are malformed, is likewise said once per run.
        private readonly ImportClosure.Cache _commonsCache = new ImportClosure.Cache();
        private readonly HashSet<string> _notedCommons = new HashSet<string>();
        private readonly HashSet<string> _rejectedCommons = new HashSet<string>();

        /// <summary>Entry files linted so far.</summary>
        public int Files { get; private set; }

        /// <summary>Errors + issues so far — what the CLI exits non-zero on. Notes do not count.</summary>
        public int Issues { get; private set; }

        /// <summary>
        /// Entries whose expanded pass was skipped over a src nobody could resolve. A note is
        /// printed once per unresolved library, so this is what tells the reader how much of the
        /// run only had the raw rules.
        /// </summary>
        public int Skipped { get; private set; }

        /// <summary>
        /// True once a finding of this run was an expansion failure over an unknown style / template
        /// name. With no common library declared that is most often a library that was never listed
        /// (spec §4.8's hint); with libraries declared the name is genuinely unknown.
        /// </summary>
        public bool SawUnknownName { get; private set; }

        /// <param name="read">Path → XML text. May throw; that is reported as an <see cref="Kind.Error"/>.</param>
        /// <param name="resolve">
        /// <c>(src, importingPath) → path</c> for every <c>&lt;Import&gt;</c> and every common
        /// library, null when unknown — see <see cref="ImportClosure.TryLoad"/>. An unresolvable src
        /// is not an error: the expanded pass is skipped with a <see cref="Kind.Note"/> and the raw
        /// rules still apply.
        /// </param>
        /// <param name="commons">
        /// The project's common libraries (<c>PromptUGUISettings.commonLibraries</c>): the
        /// <c>&lt;Import&gt;</c> every document implicitly has, resolved from the entry file and
        /// merged the way the runtime merges them (2026-09-18 commons-settings spec §4.7).
        /// </param>
        public List<Finding> Lint(string path, Func<string, string> read, Func<string, string, string> resolve,
                                  IReadOnlyList<ImportRef> commons = null)
        {
            Files++;
            var findings = new List<Finding>();

            // The mirror of PromptUGUISettings.OnValidate, for the CLI that never sees the Inspector:
            // <ns.Name/> cannot spell a namespace with a dot in it, so such a row can never be used.
            if (commons != null)
                foreach (var lib in commons)
                    if (lib.Namespace != null && lib.Namespace.Contains(".") && _rejectedCommons.Add(lib.Src))
                        Add(findings, Kind.Error, null,
                            $"common library src=\"{lib.Src}\" as=\"{lib.Namespace}\": 'as' must not contain '.' " +
                            "(the same rule as <Import as>; templates are invoked as <ns.Name/>)");

            var doc = TryParse(path, read, findings);
            if (doc == null) return findings;

            var closure = ImportClosure.TryLoad(
                path, doc, resolve, read, commons, _commonsCache, out var applied, out var unresolved);
            if (closure == null)
            {
                Skipped++;
                // A common library that cannot be found is the same for every entry — say it once.
                var isCommons = IsCommonsRow(commons, unresolved);
                if (!isCommons || _notedCommons.Add(unresolved))
                    findings.Add(new Finding(Kind.Note, path,
                        $"{path}: skipping expanded pass - cannot resolve " +
                        (isCommons ? $"common library src=\"{unresolved}\"" : $"<Import src=\"{unresolved}\">") +
                        " on disk (Addressables / custom resolver?). Raw-IR rules still applied."));
            }

            // With no closure the walker skips the expanded pass — provided it knows there ARE
            // commons: an entry without a single <Import> would otherwise be expanded on its own and
            // report every commons name as unknown. `applied` is the list minus the entry itself,
            // when the entry is one of the libraries.
            foreach (var issue in DocumentLinter.Walk(doc, SrcKeyOf(path),
                                                      closure == null ? null : s => Lookup(closure, s),
                                                      applied))
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
                if (kind == Kind.Error
                    && (issue.Message.Contains("unknown style") || issue.Message.Contains("unknown template")))
                    SawUnknownName = true;
                Add(findings, kind, file, $"{where}: [{issue.Code}] {issue.Message}{via}");
            }
            return findings;
        }

        private static bool IsCommonsRow(IReadOnlyList<ImportRef> commons, string src)
        {
            if (commons == null) return false;
            foreach (var lib in commons)
                if (lib.Src == src) return true;
            return false;
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
