// Assets/Scripts/Core/RunController.cs
using System;
using System.Collections.Generic;
using UnityEngine;

namespace RpsBuild.Core
{
    [Serializable]
    public struct Tuning
    {
        public int HandCount;               // 例：7
        public int LoseThresholdExclusive;  // 例：3（3以上で失敗）
        public int MaxMiss;                 // 例：N回で終了
        public int InitialPoints;      // 初期ポイント（例：10）
        public int PointsPerClear;     // クリア報酬（例：1）
        public float GaugeBuyAmount;   // 1ptでゲージ+（例：0.1）
        public float GaugeMax;              // 例：1
        public bool UniqueMainAcrossArchetypes; // Heavy/TwinTop/Balance(看板色) の主軸色を重複禁止にする
        public bool ShuffleEnvWeightsAcrossArchetypes; // Run開始時に heavy/balance/twinTop の重み割当をシャッフル
        public ArchetypeWeights EnvWeights; // 例：Heavy60%...


        // 将来ここに「デッキ調整幅(+2/-2)」なども集約
    }

    public sealed class RunState
    {
        public int RoundIndex { get; private set; } = 0;
        public int MissCount { get; private set; } = 0;

        public int Points { get; private set; }
        public int Score { get; private set; } = 0;

        public float LastGaugeGainGu { get; private set; } = 0f;
        public float LastGaugeGainChoki { get; private set; } = 0f;
        public float LastGaugeGainPa { get; private set; } = 0f;

        public EnvironmentState Environment { get; private set; }
        public DeckProfile PlayerProfile { get; private set; }

        public GaugeState Gauge { get; } = new GaugeState();

        // 任意ラウンドで使える「確定ドローの予約」
        public RpsColor? ReservedForcedFirstColor { get; private set; } = null;

        public Tuning Tuning => _tuning;
        private readonly List<RpsColor> _reservedForcedOrder = new();
        public IReadOnlyList<RpsColor> ReservedForcedOrder => _reservedForcedOrder;

        public bool IsGameOver => MissCount >= _tuning.MaxMiss;

        public bool LastRoundWasIntro { get; private set; } = false;
        public bool LastRoundIntroForcedClear { get; private set; } = false;

        private readonly Tuning _tuning;
        private readonly IRng _rng;
        private readonly ArchetypeDeckGenerator _enemyGen;
        private readonly IGaugeGainFormula _gainFormula;

        // 最新の敵（表示用）
        public EnemyArchetype LastEnemyArchetype { get; private set; }
        public DeckProfile LastEnemyProfile { get; private set; }

        // 次ラウンド用の敵プレビュー（UIに出してOK）
        public EnemyArchetype PreviewEnemyArchetype { get; private set; }
        // 内部用：次ラウンドで実際に使う敵デッキ
        private DeckProfile _previewEnemyDeck;

        // ---- Run-fixed enemy profiles (per archetype) ----
        private readonly Dictionary<EnemyArchetype, DeckProfile> _enemyProfilesByArchetype = new();
        private readonly Dictionary<EnemyArchetype, RpsColor> _enemyMainColorByArchetype = new();
        private readonly Dictionary<EnemyArchetype, RpsColor> _enemySecondColorByArchetype = new(); // TwinTop用（表示/将来用）

        public RpsColor PreviewEnemyMainColor { get; private set; } = RpsColor.Gu;
        public RpsColor LastEnemyMainColor { get; private set; } = RpsColor.Gu;

        public PlayerArchetype PlayerArchetype { get; private set; } = PlayerArchetype.Balance;
        public RpsColor PlayerMainColor { get; private set; } = RpsColor.Gu;
        public RpsColor PlayerSecondColor { get; private set; } = RpsColor.Choki;

        public bool LastHeavyBonusApplied { get; private set; } = false;
        public int LastHeavyBonusIndex { get; private set; } = -1;
        public RpsColor LastHeavyBonusPlayerColor { get; private set; } = RpsColor.Gu;

        public int LastTwinTopBonusWinCount { get; private set; } = 0;

