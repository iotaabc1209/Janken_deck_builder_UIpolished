using UnityEngine;
using UnityEngine.UI;
using TMPro;
using RpsBuild.Core;

public sealed class AdjustPanelView : MonoBehaviour
{
    [Header("Core")]
    [SerializeField] private RunPresenter presenter;

    [Header("Texts")]
    [SerializeField] private TMP_Text pointsText;
    [SerializeField] private TMP_Text deckText;
    [SerializeField] private TMP_Text playerArchetypeText;

    [Header("Deck Adjust (Draft) : +1 / -1 per color")]
    [SerializeField] private Button deckPlusGu;
    [SerializeField] private Button deckMinusGu;
    [SerializeField] private Button deckPlusChoki;
    [SerializeField] private Button deckMinusChoki;
    [SerializeField] private Button deckPlusPa;
    [SerializeField] private Button deckMinusPa;

    [Header("Deck Column Counts (shown)")]
    [SerializeField] private TMP_Text countGuText;
    [SerializeField] private TMP_Text countChokiText;
    [SerializeField] private TMP_Text countPaText;

    [Header("Buy Gauge (Draft) : +1 / -1 per color")]
    [SerializeField] private Button buyGaugePlusGu;
    [SerializeField] private Button buyGaugeMinusGu;
    [SerializeField] private Button buyGaugePlusChoki;
    [SerializeField] private Button buyGaugeMinusChoki;
    [SerializeField] private Button buyGaugePlusPa;
    [SerializeField] private Button buyGaugeMinusPa;

    [SerializeField] private GaugeBarView gaugeBarView;
    [SerializeField] private Button closeButton;

    [SerializeField] private bool logValidation = false;


    private bool _bound = false;

    // Draft: deck +/- per color
    private readonly int[] _add = new int[3];
    private readonly int[] _sub = new int[3];

    // Draft: gauge buy count per color
    private readonly int[] _gaugeBuy = new int[3];

    // Draft: forced draw order
    private readonly System.Collections.Generic.List<RpsColor> _draftForcedOrder = new();

    // Budget fixed at Adjust open
    private int _pointsBudget = 0;

    // Change notifications
    public System.Action OnDraftChanged;
    public System.Action OnCloseRequested;

    // “ドラフト内容が変わった”判定用（Refreshでは変えない）
    private int _draftVersion = 0;
    private int _lastNotifiedDraftVersion = -1;

    public void Open(RunPresenter p)
    {
        gameObject.SetActive(true);

        presenter = p;
        BindButtonsOnce();

        if (presenter == null || presenter.Run == null)
        {
            if (logValidation) Debug.LogWarning("[AdjustPanelView] Open: presenter/run null");
            return;
        }

        _pointsBudget = presenter.Run.Points;

        var prof = presenter.Run.PlayerProfile;
        if (deckText != null)
            deckText.text = $"デッキ：グー/チョキ/パー： {prof.Gu}/{prof.Choki}/{prof.Pa}";

        ResetDraft();                // Adjustに入るたび初期化（方針通り）
        _draftForcedOrder.Clear();   // 保険（ResetDraftでも消してるがOK）

        Refresh();
        NotifyDraftChangedIfNeeded(); // Open直後に一回だけ通知したいなら有効
    }

