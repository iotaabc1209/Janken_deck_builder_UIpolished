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
    [SerializeField] private TMP_Text bottomHintText; // 「次のページへ / 次の勝負へ」はここ（戻す）

    [Header("Buttons")]
    [SerializeField] private Button resolveTapCatcherButton;
    [SerializeField] private Button openAdjustButton; // 右窓の「調整」ボタン
    [SerializeField] private Button hudDetailToggleButton; // 左ウィンドウのクリック領域

    private bool _hudDetail = false; // 既定：畳む

    [Header("Left Window Hint")]
    [SerializeField] private TMP_Text detailHintText;
    [SerializeField] private string hintShowDetail = "▼ 詳細を見る";
    [SerializeField] private string hintCloseDetail = "▲ 閉じる";

    [Header("SafeRect Parent")]
    [SerializeField] private GameObject mainPlayRoot;
    [SerializeField] private GameObject centralWindowRoot;

    [Header("SafeRect Children")]
    [SerializeField] private GameObject resolveRoot;
    [SerializeField] private GameObject adjustPanel;
    [SerializeField] private GameObject rightWindowResolveRoot;
    [SerializeField] private GameObject rightWindowAdjustRoot;

    [Header("Left Window Auto Size")]
    [SerializeField] private RectTransform leftWindowRect;
    [SerializeField] private TMP_Text enemyInfoText;
    [SerializeField] private float leftWindowPaddingY = 32f;
    [SerializeField] private float leftWindowMinHeight = 0f;

    [Header("Resolve Right Window (Optional)")]
    [SerializeField] private TMP_Text resolveDeckText;
    [SerializeField] private TMP_Text resolvePointsText;

    [Header("Right Window HUD Raycast Block")]
    [SerializeField] private Button[] rightHudGaugeButtons;

    [Header("Views")]
    [SerializeField] private AdjustPanelView adjustView;
    [SerializeField] private GameHudView hud;
    [SerializeField] private TutorialOverlayView tutorial;
    [SerializeField] private ReservedSlotsView reservedSlotsView;

    [Header("Debug")]
    [SerializeField] private bool debugLog = true;

    private ViewMode _viewMode = ViewMode.ResolveOnly;
    private bool _booted;
    private bool _tutorialResetDone = false;
    // チュートリアル開始直後に7手を出す（Introラウンド）を1回だけ実行するガード
    private bool _tutorialIntroHandsShown = false;

    // DeckAdjust step中は「右の調整ボタンだけ押してね」モード
    private bool _deckAdjustClickOnly = false;

    private const string HintTutorial = "次のページへ";
    private const string HintNextBattle = "次の勝負へ";

    private Coroutine _fitLeftWindowCo;


    private void Reset()
    {
        presenter = FindFirstObjectByType<RunPresenter>();
    }

    private void Awake()
    {
        ForceParentsOn();

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
            adjustView.OnDraftChanged -= HandleAdjustChanged;
            adjustView.OnDraftChanged += HandleAdjustChanged;
        }

        if (hudDetailToggleButton != null)
        {
            hudDetailToggleButton.onClick.RemoveListener(ToggleHudDetail);
            hudDetailToggleButton.onClick.AddListener(ToggleHudDetail);
        }

        ApplyHudViewMode();
        RefreshDetailHint();
    }

    private void Start()
    {
        if (tutorial != null)
        {
            tutorial.OnStepChanged -= HandleTutorialStepChanged;
            tutorial.OnStepChanged += HandleTutorialStepChanged;

            tutorial.OnFinished -= HandleTutorialFinished;
            tutorial.OnFinished += HandleTutorialFinished;
        }

        SyncTutorialState();
        SetViewMode(ViewMode.ResolveOnly, "Start");
        Invoke(nameof(BootNextFrame), 0f);
    }

    private void BootNextFrame()
    {
        if (_booted) return;
        _booted = true;

        if (presenter == null)
        {
            LogError("presenter is NULL. Assign it in Inspector or ensure Reset() finds it.");
            return;
        }

        if (presenter.Run == null)
        {
            LogWarn("presenter.Run is NULL. Check RunPresenter.ResetRun() logs.");
            return;
        }

        // まずUIを整える
        RequestFitLeftWindow();

        // 初回ならチュートリアル起動（あなたの SessionGate 運用を尊重）
        bool shouldTutorial = (tutorial != null && TutorialSessionGate.TryConsume());
        if (shouldTutorial)
        {
            tutorial.OpenFromStart();
            // チュートリアル中はRoundLogを空固定（被り防止）
            if (hud != null) hud.SetRoundLog("");
            RefreshBottomHintText();
            ApplyTutorialGates(); // Stepに応じたクリック制御を反映
            return; // ★Intro(7手)は StepId側で出す
        }

        // チュートリアル無し：Introを1回回して「開始」表示を作る
        presenter.PlayIntroIfNeeded();
        presenter.RefreshHud();
        RefreshResolveRightWindow();
        RefreshProceedInteractable();
        RefreshBottomHintText();
    }

    // -------------------------
    // Handlers
    // -------------------------

    private void OnProceed()
    {
        // ★チュートリアル中は下パネル進行を禁止（連打事故防止）
        if (tutorial != null && tutorial.IsOpen) return;

        if (presenter == null) { LogWarn("OnProceed: presenter NULL"); return; }
        if (presenter.Run == null) { LogWarn("OnProceed: run NULL"); return; }

        // GameOver最優先
        if (presenter.Run.IsGameOver)
        {
            SceneFlow.GoToResult(presenter.Run.Score);
            return;
        }

        bool adjustActuallyOpen =
            (adjustPanel != null && adjustPanel.activeSelf) ||
            (_viewMode == ViewMode.WithAdjust);

        // Resolve中：スキップ→できなければ次ラウンド
        if (!adjustActuallyOpen)
        {
            if (presenter.TrySkipResolveSequence())
                return;

            presenter.PlayNextRound();
            presenter.RefreshHud();
            RefreshResolveRightWindow();
            RefreshProceedInteractable();
            return;
        }

        // Adjust中：確定できないなら止める
        if (adjustView != null && !adjustView.TryCommitDraft())
        {
            RefreshProceedInteractable();
            return;
        }

        // Adjustを閉じて次ラウンド
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

        // ResolveTapで閉じる対象はAdjustのみ
        if (_viewMode == ViewMode.WithAdjust)
        {
            SetViewMode(ViewMode.ResolveOnly, "ResolveTap:CloseAdjust");
            RefreshProceedInteractable();
        }
    }

    public void OpenAdjust()
    {
        if (debugLog) Log("OpenAdjust");

        if (presenter == null) { LogWarn("OpenAdjust: presenter NULL"); return; }
        if (presenter.Run == null) { LogWarn("OpenAdjust: run NULL"); return; }

        SetViewMode(ViewMode.WithAdjust, "OpenAdjust");

        if (adjustPanel != null) adjustPanel.SetActive(true);
        if (adjustView != null)
        {
            adjustView.gameObject.SetActive(true);
            adjustView.Open(presenter);
        }

        ApplyHudViewMode();

        if (reservedSlotsView != null && adjustView != null)
            reservedSlotsView.Render(adjustView.GetDraftForcedOrder());

        // ---- チュートリアル：DeckAdjust stepなら「調整モードに移れた」＝次へ ----
        if (tutorial != null && tutorial.IsOpen && tutorial.IsCurrentStep(TutorialOverlayView.StepId.DeckAdjust))
        {
            _deckAdjustClickOnly = false;
            ApplyTutorialGates(); // gating解除
            tutorial.Next();      // ★外部操作が完了したので進める
        }

        RefreshProceedInteractable();
    }

    private void HandleAdjustChanged()
    {
        if (_viewMode != ViewMode.WithAdjust) return;

        RefreshProceedInteractable();

        if (reservedSlotsView != null && adjustView != null)
            reservedSlotsView.Render(adjustView.GetDraftForcedOrder());

        // ---- チュートリアル：ForcedDraw step（グー予約2枚で進行） ----
        if (tutorial != null && tutorial.IsOpen && tutorial.IsCurrentStep(TutorialOverlayView.StepId.ForcedDraw))
        {
            if (adjustView != null)
            {
                var order = adjustView.GetDraftForcedOrder();
                int guCount = 0;

                if (order != null)
                {
                    for (int i = 0; i < order.Count; i++)
                        if (order[i] == RpsColor.Gu) guCount++;
                }

                if (guCount >= 2)
                    tutorial.NotifyExternalAction(); // ここで次へ
            }
        }
    }

    private void ToggleHudDetail()
    {
        _hudDetail = !_hudDetail;

        ApplyHudViewMode();
        RefreshDetailHint();

        if (presenter != null)
            presenter.RefreshHud();

        if (tutorial != null && tutorial.IsOpen && tutorial.IsCurrentStep(TutorialOverlayView.StepId.EnemyInfo))
        {
            if (_hudDetail)
                tutorial.NotifyExternalAction();
        }

        // ★ここを競合しにくい呼び方に
        RequestFitLeftWindow();
    }


    // -------------------------
    // Tutorial step handler
    // -------------------------

    private void HandleTutorialStepChanged(TutorialOverlayView.Step step)
    {
        // チュートリアル中はRoundLog空固定（被り防止）
        if (hud != null) hud.SetRoundLog("");

        ApplyTutorialGates();

        if (step == null) return;

        // ★Intro0到達時に「7手を出す」：チュートリアル開始直後に出したい要件
        if (step.id == TutorialOverlayView.StepId.Intro0)
        {
            ShowIntroHandsOnceForTutorial();
            return;
        }

        // Reveal7Hands に来ても、Intro0で出しているので基本何もしない（保険だけ）
        if (step.id == TutorialOverlayView.StepId.Reveal7Hands)
        {
            ShowIntroHandsOnceForTutorial();
            return;
        }

        // DeckAdjust では「右の調整ボタンだけ押して」モードにする
        if (step.id == TutorialOverlayView.StepId.DeckAdjust)
        {
            EnterDeckAdjustClickOnlyMode();
            return;
        }

        // ★StartGiftに入った瞬間に「予約/ゲージ購入ドラフト」を強制的に戻す（名残を復活）
        if (!_tutorialResetDone && step.id == TutorialOverlayView.StepId.StartGift)
        {
                _tutorialResetDone = true;

                if (adjustView != null)
                    adjustView.ResetTutorialGaugeAndReservation(); // ★これが「買った分/予約した分」をドラフト側でゼロに戻す

                if (reservedSlotsView != null && adjustView != null)
                    reservedSlotsView.Render(adjustView.GetDraftForcedOrder());

                // チュートリアル中はRoundLogは空固定（被り防止）
                if (presenter != null)
                    presenter.SetRoundLog("");

                if (debugLog) Log("[Tutorial] Reset draft gaugeBuy + forcedOrder at StartGift");
                return;
        }

        // ForcedDraw / EnemyInfo は wait step なので、tapCatcher側が勝手に止める
        // ここでは gating だけ整える
    }

    private void ShowIntroHandsOnceForTutorial()
    {
        if (_tutorialIntroHandsShown) return;
        _tutorialIntroHandsShown = true;

        if (presenter == null || presenter.Run == null) return;

        // Resolve表示に揃える
        SetViewMode(ViewMode.ResolveOnly, "Tutorial:ShowIntroHands");

        presenter.PlayIntroIfNeeded(); // Round0を生成して7手を出す
        presenter.RefreshHud();
        RefreshResolveRightWindow();
        RefreshProceedInteractable();
        RequestFitLeftWindow();
    }

    private void EnterDeckAdjustClickOnlyMode()
    {
        _deckAdjustClickOnly = true;

        // Resolve表示で「調整ボタン」を押させる
        SetViewMode(ViewMode.ResolveOnly, "Tutorial:DeckAdjust");

        // Adjustが開いてたら閉じる（誤爆防止）
        if (adjustPanel != null) adjustPanel.SetActive(false);

        ApplyTutorialGates();
        RefreshProceedInteractable();
    }

    private void ApplyTutorialGates()
    {
        bool tut = (tutorial != null && tutorial.IsOpen);

        // チュートリアル中は下パネル進行禁止が基本
        if (!tut)
        {
            _deckAdjustClickOnly = false;
            SetButtonRaycast(bottomPanelButton, true);
            SetButtonRaycast(resolveTapCatcherButton, true);
            SetButtonRaycast(hudDetailToggleButton, true);
            SetButtonRaycast(openAdjustButton, true);
            RefreshBottomHintText();
            return;
        }

        RefreshBottomHintText();

        // デフォルト：チュートリアル中は下パネル無効（連打事故防止）
        SetButtonRaycast(bottomPanelButton, false);

        // ResolveTapは基本無効（誤タップで閉じたりしない）
        SetButtonRaycast(resolveTapCatcherButton, false);

        // 左の開閉は、EnemyInfo stepのときだけ有効にする
        bool enemyInfo = tutorial.IsCurrentStep(TutorialOverlayView.StepId.EnemyInfo);
        SetButtonRaycast(hudDetailToggleButton, enemyInfo);

        // DeckAdjust step：右の調整ボタンだけ有効
        if (_deckAdjustClickOnly || tutorial.IsCurrentStep(TutorialOverlayView.StepId.DeckAdjust))
        {
            SetButtonRaycast(openAdjustButton, true);

            // 右HUDのゲージ等は触らせない（今は「調整ボタン」だけ）
            BlockRightHudGaugeRaycast(block: true);
            return;
        }

        // ForcedDraw step：Adjustが開いている前提で予約を触らせたいので、調整ボタンは状況次第
        bool forcedDraw = tutorial.IsCurrentStep(TutorialOverlayView.StepId.ForcedDraw);
        if (forcedDraw)
        {
            // Adjustに入る導線としては openAdjustButton は有効でOK
            SetButtonRaycast(openAdjustButton, true);
            BlockRightHudGaugeRaycast(block: false); // Adjust側UIを触るならここは妨げない
            return;
        }

        // それ以外：調整ボタンは無効でもいいが、ゲーム理解のために触られて困るものがなければ有効でもOK
        SetButtonRaycast(openAdjustButton, true);
        BlockRightHudGaugeRaycast(block: true);
    }

    private void BlockRightHudGaugeRaycast(bool block)
    {
        // block=true なら、右HUDのゲージ系はraycastを取らない
        if (rightHudGaugeButtons == null) return;

        for (int i = 0; i < rightHudGaugeButtons.Length; i++)
        {
            var b = rightHudGaugeButtons[i];
            if (b == null) continue;

            var graphics = b.GetComponentsInChildren<Graphic>(true);
            for (int j = 0; j < graphics.Length; j++)
                graphics[j].raycastTarget = !block;
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
        bool showAdjust = (m == ViewMode.WithAdjust);

        if (debugLog)
        {
            Log($"SetViewMode ENTER {m} reason={reason} " +
                $"resolve={(resolveRoot ? resolveRoot.activeSelf : false)} " +
                $"adjust={(adjustPanel ? adjustPanel.activeSelf : false)}");
        }

        if (resolveRoot != null) resolveRoot.SetActive(showResolve);
        if (adjustPanel != null) adjustPanel.SetActive(showAdjust);

        if (rightWindowResolveRoot != null) rightWindowResolveRoot.SetActive(showResolve);
        if (rightWindowAdjustRoot != null) rightWindowAdjustRoot.SetActive(showAdjust);

        if (resolveTapCatcherButton != null)
            resolveTapCatcherButton.gameObject.SetActive(showResolve);

        // Resolve中だけ「調整ボタン」押せる
        if (openAdjustButton != null)
            openAdjustButton.interactable = showResolve;

        if (showResolve)
            RefreshResolveRightWindow();

        // ★絶対に両方OFFにしない保険
        bool resolveOn = (resolveRoot != null && resolveRoot.activeSelf);
        bool adjustOn = (adjustPanel != null && adjustPanel.activeSelf);
        if (!resolveOn && !adjustOn)
        {
            LogError($"Both resolveRoot & adjustPanel are inactive! Force ResolveOnly. reason={reason}");
            if (resolveRoot != null) resolveRoot.SetActive(true);
            _viewMode = ViewMode.ResolveOnly;
        }

        // Resolve中は右HUDゲージの誤爆を防ぐ（あなたの既存方針）
        bool resolve = (m == ViewMode.ResolveOnly);
        if (rightHudGaugeButtons != null)
        {
            for (int i = 0; i < rightHudGaugeButtons.Length; i++)
            {
                var b = rightHudGaugeButtons[i];
                if (b == null) continue;

                var graphics = b.GetComponentsInChildren<Graphic>(true);
                for (int j = 0; j < graphics.Length; j++)
                    graphics[j].raycastTarget = !resolve;
            }
        }

        ApplyHudViewMode();
        RefreshProceedInteractable();
        RefreshBottomHintText();

        if (debugLog)
        {
            Log($"SetViewMode EXIT mode={_viewMode} " +
                $"resolve={(resolveRoot ? resolveRoot.activeSelf : false)} " +
                $"adjust={(adjustPanel ? adjustPanel.activeSelf : false)}");
        }
    }

    private void ForceParentsOn()
    {
        if (mainPlayRoot != null) mainPlayRoot.SetActive(true);
        if (centralWindowRoot != null) centralWindowRoot.SetActive(true);
    }

    private void RefreshProceedInteractable()
    {
        if (bottomPanelButton == null) return;

        // チュートリアル中は原則 false（ApplyTutorialGatesが管理）
        if (tutorial != null && tutorial.IsOpen)
        {
            bottomPanelButton.interactable = false;
            return;
        }

        // Resolveは常に進める
        if (_viewMode == ViewMode.ResolveOnly)
        {
            bottomPanelButton.interactable = true;
            return;
        }

        // AdjustはdraftがOKなら進める
        bottomPanelButton.interactable = (adjustView == null) || adjustView.CanProceedToNextRound();
    }

    private void OnDestroy()
    {
        if (adjustView != null)
            adjustView.OnDraftChanged -= HandleAdjustChanged;

        if (tutorial != null)
        {
            tutorial.OnStepChanged -= HandleTutorialStepChanged;
            tutorial.OnFinished -= HandleTutorialFinished;
        }
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

    private void ApplyHudViewMode()
    {
        if (hud == null) return;
        hud.SetViewMode(_hudDetail ? GameHudView.HudViewMode.Detail : GameHudView.HudViewMode.Compact);
    }


    private IEnumerator FitLeftWindowNextFrame()
    {
        // 多重起動の競合を避けたい場合は、呼び出し側で Stop する（後述）
        yield return null; // 1回目：TMP更新待ち
        FitLeftWindowOnce();

        yield return null; // 2回目：レイアウト遅延保険
        FitLeftWindowOnce();
    }

    private void FitLeftWindowOnce()
    {
        if (leftWindowRect == null || enemyInfoText == null) return;

        // TMP更新
        enemyInfoText.ForceMeshUpdate();

        float w = leftWindowRect.rect.width;
        if (w <= 1f) return;

        float availableWidth = w - leftWindowPaddingY * 2f;
        if (availableWidth < 10f) availableWidth = 10f;

        Vector2 pref = enemyInfoText.GetPreferredValues(enemyInfoText.text, availableWidth, 0f);

        float h = pref.y + leftWindowPaddingY * 2f;
        if (h < leftWindowMinHeight) h = leftWindowMinHeight;

        leftWindowRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);
    }




    private void RequestFitLeftWindow()
    {
        if (_fitLeftWindowCo != null)
            StopCoroutine(_fitLeftWindowCo);
        _fitLeftWindowCo = StartCoroutine(FitLeftWindowNextFrame());
    }


    private void RefreshDetailHint()
    {
        if (detailHintText == null) return;

        if (!detailHintText.gameObject.activeSelf)
            detailHintText.gameObject.SetActive(true);

        detailHintText.text = _hudDetail ? hintCloseDetail : hintShowDetail;
        detailHintText.ForceMeshUpdate();
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
        RefreshBottomHintText();
    }


    private void HandleTutorialFinished()
    {
        // チュートリアル終了直後は、まずドラフトの残骸を消す（安全側）
        if (adjustView != null)
            adjustView.ResetTutorialGaugeAndReservation();

        if (reservedSlotsView != null && adjustView != null)
            reservedSlotsView.Render(adjustView.GetDraftForcedOrder());


        // ★重要：tutorial.IsOpen==false になった後の入力ゲートを通常に戻す
        ApplyTutorialGates();            // ←これ追加（not tut 分岐で raycastTarget が復活する）
        RefreshProceedInteractable();    // ←保険
        // RoundLog はチュートリアル中ずっと空だったはずなので、ここから演出してOK
        StartCoroutine(TutorialExitToAdjustFlow());
    }

    private IEnumerator TutorialExitToAdjustFlow()
    {
        // 1) 準備中…（HUD_RoundLog側に出す）
        if (presenter != null)
        {
            presenter.SetRoundLog("準備中…");
            presenter.RefreshHud();
        }

        // 2) 0.3秒だけProceed無効（連打で戦闘突入を防ぐ）
        if (bottomPanelButton != null) bottomPanelButton.interactable = false;
        yield return new WaitForSecondsRealtime(0.3f);

        // 3) 調整を促すログを出して、Adjustを開く
        if (presenter != null)
        {
            presenter.SetRoundLog("デッキを調整してください");
            presenter.RefreshHud();
        }

        // ★Adjustを開く（RightWindow_Adjustを出す）
        OpenAdjust();

        // OpenAdjust後に通常の可否へ戻す
        RefreshProceedInteractable();
    }


    // -------------------------
    // Utility: Raycast gating
    // -------------------------

    private static void SetButtonRaycast(Button b, bool enabled)
    {
        if (b == null) return;

        b.interactable = enabled;

        var graphics = b.GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < graphics.Length; i++)
            graphics[i].raycastTarget = enabled;
    }

    // -------------------------
    // Logs
    // -------------------------

    private void Log(string msg) => Debug.Log($"[RoundFlowUI] {msg}");
    private void LogWarn(string msg) => Debug.LogWarning($"[RoundFlowUI] {msg}");
    private void LogError(string msg) => Debug.LogError($"[RoundFlowUI] {msg}");
}
