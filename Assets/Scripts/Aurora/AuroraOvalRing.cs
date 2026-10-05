// AuroraOvalRing.cs
// 在地球南／北極的極冠範圍內，生成不規則分佈、會淡入淡出並換位重生的極光。
//
// 套用位置：Assets/Scripts/Aurora/AuroraOvalRing.cs
// （類別名稱沿用 AuroraOvalRing，以免場景中已掛上的元件參照失效；
//   實際行為已不是「環」，而是極冠內的隨機片段。）
//
// ── 為什麼舊版一直像環 ────────────────────────────────────────────
// Arc90 模型是一段以原點為圓心的 90° 圓弧。舊版把圓心放在極軸上，
// 不論怎麼打散緯度／方位／亮度，每段都與極點同心，拼起來必然是環。
// 新版改為：每片極光各自在極冠內選一個落點，貼著該點地表放置，
// 並繞地表法線任意旋轉 —— 不再以極點為圓心。
//
// ── 生命週期（Play 模式）─────────────────────────────────────────
//   淡入 → 持續 → 淡出 → 暗一段時間 → 在「別處」重生
// 選新位置時採 best-candidate 取樣：抽數個候選點，取離其他極光（含自己舊位置）
// 最遠者，並可偏向夜側。結果是不規則但不擠成一團的分佈。
//
// ── 已知事實（前幾輪診斷確認）────────────────────────────────────
//   * 地球極軸為 UnitEarth 本地 +Z（1863 個城市反推，平均誤差 0.000014）
//   * 地球半徑為本地 0.25
//   * 套件網格缺 UV1，由 AuroraMeshPreparer 補建
//   * FBX 匯入有 ×0.01 單位縮放 —— 一律讀 mesh.bounds，不寫死尺寸
//   * 極光為水平薄片堆疊，須從上方俯視才明顯

using System.Collections.Generic;
using UnityEngine;

namespace SpaceCupola.Aurora
{
    [ExecuteAlways]
    public class AuroraOvalRing : MonoBehaviour
    {
        private const string ContainerName = "AuroraOvalRings (generated)";

        public enum PolarAxis { XPositive, YPositive, ZPositive }

        // ─────────────────────────────────────────────────────────────
        #region Inspector

        [Header("必要資源")]
        [Tooltip("極光使用的網格，每次生成時隨機挑一個。建議混用弧形與波浪形以打破規律。\n" +
                 "留空會自動帶入 Arc90 / Arc180 / Curve01 / Curve02。")]
        [SerializeField] private List<Mesh> auroraMeshes = new List<Mesh>();

        [Tooltip("舊版欄位。auroraMeshes 為空時作為後備。")]
        [SerializeField] private Mesh arcMesh;

        [Tooltip("極光材質（AuroraBorealis (URP) shader）。材質上的 Color_Multiplier 為整體亮度基準。")]
        [SerializeField] private Material auroraMaterial;

        [Header("地球")]
        [Tooltip("留空則自動尋找場景中的 UnitEarth。生成物會成為它的子物件，隨地球自轉。")]
        [SerializeField] private Transform earthRoot;

        [Tooltip("地球模型的極軸（北極方向）。本專案已確認為 +Z。")]
        [SerializeField] private PolarAxis polarAxis = PolarAxis.ZPositive;

        [Tooltip("地球半徑（UnitEarth 本地空間）。本專案為 0.25。")]
        [SerializeField] private float earthRadius = 0.25f;

        [Header("南北極")]
        [SerializeField] private bool northPole = true;
        [SerializeField] private bool southPole = true;

        [Header("分佈範圍")]
        [Tooltip("每極同時存在的極光片數（含正在淡入淡出與暫時隱藏者）。")]
        [Range(1, 30)]
        [SerializeField] private int patchesPerPole = 8;

        [Tooltip("極冠範圍：距極點的角度（最小, 最大）。極光落點在此範圍內依面積均勻隨機。")]
        [SerializeField] private Vector2 capColatitudeRange = new Vector2(3f, 27f);

