// Assets/Scripts/Unity/RunPresenter.cs
using UnityEngine;
using RpsBuild.Core;
using TMPro;

public sealed class RunPresenter : MonoBehaviour
{
    [Header("Config")]
    [SerializeField] private TuningAsset tuningAsset;

    [Header("Initial Player Deck (must total 30)")]
    [SerializeField] private int initialGu = 0;
    [SerializeField] private int initialChoki = 0;
    [SerializeField] private int initialPa = 30;

    [Header("Enemy Generator Tuning")]
    [SerializeField] private int heavyMain = 20;
    [SerializeField] private int heavySub1 = 5;
    [SerializeField] private int heavySub2 = 5;
    [SerializeField] private RpsColor heavyMainColor = RpsColor.Gu;
    [SerializeField] private int balanceEach = 10;
    [SerializeField] private int twinTopA = 14;
    [SerializeField] private int twinTopB = 14;
    [SerializeField] private int twinTopC = 2;

    // Range tuning
    [SerializeField] private bool useRangeTuning = true;
    [SerializeField] private int heavyMainMin = 16;
    [SerializeField] private int heavyMainMax = 24;
    [SerializeField] private int heavySubMin = 0;
    [SerializeField] private int heavySubMax = 14;
    [SerializeField] private int balanceMin = 11;
    [SerializeField] private int balanceMax = 14;
    [SerializeField] private int twinTopMainMin = 12;
    [SerializeField] private int twinTopMainMax = 20;
    [SerializeField] private int twinTopDeltaMin = 1;
    [SerializeField] private int twinTopDeltaMax = 6;
    [SerializeField] private int twinTopCMin = 0;
    [SerializeField] private int twinTopCMax = 10;

    [Header("Views")]
    [SerializeField] private GameHudView hud;
    [SerializeField] private TMP_Text outcomeText;
    [SerializeField] private GaugeBarView gaugeBarView;
    [SerializeField] private ResolveHandsSequenceView handsSequenceView;
    [SerializeField] private ForcedFirstToggleView forcedFirstToggleView;
    [SerializeField] private ResolveOutcomeRowView outcomeRowView;
    [SerializeField] private TutorialOverlayView tutorial;


    [Header("Debug")]
    [SerializeField] private bool logLifecycle = false; // Reset/Startなど
    [SerializeField] private bool logRoundSummary = false; // 1行サマリ
    [SerializeField] private bool logRoundDetail = false;  // 手/勝敗配列など（重い）

    private RunState _run;
    public RunState Run => _run;


    private void Start()
    {
        ResetRun();
    }

    [ContextMenu("Reset Run")]
    public void ResetRun()
    {
        if (tuningAsset == null)
        {
            Debug.LogError("[RunPresenter] TuningAsset is missing.");
            return;
        }

        var tuning = tuningAsset.ToTuning();

        var player = new DeckProfile(initialGu, initialChoki, initialPa);
        if (player.Total != 30)
        {
            Debug.LogError($"[RunPresenter] Initial player deck must total 30. Current={player.Total}");
            return;
        }

        var rng = new UnityRng();

        var enemyGen = new ArchetypeDeckGenerator
        {
            HeavyMain = heavyMain,
            HeavySub1 = heavySub1,
            HeavySub2 = heavySub2,
            DefaultHeavyMainColor = heavyMainColor,
            BalanceEach = balanceEach,
            TwinTopA = twinTopA,
            TwinTopB = twinTopB,
            TwinTopC = twinTopC,

            UseRangeTuning = useRangeTuning,
            HeavyMainMin = heavyMainMin,
            HeavyMainMax = heavyMainMax,
            HeavySubMin = heavySubMin,
            HeavySubMax = heavySubMax,
            BalanceMin = balanceMin,
            BalanceMax = balanceMax,
            TwinTopMainMin = twinTopMainMin,
            TwinTopMainMax = twinTopMainMax,
            TwinTopDeltaMin = twinTopDeltaMin,
            TwinTopDeltaMax = twinTopDeltaMax,
            TwinTopCMin = twinTopCMin,
            TwinTopCMax = twinTopCMax
        };

        var gainFormula = tuningAsset.CreateGainFormula();

        _run = new RunState(tuning, player, rng, enemyGen, gainFormula);

        if (logLifecycle)
        {
            Debug.Log($"[RunPresenter] ResetRun OK | EnvWeights H={tuning.EnvWeights.Heavy} B={tuning.EnvWeights.Balance} T={tuning.EnvWeights.TwinTop}");
            Debug.Log($"[RunPresenter] PlayerDeck G/C/P = {player.Gu}/{player.Choki}/{player.Pa}");
        }

        RefreshHud();
    }