        public bool LastBalanceBonusApplied { get; private set; } = false;
        public int LastBalanceBonusIndex { get; private set; } = -1;
        public RpsColor LastBalanceBonusFrom { get; private set; } = RpsColor.Gu;
        public RpsColor LastBalanceBonusTo { get; private set; } = RpsColor.Gu;
        public RpsOutcome LastBalanceBonusResult { get; private set; } = RpsOutcome.Tie;
        public bool LastBalanceBonusCreatedMissing { get; private set; } = false;

        private readonly List<int> _lastTwinTopBonusIndices = new();
        public IReadOnlyList<int> LastTwinTopBonusIndices => _lastTwinTopBonusIndices;

        public readonly List<(RpsColor color, int index)> LastForcedPlacements = new();
        public readonly List<(RpsColor toColor, int index, RpsColor fromColor, RpsOutcome outcome, bool createdMissing)> LastBalanceMoves
            = new();
        private readonly List<int> _lastBalanceBonusIndices = new();
        public IReadOnlyList<int> LastBalanceBonusIndices => _lastBalanceBonusIndices;
        public int LastBalanceRefundGu { get; private set; } = 0;
        public int LastBalanceRefundChoki { get; private set; } = 0;
        public int LastBalanceRefundPa { get; private set; } = 0;







        // ---- Archetype Stats (Core) ----

        public struct ArchetypeHandStat
        {
            public int N;
            public int Gu;
            public int Choki;
            public int Pa;

            public void AddHands(IReadOnlyList<RpsColor> hands)
            {
                N++;
                for (int i = 0; i < hands.Count; i++)
                {
                    switch (hands[i])
                    {
                        case RpsColor.Gu: Gu++; break;
                        case RpsColor.Choki: Choki++; break;
                        case RpsColor.Pa: Pa++; break;
                    }
                }
            }
        }

        private readonly Dictionary<EnemyArchetype, ArchetypeHandStat> _archetypeStats = new();



        public RunState(
            Tuning tuning,
            DeckProfile initialPlayer,
            IRng rng,
            ArchetypeDeckGenerator enemyGen,
            IGaugeGainFormula gainFormula)
        {
            _tuning = tuning;
            _rng = rng;
            _enemyGen = enemyGen;
            _gainFormula = gainFormula;

            if (initialPlayer.Total != 30) throw new ArgumentException("Player deck must total 30.");
            PlayerProfile = initialPlayer;

            RecalcPlayerArchetype();

            var envWeights = _tuning.EnvWeights;

            if (_tuning.ShuffleEnvWeightsAcrossArchetypes)
                envWeights = EnvironmentGenerator.ShuffleWeightsAcrossArchetypes(envWeights, _rng);

            Environment = EnvironmentGenerator.CreateDefault(envWeights);

            Gauge.SetMax(_tuning.GaugeMax);

            Points = _tuning.InitialPoints;

            BuildRunFixedEnemyProfiles();
            GenerateEnemyPreview();

        }