        [Tooltip("每片極光的長度範圍（× 地球半徑）。")]
        [SerializeField] private Vector2 patchSpanRange = new Vector2(0.10f, 0.30f);

        [Tooltip("每片極光的簾幕高度範圍（× 地球半徑）。真實極光約 0.016–0.05。")]
        [SerializeField] private Vector2 patchHeightRange = new Vector2(0.025f, 0.065f);

        [Tooltip("每片極光的亮度倍率範圍（乘在材質 Color_Multiplier 上）。")]
        [SerializeField] private Vector2 brightnessRange = new Vector2(0.4f, 1.0f);

        [Tooltip("夜側偏好：0 = 不偏；1 = 強烈偏向背光側。以場景主方向光判定。")]
        [Range(0f, 1f)]
        [SerializeField] private float nightSideBias = 0.5f;

        [Tooltip("選位置時抽幾個候選點，取離其他極光最遠者。\n" +
                 "1 = 純隨機（可能擠在一起）；越大分得越開、也越均勻。建議 3–6。")]
        [Range(1, 16)]
        [SerializeField] private int spreadCandidates = 4;

        [Tooltip("簾幕底部額外沒入地表的比例（曲率補償之外）。")]
        [Range(0f, 0.5f)]
        [SerializeField] private float baseSink = 0.05f;

        [Tooltip("曲率容忍度：片段兩端與中央的高低差，最多可佔簾幕高度的多少。\n" +
                 "越小越貼地但片段越短；越大片段越長但兩端可能浮起／埋入。\n0.2 時實測：浮起 ≤ 5%、沉入 ≤ 15%（佔簾幕高度）。")]
        [Range(0.05f, 1f)]
        [SerializeField] private float curvatureTolerance = 0.2f;

        [Header("生命週期（Play 模式）")]
        [Tooltip("每片極光從出現到消失的總時間（秒）。")]
        [SerializeField] private Vector2 lifetimeRange = new Vector2(14f, 32f);

        [Tooltip("淡入時間（秒）。")]
        [SerializeField] private Vector2 fadeInRange = new Vector2(3f, 6f);

        [Tooltip("淡出時間（秒）。")]
        [SerializeField] private Vector2 fadeOutRange = new Vector2(4f, 8f);

        [Tooltip("消失後到在別處重生之間的空檔（秒）。")]
        [SerializeField] private Vector2 respawnDelayRange = new Vector2(0.5f, 5f);

        [Tooltip("存活期間的微幅閃爍強度。0 = 不閃爍。")]
        [Range(0f, 0.5f)]
        [SerializeField] private float flickerAmount = 0.12f;

        [Tooltip("每片極光使用不同的噪聲貼圖偏移，避免每片動態一模一樣。")]
        [SerializeField] private bool randomizeNoiseOffset = true;

        [Header("網格修正")]
        [Tooltip("套件網格缺少 shader 需要的 UV1，開啟後在執行期補建（原始資產不動）。請保持開啟。")]
        [SerializeField] private bool autoBuildUv1 = true;

        [Tooltip("反轉簾幕的高度分佈。屬視覺偏好。")]
        [SerializeField] private bool invertCurtainHeight = false;

        [Header("其他")]
        [Tooltip("生成物所在的 Layer。不可為 Water（WorldMapManager 會剔除）。")]
        [SerializeField] private string targetLayer = "Default";

        [Tooltip("在編輯模式也生成（靜態快照，不跑生命週期）。生成物不會寫入場景檔。")]
        [SerializeField] private bool previewInEditMode = true;

        [Tooltip("亂數種子。改這個數字可換一組樣貌；相同種子結果相同。")]
        [SerializeField] private int randomSeed = 20260930;

        #endregion

        // ─────────────────────────────────────────────────────────────
        #region 執行期狀態

        private class Patch
        {
            public GameObject   go;
            public MeshFilter   mf;
            public MeshRenderer mr;
            public float        poleSign;
            public bool         placed;
            public Vector3      normal;        // 落點的地表法線（本地空間、單位向量）
            public float        age;
            public float        life;
            public float        fadeIn;
            public float        fadeOut;
            public float        delay;         // >0 表示正在隱藏、等待重生
            public float        brightness;
            public float        flickerPhase;
            public float        flickerPeriod;
            public Vector4      noiseST;
        }