    [ContextMenu("Play Next Round")]
    public void PlayNextRound()
    {
        if (_run == null)
        {
            Debug.LogWarning("[RunPresenter] Run not initialized. ResetRun first.");
            return;
        }
        if (_run.IsGameOver)
        {
            if (logRoundSummary)
                Debug.Log($"[RunPresenter] GameOver | Round={_run.RoundIndex} Miss={_run.MissCount} Score={_run.Score}");
            return;
        }

        var rr = _run.PlayNextRound();
        bool wasIntro = _run.LastRoundWasIntro;

        // ----- Views -----

        if (gaugeBarView != null)
        {
            gaugeBarView.SetGauge(
                _run.Gauge.Get(RpsColor.Gu),
                _run.Gauge.Get(RpsColor.Choki),
                _run.Gauge.Get(RpsColor.Pa),
                _run.Gauge.Max
            );
        }

        if (forcedFirstToggleView != null)
            forcedFirstToggleView.RefreshVisual();

        if (handsSequenceView != null)
        {
            var hi = new System.Collections.Generic.List<int>(4);

            if (_run.LastHeavyBonusApplied)
                hi.Add(_run.LastHeavyBonusIndex);

            if (_run.LastTwinTopBonusIndices != null && _run.LastTwinTopBonusIndices.Count > 0)
                hi.AddRange(_run.LastTwinTopBonusIndices);

            if (_run.LastBalanceBonusApplied)
                hi.Add(_run.LastBalanceBonusIndex);

            handsSequenceView.Play(rr.EnemyHands, rr.PlayerHands, rr.Outcomes, hi);
        }
        else if (logLifecycle)
        {
            Debug.LogWarning("[RunPresenter] handsSequenceView is null (not assigned in Inspector)");
        }

        if (outcomeText != null)
        {
            if (_run.LastRoundWasIntro)
            {
                // ★チュートリアルが存在していて、かつ開いている時だけ「チュートリアル」
                bool inTutorial = (tutorial != null && tutorial.IsOpen);
                outcomeText.text = inTutorial ? "チュートリアル" : "開始";
            }
            else
            {
                outcomeText.text = rr.IsClear
                    ? "WIN"
                    : (_run.IsGameOver ? "GAME OVER" : "LOSE");
            }
        }


        // HUD log
        if (hud != null)
        {
            if (_run.LastRoundWasIntro)
                {
                    hud.SetRoundLog("");
                    hud.Render(this); // 数値系だけは更新したいなら
                }
            else
            {
                CountOutcomes(rr.Outcomes, out int w, out int l, out int t);

                string resultLabel = rr.IsClear ? "勝ち" : (_run.IsGameOver ? "ゲームオーバー" : "負け");
                string summary = $"結果：{resultLabel}（{w}勝{l}敗{t}分）";

                string missingLine = "";
                if (rr.IsClear && rr.MissingColors != null && rr.MissingColors.Count > 0)
                {
                    var sb = new System.Text.StringBuilder();
                    sb.Append("欠損：");

                    bool first = true;
                    void AddOne(string name, float gain)
                    {
                        if (gain <= 0f) return;
                        if (!first) sb.Append(" / ");
                        first = false;
                        sb.Append($"{name}(+{gain:0.00})");
                    }

                    AddOne("グー", _run.LastGaugeGainGu);
                    AddOne("チョキ", _run.LastGaugeGainChoki);
                    AddOne("パー", _run.LastGaugeGainPa);

                    missingLine = first ? "" : ("\n" + sb.ToString());
                }

                string bonusLine = BuildBonusLogJa(rr);
                hud.SetRoundLog(summary + missingLine + bonusLine);

                hud.Render(this);
            }
        }

        // ----- Logs -----
        if (logRoundSummary)
        {
            Debug.Log($"[RunPresenter] Round={_run.RoundIndex} Clear={rr.IsClear} Loss={rr.LossCount} Miss={_run.MissCount} Score={_run.Score}");
        }

        if (logRoundDetail)
        {
            Debug.Log($"[RunPresenter] Enemy={_run.LastEnemyArchetype} Deck(G/C/P)=({_run.LastEnemyProfile.Gu}/{_run.LastEnemyProfile.Choki}/{_run.LastEnemyProfile.Pa})");
            Debug.Log($"  Hands: P={HandsToString(rr.PlayerHands)} vs E={HandsToString(rr.EnemyHands)}");
            Debug.Log($"  Outcomes: {OutcomesToString(rr.Outcomes)}");
            if (rr.MissingColors != null && rr.MissingColors.Count > 0)
                Debug.Log($"  MissingColors: {string.Join(",", rr.MissingColors)}");
        }
    }