    public void Refresh()
    {
        if (presenter == null || presenter.Run == null) return;

        var run = presenter.Run;
        var shown = GetShownProfile(run.PlayerProfile);
        bool okTotal = (shown.Total == 30);   // ★これを追加

        // ---- counts ----
        if (countGuText != null)    countGuText.text = $"{shown.Gu}枚";
        if (countChokiText != null) countChokiText.text = $"{shown.Choki}枚";
        if (countPaText != null)    countPaText.text = $"{shown.Pa}枚";

        var col = okTotal
            ? new Color32(0x10, 0x21, 0x2F, 0xFF)   // #10212F（通常）
            : new Color32(0xE3, 0x58, 0x58, 0xFF); // エラー



        if (countGuText != null)    countGuText.color = col;
        if (countChokiText != null) countChokiText.color = col;
        if (countPaText != null)    countPaText.color = col;

        // ---- archetype label ----
        if (playerArchetypeText != null)
        {
            playerArchetypeText.color = col;

            if (!okTotal)
            {
                playerArchetypeText.text = "自分：—（合計30枚で確定）";
            }
            else
            {
                var info = PlayerArchetypeClassifier.Classify(shown);
                playerArchetypeText.text = $"自分：{info.ToJaLabel()}";
            }
        }

        // ---- points ----
        if (pointsText != null)
            pointsText.text = $"　　x{PointsLeft()} / {_pointsBudget}";

        // ---- buttons interactable ----
        bool canAdd = CanAdd(run);
        bool canSub = CanSub(run);

        bool canCancelPlusGu    = _sub[(int)RpsColor.Gu] > 0;
        bool canCancelPlusChoki = _sub[(int)RpsColor.Choki] > 0;
        bool canCancelPlusPa    = _sub[(int)RpsColor.Pa] > 0;

        bool canCancelMinusGu    = _add[(int)RpsColor.Gu] > 0;
        bool canCancelMinusChoki = _add[(int)RpsColor.Choki] > 0;
        bool canCancelMinusPa    = _add[(int)RpsColor.Pa] > 0;

        if (deckPlusGu != null)    deckPlusGu.interactable = canAdd || canCancelPlusGu;
        if (deckPlusChoki != null) deckPlusChoki.interactable = canAdd || canCancelPlusChoki;
        if (deckPlusPa != null)    deckPlusPa.interactable = canAdd || canCancelPlusPa;

        if (deckMinusGu != null)    deckMinusGu.interactable = (canSub && shown.Gu > 0) || canCancelMinusGu;
        if (deckMinusChoki != null) deckMinusChoki.interactable = (canSub && shown.Choki > 0) || canCancelMinusChoki;
        if (deckMinusPa != null)    deckMinusPa.interactable = (canSub && shown.Pa > 0) || canCancelMinusPa;

        bool canGaugeAdd = CanGaugeAdd(run);

        if (buyGaugePlusGu != null)    buyGaugePlusGu.interactable    = canGaugeAdd;
        if (buyGaugePlusChoki != null) buyGaugePlusChoki.interactable = canGaugeAdd;
        if (buyGaugePlusPa != null)    buyGaugePlusPa.interactable    = canGaugeAdd;

        if (buyGaugeMinusGu != null)    buyGaugeMinusGu.interactable    = CanGaugeActuallyDecrease(RpsColor.Gu);
        if (buyGaugeMinusChoki != null) buyGaugeMinusChoki.interactable = CanGaugeActuallyDecrease(RpsColor.Choki);
        if (buyGaugeMinusPa != null)    buyGaugeMinusPa.interactable    = CanGaugeActuallyDecrease(RpsColor.Pa);

        // ---- forced order safety & gauge preview ----
        ValidateDraftForcedOrder();
        RefreshGaugePreview();
    }