        private readonly List<Patch> _patches = new List<Patch>();
        private readonly List<Mesh>  _preparedMeshes = new List<Mesh>();
        private Transform            _container;
        private System.Random        _rng;
        private MaterialPropertyBlock _mpb;
        private float                _baseMultiplier = 1f;
        private Vector4              _baseNoiseST = new Vector4(1f, 1f, 0f, 0f);
        private bool                 _hasNightDir;
        private Vector3              _nightDirLocal;

        private static readonly int ColorMultiplierId = Shader.PropertyToID("_Color_Multiplier");
        private static readonly int NoiseSTId         = Shader.PropertyToID("_Noise_ST");

        #endregion

        // ─────────────────────────────────────────────────────────────
        #region Unity 生命週期

        private void OnEnable()  => Rebuild();
        private void OnDisable() => ClearSpawned();

        // 注意：刻意不在 OnDestroy 清除 AuroraMeshPreparer 的快取。
        // [ExecuteAlways] 元件在重新編譯時也會觸發 OnDestroy，
        // 若在此銷毀網格，場景中的 renderer 會指到已銷毀的網格而消失。

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!isActiveAndEnabled) return;
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null && isActiveAndEnabled) Rebuild();
            };
        }

        private void Reset() => AutoFillDefaults();

        private static readonly string[] DefaultMeshPaths =
        {
            "Assets/AuroraBorealisPack/Models/Arc90/Arc90_Subdivs200.fbx",
            "Assets/AuroraBorealisPack/Models/Arc180/Arc180_Subdivs200.fbx",
            "Assets/AuroraBorealisPack/Models/Curve01/Curve01_Subdivs200.fbx",
            "Assets/AuroraBorealisPack/Models/Curve02/Curve02_Subdivs200.fbx",
        };

        /// <summary>網格清單為空時自動帶入套件的四種網格；材質為空時帶入預設材質。</summary>
        private void AutoFillDefaults()
        {
            bool empty = auroraMeshes == null || auroraMeshes.Count == 0 || auroraMeshes.TrueForAll(m => m == null);
            if (empty)
            {
                auroraMeshes = new List<Mesh>();
                foreach (string path in DefaultMeshPaths)
                {
                    Mesh m = UnityEditor.AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if (m != null) auroraMeshes.Add(m);
                }
                if (auroraMeshes.Count == 0 && arcMesh != null) auroraMeshes.Add(arcMesh);
            }

            if (auroraMaterial == null)
            {
                auroraMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(
                    "Assets/AuroraBorealisPack/Materials/AuroraBorealis_Default.mat");
            }
        }
