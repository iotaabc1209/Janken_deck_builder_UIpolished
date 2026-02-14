using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using TMPro;
using RpsBuild.Core;

public sealed class RoundFlowUI : MonoBehaviour
{
    public enum ViewMode { ResolveOnly, WithAdjust }

    [Header("Core")]
    [SerializeField] private RunPresenter presenter;

    [Header("Bottom Proceed Panel")]
    [SerializeField] private Button bottomPanelButton;
    [SerializeField] private TMP_Text bottomHintText; // 任意

    [Header("Buttons")]
    [SerializeField] private Button resolveTapCatcherButton;
    [SerializeField] private Button openAdjustButton; // 右窓の「調整」ボタン
    [SerializeField] private Button hudDetailToggleButton; // 左ウィンドウのクリック領域（透明ボタンでOK）
    private bool _hudDetail = false; // 既定：畳む

    [Header("Left Window Hint")]
    [SerializeField] private TMP_Text detailHintText;
    [SerializeField] private string hintShowDetail = "▼ 詳細を見る";
    [SerializeField] private string hintCloseDetail = "▲ 閉じる";



    [Header("SafeRect Parent")]
    [SerializeField] private GameObject mainPlayRoot;      // MainPlayRoot
    [SerializeField] private GameObject centralWindowRoot; // CentralWindow

    [Header("SafeRect Children")]
    [SerializeField] private GameObject resolveRoot;
    [SerializeField] private GameObject adjustPanel;
    [SerializeField] private GameObject rightWindowResolveRoot; // RightWindow_resolve
    [SerializeField] private GameObject rightWindowAdjustRoot;  // RightWindow_adjust（あるなら）


    [Header("Left Window Auto Size")]
    [SerializeField] private RectTransform leftWindowRect; // HUD_Root/LeftWindow
    [SerializeField] private TMP_Text enemyInfoText;        // EnemyInfo
    [SerializeField] private float leftWindowPaddingY = 32f; // 上下パディング合計（適当に調整）
    [SerializeField] private float leftWindowMinHeight = 0f; // 必要なら

    // RoundFlowUI.cs
    [Header("Resolve Right Window (Optional)")]
    [SerializeField] private TMP_Text resolveDeckText;
    [SerializeField] private TMP_Text resolvePointsText;

    [Header("Right Window HUD Raycast Block")]
    [SerializeField] private Button[] rightHudGaugeButtons; // Gu/Choki/Pa の3つ



    [Header("Views")]
    [SerializeField] private AdjustPanelView adjustView;
    [SerializeField] private GameHudView hud;
    [SerializeField] private TutorialOverlayView tutorial;
    [SerializeField] private GameObject rightWindowRoot; // MainPlayRootのRightWindowを刺す
    [SerializeField] private ReservedSlotsView reservedSlotsView;



    [Header("Debug")]
    [SerializeField] private bool debugLog = true;

    private ViewMode _viewMode = ViewMode.ResolveOnly;
    private bool _booted;
    private bool _tutorialResetDone = false;
    private const string HintTutorial = "次のページへ";
    private const string HintNextBattle = "次の勝負へ";







    private void Reset()
    {
        presenter = FindFirstObjectByType<RunPresenter>();
    }

    private void Awake()
    {
        // 参照がズレてても、少なくともUIを沈めない初期状態へ
        ForceParentsOn();

        // listener は Awake で一度だけ（SetActive揺れに強く）
        if (bottomPanelButton != null)
        {
            bottomPanelButton.onClick.RemoveListener(OnProceed);
            bottomPanelButton.onClick.AddListener(OnProceed);
        }

        if (resolveTapCatcherButton != null)
        {
            resolveTapCatcherButton.onClick.RemoveListener(OnResolveTap);
            resolveTapCatcherButton.onClick.AddListener(OnResolveTap);
        }

        if (openAdjustButton != null)
        {
            openAdjustButton.onClick.RemoveListener(OpenAdjust);
            openAdjustButton.onClick.AddListener(OpenAdjust);
        }

        if (adjustView != null)
        {
            // OnDestroyで解除する
            adjustView.OnDraftChanged += HandleAdjustChanged;
        }

        if (hudDetailToggleButton != null)
            hudDetailToggleButton.onClick.AddListener(ToggleHudDetail);

        ApplyHudViewMode(); // ★起動時に必ず反映（勝手にDetailにならない）
        RefreshDetailHint();

    }

    private void Start()
    {
        if (tutorial != null)
        {
            tutorial.OnStepChanged -= HandleTutorialStepChanged; // ★残骸対策
            tutorial.OnStepChanged += HandleTutorialStepChanged;
        }

        SyncTutorialState();
        SetViewMode(ViewMode.ResolveOnly, "Start");
        Invoke(nameof(BootNextFrame), 0f);
    }


