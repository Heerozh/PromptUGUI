using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PromptUGUI.Application {
    /// <summary>
    /// Project-level icon set. Author 拖一个文件夹做 sourceFolder（Editor only），
    /// 同步工具按 XML 引用扫描结果重建 atlas。运行时仅读 setName + atlas。
    /// </summary>
    [CreateAssetMenu(menuName = "PromptUGUI/Icon Set", fileName = "IconSet")]
    public sealed class IconSet : ScriptableObject {
        [SerializeField] string setName;
        [SerializeField] SpriteAtlas atlas;
        [SerializeField] List<string> alwaysInclude = new();

#if UNITY_EDITOR
        [SerializeField] DefaultAsset sourceFolder;
        public DefaultAsset SourceFolder => sourceFolder;
        public string SourceFolderPath =>
            sourceFolder != null ? AssetDatabase.GetAssetPath(sourceFolder) : null;

        // Editor-only: 同步工具回填 atlas 字段
        internal void SetAtlasInternal(SpriteAtlas a) {
            atlas = a;
            EditorUtility.SetDirty(this);
        }
#endif

        public string SetName => setName;
        public SpriteAtlas Atlas => atlas;
        public IReadOnlyList<string> AlwaysInclude => alwaysInclude;
    }
}
