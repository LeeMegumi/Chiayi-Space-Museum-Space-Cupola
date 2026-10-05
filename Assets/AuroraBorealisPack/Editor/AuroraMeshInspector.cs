// AuroraMeshInspector.cs
// 診斷用：印出 AuroraBorealisPack 網格「Unity 實際匯入後」的頂點通道內容。
//
// 套用位置：
//   Assets/AuroraBorealisPack/Editor/AuroraMeshInspector.cs
//   ※ 必須放在名為 Editor 的資料夾內。
//
// 為什麼需要這支腳本：
//   AuroraBorealis shader 的主要邏輯讀 TEXCOORD1（Unity 的 mesh.uv2）：
//       endFade = saturate(lerp(u, 1-u, step(0.5,u)) / 0.1)，其中 u = uv1.x
//   若 uv1 不存在（讀到 0），endFade 即為 0，最終顏色整片乘成 0 —— 極光完全不可見。
//
//   而直接解析 FBX 二進位的結果是：這些模型只有「一組」UV
//   （LayerElementUV 僅一個節點，UV 陣列長度 = 頂點數 × 2），
//   且 .meta 內 swapUVChannels = 0、generateSecondaryUV = 0。
//
//   單靠讀檔無法確定 Unity 匯入後 uv2 到底有沒有資料，因此用這支腳本實測。
//
// 使用方式：選單 Tools > Aurora Borealis > 檢查網格頂點通道

#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AuroraBorealisPack.EditorTools
{
    public static class AuroraMeshInspector
    {
        private static readonly string[] MeshPaths =
        {
            "Assets/AuroraBorealisPack/Models/Plane/Plane_Subdivs200.fbx",
            "Assets/AuroraBorealisPack/Models/Arc90/Arc90_Subdivs200.fbx",
            "Assets/AuroraBorealisPack/Models/Arc180/Arc180_Subdivs200.fbx",
            "Assets/AuroraBorealisPack/Models/Curve01/Curve01_Subdivs200.fbx",
        };

        [MenuItem("Tools/Aurora Borealis/檢查網格頂點通道")]
        private static void Inspect()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== AuroraBorealisPack 網格頂點通道診斷 ===");
            sb.AppendLine();

            foreach (string path in MeshPaths)
            {
                Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (mesh == null)
                {
                    // FBX 根資產不是 Mesh，需從子資產中找
                    foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(path))
                    {
                        if (sub is Mesh m) { mesh = m; break; }
                    }
                }

                sb.AppendLine($"--- {System.IO.Path.GetFileName(path)}");
                if (mesh == null)
                {
                    sb.AppendLine("    找不到網格（路徑有誤或資產未匯入）");
                    sb.AppendLine();
                    continue;
                }

                sb.AppendLine($"    頂點數       : {mesh.vertexCount}");
                sb.AppendLine($"    子網格數     : {mesh.subMeshCount}");
                sb.AppendLine($"    uv  (TEXCOORD0): {Describe(mesh.uv, mesh.vertexCount)}");
                sb.AppendLine($"    uv2 (TEXCOORD1): {Describe(mesh.uv2, mesh.vertexCount)}   <== shader 主邏輯靠這個");
                sb.AppendLine($"    uv3 (TEXCOORD2): {Describe(mesh.uv3, mesh.vertexCount)}");
                sb.AppendLine($"    colors         : {(mesh.colors != null && mesh.colors.Length > 0 ? $"{mesh.colors.Length} 筆" : "無")}");

                if (mesh.uv != null && mesh.uv.Length > 0)
                    sb.AppendLine($"    uv  範圍     : {RangeOf(mesh.uv)}");
                if (mesh.uv2 != null && mesh.uv2.Length > 0)
                    sb.AppendLine($"    uv2 範圍     : {RangeOf(mesh.uv2)}");
                if (mesh.colors != null && mesh.colors.Length > 0)
                    sb.AppendLine($"    color.r 範圍 : {ColorRedRange(mesh.colors)}");

                // 結論
                bool hasUv2 = mesh.uv2 != null && mesh.uv2.Length == mesh.vertexCount;
                sb.AppendLine(hasUv2
                    ? "    → uv2 存在，shader 可正常運作。"
                    : "    → ⚠ uv2 缺失！原版 shader 的 endFade 會算成 0，極光將完全不可見。");
                sb.AppendLine();
            }

            sb.AppendLine("請把以上完整內容回報，以決定是否需要在載入時補建 UV1。");
            Debug.Log(sb.ToString());
        }

        private static string Describe(Vector2[] arr, int vertexCount)
        {
            if (arr == null || arr.Length == 0) return "無";
            return arr.Length == vertexCount
                ? $"{arr.Length} 筆（完整）"
                : $"{arr.Length} 筆（與頂點數 {vertexCount} 不符）";
        }

        private static string RangeOf(Vector2[] arr)
        {
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (Vector2 v in arr)
            {
                if (v.x < minX) minX = v.x;
                if (v.x > maxX) maxX = v.x;
                if (v.y < minY) minY = v.y;
                if (v.y > maxY) maxY = v.y;
            }
            return $"x[{minX:F3}, {maxX:F3}]  y[{minY:F3}, {maxY:F3}]";
        }

        private static string ColorRedRange(Color[] arr)
        {
            float min = float.MaxValue, max = float.MinValue;
            foreach (Color c in arr)
            {
                if (c.r < min) min = c.r;
                if (c.r > max) max = c.r;
            }
            return $"[{min:F3}, {max:F3}]";
        }
    }
}
#endif