        /// <summary>
        /// ラウンドを1回進める（7枚自動ドロー→判定→ミス更新→欠損勝利→ゲージ加算）。
        /// </summary>
        public RoundResult PlayNextRound()
        {
            if (IsGameOver) throw new InvalidOperationException("Game is over.");

            RoundResult result = null;

            // ---- Intro handling (Round 0) ----
            bool isIntro = (RoundIndex == 0);
            LastRoundWasIntro = isIntro;
            LastRoundIntroForcedClear = false;


            // ★次ラウンド用プレビューを「消費」して今回の敵にする
            var archetype = PreviewEnemyArchetype;
            var enemyProfile = _previewEnemyDeck;

            LastEnemyArchetype = archetype;
            LastEnemyProfile = enemyProfile;
            LastEnemyMainColor = PreviewEnemyMainColor;
            RefreshPlayerArchetypeFromProfile();

            var playerDeck = new Deck(PlayerProfile);
            var enemyDeck = new Deck(enemyProfile);

            // 予約された確定ドロー色（あれば）を適用
            List<RpsColor> forced = null;

            // ★このラウンドで Balance も「予約がある時だけ」発動したい場合のトリガー色
            // （＝予約が1つでも成功消費できた色）
            RpsColor? balanceTriggerColor = null;

            if (!isIntro && _reservedForcedOrder.Count > 0)
            {
                // ★この時点で予約リストはローカルに退避して空にする
                // （ログが残って次ラウンドに効く問題を根絶）
                var reserved = new List<RpsColor>(_reservedForcedOrder);
                _reservedForcedOrder.Clear();

                forced = new List<RpsColor>(reserved.Count);
                LastForcedPlacements.Clear();

                // 順番通りに「1回分ずつ」消費（使えた分だけforcedに入れる）
                for (int i = 0; i < reserved.Count; i++)
                {
                    var c = reserved[i];
                    if (Gauge.TryConsumeCharged(c))
                    {
                        forced.Add(c);

                        if (!balanceTriggerColor.HasValue)
                            balanceTriggerColor = c;
                    }
                }

                Debug.Log($"[Forced] gauge Gu={Gauge.Get(RpsColor.Gu):0.###} Ch={Gauge.Get(RpsColor.Choki):0.###} Pa={Gauge.Get(RpsColor.Pa):0.###}  Max={Gauge.Max:0.###}");
                Debug.Log($"[Forced] count={(forced != null ? forced.Count : 0)}  list={(forced != null ? string.Join(",", forced) : "-")}");
            }

            // ---- Intro handling (Round 0): force CLEAR by rerolling (bounded) ----
            LastHeavyBonusApplied = false;
            LastHeavyBonusIndex = -1;
            LastTwinTopBonusWinCount = 0;
            if (_lastTwinTopBonusIndices != null) _lastTwinTopBonusIndices.Clear();
            LastBalanceBonusApplied = false;
            LastBalanceBonusIndex = -1;
            LastBalanceBonusCreatedMissing = false;
            if (_lastBalanceBonusIndices != null) _lastBalanceBonusIndices.Clear();
            LastBalanceRefundGu = 0;
            LastBalanceRefundChoki = 0;
            LastBalanceRefundPa = 0;


            if (!isIntro)
            {
                // ★ Balanceのときは「確定ドロー=スワップ」をやらない（引き直し/置換だけに寄せる）
                bool disableForcedSwapForBalance = (PlayerArchetype == PlayerArchetype.Balance);

                    result = RoundSimulator.Simulate(
                        playerDeck,
                        enemyDeck,
                        _tuning.HandCount,
                        _tuning.LoseThresholdExclusive,
                        _rng,
                        forcedForPlayer: disableForcedSwapForBalance ? null : forced
                );

                // ---- Balance（予約がある時だけ） ----
                // ★確定ドローが2回なら介入も2回（最小差分：既存の置換関数を複数回回す）
                LastBalanceMoves.Clear();

                    if (PlayerArchetype == PlayerArchetype.Balance && forced != null && forced.Count > 0)
                    {
                                // 消費した（=forcedに入った）回数を色別に集計
                                int consumedGu = 0, consumedCh = 0, consumedPa = 0;
                                for (int i = 0; i < forced.Count; i++)
                                {
                                        switch (forced[i])
                                        {
                                            case RpsColor.Gu: consumedGu++; break;
                                            case RpsColor.Choki: consumedCh++; break;
                                            case RpsColor.Pa: consumedPa++; break;
                                        }
                                }

                                    var tmpMoves = new System.Collections.Generic.List<RoundSimulator.BalanceMove>(forced.Count);

                                    var before = result;
                                    result = RoundSimulator.ApplyBalance_ChargedGaugeReplaceMultiHands_FullSearch(
                                        result,
                                        _tuning.LoseThresholdExclusive,
                                        PlayerProfile,
                                        forced,
                                        out var anyApplied,
                                        out var anyCreatedMissing,
                                        tmpMoves,
                                        out int usedGu,
                                        out int usedCh,
                                        out int usedPa
                                    );

                                    // ★段階が上がらない場合：ApplyBalance が rr を返す（anyApplied=false想定）
                                    //    ただし保険で「ReferenceEquals」も見る
                                    bool changed = (!ReferenceEquals(result, before));

                                    if (anyApplied && changed && tmpMoves.Count > 0)
                                    {
                                        LastBalanceBonusApplied = true;
                                        LastBalanceBonusCreatedMissing = anyCreatedMissing;

                                        for (int i = 0; i < tmpMoves.Count; i++)
                                        {
                                            var m = tmpMoves[i];
                                            LastBalanceMoves.Add((m.toColor, m.index, m.fromColor, m.outcomeAfter, m.createdMissing));

                                            // 互換：最後に起きたもの
                                            LastBalanceBonusIndex = m.index;
                                            LastBalanceBonusFrom = m.fromColor;
                                            LastBalanceBonusTo = m.toColor;
                                            LastBalanceBonusResult = m.outcomeAfter;
                                        }

                                        // 最終結果で outcome を上書き（既存踏襲）
                                        if (LastBalanceBonusIndex >= 0 && LastBalanceBonusIndex < result.Outcomes.Count)
                                            LastBalanceBonusResult = result.Outcomes[LastBalanceBonusIndex];

                                        for (int i = 0; i < LastBalanceMoves.Count; i++)
                                        {
                                            var m = LastBalanceMoves[i];
                                            if (m.index >= 0 && m.index < result.Outcomes.Count)
                                                LastBalanceMoves[i] = (m.toColor, m.index, m.fromColor, result.Outcomes[m.index], m.createdMissing);
                                        }

                                        // ★使わなかった分を返却（工数最小：後からGauge.Add）
                                        int refundGu = Mathf.Max(0, consumedGu - usedGu);
                                        int refundCh = Mathf.Max(0, consumedCh - usedCh);
                                        int refundPa2 = Mathf.Max(0, consumedPa - usedPa);

                                        if (refundGu > 0) Gauge.Add(RpsColor.Gu, _tuning.GaugeMax * refundGu);
                                        if (refundCh > 0) Gauge.Add(RpsColor.Choki, _tuning.GaugeMax * refundCh);
                                        if (refundPa2 > 0) Gauge.Add(RpsColor.Pa, _tuning.GaugeMax * refundPa2);

                                        LastBalanceRefundGu = refundGu;
                                        LastBalanceRefundChoki = refundCh;
                                        LastBalanceRefundPa = refundPa2;
                    }
                    else
                    {
                                        // ★段階が上がらない：介入しない。消費済みは全返却
                        LastBalanceBonusApplied = false;
                        LastBalanceBonusCreatedMissing = false;

                        if (consumedGu > 0) Gauge.Add(RpsColor.Gu, _tuning.GaugeMax * consumedGu);
                        if (consumedCh > 0) Gauge.Add(RpsColor.Choki, _tuning.GaugeMax * consumedCh);
                        if (consumedPa > 0) Gauge.Add(RpsColor.Pa, _tuning.GaugeMax * consumedPa);

                        LastBalanceRefundGu = consumedGu;
                        LastBalanceRefundChoki = consumedCh;
                        LastBalanceRefundPa = consumedPa;
                    }
                }


                // Heavy
                if (PlayerArchetype == PlayerArchetype.Heavy)
                {
                    result = RoundSimulator.ApplyHeavy_FirstLoseToWin(
                        result,
                        _tuning.LoseThresholdExclusive,
                        PlayerMainColor,
                        out var applied,
                        out var idx,
                        out var col
                    );

                    if (applied)
                    {
                        LastHeavyBonusApplied = true;
                        LastHeavyBonusIndex = idx;
                        LastHeavyBonusPlayerColor = col;
                    }
                }

                // TwinTop
                if (PlayerArchetype == PlayerArchetype.TwinTop)
                {
                    result = RoundSimulator.ApplyTwinTop_ChainSecondLoseToWin(
                        result,
                        _tuning.LoseThresholdExclusive,
                        PlayerMainColor,
                        PlayerSecondColor,
                        _lastTwinTopBonusIndices,
                        out var winFlip
                    );

                    LastTwinTopBonusWinCount = winFlip;
                }

                // ★最後に：Balanceログの「勝ち/引き分け」を最終結果に合わせて更新
                if (LastBalanceBonusApplied && LastBalanceBonusIndex >= 0 && LastBalanceBonusIndex < result.Outcomes.Count)
                {
                    LastBalanceBonusResult = result.Outcomes[LastBalanceBonusIndex];
                }
            }
            else
            {
                const int MaxIntroTries = 30;
                RoundResult last = default;

                for (int t = 0; t < MaxIntroTries; t++)
                {
                    var pDeck = new Deck(PlayerProfile);
                    var eDeck = new Deck(enemyProfile);

                    last = RoundSimulator.Simulate(
                        pDeck,
                        eDeck,
                        _tuning.HandCount,
                        _tuning.LoseThresholdExclusive,
                        _rng,
                        forcedForPlayer: forced
                    );

                    if (last.IsClear)
                    {
                        if (t > 0) LastRoundIntroForcedClear = true;
                        result = last;
                        goto INTRO_DONE;
                    }
                }

                result = last;
            INTRO_DONE: ;
            }

            // ★ここに置く：result が最終確定した後（INTRO_DONE の後、stats update の前）
            // 確定ドローの配置手番を記録（0-based index、見つからなければ -1）
            LastForcedPlacements.Clear();

            bool isBalance = (PlayerArchetype == PlayerArchetype.Balance);

            if (!isBalance && forced != null && forced.Count > 0 && result != null && result.PlayerHands != null)
            {
                var used = new bool[result.PlayerHands.Count];

                for (int i = 0; i < forced.Count; i++)
                {
                    var c = forced[i];
                    int found = -1;

                    for (int h = 0; h < result.PlayerHands.Count; h++)
                    {
                        if (used[h]) continue;
                        if (result.PlayerHands[h] != c) continue;

                        used[h] = true;
                        found = h;
                        break;
                    }

                    LastForcedPlacements.Add((c, found));
                }

                // ★デバッグ（1回確認用）：ここで found が -1 なら forced が hands に反映されてない
                Debug.Log($"[ForcedPlacement] forced={string.Join(",", forced)} hands={string.Join(",", result.PlayerHands)} placements={string.Join(" / ", LastForcedPlacements)}");
            }


                // ---- side effects (skip in Intro) ----
                if (!isIntro)
                {
                    // ---- stats update ----
                    if (!_archetypeStats.TryGetValue(LastEnemyArchetype, out var st))
                        st = default;

                    st.AddHands(result.EnemyHands);
                    _archetypeStats[LastEnemyArchetype] = st;

                    if (!result.IsClear) MissCount++;

                    if (result.IsClear)
                    {
                        Points += _tuning.PointsPerClear;
                        Score += 1;
                    }

                    LastGaugeGainGu = 0f;
                    LastGaugeGainChoki = 0f;
                    LastGaugeGainPa = 0f;

                    if (result.IsClear && result.MissingColors.Count > 0)
                    {
                        foreach (var missing in result.MissingColors)
                        {
                            float gain = _gainFormula.CalcGain(missing, PlayerProfile);
                            Gauge.Add(missing, gain);

                            switch (missing)
                            {
                                case RpsColor.Gu:    LastGaugeGainGu += gain; break;
                                case RpsColor.Choki: LastGaugeGainChoki += gain; break;
                                case RpsColor.Pa:    LastGaugeGainPa += gain; break;
                            }
                        }
                    }
                }
                else
                {
                    // Introでは「表示用のゲージ増加ログ」を必ず0にしておく（見た目の残骸防止）
                    LastGaugeGainGu = 0f;
                    LastGaugeGainChoki = 0f;
                    LastGaugeGainPa = 0f;
                }


            RoundIndex++;
            GenerateEnemyPreview();
            return result;
        }