    public void PlayIntroIfNeeded()
    {
        if (_run == null) return;
        if (_run.RoundIndex != 0) return;

        PlayNextRound();
        RefreshHud();
    }

    public void RefreshHud()
    {
        if (hud != null) hud.Render(this);
    }

    public void SetRoundLog(string text)
    {
        if (hud != null) hud.SetRoundLog(text);
    }

    public bool TrySkipResolveSequence()
    {
        if (handsSequenceView == null) return false;
        return handsSequenceView.RequestSkip();
    }

    public string GetNextEnemyArchetypeLabelSafe()
    {
        return _run != null
            ? _run.PreviewEnemyArchetype.ToJaLabel(_run.PreviewEnemyMainColor)
            : "不明";
    }

    // ---- Debug-only helpers (Editor) ----
#if UNITY_EDITOR
    [ContextMenu("Reserve Forced First: Gu")]
    private void ReserveForcedGu() => ReserveForced(RpsColor.Gu);

    [ContextMenu("Reserve Forced First: Choki")]
    private void ReserveForcedChoki() => ReserveForced(RpsColor.Choki);

    [ContextMenu("Reserve Forced First: Pa")]
    private void ReserveForcedPa() => ReserveForced(RpsColor.Pa);

    private void ReserveForced(RpsColor c)
    {
        if (_run == null) return;

        bool ok = _run.TryReserveForcedFirst(c);
        if (logLifecycle)
        {
            Debug.Log(ok
                ? $"[RunPresenter] Force Reserved: {c}"
                : $"[RunPresenter] Force Failed: {c}");
        }
    }
#endif

    // ---- Internal helpers ----

    private static void CountOutcomes(
        System.Collections.Generic.IReadOnlyList<RpsOutcome> os,
        out int win, out int lose, out int tie)
    {
        win = 0; lose = 0; tie = 0;
        for (int i = 0; i < os.Count; i++)
        {
            switch (os[i])
            {
                case RpsOutcome.Win: win++; break;
                case RpsOutcome.Lose: lose++; break;
                case RpsOutcome.Tie: tie++; break;
            }
        }
    }

    private static string HandsToString(System.Collections.Generic.IReadOnlyList<RpsColor> hands)
    {
        var s = new System.Text.StringBuilder();
        for (int i = 0; i < hands.Count; i++)
        {
            if (i > 0) s.Append(' ');
            s.Append(hands[i]);
        }
        return s.ToString();
    }

