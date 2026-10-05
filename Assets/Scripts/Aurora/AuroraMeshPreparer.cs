// AuroraMeshPreparer.cs
// 為 AuroraBorealisPack 的網格補建 shader 所需的 UV 通道。
//
// 套用位置：
//   Assets/Scripts/Aurora/AuroraMeshPreparer.cs
//
// ── 問題 ──────────────────────────────────────────────────────────
// AuroraBorealis shader 的主要邏輯讀 TEXCOORD1（Unity 的 mesh.uv2），
// 但套件所有 FBX 都只有一組 UV，匯入後 uv2 為空。
// 此時 endFade = saturate(lerp(0, 1, step(0.5,0)) / 0.1) = 0，
// 最終顏色整片乘成 0 —— 極光完全不可見（已用 AuroraMeshInspector 實測確認）。
//
// ── 依據 ──────────────────────────────────────────────────────────
// 直接解析 FBX 的頂點／UV／頂點色三組陣列並做相關性分析，得到通道語意
// （Plane / Arc90 / Arc180 / Curve01 四個模型結果一致）：
//
//   uv0.x   = 沿緞帶長度 0→1        corr(uv.x, 弧角)   = +1.0000
//   uv0.y   = 跨緞帶厚度 0→1        唯一值僅 2 個（內外緣）
//   color.r = 1 − (pos.y / 11.2)    corr(color.r, y)   = −1.0000
//                                   max|color.r-(1-y/11.2)| = 0.000000
//
// 模型結構：201 片水平薄片沿 Y 軸堆疊（即 README 所說的 overlapping planes）。
//
// ── 補建方式 ──────────────────────────────────────────────────────
//   uv1 = (uv0.x, height)    → 長度、高度：驅動噪聲捲動、簾幕形狀、兩端淡出
//   uv0 = (uv0.y, height)    → 厚度：驅動 (1-u)·u·4 的厚度柔邊，產生體積感
//   其中 height = 1 − color.r
//
// 原始 FBX 資產完全不動 —— 在執行期建立網格副本並快取。

using System.Collections.Generic;
using UnityEngine;

namespace SpaceCupola.Aurora
{
    public static class AuroraMeshPreparer
    {
        /// <summary>模型的簾幕高度（模型單位）。僅供對照用，實際高度由 color.r 推得。</summary>
        public const float ModelHeight = 11.2f;

        private struct CacheKey
        {
            public int sourceId;
            public bool invertHeight;

            public override int GetHashCode() => sourceId * 397 ^ (invertHeight ? 1 : 0);
            public override bool Equals(object obj) =>
                obj is CacheKey k && k.sourceId == sourceId && k.invertHeight == invertHeight;
        }

        private static readonly Dictionary<CacheKey, Mesh> Cache = new Dictionary<CacheKey, Mesh>();

        /// <summary>
        /// 取得補好 UV 通道的網格。若來源已具備完整 uv2 則原樣回傳。
        /// 產生的副本會被快取，同一來源不會重複建立。
        /// </summary>
        /// <param name="source">套件原始網格。</param>
        /// <param name="invertHeight">
        /// 反轉高度方向。影響簾幕沿高度的分佈，屬視覺偏好，可直接切換比較。
        /// </param>
        public static Mesh GetPrepared(Mesh source, bool invertHeight = false)
        {
            if (source == null) return null;

            // 已經有完整 uv2 —— 不需要處理
            if (source.uv2 != null && source.uv2.Length == source.vertexCount)
                return source;

            var key = new CacheKey { sourceId = source.GetInstanceID(), invertHeight = invertHeight };
            if (Cache.TryGetValue(key, out Mesh cached) && cached != null) return cached;

            Mesh built = Build(source, invertHeight);
            Cache[key] = built;
            return built;
        }

        private static Mesh Build(Mesh source, bool invertHeight)
        {
            Vector2[] srcUv = source.uv;
            Color[] colors = source.colors;
            int count = source.vertexCount;

            if (srcUv == null || srcUv.Length != count)
            {
                Debug.LogError($"[AuroraMeshPreparer] 「{source.name}」缺少 uv0，無法補建 UV1。");
                return source;
            }
            if (colors == null || colors.Length != count)
            {
                Debug.LogError($"[AuroraMeshPreparer] 「{source.name}」缺少頂點色，" +
                               "無法由 color.r 推得高度。");
                return source;
            }

            var uv0 = new Vector2[count];   // (厚度, 高度)
            var uv1 = new Vector2[count];   // (長度, 高度)

            for (int i = 0; i < count; i++)
            {
                float lengthCoord = srcUv[i].x;
                float depthCoord  = srcUv[i].y;

                // color.r = 1 − 正規化高度（底部為 1、頂部為 0）
                float height = 1f - colors[i].r;
                if (invertHeight) height = 1f - height;

                uv1[i] = new Vector2(lengthCoord, height);
                uv0[i] = new Vector2(depthCoord,  height);
            }

            Mesh mesh = Object.Instantiate(source);
            mesh.name = source.name + "_UV1";
            mesh.hideFlags = HideFlags.HideAndDontSave;   // 不寫入資產或場景
            mesh.uv  = uv0;
            mesh.uv2 = uv1;
            mesh.UploadMeshData(false);

            Debug.Log($"[AuroraMeshPreparer] 已為「{source.name}」補建 UV1（{count} 頂點）。" +
                      "原始資產未變動。");
            return mesh;
        }

        /// <summary>清除快取並釋放產生的網格。切換參數或離開 Play 模式時呼叫。</summary>
        public static void ClearCache()
        {
            foreach (Mesh m in Cache.Values)
            {
                if (m == null) continue;
                if (Application.isPlaying) Object.Destroy(m);
                else Object.DestroyImmediate(m);
            }
            Cache.Clear();
        }
    }
}