        /// <summary>
        /// ラウンド間のデッキ調整（最小限の雛形）。
        /// 例：+2/-2の具体UIはここをラップする。
        /// </summary>
        public void SetPlayerProfile(DeckProfile profile)
        {
            if (profile.Total != 30) throw new ArgumentException("Player deck must total 30.");
            PlayerProfile = profile;
            RecalcPlayerArchetype();
        }

        private void GenerateEnemyPreview()
        {
            PreviewEnemyArchetype = EnvironmentGenerator.RollArchetype(Environment, _rng);

            // 保険：辞書に無ければ作り直す（初期化漏れ/順序ミスでも落ちない）
            if (!_enemyProfilesByArchetype.TryGetValue(PreviewEnemyArchetype, out var deck))
            {
                BuildRunFixedEnemyProfiles();
                deck = _enemyProfilesByArchetype[PreviewEnemyArchetype];
            }

            _previewEnemyDeck = deck;

            if (!_enemyMainColorByArchetype.TryGetValue(PreviewEnemyArchetype, out var main))
                main = RpsColor.Gu;

            PreviewEnemyMainColor = main;
        }

        public bool TryGetEnemyMainColor(EnemyArchetype archetype, out RpsColor mainColor)
        {
            return _enemyMainColorByArchetype.TryGetValue(archetype, out mainColor);
        }




