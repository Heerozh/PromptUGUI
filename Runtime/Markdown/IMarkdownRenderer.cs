using System.Collections.Generic;
using PromptUGUI.IR;

namespace PromptUGUI
{
    /// <summary>Turns Markdown source into a PromptUGUI IR tree. Implemented by the Markdig backend
    /// (gated asmdef) and injected into <see cref="PromptUGUI.Application.UI.Markdown.Renderer"/>.</summary>
    public interface IMarkdownRenderer
    {
        public MarkdownRenderResult Render(string markdown, MarkdownStyle style);
    }

    public sealed class MarkdownRenderResult
    {
        /// <summary>Root block container (a VStack node) holding all rendered blocks.</summary>
        public ElementNode Root;
        /// <summary>Block-level images to load asynchronously after instantiation.</summary>
        public IReadOnlyList<ImageRequest> Images = System.Array.Empty<ImageRequest>();
    }

    public readonly struct ImageRequest
    {
        public readonly string NodeId;  // id of the RawImage node in the tree (control resolves via Get)
        public readonly string Url;     // passed to the image resolver
        public readonly string Alt;     // alt / placeholder text

        public ImageRequest(string nodeId, string url, string alt)
        {
            NodeId = nodeId;
            Url = url;
            Alt = alt;
        }
    }
}