    private void BootNextFrame()
    {
        if (_booted) return;
        _booted = true;

        if (debugLog) Log($"BootNextFrame presenter={(presenter ? "OK" : "NULL")} run={(presenter != null && presenter.Run != null ? "OK" : "NULL")}");

        // presenterが無いならそれ以上進めない（ただしUIは生きてる）
        if (presenter == null)
        {
            LogError("presenter is NULL. Assign it in Inspector or ensure Reset() finds it.");
            return;
        }

        // RunPresenter.Start() が ResetRun 済みなら Run があるはず。
        // もし無いなら tuningAsset不足など。ログでわかる。
        if (presenter.Run == null)
        {
            LogWarn("presenter.Run is NULL. Check RunPresenter.ResetRun() logs (e.g., TuningAsset missing).");
            return;
        }

        // Introを1回だけ回す（Round0表示を作る）
        StartCoroutine(FitLeftWindowNextFrame());
        presenter.PlayIntroIfNeeded();
        presenter.RefreshHud();
        RefreshProceedInteractable();
    }

    // -------------------------
    // Handlers
    // -------------------------

    private void OnProceed()
    {
        // ★チュートリアル中はProceedで進行させない
        if (tutorial != null && tutorial.IsOpen)
            return;

        if (presenter == null) { LogWarn("OnProceed: presenter NULL"); return; }
        if (presenter.Run == null) { LogWarn("OnProceed: run NULL"); return; }

        // GameOver 最優先
        if (presenter.Run.IsGameOver)
        {
            SceneFlow.GoToResult(presenter.Run.Score);
            return;
        }

        // Resolve中：スキップ → できなければ次ラウンド
        if (_viewMode == ViewMode.ResolveOnly)
        {
            if (presenter.TrySkipResolveSequence())
                return;

            presenter.PlayNextRound();
            presenter.RefreshHud();
            RefreshResolveRightWindow();
            RefreshProceedInteractable();
            return;
        }

        // Adjust中：確定できないなら止める（Proceed押せないはずだが保険）
        if (adjustView != null && !adjustView.TryCommitDraft())
        {
            RefreshProceedInteractable();
            return;
        }

        // 次ラウンド開始
        SetViewMode(ViewMode.ResolveOnly, "Proceed:CloseAdjust");
        presenter.PlayNextRound();
        presenter.RefreshHud();
        RefreshResolveRightWindow();
        RefreshProceedInteractable();
    }

    private void OnResolveTap()
    {
        if (presenter == null) return;

        // 再生中はスキップ最優先
        if (presenter.TrySkipResolveSequence())
            return;

        // ResolveTapで閉じる対象は「Adjust」のみ（誤爆防止）
        if (_viewMode == ViewMode.WithAdjust)
        {
            SetViewMode(ViewMode.ResolveOnly, "ResolveTap:CloseAdjust");
            ApplyHudViewMode();
            RefreshProceedInteractable();
        }
    }

    public void OpenAdjust()
    {
        if (debugLog) Log("OpenAdjust");

        if (presenter == null) { LogWarn("OpenAdjust: presenter NULL"); return; }
        if (presenter.Run == null) { LogWarn("OpenAdjust: run NULL"); return; }

        SetViewMode(ViewMode.WithAdjust, "OpenAdjust");

        // AdjustPanelViewのCloseが gameObject.SetActive(false) しても沈まないよう、
        // Openするたび必ず adjustPanel/adjustView を起こす。
        if (adjustPanel != null) adjustPanel.SetActive(true);
        if (adjustView != null)
        {
            adjustView.gameObject.SetActive(true);
            adjustView.Open(presenter);
        }

        ApplyHudViewMode();

        if (reservedSlotsView != null && adjustView != null)
            reservedSlotsView.Render(adjustView.GetDraftForcedOrder());

        if (tutorial != null)
                tutorial.NotifyExternalAction();

        SyncTutorialState();
        RefreshProceedInteractable();
    }

    private void HandleAdjustChanged()
    {
        if (_viewMode != ViewMode.WithAdjust) return;

        RefreshProceedInteractable();

        if (reservedSlotsView != null && adjustView != null)
            reservedSlotsView.Render(adjustView.GetDraftForcedOrder());

        // ★チュートリアル：グーを2枚予約できたら次へ（Element12想定）
        if (tutorial != null && adjustView != null)
            {
                // StepId はあなたのElement12のidに合わせてください（例：ForcedDraw など）
                if (tutorial.IsCurrentStep(TutorialOverlayView.StepId.ForcedDraw))
                {
                    var order = adjustView.GetDraftForcedOrder();
                    int guCount = 0;

                    if (order != null)
                    {
                        for (int i = 0; i < order.Count; i++)
                            if (order[i] == RpsColor.Gu) guCount++;
                    }

                    if (guCount >= 2)
                        tutorial.Next();
                }
            }

    }