        private bool TrySpendPoints(int cost)
        {
            if (cost <= 0) return true;
            if (Points < cost) return false;
            Points -= cost;
            return true;
        }

        /// <summary>
        /// 旧：即時にデッキを確定変更する（デバッグ/プロト用）
        /// 本番UIは AdjustPanelView のドラフト→TryCommitDeckProfileByPoints を使用
        /// </summary>
        public bool TryAdjustDeckByPoint(RpsColor addColor, RpsColor subColor, int amount = 1)
        {
            if (amount <= 0) return true;
            if (Points < amount) return false;

            var cur = PlayerProfile;

            // 1pt=1枚なので add/sub amount は同じ
            bool ok = DeckAdjust.TryAdjust(ref cur,
                addColor: addColor, addAmount: amount,
                subColor: subColor, subAmount: amount);

            if (!ok) return false;

            // ここで消費確定
            Points -= amount;
            PlayerProfile = cur;
            RecalcPlayerArchetype();
            return true;
        }

        public bool TryCommitDeckProfileByPoints(DeckProfile profile, int cost)
        {
            if (profile.Total != 30) return false;
            if (cost < 0) return false;
            if (!TrySpendPoints(cost)) return false;

            PlayerProfile = profile;
            return true;
        }


        /// <summary>
        /// 1ptでゲージを +GaugeBuyAmount（任意色）
        /// — amount回買える（=ポイントamount消費）
        /// </summary>
        public bool TryBuyGaugeByPoint(RpsColor color, int amount = 1)
        {
            Debug.Log($"[BuyGauge] color={color}, amount={amount}, points(before)={Points}");

            if (amount <= 0) return true;
            if (!TrySpendPoints(amount))
            {
                Debug.Log("[BuyGauge] failed: not enough points");
                return false;
            }

            float add = _tuning.GaugeBuyAmount * amount;
            Debug.Log($"[BuyGauge] gauge add = {add}");

            Gauge.Add(color, add);

            Debug.Log($"[BuyGauge] points(after)={Points}, gauge={Gauge.Get(color)}");
            return true;
        }


