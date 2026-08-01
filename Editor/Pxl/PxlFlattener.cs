using System.Collections.Generic;

namespace PromptUGUI.Editor
{
    /// <summary>图层合成（spec 2026-08-01-pxl-layers §2.1）：底 → 顶逐层覆盖——上层非 '.'
    /// 字符替换下层，'.' 表示穿透（不绘制）。
    ///
    /// 合成是**字符级**的，不需要任何颜色信息。这不是实现细节而是格式的定义：合成结果的
    /// 色集必然是各层色集的并集，既不产生新颜色（palette 强制约束自动成立），也不需要重新
    /// 分配 chars 字符。副作用之一是 'X: transparent' 这类非 '.' 的透明字符天然成为橡皮擦
    /// ——它覆盖下层而非穿透。
    ///
    /// 无 Unity 类型依赖：.lint/PxlPreview 编译同一份源文件，CLI 与导入器的合成结果按构造
    /// 一致。</summary>
    internal static class PxlFlattener
    {
        /// <summary>调用方契约：layers 非空，且每层已校验为 width × height。</summary>
        public static List<string> Flatten(IReadOnlyList<PxlLayer> layers, int width, int height)
        {
            // 扁平文件（单层）是恒等映射——绝大多数 .pxl 走这条路径，不做逐格拷贝。
            if (layers.Count == 1) return new List<string>(layers[0].Rows);

            var buf = new char[height][];
            for (var y = 0; y < height; y++) buf[y] = layers[0].Rows[y].ToCharArray();
            for (var i = 1; i < layers.Count; i++)
            {
                var rows = layers[i].Rows;
                for (var y = 0; y < height; y++)
                {
                    var row = rows[y];
                    for (var x = 0; x < width; x++)
                    {
                        if (row[x] != '.') buf[y][x] = row[x];
                    }
                }
            }

            var result = new List<string>(height);
            for (var y = 0; y < height; y++) result.Add(new string(buf[y]));
            return result;
        }
    }
}