    // RoundFlowUI が「次ラウンド開始」できるか
    public bool CanProceedToNextRound()
    {
        if (presenter == null || presenter.Run == null)
        {
            if (logValidation) Debug.Log("[AdjustPanelView] CanProceed blocked: presenter/run null");
            return false;
        }

        ValidateDraftForcedOrder();

        var run = presenter.Run;
        var shown = GetShownProfile(run.PlayerProfile);

        if (shown.Total != 30)
        {
            if (logValidation) Debug.Log($"[AdjustPanelView] CanProceed blocked: deck total {shown.Total} != 30");
            return false;
        }

        int cost = DraftCost();
        if (cost > _pointsBudget)
        {
            if (logValidation) Debug.Log($"[AdjustPanelView] CanProceed blocked: cost {cost} > budget {_pointsBudget}");
            return false;
        }

        if (_draftForcedOrder.Count > run.Tuning.HandCount)
        {
            if (logValidation) Debug.Log($"[AdjustPanelView] CanProceed blocked: forced {_draftForcedOrder.Count} > hand {run.Tuning.HandCount}");
            return false;
        }

        if (_draftForcedOrder.Count > 0)
        {
            // デッキに無い色が混ざってたらNG（Validateで消えるはずだが保険）
            for (int i = 0; i < _draftForcedOrder.Count; i++)
            {
                var c = _draftForcedOrder[i];
                if (!shown.Has(c))
                {
                    if (logValidation) Debug.Log($"[AdjustPanelView] CanProceed blocked: deck has no {c}");
                    return false;
                }
            }

            int maxGu = GetShownChargedCount(RpsColor.Gu, run);
            int maxCh = GetShownChargedCount(RpsColor.Choki, run);
            int maxPa = GetShownChargedCount(RpsColor.Pa, run);

            int nGu = GetDraftForcedCount(RpsColor.Gu);
            int nCh = GetDraftForcedCount(RpsColor.Choki);
            int nPa = GetDraftForcedCount(RpsColor.Pa);

            if (nGu > maxGu) return false;
            if (nCh > maxCh) return false;
            if (nPa > maxPa) return false;
        }

        return true;
    }

    // RoundFlowUI が「次ラウンド開始」直前に呼ぶ（確定）
    public bool TryCommitDraft()
    {
        if (presenter == null || presenter.Run == null) return false;
        var run = presenter.Run;

        var shown = GetShownProfile(run.PlayerProfile);
        if (shown.Total != 30) return false;

        int totalCost = DraftCost();
        if (totalCost > _pointsBudget) return false;

        // 1) デッキ確定（ポイント消費まとめ）
        if (totalCost > 0)
        {
            bool ok = run.TryCommitDeckProfileByPoints(shown, totalCost);
            if (!ok) return false;
        }
        else
        {
            run.SetPlayerProfile(shown);
        }

        // 2) ゲージ確定
        float buy = run.Tuning.GaugeBuyAmount;
        for (int i = 0; i < 3; i++)
        {
            int buyCount = _gaugeBuy[i];
            if (buyCount > 0)
                run.Gauge.Add((RpsColor)i, buyCount * buy);
        }

        // 3) 確定ドロー予約の確定
        run.SetReservedForcedOrder(new System.Collections.Generic.List<RpsColor>(_draftForcedOrder));

        // 4) ドラフトリセット
        ResetDraft();
        _draftForcedOrder.Clear();

        // 5) 表示更新（実ゲージ）
        if (gaugeBarView != null)
        {
            gaugeBarView.SetGauge(
                run.Gauge.Get(RpsColor.Gu),
                run.Gauge.Get(RpsColor.Choki),
                run.Gauge.Get(RpsColor.Pa),
                run.Gauge.Max
            );
        }

        Refresh();
        NotifyDraftChangedIfNeeded();
        return true;
    }

    // -------------------------
    // UI button binding
    // -------------------------

    private void BindButtonsOnce()
    {
        if (_bound) return;
        _bound = true;

        if (deckPlusGu != null)       deckPlusGu.onClick.AddListener(() => OnDeckPlus(RpsColor.Gu));
        if (deckMinusGu != null)      deckMinusGu.onClick.AddListener(() => OnDeckMinus(RpsColor.Gu));
        if (deckPlusChoki != null)    deckPlusChoki.onClick.AddListener(() => OnDeckPlus(RpsColor.Choki));
        if (deckMinusChoki != null)   deckMinusChoki.onClick.AddListener(() => OnDeckMinus(RpsColor.Choki));
        if (deckPlusPa != null)       deckPlusPa.onClick.AddListener(() => OnDeckPlus(RpsColor.Pa));
        if (deckMinusPa != null)      deckMinusPa.onClick.AddListener(() => OnDeckMinus(RpsColor.Pa));

        if (buyGaugePlusGu != null)      buyGaugePlusGu.onClick.AddListener(() => OnBuyGaugePlus(RpsColor.Gu));
        if (buyGaugeMinusGu != null)     buyGaugeMinusGu.onClick.AddListener(() => OnBuyGaugeMinus(RpsColor.Gu));
        if (buyGaugePlusChoki != null)   buyGaugePlusChoki.onClick.AddListener(() => OnBuyGaugePlus(RpsColor.Choki));
        if (buyGaugeMinusChoki != null)  buyGaugeMinusChoki.onClick.AddListener(() => OnBuyGaugeMinus(RpsColor.Choki));
        if (buyGaugePlusPa != null)      buyGaugePlusPa.onClick.AddListener(() => OnBuyGaugePlus(RpsColor.Pa));
        if (buyGaugeMinusPa != null)     buyGaugeMinusPa.onClick.AddListener(() => OnBuyGaugeMinus(RpsColor.Pa));

        if (closeButton != null)
            closeButton.onClick.AddListener(OnCloseClicked);
    }