#endif

        private void Update()
        {
            if (!Application.isPlaying) return;

            float dt = Time.deltaTime;
            float t  = Time.time;

            for (int i = 0; i < _patches.Count; i++)
            {
                Patch p = _patches[i];
                if (p.go == null) continue;

                // 隱藏中：倒數，時間到就在別處重生
                if (p.delay > 0f)
                {
                    p.delay -= dt;
                    if (p.delay > 0f) { ApplyBrightness(p, 0f); continue; }
                    Respawn(p, initial: false);
                }

                p.age += dt;
                if (p.age >= p.life)
                {
                    p.delay = RandRange(respawnDelayRange);
                    ApplyBrightness(p, 0f);
                    continue;
                }

                float flicker = 1f + flickerAmount *
                                Mathf.Sin((t + p.flickerPhase) / p.flickerPeriod * Mathf.PI * 2f);
                ApplyBrightness(p, p.brightness * Envelope(p) * flicker);
            }
        }

        #endregion

        // ─────────────────────────────────────────────────────────────
        #region 生成

        [ContextMenu("重新生成極光")]
        public void Rebuild()
        {
            ClearSpawned();

            if (!Application.isPlaying && !previewInEditMode) return;

#if UNITY_EDITOR
            AutoFillDefaults();
#endif
            if (auroraMaterial == null)
            {
                Debug.LogWarning("[AuroraOvalRing] 尚未指定 auroraMaterial，略過生成。", this);
                return;
            }

            PrepareMeshes();
            if (_preparedMeshes.Count == 0)
            {
                Debug.LogWarning("[AuroraOvalRing] 沒有可用的極光網格，略過生成。", this);
                return;
            }

            Transform parent = ResolveEarthRoot();
            if (parent == null)
            {
                Debug.LogWarning("[AuroraOvalRing] 找不到 UnitEarth，也未指定 earthRoot，略過生成。", this);
                return;
            }

            // 重新編譯後 _patches 會遺失，舊生成物可能殘留 —— 依名稱清掉
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform child = parent.GetChild(i);
                if (child.name == ContainerName) DestroySafe(child.gameObject);
            }

            _container = new GameObject(ContainerName).transform;
            _container.SetParent(parent, false);
            _container.localPosition = Vector3.zero;
            _container.localRotation = Quaternion.identity;
            _container.localScale    = Vector3.one;
            _container.gameObject.hideFlags = HideFlags.DontSave;

            _rng = new System.Random(randomSeed);
            if (_mpb == null) _mpb = new MaterialPropertyBlock();

            _baseMultiplier = auroraMaterial.HasProperty(ColorMultiplierId)
                ? auroraMaterial.GetFloat(ColorMultiplierId) : 1f;

            if (auroraMaterial.HasProperty("_Noise"))
            {
                Vector2 s = auroraMaterial.GetTextureScale("_Noise");
                Vector2 o = auroraMaterial.GetTextureOffset("_Noise");
                _baseNoiseST = new Vector4(s.x, s.y, o.x, o.y);
            }

            _hasNightDir = TryGetNightDirectionLocal(out _nightDirLocal);

            if (northPole) CreatePatches(+1f);
            if (southPole) CreatePatches(-1f);
        }

        private void PrepareMeshes()
        {
            _preparedMeshes.Clear();

            var sources = new List<Mesh>();
            if (auroraMeshes != null)
                foreach (Mesh m in auroraMeshes) if (m != null) sources.Add(m);
            if (sources.Count == 0 && arcMesh != null) sources.Add(arcMesh);

            foreach (Mesh m in sources)
            {
                Mesh prepared = autoBuildUv1 ? AuroraMeshPreparer.GetPrepared(m, invertCurtainHeight) : m;
                if (prepared != null) _preparedMeshes.Add(prepared);
            }
        }

        private void CreatePatches(float poleSign)
        {
            int layer = LayerMask.NameToLayer(targetLayer);
            if (layer < 0) layer = 0;

            for (int i = 0; i < patchesPerPole; i++)
            {
                var go = new GameObject($"Aurora_{(poleSign > 0 ? "N" : "S")}{i}");
                go.layer = layer;
                go.hideFlags = HideFlags.DontSave;
                go.transform.SetParent(_container, false);

                var mf = go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial       = auroraMaterial;
                mr.shadowCastingMode    = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows       = false;
                mr.lightProbeUsage      = UnityEngine.Rendering.LightProbeUsage.Off;
                mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

                var p = new Patch { go = go, mf = mf, mr = mr, poleSign = poleSign };
                _patches.Add(p);
                Respawn(p, initial: true);

                if (!Application.isPlaying)
                {
                    // 編輯模式：靜態快照，直接以該片的亮度顯示
                    ApplyBrightness(p, p.brightness);
                }
            }
        }

        /// <summary>替這一片極光挑新網格、新位置、新外觀，並重設生命週期。</summary>
        private void Respawn(Patch p, bool initial)
        {
            Mesh mesh = _preparedMeshes[_rng.Next(_preparedMeshes.Count)];
            p.mf.sharedMesh = mesh;

            Vector3 n = PickNormal(p);
            p.normal = n;
            p.placed = true;

            // ── 尺寸：一律依 mesh.bounds（已含 FBX ×0.01 匯入縮放）──
            Bounds b = mesh.bounds;
            float spanModel   = Mathf.Max(Mathf.Max(b.size.x, b.size.z), 1e-5f);
            float heightModel = Mathf.Max(b.size.y, 1e-6f);

            float height = RandRange(patchHeightRange) * earthRadius;
            float span   = RandRange(patchSpanRange)   * earthRadius;

            // ── 依曲率限制長度 ──
            // 平的片段貼在彎的球面上，兩端與中央的高低差（弦高）≈ (半對角線)² / 2R。
            // 若弦高相對簾幕高度太大，不論怎麼平移都會「一端浮空、另一端埋進地表」。
            // 數值模擬：不加限制時最壞會埋掉簾幕高度的 58%（而底部正是最亮的部分）。
            // 因此先決定高度，再把長度限制在「弦高 ≤ curvatureTolerance × 高度」之內。
            float diagFactor = Mathf.Sqrt(b.size.x * b.size.x + b.size.z * b.size.z) / spanModel;
            float maxSpan = (2f / Mathf.Max(diagFactor, 1f))
                          * Mathf.Sqrt(2f * earthRadius * curvatureTolerance * height);
            span = Mathf.Min(span, maxSpan);

            float sXZ = span / spanModel;
            float sY  = height / heightModel;

            // ── 方向：模型 +Y 對齊地表法線，再繞法線任意旋轉 ──
            Quaternion rot = Quaternion.AngleAxis(RandRange(0f, 360f), n)
                           * Quaternion.FromToRotation(Vector3.up, n);

            // ── 位置：讓網格包圍盒的「底面中心」落在地表點上 ──
            // Arc 系列網格的樞紐在圓心而非弧上，必須以包圍盒中心修正，否則會偏離落點。
            Vector3 scale = new Vector3(sXZ, sY, sXZ);
            Vector3 pivotToBaseCenter = new Vector3(b.center.x * sXZ, b.min.y * sY, b.center.z * sXZ);
            Vector3 pos = n * earthRadius - rot * pivotToBaseCenter;

            // ── 曲率補償 ──
            // 片段是平的、地球是彎的：放在切平面上時，離落點越遠的底緣越會浮離球面。
            // 數值模擬顯示未補償時，最壞會浮起簾幕高度的 43%（Arc90、短簾幕）。
            // 做法：實算底緣每個頂點到球面的偏差（範圍 lo..hi），沿法線平移到偏差的中點，
            // 讓誤差一半沉入、一半浮起，再加上 baseSink 往下沉。
            Vector3[] baseVerts = GetBaseVertices(mesh);
            float shift = height * baseSink;
            if (baseVerts.Length > 0)
            {
                float lo = float.MaxValue, hi = float.MinValue;
                for (int i = 0; i < baseVerts.Length; i++)
                {
                    Vector3 w = pos + rot * Vector3.Scale(baseVerts[i], scale);
                    float d = w.magnitude - earthRadius;
                    if (d < lo) lo = d;
                    if (d > hi) hi = d;
                }
                shift += (lo + hi) * 0.5f;
            }
            pos -= n * shift;

            Transform tr = p.go.transform;
            tr.localRotation = rot;
            tr.localScale    = scale;
            tr.localPosition = pos;

            // ── 外觀與生命週期 ──
            p.brightness    = RandRange(brightnessRange);
            p.life          = RandRange(lifetimeRange);
            p.fadeIn        = Mathf.Min(RandRange(fadeInRange),  p.life * 0.45f);
            p.fadeOut       = Mathf.Min(RandRange(fadeOutRange), p.life * 0.45f);
            p.flickerPhase  = RandRange(0f, 100f);
            p.flickerPeriod = RandRange(2.5f, 6f);
            p.delay         = 0f;
            // 初次生成時錯開年齡，避免所有極光同時亮起、同時消失
            p.age           = initial ? RandRange(0f, p.life) : 0f;

            if (randomizeNoiseOffset)
            {
                float scaleJitter = RandRange(0.8f, 1.25f);
                p.noiseST = new Vector4(
                    _baseNoiseST.x * scaleJitter, _baseNoiseST.y,
                    _baseNoiseST.z + RandRange(0f, 1f), _baseNoiseST.w + RandRange(0f, 1f));
            }
            else
            {
                p.noiseST = _baseNoiseST;
            }
        }

        /// <summary>
        /// 在極冠內選落點。抽 spreadCandidates 個候選，
        /// 分數 = 與同極其他極光（含自己舊位置）的最小距離 × 夜側權重，取最高者。
        /// </summary>
        private Vector3 PickNormal(Patch self)
        {
            Vector3 axis = PolarDirection * self.poleSign;
            Vector3 u = Vector3.Cross(axis, Vector3.up);
            if (u.sqrMagnitude < 1e-6f) u = Vector3.Cross(axis, Vector3.right);
            u.Normalize();
            Vector3 v = Vector3.Cross(axis, u).normalized;

            float minC = Mathf.Clamp(Mathf.Min(capColatitudeRange.x, capColatitudeRange.y), 0f, 89f);
            float maxC = Mathf.Clamp(Mathf.Max(capColatitudeRange.x, capColatitudeRange.y), 0f, 89f);
            float cosHi = Mathf.Cos(minC * Mathf.Deg2Rad);   // 靠近極點
            float cosLo = Mathf.Cos(maxC * Mathf.Deg2Rad);   // 極冠外緣

            Vector3 best = axis;
            float bestScore = float.NegativeInfinity;

            int candidates = Mathf.Max(1, spreadCandidates);
            for (int c = 0; c < candidates; c++)
            {
                // cos(θ) 均勻 → 在球冠上依面積均勻分佈（不會擠在極點）
                float cosT = RandRange(cosLo, cosHi);
                float sinT = Mathf.Sqrt(Mathf.Max(0f, 1f - cosT * cosT));
                float phi  = RandRange(0f, Mathf.PI * 2f);
                Vector3 horiz = u * Mathf.Cos(phi) + v * Mathf.Sin(phi);
                Vector3 n = axis * cosT + horiz * sinT;

                // 與同極其他極光的最小距離（1 - dot，0 = 同點，2 = 對蹠）
                float minDist = 2f;
                for (int i = 0; i < _patches.Count; i++)
                {
                    Patch o = _patches[i];
                    if (!o.placed || o.poleSign != self.poleSign) continue;
                    float d = 1f - Vector3.Dot(o.normal, n);
                    if (d < minDist) minDist = d;
                }

                float nightWeight = 1f;
                if (_hasNightDir && nightSideBias > 0f)
                {
                    float nightness = Vector3.Dot(horiz, _nightDirLocal) * 0.5f + 0.5f; // 0 日側 … 1 夜側
                    nightWeight = Mathf.Lerp(1f, 0.25f + 0.75f * nightness, nightSideBias);
                }

                // 小幅隨機項，避免分數相同時總是選第一個
                float score = minDist * nightWeight * RandRange(0.85f, 1f);
                if (score > bestScore) { bestScore = score; best = n; }
            }

            return best.normalized;
        }

        #endregion

        // ─────────────────────────────────────────────────────────────
        #region 亮度

        /// <summary>淡入 × 淡出 的平滑包絡，0..1。</summary>
        private static float Envelope(Patch p)
        {
            float fin  = p.fadeIn  > 0f ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(p.age / p.fadeIn)) : 1f;
            float fout = p.fadeOut > 0f ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((p.life - p.age) / p.fadeOut)) : 1f;
            return fin * fout;
        }

        private void ApplyBrightness(Patch p, float value)
        {
            if (p.mr == null) return;

            // 幾乎看不見時直接關掉 renderer，省下透明疊繪的成本
            bool visible = value > 0.002f;
            if (p.mr.enabled != visible) p.mr.enabled = visible;
            if (!visible) return;

            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            p.mr.GetPropertyBlock(_mpb);
            _mpb.SetFloat(ColorMultiplierId, _baseMultiplier * value);
            _mpb.SetVector(NoiseSTId, p.noiseST);
            p.mr.SetPropertyBlock(_mpb);
        }

        /// <summary>由場景主方向光推得夜側方向（本地空間、垂直於極軸的分量）。</summary>
        private bool TryGetNightDirectionLocal(out Vector3 nightDirLocal)
        {
            nightDirLocal = Vector3.zero;

            Light sun = RenderSettings.sun;
            if (sun == null)
            {
                foreach (Light l in FindObjectsOfType<Light>())
                    if (l.type == LightType.Directional) { sun = l; break; }
            }
            if (sun == null) return false;

            Transform parent = ResolveEarthRoot();
            if (parent == null) return false;

            // 方向光的 forward 是光射去的方向 —— 背光面（夜側）就在這個方向
            Vector3 local = parent.InverseTransformDirection(sun.transform.forward);
            Vector3 axis = PolarDirection;
            local -= axis * Vector3.Dot(local, axis);
            if (local.sqrMagnitude < 1e-6f) return false;

            nightDirLocal = local.normalized;
            return true;
        }

        #endregion

        // ─────────────────────────────────────────────────────────────
        #region 工具

        // 每個網格的「最底層」頂點（模型空間），用於曲率補償。只算一次。
        private static readonly Dictionary<Mesh, Vector3[]> BaseVertexCache = new Dictionary<Mesh, Vector3[]>();

        private static Vector3[] GetBaseVertices(Mesh mesh)
        {
            if (mesh == null) return System.Array.Empty<Vector3>();
            if (BaseVertexCache.TryGetValue(mesh, out Vector3[] cached)) return cached;

            Vector3[] all = mesh.isReadable ? mesh.vertices : null;
            if (all == null || all.Length == 0)
            {
                BaseVertexCache[mesh] = System.Array.Empty<Vector3>();
                return BaseVertexCache[mesh];
            }

            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (Vector3 v in all) { if (v.y < minY) minY = v.y; if (v.y > maxY) maxY = v.y; }
            float eps = (maxY - minY) * 1e-3f;

            var list = new List<Vector3>();
            foreach (Vector3 v in all) if (v.y <= minY + eps) list.Add(v);

            Vector3[] result = list.ToArray();
            BaseVertexCache[mesh] = result;
            return result;
        }

        private Vector3 PolarDirection
        {
            get
            {
                switch (polarAxis)
                {
                    case PolarAxis.XPositive: return Vector3.right;
                    case PolarAxis.YPositive: return Vector3.up;
                    default:                  return Vector3.forward;
                }
            }
        }

        private float RandRange(float a, float b) => a + (float)_rng.NextDouble() * (b - a);
        private float RandRange(Vector2 r)        => RandRange(r.x, r.y);

        private Transform ResolveEarthRoot()
        {
            if (earthRoot != null) return earthRoot;
            var unit = FindObjectOfType<UnitEarth>();
            return unit != null ? unit.transform : null;
        }

        private static void DestroySafe(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        private void ClearSpawned()
        {
            foreach (Patch p in _patches) if (p.go != null) DestroySafe(p.go);
            _patches.Clear();

            if (_container != null)
            {
                DestroySafe(_container.gameObject);
                _container = null;
            }
        }

        #endregion

        // ─────────────────────────────────────────────────────────────
        #region 編輯器輔助

#if UNITY_EDITOR
        [ContextMenu("診斷：印出生成物狀態")]
        private void DumpDiagnostics()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== AuroraOvalRing 診斷 ===");

            Transform parent = ResolveEarthRoot();
            sb.AppendLine($"earthRoot : {(parent != null ? parent.name : "null")}");
            if (parent != null)
            {
                sb.AppendLine($"  lossyScale={parent.lossyScale}  極軸(world)={parent.TransformDirection(PolarDirection)}");
                sb.AppendLine($"  世界空間地球半徑 ≈ {earthRadius * parent.lossyScale.x:F3}");
            }
            sb.AppendLine($"material  : {(auroraMaterial ? auroraMaterial.name + " / " + auroraMaterial.shader.name + $" / base Color_Multiplier={_baseMultiplier}" : "null")}");
            sb.AppendLine($"網格數     : {_preparedMeshes.Count}");
            foreach (Mesh m in _preparedMeshes)
                if (m) sb.AppendLine($"  {m.name}  bounds.size={m.bounds.size}  uv2={m.uv2.Length}");
            sb.AppendLine($"夜側方向   : {(_hasNightDir ? _nightDirLocal.ToString() : "無（找不到方向光）")}");
            sb.AppendLine($"極光片數   : {_patches.Count}");

            int shown = 0;
            foreach (Patch p in _patches)
            {
                if (p.go == null) continue;
                float colat = Vector3.Angle(PolarDirection * p.poleSign, p.normal);
                string state = p.delay > 0f ? $"隱藏中（{p.delay:F1}s 後重生）" : $"age {p.age:F1}/{p.life:F1}s  包絡 {Envelope(p):F2}";
                sb.AppendLine($"  {p.go.name}: 距極點 {colat:F1}°  mesh={(p.mf.sharedMesh ? p.mf.sharedMesh.name : "null")}  " +
                              $"renderer={(p.mr.enabled ? "開" : "關")}  {state}");
                if (++shown >= 16) break;
            }

            Debug.Log(sb.ToString(), this);
        }

        [ContextMenu("Scene 視角：俯視北極")]
        private void SceneViewLookAtNorthPole() => SceneViewLookAtPole(+1f);

        [ContextMenu("Scene 視角：俯視南極")]
        private void SceneViewLookAtSouthPole() => SceneViewLookAtPole(-1f);

        private void SceneViewLookAtPole(float sign)
        {
            Transform parent = ResolveEarthRoot();
            var sceneView = UnityEditor.SceneView.lastActiveSceneView;
            if (parent == null || sceneView == null) return;

            Vector3 axisWS = parent.TransformDirection(PolarDirection * sign).normalized;
            Vector3 poleWS = parent.TransformPoint(PolarDirection * sign * earthRadius);
            float   size   = earthRadius * parent.lossyScale.x * 1.2f;

            Vector3 forward = -axisWS;
            Vector3 up = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) < 0.99f ? Vector3.up : Vector3.forward;
            sceneView.LookAt(poleWS, Quaternion.LookRotation(forward, up), size);
            sceneView.Repaint();
        }