        public ArchetypeHandStat GetArchetypeHandStat(EnemyArchetype archetype)
        {
            if (!_archetypeStats.TryGetValue(archetype, out var st))
            {
                st = default;
                _archetypeStats[archetype] = st;
            }
            return st;
        }


        private void BuildRunFixedEnemyProfiles()
        {
            _enemyProfilesByArchetype.Clear();
            _enemyMainColorByArchetype.Clear();
            _enemySecondColorByArchetype.Clear();

            // 主軸色を決める（UniqueMainAcrossArchetypes なら被りなし）
            // HeavyMain, BalanceBanner, TwinTopMain を割り当てる
            var colors = new List<RpsColor> { RpsColor.Gu, RpsColor.Choki, RpsColor.Pa };

            RpsColor PickAny() => colors[_rng.Range(0, colors.Count)];

            RpsColor heavyMain;
            RpsColor balanceBanner;
            RpsColor twinMain;

            if (_tuning.UniqueMainAcrossArchetypes)
            {
                // 3色をシャッフルして割当
                for (int i = colors.Count - 1; i > 0; i--)
                {
                    int j = _rng.Range(0, i + 1);
                    (colors[i], colors[j]) = (colors[j], colors[i]);
                }
                heavyMain = colors[0];
                balanceBanner = colors[1];
                twinMain = colors[2];
            }
            else
            {
                heavyMain = PickAny();
                balanceBanner = PickAny();
                twinMain = PickAny();
            }

            // TwinTopの secondColor は main以外の2色から選ぶ（必要ならUniqueを強める拡張点）
            var twinOthers = new List<RpsColor> { RpsColor.Gu, RpsColor.Choki, RpsColor.Pa };
            twinOthers.Remove(twinMain);
            var twinSecond = twinOthers[_rng.Range(0, twinOthers.Count)];

            // ---- Heavy ----
            _enemyMainColorByArchetype[EnemyArchetype.Heavy] = heavyMain;
            _enemyProfilesByArchetype[EnemyArchetype.Heavy] = _enemyGen.GenerateHeavyFixed(_rng, heavyMain);

            // ---- Balance ---- (bannerColorは表示用。生成にも渡して割当を安定させる)
            _enemyMainColorByArchetype[EnemyArchetype.Balance] = balanceBanner;
            _enemyProfilesByArchetype[EnemyArchetype.Balance] = _enemyGen.GenerateBalanceFixed(_rng, balanceBanner);

            // ---- TwinTop ----
            _enemyMainColorByArchetype[EnemyArchetype.TwinTop] = twinMain;
            _enemySecondColorByArchetype[EnemyArchetype.TwinTop] = twinSecond;
            _enemyProfilesByArchetype[EnemyArchetype.TwinTop] = _enemyGen.GenerateTwinTopFixed(_rng, twinMain, twinSecond);
        }