    // -------------------------
    // ViewMode core (ONLY place that toggles UI)
    // -------------------------

    private void SetViewMode(ViewMode m, string reason)
    {
        _viewMode = m;

        ForceParentsOn();

        bool showResolve = (m == ViewMode.ResolveOnly);
        bool showAdjust  = (m == ViewMode.WithAdjust);

        if (debugLog)
        {
            Log($"SetViewMode ENTER {m} reason={reason} " +
                $"resolve={(resolveRoot ? resolveRoot.activeSelf : false)} " +
                $"adjust={(adjustPanel ? adjustPanel.activeSelf : false)}");
        }

        if (resolveRoot != null) resolveRoot.SetActive(showResolve);
        if (adjustPanel != null) adjustPanel.SetActive(showAdjust);
        if (rightWindowResolveRoot != null) rightWindowResolveRoot.SetActive(showResolve);
        if (rightWindowAdjustRoot != null)  rightWindowAdjustRoot.SetActive(showAdjust);

        if (resolveTapCatcherButton != null)
            resolveTapCatcherButton.gameObject.SetActive(showResolve);

        if (openAdjustButton != null)
            openAdjustButton.interactable = showResolve;

        if (_viewMode == ViewMode.ResolveOnly)
                RefreshResolveRightWindow();


        // ★絶対に両方OFFにしない保険
        bool resolveOn = (resolveRoot != null && resolveRoot.activeSelf);
        bool adjustOn  = (adjustPanel != null && adjustPanel.activeSelf);
        if (!resolveOn && !adjustOn)
        {
            LogError($"Both resolveRoot & adjustPanel are inactive! Force ResolveOnly. reason={reason}");
            if (resolveRoot != null) resolveRoot.SetActive(true);
            _viewMode = ViewMode.ResolveOnly;
        }

        // Resolve中は「調整を開く」の邪魔になるのでゲージボタンをクリック透過
        bool resolve = (m == ViewMode.ResolveOnly);

        if (rightHudGaugeButtons != null)
        {
            for (int i = 0; i < rightHudGaugeButtons.Length; i++)
            {
                var b = rightHudGaugeButtons[i];
                if (b == null) continue;

                // Buttonの見た目は一切いじらない。Raycastだけ切る。
                var g = b.targetGraphic;
                if (g != null) g.raycastTarget = !resolve;

                // 保険：子のGraphicがRaycastを取る構成なら、まとめて切る
                var graphics = b.GetComponentsInChildren<UnityEngine.UI.Graphic>(true);
                for (int j = 0; j < graphics.Length; j++)
                    graphics[j].raycastTarget = !resolve;
            }
        }



        if (debugLog)
        {
            Log($"SetViewMode EXIT mode={_viewMode} " +
                $"resolve={(resolveRoot ? resolveRoot.activeSelf : false)} " +
                $"adjust={(adjustPanel ? adjustPanel.activeSelf : false)}");
        }

        ApplyHudViewMode();
        RefreshProceedInteractable();
        RefreshBottomHintText();
    }

    private void ForceParentsOn()
    {
        if (mainPlayRoot != null) mainPlayRoot.SetActive(true);
        if (centralWindowRoot != null) centralWindowRoot.SetActive(true);
    }

    private void RefreshProceedInteractable()
    {
        if (bottomPanelButton == null) return;

        // Resolveは常に進める（スキップ or 次へ）
        if (_viewMode == ViewMode.ResolveOnly)
        {
            bottomPanelButton.interactable = true;
            return;
        }

        // AdjustはdraftがOKなら進める
        bottomPanelButton.interactable =
            (adjustView == null) || adjustView.CanProceedToNextRound();
    }

    private void OnDestroy()
    {
        if (adjustView != null)
            adjustView.OnDraftChanged -= HandleAdjustChanged;

        if (tutorial != null)
                tutorial.OnStepChanged -= HandleTutorialStepChanged;
    }

    private void RefreshResolveRightWindow()
    {
        if (presenter == null || presenter.Run == null) return;

        var run = presenter.Run;
        var prof = run.PlayerProfile;

        if (resolveDeckText != null)
            resolveDeckText.text = $"デッキ：{prof.Gu}/{prof.Choki}/{prof.Pa}";

        if (resolvePointsText != null)
            resolvePointsText.text = $"x{run.Points}";
    }