#endif

        private void OnDrawGizmosSelected()
        {
            Transform parent = ResolveEarthRoot();
            if (parent == null) return;

            Gizmos.matrix = parent.localToWorldMatrix;
            Vector3 axis = PolarDirection;

            Gizmos.color = Color.white;
            Gizmos.DrawLine(-axis * earthRadius * 1.4f, axis * earthRadius * 1.4f);

            // 極冠範圍的內外緣
            Gizmos.color = new Color(0.4f, 1f, 0.7f, 0.8f);
            foreach (float deg in new[] { capColatitudeRange.x, capColatitudeRange.y })
            {
                float t = Mathf.Clamp(deg, 0f, 89f) * Mathf.Deg2Rad;
                float r = earthRadius * Mathf.Sin(t);
                float h = earthRadius * Mathf.Cos(t);
                if (r < 1e-5f) continue;
                if (northPole) DrawCircleGizmo( axis * h, axis, r);
                if (southPole) DrawCircleGizmo(-axis * h, axis, r);
            }
        }

        private static void DrawCircleGizmo(Vector3 center, Vector3 axis, float radius)
        {
            const int steps = 48;
            Vector3 u = Vector3.Cross(axis, Vector3.up);
            if (u.sqrMagnitude < 1e-6f) u = Vector3.Cross(axis, Vector3.right);
            u.Normalize();
            Vector3 v = Vector3.Cross(axis, u).normalized;

            Vector3 prev = center + u * radius;
            for (int i = 1; i <= steps; i++)
            {
                float a = i * Mathf.PI * 2f / steps;
                Vector3 next = center + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * radius;
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }

        #endregion
    }
}