    private static string OutcomesToString(System.Collections.Generic.IReadOnlyList<RpsOutcome> os)
    {
        var s = new System.Text.StringBuilder();
        for (int i = 0; i < os.Count; i++)
        {
            if (i > 0) s.Append(' ');
            s.Append(os[i]);
        }
        return s.ToString();
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

    private static string ToStepNumber(int n)
    {
        return $"({n})";
    }



    // =========================
    // Assets/Scripts/Unity/RunPresenter.cs
    // BuildBonusLogJa に「返却ログ」を追加（Balanceが不発でも表示できる）
    // =========================

            private string BuildBonusLogJa(RoundResult rr)
            {
                if (_run == null || rr == null) return "";

                var sb = new System.Text.StringBuilder();

                if (_run.LastHeavyBonusApplied)
                {
                    sb.Append('\n');
                    sb.Append($"偏重ボーナス：一度だけ{ToJpColor(_run.LastHeavyBonusPlayerColor)}での負けが勝ちになりました");
                }

                if (_run.LastTwinTopBonusWinCount > 0)
                {
                    sb.Append('\n');
                    sb.Append($"２トップボーナス：３連で交互に出せた時の負けが{_run.LastTwinTopBonusWinCount}回勝ちになりました");
                }

                // ---- Balance ----
                bool hasRefund =
                    (_run.LastBalanceRefundGu > 0) ||
                    (_run.LastBalanceRefundChoki > 0) ||
                    (_run.LastBalanceRefundPa > 0);

                if (_run.LastBalanceBonusApplied)
                {
                    sb.Append('\n');
                    sb.Append("バランスボーナス：確定ドローにより引き直しを行いました");

                    if (_run.LastBalanceMoves != null && _run.LastBalanceMoves.Count > 0)
                    {
                        sb.Append('\n');

                        for (int i = 0; i < _run.LastBalanceMoves.Count; i++)
                        {
                            var m = _run.LastBalanceMoves[i];
                            int handNo1 = m.index + 1;

                            if (i > 0) sb.Append("　");

                            string outcomeJa = m.outcome switch
                            {
                                RpsOutcome.Win => "勝ち",
                                RpsOutcome.Tie => "引き分け",
                                _ => "負け"
                            };

                            sb.Append($"({i + 1}){handNo1}手目 ");
                            sb.Append($"{ToJpColor(m.fromColor)}→{ToJpColor(m.toColor)}（{outcomeJa}）");
                        }
                    }

                    if (hasRefund)
                    {
                        sb.Append('\n');
                        sb.Append(BuildBalanceRefundLineJa());
                    }

                    if (rr.IsClear && rr.MissingColors != null && rr.MissingColors.Count > 0 && _run.LastBalanceBonusCreatedMissing)
                    {
                        sb.Append('\n');
                        sb.Append("これにより欠損勝利が発生し、ゲージが溜まりました。");
                    }
                }
                else if (hasRefund)
                {
                    // ★介入不発でも返却ログは出す
                    sb.Append('\n');
                    sb.Append(BuildBalanceRefundLineJa());
                }

                return sb.ToString();
            }

            private string BuildBalanceRefundLineJa()
            {
                // 「使わなかったゲージ（グーx2, パーx1）は返却されました」
                var parts = new System.Collections.Generic.List<string>(3);

                if (_run.LastBalanceRefundGu > 0) parts.Add($"グーx{_run.LastBalanceRefundGu}");
                if (_run.LastBalanceRefundChoki > 0) parts.Add($"チョキx{_run.LastBalanceRefundChoki}");
                if (_run.LastBalanceRefundPa > 0) parts.Add($"パーx{_run.LastBalanceRefundPa}");

                string mid = string.Join(", ", parts);
                return $"使わなかったゲージ（{mid}）は返却されました";
            }

}
