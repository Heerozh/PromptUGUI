using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PromptUGUI.Application
{
    /// <summary>
    /// Project-level sprite set. Author 拖一个文件夹做 sourceFolder（Editor only），
    /// 同步工具按 XML 引用扫描结果重建 atlas。运行时仅读 setName + entries（key→Sprite）。
    /// </summary>
    [CreateAssetMenu(menuName = "PromptUGUI/Sprite Set", fileName = "SpriteSet")]
    public sealed class SpriteSet : ScriptableObject
    {
        [SerializeField] private string setName;
        [SerializeField] private SpriteAtlas atlas;
        [SerializeField] private List<string> alwaysInclude = new();

        // Sync 工具填入的 (key → sprite)；同 setName 内 key 唯一。
        // 同名 PNG 散落在不同子目录时，每条 PNG 至少出现一次以路径形作为 key
        // （e.g. "UI/heart"），不冲突时还会再补一条裸名别名（e.g. "heart"）。
        // 比裸名扫 atlas 的老路径更稳：sprite 在 atlas 里的 .name 可能撞名。
        [Serializable]
        internal struct Entry
        {
            public string key;
            public Sprite sprite;
        }
        [SerializeField] private List<Entry> entries = new();

#if UNITY_EDITOR
        [SerializeField] private DefaultAsset sourceFolder;
        public DefaultAsset SourceFolder => sourceFolder;
        public string SourceFolderPath =>
            sourceFolder != null ? AssetDatabase.GetAssetPath(sourceFolder) : null;

        internal void SetAtlasInternal(SpriteAtlas a)
        {
            atlas = a;
            EditorUtility.SetDirty(this);
        }

        internal void SetEntriesInternal(IList<(string key, Sprite sprite)> es)
        {
            entries.Clear();
            for (var i = 0; i < es.Count; i++)
                entries.Add(new Entry { key = es[i].key, sprite = es[i].sprite });
            EditorUtility.SetDirty(this);
        }
#endif

        public string SetName => setName;
        public SpriteAtlas Atlas => atlas;
        public IReadOnlyList<string> AlwaysInclude => alwaysInclude;

        /// <summary>(key → Sprite) — 运行时由 SpriteResolverHelpers 直接读取，
        /// 跨过 SpriteAtlas 内部以 sprite name 索引时遇到的撞名歧义。</summary>
        public IEnumerable<(string key, Sprite sprite)> Entries
        {
            get
            {
                foreach (var e in entries) yield return (e.key, e.sprite);
            }
        }
    }
}