        private void RecalcPlayerArchetype()
                {
                    var info = PlayerArchetypeClassifier.Classify(PlayerProfile);
                    PlayerArchetype = info.Archetype;
                    PlayerMainColor = info.MainColor;
                    PlayerSecondColor = info.SecondColor;
                }

                // Assets/Scripts/Core/RunController.cs
                // RunState クラス内に追加（どこでもOK）

        private bool TryGetChargedGaugeColorForBalance(out RpsColor color)
                {
                    // 優先順は固定でOK（将来 “最大ゲージ色” にしたければここを変えるだけ）
                    // ※今回は「満タンを消費する」仕様なので IsCharged 前提
                    if (Gauge.IsCharged(RpsColor.Gu)) { color = RpsColor.Gu; return true; }
                    if (Gauge.IsCharged(RpsColor.Choki)) { color = RpsColor.Choki; return true; }
                    if (Gauge.IsCharged(RpsColor.Pa)) { color = RpsColor.Pa; return true; }

                    color = RpsColor.Gu;
                    return false;
                }

                public bool TryReserveForcedFirst(RpsColor color)
                {
                    if (!PlayerProfile.Has(color)) return false;

                    int cap = Gauge.GetChargedCount(color);
                    int cur = 0;
                    for (int i = 0; i < _reservedForcedOrder.Count; i++)
                        if (_reservedForcedOrder[i] == color) cur++;

                    if (cur >= cap) return false;

                    _reservedForcedOrder.Add(color);
                    return true;
                }





        public bool TryCancelReservedForcedFirst()
                {
                    // 最小差分：全解除
                    if (_reservedForcedOrder.Count <= 0) return false;
                    _reservedForcedOrder.Clear();
                    return true;
                }

        // 互換：旧UIが参照しても落ちないように
        public RpsColor? ReservedForcedFirst
                    => (_reservedForcedOrder != null && _reservedForcedOrder.Count > 0)
                        ? _reservedForcedOrder[0]
                        : (RpsColor?)null;

        private int CountReserved(RpsColor c)
            {
                            int n = 0;
                            for (int i = 0; i < _reservedForcedOrder.Count; i++)
                                if (_reservedForcedOrder[i] == c) n++;
                            return n;
            }

        public void ClearReservedForcedOrder()
            {
                            _reservedForcedOrder.Clear();
            }

        public void SetReservedForcedOrder(System.Collections.Generic.List<RpsColor> order)
            {
                _reservedForcedOrder.Clear();
                if (order == null || order.Count == 0) return;

                // ここでは "チェックしない"（Adjust側で検証済み）
                _reservedForcedOrder.AddRange(order);
            }


        private void RefreshPlayerArchetypeFromProfile()
            {
                // 念のため：調整途中（30枚でない）では何もしない
                if (PlayerProfile.Total != 30)
                    return;

                var info = PlayerArchetypeClassifier.Classify(PlayerProfile);

                PlayerArchetype   = info.Archetype;
                PlayerMainColor   = info.MainColor;
                PlayerSecondColor = info.SecondColor;
            }

    }
}