    private void OnCloseClicked()
    {
        OnCloseRequested?.Invoke();
    }

    // -------------------------
    // Draft operations (ONLY places that change draft)
    // -------------------------

    private void OnDeckPlus(RpsColor c)
    {
        if (presenter == null || presenter.Run == null) return;
        var run = presenter.Run;

        int i = (int)c;

        // cancel
        if (_sub[i] > 0)
        {
            _sub[i] -= 1;
            TouchDraft();
            return;
        }

        if (!CanAdd(run)) return;

        _add[i] += 1;
        TouchDraft();
    }

    private void OnDeckMinus(RpsColor c)
    {
        if (presenter == null || presenter.Run == null) return;
        var run = presenter.Run;

        int i = (int)c;

        // cancel
        if (_add[i] > 0)
        {
            _add[i] -= 1;
            TouchDraft();
            return;
        }

        if (!CanSub(run)) return;

        var shown = GetShownProfile(run.PlayerProfile);
        if (shown.Get(c) <= 0) return;

        _sub[i] += 1;
        TouchDraft();
    }

    private void OnBuyGaugePlus(RpsColor c)
    {
        if (presenter == null || presenter.Run == null) return;
        var run = presenter.Run;

        if (!CanGaugeAdd(run)) return;

        _gaugeBuy[(int)c] += 1;
        TouchDraft();
    }

    private void OnBuyGaugeMinus(RpsColor c)
    {
        int i = (int)c;
        if (_gaugeBuy[i] <= 0) return;

        _gaugeBuy[i] -= 1;
        TouchDraft();
    }

    // 予約のトグル（UIから呼ぶ想定）
    public bool ToggleDraftForcedFirst(RpsColor c)
    {
        if (presenter == null || presenter.Run == null) return false;
        var run = presenter.Run;

        var shownDeck = GetShownProfile(run.PlayerProfile);
        if (!shownDeck.Has(c)) return false;

        int maxByGauge = Mathf.FloorToInt(GetShownGaugeValue_NoClamp(c, run) / run.Gauge.Max);
        int maxByDeck = shownDeck.Get(c);
        int maxByHand = run.Tuning.HandCount;

        int maxCount = Mathf.Min(maxByGauge, maxByDeck, maxByHand);

        if (maxCount <= 0)
        {
            RemoveAllFromDraftOrder(c);
            TouchDraft();
            return true;
        }

        int cur = GetDraftForcedCount(c);

        if (cur < maxCount)
            _draftForcedOrder.Add(c);
        else
            RemoveAllFromDraftOrder(c);

        TouchDraft();
        return true;
    }

    // Draft changed: refresh & notify (only once per change)
    private void TouchDraft()
    {
        _draftVersion++;
        Refresh();
        NotifyDraftChangedIfNeeded();
    }

    private void NotifyDraftChangedIfNeeded()
    {
        if (_lastNotifiedDraftVersion == _draftVersion) return;
        _lastNotifiedDraftVersion = _draftVersion;
        OnDraftChanged?.Invoke();
    }

    // -------------------------
    // Cost / budget
    // -------------------------