    private void ToggleHudDetail()
    {
        _hudDetail = !_hudDetail;

        ApplyHudViewMode();
        RefreshDetailHint();

        if (presenter != null)
            presenter.RefreshHud();

        // ★チュートリアル：左ウィンドウを開けたら次へ（Element16）
        if (tutorial != null && tutorial.IsCurrentStep(TutorialOverlayView.StepId.EnemyInfo))
            {
                // 「確認してみましょう」なので、開いた瞬間に進めるのが自然
                // （閉じた時に進むのは変なので、開いた時だけ）
                if (_hudDetail)
                    tutorial.NotifyExternalAction();
            }

        // ★LayoutRebuildではなく「高さフィット」を呼ぶ
        StartCoroutine(RefitLeftWindowHeightNextFrame());
    }



    private void ApplyHudViewMode()
    {
        if (hud == null) return;
        hud.SetViewMode(_hudDetail ? GameHudView.HudViewMode.Detail : GameHudView.HudViewMode.Compact);
    }

    private System.Collections.IEnumerator RefitLeftWindowHeightNextFrame()
    {
        yield return null; // TMPのpreferred更新待ち

        if (leftWindowRect == null || enemyInfoText == null) yield break;

        // preferredHeightを確実に更新
        enemyInfoText.ForceMeshUpdate();

        float h = enemyInfoText.preferredHeight + leftWindowPaddingY;
        if (h < leftWindowMinHeight) h = leftWindowMinHeight;

        leftWindowRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);
    }

    private IEnumerator FitLeftWindowNextFrame()
    {
        // 1回目：テキスト更新直後の確定待ち
        yield return null;
        FitLeftWindowOnce();

        // 2回目：TMP/レイアウトが1フレ遅れて反映されるケースの保険
        yield return null;
        FitLeftWindowOnce();
    }

    private void FitLeftWindowOnce()
    {
        if (leftWindowRect == null || enemyInfoText == null) return;

        float w = leftWindowRect.rect.width;
        if (w <= 1f) return;

        float availableWidth = w - leftWindowPaddingY * 2f;
        if (availableWidth < 10f) availableWidth = 10f;

        Vector2 pref = enemyInfoText.GetPreferredValues(enemyInfoText.text, availableWidth, 0f);

        float h = pref.y + leftWindowPaddingY * 2f;
        if (h < leftWindowMinHeight) h = leftWindowMinHeight;

        leftWindowRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);
    }

    private void RefreshDetailHint()
    {
        if (detailHintText == null) return;
        detailHintText.text = _hudDetail ? hintCloseDetail : hintShowDetail;
    }

    private void HandleTutorialFinished()
    {
        // ★チュートリアルで触った「予約ドラフト」だけ消す
        if (adjustView != null)
            adjustView.ClearDraftForcedOrder();

        // 右の予約スロット表示も即更新
        if (reservedSlotsView != null && adjustView != null)
            reservedSlotsView.Render(adjustView.GetDraftForcedOrder());

        // HUDも更新
        if (presenter != null)
            presenter.RefreshHud();
    }

    private void HandleTutorialStepChanged(TutorialOverlayView.Step step)
    {
        SyncTutorialState();

        if (_tutorialResetDone) return;
        if (step == null) return;

        // ★本命：このStepIdに到達した瞬間に戻す（あなたの実際のIDに合わせる）
        bool isStartGiftStep = (step.id == TutorialOverlayView.StepId.StartGift);

        // ★保険：ID付け間違い/ズレでも文言で拾う（「10コイン」「始めましょう」）
        bool looksLikeStartGiftText =
            !string.IsNullOrEmpty(step.text) &&
            (step.text.Contains("10コイン") || step.text.Contains("プレゼント") || step.text.Contains("始めましょう"));

        if (!isStartGiftStep && !looksLikeStartGiftText) return;

        _tutorialResetDone = true;

        if (adjustView != null)
            adjustView.ResetTutorialGaugeAndReservation();

        if (reservedSlotsView != null && adjustView != null)
            reservedSlotsView.Render(adjustView.GetDraftForcedOrder());


        RefreshBottomHintText();

        if (debugLog)
            Log("[Tutorial] Reset gaugeBuy + forcedOrder at StartGift/Start text step");
    }

    private void RefreshBottomHintText()
    {
        if (bottomHintText == null) return;

        if (tutorial != null && tutorial.IsOpen)
            bottomHintText.text = HintTutorial;
        else
            bottomHintText.text = HintNextBattle;
    }

    private void SyncTutorialState()
    {
        RefreshBottomHintText(); // ついでに下パネルも同期（既存関数流用）
    }















    private void Log(string msg) => Debug.Log($"[RoundFlowUI] {msg}");
    private void LogWarn(string msg) => Debug.LogWarning($"[RoundFlowUI] {msg}");
    private void LogError(string msg) => Debug.LogError($"[RoundFlowUI] {msg}");
}
