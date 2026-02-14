using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;


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

    [Serializable]
    public sealed class Step
    {
        public StepId id;
        [TextArea(2, 4)] public string text;
        public RectTransform highlightTarget;

        public bool waitForExternalAction;

        // ★追加：このStepで必要な外部操作回数
        public int requiredExternalCount;
    }


    [Header("UI")]
    [SerializeField] private GameObject root;
    [SerializeField] private TMP_Text bodyText;

    [Header("Highlight")]
    [SerializeField] private RectTransform highlightFrame; // 枠画像(透明背景の上に表示)
    [SerializeField] private Vector2 highlightPadding = new(16, 10);

    [Header("Input")]
    [SerializeField] private Button tapCatcher;
    [SerializeField] private float holdToSkipSec = 0.35f;

    [Header("Steps")]
    [SerializeField] private Step[] steps;

    [SerializeField] private GaugeBarView gaugeBarView;
    [SerializeField] private GameHudView hud;
    [SerializeField] private RunPresenter presenter;






    public bool IsOpen => root != null && root.activeSelf;

    public event Action<Step> OnStepChanged;
    public event Action OnFinished;

    private int _index = -1;
    private float _pressTime = 0f;
    private bool _pressing = false;

    public bool waitForExternalAction;
    private int _externalCount;




    private void Awake()
    {
        if (tapCatcher != null)
        {
            tapCatcher.onClick.AddListener(OnTapNext);

            // 長押し判定（簡易）
            var trigger = tapCatcher.gameObject.AddComponent<TutorialPressCatcher>();
            trigger.OnDown += () => { _pressing = true; _pressTime = 0f; };
            trigger.OnUp += () => { _pressing = false; _pressTime = 0f; };
        }

        CloseImmediate();
    }

    private void Update()
    {
        if (!IsOpen) return;
        if (!_pressing) return;

        _pressTime += Time.unscaledDeltaTime;
        if (_pressTime >= holdToSkipSec)
        {
            Finish();
        }
    }

    public void OpenFromStart()
    {
        Debug.Log($"[TutorialOverlayView] OpenFromStart root={(root!=null ? root.name : "null")} beforeActive={(root!=null ? root.activeSelf.ToString() : "-")}");
        if (root != null) root.SetActive(true);
        Debug.Log($"[TutorialOverlayView] OpenFromStart afterActive={(root!=null ? root.activeSelf.ToString() : "-")}");

        _index = -1;
        Next();
    }


    public void CloseImmediate()
    {
        if (root != null) root.SetActive(false);
        _index = -1;
        ApplyHighlight(null);
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

        OnStepChanged?.Invoke(s);
        _externalCount = 0;
        ApplyStepInputMode(s);
    }

    private void Finish()
    {
        CloseImmediate();
        OnFinished?.Invoke();
    }

    private void ApplyHighlight(RectTransform target)
    {
        if (highlightFrame == null) return;

        if (target == null || !target.gameObject.activeInHierarchy)
        {
            highlightFrame.gameObject.SetActive(false);
            return;
        }

        highlightFrame.gameObject.SetActive(true);

        // highlightFrame の親（座標系の基準）
        var parent = highlightFrame.parent as RectTransform;
        if (parent == null)
        {
            // 親がRectTransformじゃないのはUI的におかしいのでフォールバック
            highlightFrame.position = target.position;
            highlightFrame.sizeDelta = target.rect.size + highlightPadding * 2f;
            return;
        }

        // Canvas と Camera を取得（ScreenPoint変換に必要）
        var canvas = highlightFrame.GetComponentInParent<Canvas>();
        Camera cam = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = canvas.worldCamera;

        // target のワールド四隅 → スクリーン座標 → parentローカル座標へ変換
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

        // ローカル矩形から中心とサイズを作る
        Vector2 center = (min + max) * 0.5f;
        Vector2 size = (max - min);

        // パディングを足す
        size += highlightPadding * 2f;

        // 反映（アンカーは触らない：anchoredPositionとsizeDeltaだけ）
        highlightFrame.anchoredPosition = center;
        highlightFrame.sizeDelta = size;
    }


    // 長押し用（簡易）
    private sealed class TutorialPressCatcher : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public Action OnDown;
        public Action OnUp;

        public void OnPointerDown(UnityEngine.EventSystems.PointerEventData eventData) => OnDown?.Invoke();
        public void OnPointerUp(UnityEngine.EventSystems.PointerEventData eventData) => OnUp?.Invoke();
    }



    public void NotifyExternalAction()
    {
        if (!IsOpen) return;
        if (steps == null || steps.Length == 0) return;
        if (_index < 0 || _index >= steps.Length) return;

        var s = steps[_index];
        if (!s.waitForExternalAction) return;

        _externalCount++;

        // ★必要回数に達したら次へ
        if (_externalCount >= Mathf.Max(1, s.requiredExternalCount))
        {
            Next();
        }
    }


    private void OnTapNext()
    {
        if (!IsOpen) return;
        if (steps == null || steps.Length == 0) { Finish(); return; }
        if (_index < 0 || _index >= steps.Length) { Next(); return; }

        // ★待ちStepならタップでは進まない
        if (steps[_index].waitForExternalAction)
            return;

        Next();
    }

    private void ApplyStepInputMode(Step s)
    {
        if (tapCatcher == null) return;

        bool wait = (s != null && s.waitForExternalAction);

        // 待ちStepでは、下のUIにクリックを通す
        tapCatcher.interactable = !wait;

        // Buttonが持ってるGraphic(Image)のRaycastも切る（これが超重要）
        var g = tapCatcher.targetGraphic;
        if (g != null) g.raycastTarget = !wait;

        // もし tapCatcher の子に Image がある構成なら、まとめて切ってもOK（保険）
        var imgs = tapCatcher.GetComponentsInChildren<UnityEngine.UI.Image>(true);
        for (int i = 0; i < imgs.Length; i++)
            imgs[i].raycastTarget = !wait;
    }

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


}