    private int DraftCost()
    {
        int deckCost = Mathf.Max(_add[0] + _add[1] + _add[2], _sub[0] + _sub[1] + _sub[2]);
        int gaugeCost = GaugeDraftCost();
        return deckCost + gaugeCost;
    }

    private int GaugeDraftCost()
    {
        return _gaugeBuy[0] + _gaugeBuy[1] + _gaugeBuy[2];
    }

    private int PointsLeft()
    {
        return _pointsBudget - DraftCost();
    }

    private bool CanAdd(RunState run)
    {
        int sumAdd = _add[0] + _add[1] + _add[2];
        int sumSub = _sub[0] + _sub[1] + _sub[2];
        int deckCostAfter = Mathf.Max(sumAdd + 1, sumSub);

        int totalAfter = deckCostAfter + GaugeDraftCost();
        return totalAfter <= _pointsBudget;
    }

    private bool CanSub(RunState run)
    {
        int sumAdd = _add[0] + _add[1] + _add[2];
        int sumSub = _sub[0] + _sub[1] + _sub[2];
        int deckCostAfter = Mathf.Max(sumAdd, sumSub + 1);

        int totalAfter = deckCostAfter + GaugeDraftCost();
        return totalAfter <= _pointsBudget;
    }

    private bool CanGaugeAdd(RunState run)
    {
        int gaugeCostAfter = GaugeDraftCost() + 1;

        int deckCost = Mathf.Max(
            _add[0] + _add[1] + _add[2],
            _sub[0] + _sub[1] + _sub[2]
        );

        return deckCost + gaugeCostAfter <= _pointsBudget;
    }

    // -------------------------
    // Forced order helpers
    // -------------------------

    public int GetDraftForcedCount(RpsColor c)
    {
        int n = 0;
        for (int i = 0; i < _draftForcedOrder.Count; i++)
            if (_draftForcedOrder[i] == c) n++;
        return n;
    }


    private void RemoveAllFromDraftOrder(RpsColor c)
    {
        for (int i = _draftForcedOrder.Count - 1; i >= 0; i--)
            if (_draftForcedOrder[i] == c)
                _draftForcedOrder.RemoveAt(i);
    }

    private void ValidateDraftForcedOrder()
    {
        if (_draftForcedOrder == null || _draftForcedOrder.Count <= 0) return;
        if (presenter == null || presenter.Run == null) { _draftForcedOrder.Clear(); return; }

        var run = presenter.Run;
        var shownDeck = GetShownProfile(run.PlayerProfile);

        int maxGu = Mathf.Min(Mathf.FloorToInt(GetShownGaugeValue_NoClamp(RpsColor.Gu, run) / run.Gauge.Max), shownDeck.Get(RpsColor.Gu), run.Tuning.HandCount);
        int maxCh = Mathf.Min(Mathf.FloorToInt(GetShownGaugeValue_NoClamp(RpsColor.Choki, run) / run.Gauge.Max), shownDeck.Get(RpsColor.Choki), run.Tuning.HandCount);
        int maxPa = Mathf.Min(Mathf.FloorToInt(GetShownGaugeValue_NoClamp(RpsColor.Pa, run) / run.Gauge.Max), shownDeck.Get(RpsColor.Pa), run.Tuning.HandCount);

        for (int i = _draftForcedOrder.Count - 1; i >= 0; i--)
        {
            var c = _draftForcedOrder[i];
            if (!shownDeck.Has(c)) _draftForcedOrder.RemoveAt(i);
        }

        while (GetDraftForcedCount(RpsColor.Gu) > maxGu) RemoveLastOf(RpsColor.Gu);
        while (GetDraftForcedCount(RpsColor.Choki) > maxCh) RemoveLastOf(RpsColor.Choki);
        while (GetDraftForcedCount(RpsColor.Pa) > maxPa) RemoveLastOf(RpsColor.Pa);

        while (_draftForcedOrder.Count > run.Tuning.HandCount)
            _draftForcedOrder.RemoveAt(_draftForcedOrder.Count - 1);
    }

