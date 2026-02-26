// Assets/Scripts/Unity/TutorialOverlayView.cs
using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public sealed class TutorialOverlayView : MonoBehaviour
{
    public enum StepId
    {
        Intro0,
        Reveal7Hands,
        RiskCounter,
        GameOverRule,
        CoinReward,
        DeckAdjust,
        GaugeBuy,
        ForcedDraw,
        GaugeByMissing,
        GaugeScaleWithDeck,
        EnemyInfo,
        StartGift
    }

    public enum ArrowSide { None, Left, Right }

    [Serializable]
    public sealed class Step
    {
        public StepId id;

        [TextArea(2, 4)]
        public string text;

        public RectTransform highlightTarget;

        // true のとき、このオーバーレイ上のクリックでは進まない（外部操作待ち）
        public bool waitForExternalAction;
    }

    [Header("UI")]
    [SerializeField] private GameObject root;
    [SerializeField] private TMP_Text bodyText;

    [Header("Focus Dimmer (Hole Mask Shader)")]
    [SerializeField] private GameObject dimmer;          // Darkmask ルート
    [SerializeField] private Image dimmerImage;          // Darkmask の Image（穴あきMaterialを持つ）
    [SerializeField] private bool defaultDim = true;     // ほぼ全Stepで暗くする
    [SerializeField] private Vector2 spotlightPadding = Vector2.zero; // 穴を少し広げたい時

    [Header("Highlight")]
    [SerializeField] private RectTransform highlightFrame;
    [SerializeField] private Vector2 highlightPadding = new(16, 10);

    [Header("Arrow (optional)")]
    [SerializeField] private RectTransform arrow;
    [SerializeField] private Vector2 arrowMargin = new(24f, 0f);

    [Header("Input")]
    [SerializeField] private Button tapCatcher;

    [Header("Steps")]
    [SerializeField] private Step[] steps;

    // 既存参照（壊しにくいので残す）
    [SerializeField] private GaugeBarView gaugeBarView;
    [SerializeField] private GameHudView hud;
    [SerializeField] private RunPresenter presenter;

    public bool IsOpen => root != null && root.activeSelf;

    public event Action<Step> OnStepChanged;
    public event Action OnFinished;

    private int _index = -1;
    private int _externalCount = 0;

    // ★現在のハイライト対象（穴の基準）
    private RectTransform _currentHighlightTarget;

    // シェーダープロパティ
    private static readonly int ShaderHoleRect = Shader.PropertyToID("_HoleRect");
    private static readonly int ShaderUseHole  = Shader.PropertyToID("_UseHole");

    private void Awake()
    {
        if (tapCatcher != null)
        {
            tapCatcher.onClick.RemoveListener(OnTapNext);
            tapCatcher.onClick.AddListener(OnTapNext);
        }

        // クリックを邪魔しないための保険
        ForceRaycastOff(dimmer);
        ForceRaycastOff(arrow != null ? arrow.gameObject : null);

        CloseImmediate();
    }

    public void OpenFromStart()
    {
        if (root != null) root.SetActive(true);

        _index = -1;
        _externalCount = 0;

        Next();
    }

    public void CloseImmediate()
    {
        if (root != null) root.SetActive(false);
        _index = -1;
        _externalCount = 0;

        ApplyHighlight(null);

        // 見た目は全部OFF
        SetFocus(dim: false, showArrow: false, side: ArrowSide.None);
    }

    public void Next()
    {
        if (steps == null || steps.Length == 0)
        {
            Finish();
            return;
        }

        _index++;
        if (_index >= steps.Length)
        {
            Finish();
            return;
        }

        var s = steps[_index];

        if (bodyText != null)
            bodyText.text = s.text;

        ApplyHighlight(s.highlightTarget);

        _externalCount = 0;
        ApplyStepInputMode(s);

        // ★デフォルトで暗くする（矢印は必要なStepだけ上書き）
        SetFocus(dim: defaultDim, showArrow: false, side: ArrowSide.None);

        OnStepChanged?.Invoke(s);
    }

    private void Finish()
    {
        CloseImmediate();
        OnFinished?.Invoke();
    }

    // -------------------------
    // External action gate
    // -------------------------

    public void NotifyExternalAction()
    {
        if (!IsOpen) return;
        if (steps == null || steps.Length == 0) return;
        if (_index < 0 || _index >= steps.Length) return;

        var s = steps[_index];
        if (!s.waitForExternalAction) return;

        _externalCount++;
        Next();
    }

    private void OnTapNext()
    {
        if (!IsOpen) return;
        if (steps == null || steps.Length == 0) { Finish(); return; }
        if (_index < 0 || _index >= steps.Length) { Next(); return; }

        if (steps[_index].waitForExternalAction)
            return;

        Next();
    }

    private void ApplyStepInputMode(Step s)
    {
        if (tapCatcher == null) return;

        bool wait = (s != null && s.waitForExternalAction);
        SetButtonRaycast(tapCatcher, enabled: !wait);
    }

    // -------------------------
    // Highlight
    // -------------------------

    private void ApplyHighlight(RectTransform target)
    {
        _currentHighlightTarget = target; // ★穴の基準は target

        if (highlightFrame == null) return;

        if (target == null || !target.gameObject.activeInHierarchy)
        {
            highlightFrame.gameObject.SetActive(false);
            return;
        }

        highlightFrame.gameObject.SetActive(true);

        var parent = highlightFrame.parent as RectTransform;
        if (parent == null)
        {
            highlightFrame.position = target.position;
            highlightFrame.sizeDelta = target.rect.size + highlightPadding * 2f;
            return;
        }

        var canvas = highlightFrame.GetComponentInParent<Canvas>();
        Camera cam = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = canvas.worldCamera;

        Vector3[] wc = new Vector3[4];
        target.GetWorldCorners(wc);

        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);

        for (int i = 0; i < 4; i++)
        {
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, wc[i]);
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, cam, out var local))
            {
                min = Vector2.Min(min, local);
                max = Vector2.Max(max, local);
            }
        }

        Vector2 center = (min + max) * 0.5f;
        Vector2 size = (max - min) + highlightPadding * 2f;

        highlightFrame.anchoredPosition = center;
        highlightFrame.sizeDelta = size;
    }

    // -------------------------
    // Focus visuals (Dimmer / Arrow)
    // -------------------------

    public void SetFocus(bool dim, bool showArrow, ArrowSide side)
    {
        if (_currentHighlightTarget == null)
                dim = false;

        if (dimmer != null) dimmer.SetActive(dim);

        // ★穴あきシェーダー更新
        UpdateSpotlightShader(dim);

        if (arrow == null) return;

        bool on = showArrow && side != ArrowSide.None;
        arrow.gameObject.SetActive(on);

        if (on)
            PositionArrow(side);
    }

    private void PositionArrow(ArrowSide side)
    {
        if (arrow == null || highlightFrame == null) return;
        if (!highlightFrame.gameObject.activeInHierarchy) return;

        var frame = highlightFrame;
        float halfW = frame.rect.width * 0.5f;
        Vector2 p = frame.anchoredPosition;

        if (side == ArrowSide.Left)
            p.x = frame.anchoredPosition.x - halfW - arrowMargin.x;
        else if (side == ArrowSide.Right)
            p.x = frame.anchoredPosition.x + halfW + arrowMargin.x;

        p.y = frame.anchoredPosition.y + arrowMargin.y;
        arrow.anchoredPosition = p;
    }

    // -------------------------
    // Hole-mask shader update
    // -------------------------

    private void UpdateSpotlightShader(bool dim)
    {
        if (!dim || dimmerImage == null) return;

        var mat = dimmerImage.material;
        if (mat == null) return;

        var target = _currentHighlightTarget;

        // ★ハイライトが無いStepは「暗くしない」
        if (target == null || !target.gameObject.activeInHierarchy)
        {
            // 暗幕そのものをOFF
            if (dimmer != null)
                dimmer.SetActive(false);

            mat.SetFloat(ShaderUseHole, 0f);
            return;
        }

        var dimRt = dimmerImage.rectTransform;
        if (dimRt == null)
        {
            mat.SetFloat(ShaderUseHole, 0f);
            return;
        }

        if (!TryGetRectInLocal(dimRt, target, out var holeLocal))
        {
            mat.SetFloat(ShaderUseHole, 0f);
            return;
        }

        // 任意：少し広げる
        holeLocal.xMin -= spotlightPadding.x;
        holeLocal.xMax += spotlightPadding.x;
        holeLocal.yMin -= spotlightPadding.y;
        holeLocal.yMax += spotlightPadding.y;

        var pr = dimRt.rect;

        float xMin = Mathf.InverseLerp(pr.xMin, pr.xMax, holeLocal.xMin);
        float xMax = Mathf.InverseLerp(pr.xMin, pr.xMax, holeLocal.xMax);
        float yMin = Mathf.InverseLerp(pr.yMin, pr.yMax, holeLocal.yMin);
        float yMax = Mathf.InverseLerp(pr.yMin, pr.yMax, holeLocal.yMax);

        xMin = Mathf.Clamp01(xMin);
        xMax = Mathf.Clamp01(xMax);
        yMin = Mathf.Clamp01(yMin);
        yMax = Mathf.Clamp01(yMax);

        if (xMax < xMin) xMax = xMin;
        if (yMax < yMin) yMax = yMin;

        mat.SetVector(ShaderHoleRect, new Vector4(xMin, yMin, xMax, yMax));
        mat.SetFloat(ShaderUseHole, 1f);
    }

    // -------------------------
    // Queries
    // -------------------------

    public bool IsCurrentStep(StepId id)
    {
        return IsOpen && steps != null && _index >= 0 && _index < steps.Length && steps[_index].id == id;
    }

    public void TryAdvanceIf(Func<bool> predicate)
    {
        if (!IsOpen) return;
        if (predicate == null) return;

        if (predicate())
            Next();
    }

    // -------------------------
    // Utilities
    // -------------------------

    private static void SetButtonRaycast(Button b, bool enabled)
    {
        if (b == null) return;

        b.interactable = enabled;

        var graphics = b.GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < graphics.Length; i++)
            graphics[i].raycastTarget = enabled;
    }

    private static void ForceRaycastOff(GameObject go)
    {
        if (go == null) return;
        var graphics = go.GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < graphics.Length; i++)
            graphics[i].raycastTarget = false;
    }

    private static bool TryGetRectInLocal(RectTransform localRoot, RectTransform target, out Rect rect)
    {
        rect = default;
        if (localRoot == null || target == null) return false;

        var canvas = localRoot.GetComponentInParent<Canvas>();
        Camera cam = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = canvas.worldCamera;

        Vector3[] wc = new Vector3[4];
        target.GetWorldCorners(wc);

        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);

        for (int i = 0; i < 4; i++)
        {
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, wc[i]);
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(localRoot, screen, cam, out var local))
            {
                min = Vector2.Min(min, local);
                max = Vector2.Max(max, local);
            }
        }

        rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        return true;
    }

    public void RefreshHighlightNow()
    {
        if (!IsOpen) return;

        // いま刺さってる target で枠を作り直す
        ApplyHighlight(_currentHighlightTarget);

        // dimmerがONなら穴も更新（material更新）
        // defaultDim運用なので「dimmerがActiveか」で十分
        bool dim = (dimmer != null && dimmer.activeSelf);
        UpdateSpotlightShader(dim);
    }
}