    private void RemoveLastOf(RpsColor c)
    {
        for (int i = _draftForcedOrder.Count - 1; i >= 0; i--)
        {
            if (_draftForcedOrder[i] == c)
            {
                _draftForcedOrder.RemoveAt(i);
                return;
            }
        }
    }

    // -------------------------
    // Gauge preview
    // -------------------------

    private void RefreshGaugePreview()
    {
        if (presenter == null || presenter.Run == null) return;
        var run = presenter.Run;

        float buy = run.Tuning.GaugeBuyAmount;

        float gu = run.Gauge.Get(RpsColor.Gu) + _gaugeBuy[(int)RpsColor.Gu] * buy;
        float ch = run.Gauge.Get(RpsColor.Choki) + _gaugeBuy[(int)RpsColor.Choki] * buy;
        float pa = run.Gauge.Get(RpsColor.Pa) + _gaugeBuy[(int)RpsColor.Pa] * buy;

        gu -= GetDraftForcedCount(RpsColor.Gu) * run.Gauge.Max;
        ch -= GetDraftForcedCount(RpsColor.Choki) * run.Gauge.Max;
        pa -= GetDraftForcedCount(RpsColor.Pa) * run.Gauge.Max;

        if (gu < 0f) gu = 0f;
        if (ch < 0f) ch = 0f;
        if (pa < 0f) pa = 0f;

        if (gaugeBarView != null)
            gaugeBarView.SetGauge(gu, ch, pa, run.Gauge.Max);
    }

    private bool CanGaugeActuallyDecrease(RpsColor c)
    {
        return _gaugeBuy[(int)c] > 0;
    }

    private int GetShownChargedCount(RpsColor c, RunState run)
    {
        float v = GetShownGaugeValue_NoClamp(c, run);
        return (int)System.Math.Floor((v + 1e-6f) / run.Gauge.Max);
    }

    private float GetShownGaugeValue_NoClamp(RpsColor c, RunState run)
    {
        float buy = run.Tuning.GaugeBuyAmount;
        float v = run.Gauge.Get(c) + _gaugeBuy[(int)c] * buy;
        if (v < 0f) v = 0f;
        return v;
    }

    // -------------------------
    // Deck helpers
    // -------------------------

    private DeckProfile GetShownProfile(DeckProfile baseProfile)
    {
        int gu = baseProfile.Gu + _add[(int)RpsColor.Gu] - _sub[(int)RpsColor.Gu];
        int ch = baseProfile.Choki + _add[(int)RpsColor.Choki] - _sub[(int)RpsColor.Choki];
        int pa = baseProfile.Pa + _add[(int)RpsColor.Pa] - _sub[(int)RpsColor.Pa];
        return new DeckProfile(gu, ch, pa);
    }

    private void ResetDraft(bool notify = false)
    {
        for (int i = 0; i < 3; i++)
        {
            _add[i] = 0;
            _sub[i] = 0;
            _gaugeBuy[i] = 0;
        }

        _draftForcedOrder.Clear();
        _draftVersion++;

        if (notify)
            _lastNotifiedDraftVersion = -1;
    }


    public void ClearDraftForcedOrder()
    {
        if (_draftForcedOrder.Count <= 0) return;

        _draftForcedOrder.Clear();
        TouchDraft(); // Refresh + 通知 まで一発でやる（既存関数流用）
    }

    public void ResetTutorialGaugeAndReservation()
    {
        ClearDraftForcedOrder();
        for (int i = 0; i < 3; i++)
            _gaugeBuy[i] = 0;

        TouchDraft();
    }




    private static string ToJpColor(RpsColor c)
    {
        return c switch
        {
            RpsColor.Gu => "グー",
            RpsColor.Choki => "チョキ",
            _ => "パー"
        };
    }

    public System.Collections.Generic.IReadOnlyList<RpsColor> GetDraftForcedOrder()
    {
        return _draftForcedOrder;
    }
}
