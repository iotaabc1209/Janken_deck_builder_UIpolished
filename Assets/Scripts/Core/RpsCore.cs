// Assets/Scripts/Core/RpsCore.cs
using System;
using System.Collections.Generic;

namespace RpsBuild.Core
{
    // =========================
    // Domain
    // =========================

    public enum RpsColor { Gu = 0, Choki = 1, Pa = 2 }
    public enum RpsOutcome { Win = 0, Lose = 1, Tie = 2 }

    [Serializable]
    public struct DeckProfile
    {
        public int Gu;
        public int Choki;
        public int Pa;

        public int Total => Gu + Choki + Pa;

        public DeckProfile(int gu, int choki, int pa)
        {
            Gu = gu; Choki = choki; Pa = pa;
        }

        public int Get(RpsColor c) => c switch
        {
            RpsColor.Gu => Gu,
            RpsColor.Choki => Choki,
            RpsColor.Pa => Pa,
            _ => 0
        };

        public bool Has(RpsColor c) => Get(c) > 0;

        public void Set(RpsColor c, int value)
        {
            value = Math.Max(0, value);
            switch (c)
            {
                case RpsColor.Gu: Gu = value; break;
                case RpsColor.Choki: Choki = value; break;
                case RpsColor.Pa: Pa = value; break;
            }
        }
    }

    public interface IRng
    {
        int Range(int minInclusive, int maxExclusive);
        float Value01();
    }

    // =========================
    // Deck (batch draw)
    // =========================

    /// <summary>
    /// 「内訳」から「1ラウンド分のN枚」を引く。テンポ仕様の要。
    /// 1手目だけ確定色を入れられる（任意ラウンドで権利を使う）。
    /// </summary>
    public sealed class Deck
    {
        private readonly DeckProfile _profile;

        public Deck(DeckProfile profile)
        {
            if (profile.Total != 30) throw new ArgumentException("DeckProfile total must be 30.");
            _profile = profile;
        }

        public DeckProfile Profile => _profile;

        // Deck.DrawBatch シグネチャ変更
        public List<RpsColor> DrawBatch(
            int handCount,
            IRng rng,
            RpsColor? forcedFirst = null,
            System.Collections.Generic.IReadOnlyList<RpsColor> forcedOrder = null)
        {
            if (handCount <= 0) throw new ArgumentOutOfRangeException(nameof(handCount));

            var pool = BuildPool();
            Shuffle(pool, rng);

            // ★複数確定（優先）
            if (forcedOrder != null && forcedOrder.Count > 0)
            {
                int kMax = Math.Min(handCount, forcedOrder.Count);
                for (int k = 0; k < kMax; k++)
                {
                    var c = forcedOrder[k];
                    if (!_profile.Has(c)) continue; // 安全策

                    // k番目以降から探して、見つかったら k に持ってくる
                    int idx = pool.FindIndex(k, x => x == c);
                    if (idx >= 0)
                    {
                        var temp = pool[k];
                        pool[k] = pool[idx];
                        pool[idx] = temp;
                    }
                }
            }
            else if (forcedFirst.HasValue)
            {
                // 旧：1手目だけ確定（互換維持）
                var c = forcedFirst.Value;
                if (_profile.Has(c))
                {
                    int idx = pool.FindIndex(x => x == c);
                    if (idx >= 0)
                    {
                        var temp = pool[0];
                        pool[0] = pool[idx];
                        pool[idx] = temp;
                    }
                }
            }

            var result = new List<RpsColor>(handCount);
            for (int i = 0; i < handCount; i++)
                result.Add(pool[i]);

            return result;
        }



        private List<RpsColor> BuildPool()
        {
            var pool = new List<RpsColor>(30);
            for (int i = 0; i < _profile.Gu; i++) pool.Add(RpsColor.Gu);
            for (int i = 0; i < _profile.Choki; i++) pool.Add(RpsColor.Choki);
            for (int i = 0; i < _profile.Pa; i++) pool.Add(RpsColor.Pa);
            if (pool.Count != 30) throw new InvalidOperationException("Pool must be 30.");
            return pool;
        }

        private static void Shuffle(List<RpsColor> list, IRng rng)
        {
            // Fisher-Yates
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }

    // =========================
    // Gauge (per color)
    // =========================

    /// <summary>
    /// 色別ゲージ。最大到達した色は「確定ドロー権あり」。
    /// 任意ラウンドで使用可能。使うと0に戻る。
    /// </summary>
    public sealed class GaugeState
    {
        private readonly Dictionary<RpsColor, float> _g = new()
        {
            { RpsColor.Gu, 0f },
            { RpsColor.Choki, 0f },
            { RpsColor.Pa, 0f },
        };

        public float Max { get; private set; } = 1f;

        public void SetMax(float max)
        {
            Max = Math.Max(0.0001f, max);
            // ★ clamp しない（スタックを許可）
        }

        public void Add(RpsColor c, float amount)
        {
            if (amount <= 0f) return;
            _g[c] = _g[c] + amount; // ★ clampしない
        }



        public float Get(RpsColor c) => _g[c];

        public bool IsCharged(RpsColor c) => _g[c] >= Max - 1e-6f;

        public bool TryConsumeCharged(RpsColor c)
        {
            if (_g[c] < Max - 1e-6f) return false;
            _g[c] = Math.Max(0f, _g[c] - Max);
            return true;
        }


        public int GetChargedCount(RpsColor c)
        {
            // 1.999999 を 2 と見なすためのε（IsCharged と同じ思想）
            double v = _g[c];
            double m = Max;
            if (m <= 1e-9) return 0;

            return (int)System.Math.Floor((v + 1e-6) / m);
        }

    }

    /// <summary>
    /// ゲージ増加量の式は調整弁なので Strategy で差し替え可能に。
    /// </summary>
    public interface IGaugeGainFormula
    {
        float CalcGain(RpsColor missingColor, DeckProfile playerProfile);
    }

    /// <summary>
    /// 既定：枚数/30（ただし係数と除数は後でConfig化しやすいように持たせる）
    /// </summary>
    public sealed class LinearGaugeGainFormula : IGaugeGainFormula
    {
        public float NumeratorScale = 1f;  // 調整弁：係数
        public float Denominator = 30f;    // 調整弁：除数（基本30）

        public float CalcGain(RpsColor missingColor, DeckProfile playerProfile)
        {
            float count = playerProfile.Get(missingColor);
            if (count <= 0) return 0f; // そもそも入ってない色は欠損対象外
            return (count / Denominator) * NumeratorScale;
        }
    }

    // =========================
    // Round simulation
    // =========================

    public sealed class RoundResult
    {
        public readonly int HandCount;
        public readonly List<RpsColor> PlayerHands;
        public readonly List<RpsColor> EnemyHands;
        public readonly List<RpsOutcome> Outcomes;
        public readonly int LossCount;
        public readonly bool IsClear;

        // 欠損（このラウンドで1度も引けなかった対象色）
        public readonly List<RpsColor> MissingColors;

        public RoundResult(
            int handCount,
            List<RpsColor> playerHands,
            List<RpsColor> enemyHands,
            List<RpsOutcome> outcomes,
            int lossCount,
            bool isClear,
            List<RpsColor> missingColors)
        {
            HandCount = handCount;
            PlayerHands = playerHands;
            EnemyHands = enemyHands;
            Outcomes = outcomes;
            LossCount = lossCount;
            IsClear = isClear;
            MissingColors = missingColors;
        }
    }

    public static class RoundSimulator
    {
        public static RoundResult Simulate(
            Deck playerDeck,
            Deck enemyDeck,
            int handCount,
            int loseThresholdExclusive,
            IRng rng,
            RpsColor? forcedFirstColorForPlayer = null,
            System.Collections.Generic.IReadOnlyList<RpsColor> forcedForPlayer = null)
        {
            var p = playerDeck.DrawBatch(handCount, rng,
                forcedFirst: forcedFirstColorForPlayer,
                forcedOrder: forcedForPlayer);
            var e = enemyDeck.DrawBatch(handCount, rng, forcedOrder: null);

            var outcomes = new List<RpsOutcome>(handCount);
            int losses = 0;

            // 欠損判定のための出現トラッキング
            var seen = new HashSet<RpsColor>();
            for (int i = 0; i < handCount; i++)
            {
                seen.Add(p[i]);

                var o = Judge(p[i], e[i]);
                outcomes.Add(o);
                if (o == RpsOutcome.Lose) losses++;
            }

            bool clear = losses < loseThresholdExclusive;

            // 欠損対象＝デッキに入っている色のみ
            var missing = new List<RpsColor>(3);
            foreach (var c in new[] { RpsColor.Gu, RpsColor.Choki, RpsColor.Pa })
            {
                if (playerDeck.Profile.Has(c) && !seen.Contains(c))
                    missing.Add(c);
            }

            return new RoundResult(handCount, p, e, outcomes, losses, clear, missing);
        }

        public static RoundResult ApplyHeavy_FirstLoseToWin(
            RoundResult rr,
            int loseThresholdExclusive,
            RpsColor onlyColor,
            out bool applied,
            out int changedIndex,
            out RpsColor changedPlayerColor)
        {
            applied = false;
            changedIndex = -1;
            changedPlayerColor = RpsColor.Gu;

            if (rr == null) return rr;
            if (rr.Outcomes == null || rr.PlayerHands == null) return rr;

            int firstLose = -1;
            for (int i = 0; i < rr.Outcomes.Count; i++)
            {
                if (rr.Outcomes[i] != RpsOutcome.Lose) continue;

                if (i >= rr.PlayerHands.Count) continue;
                if (rr.PlayerHands[i] != onlyColor) continue;

                firstLose = i;
                break;
            }
            if (firstLose < 0) return rr;


            var outcomes2 = new List<RpsOutcome>(rr.Outcomes);
            outcomes2[firstLose] = RpsOutcome.Win;

            int losses2 = 0;
            for (int i = 0; i < outcomes2.Count; i++)
                if (outcomes2[i] == RpsOutcome.Lose) losses2++;

            bool clear2 = losses2 < loseThresholdExclusive;

            applied = true;
            changedIndex = firstLose;
            if (firstLose < rr.PlayerHands.Count)
                changedPlayerColor = rr.PlayerHands[firstLose];

            return new RoundResult(
                rr.HandCount,
                rr.PlayerHands,
                rr.EnemyHands,
                outcomes2,
                losses2,
                clear2,
                rr.MissingColors
            );
        }

        public static RpsOutcome Judge(RpsColor player, RpsColor enemy)
        {
            if (player == enemy) return RpsOutcome.Tie;

            // Gu beats Choki, Choki beats Pa, Pa beats Gu
            return (player, enemy) switch
            {
                (RpsColor.Gu, RpsColor.Choki) => RpsOutcome.Win,
                (RpsColor.Choki, RpsColor.Pa) => RpsOutcome.Win,
                (RpsColor.Pa, RpsColor.Gu) => RpsOutcome.Win,
                _ => RpsOutcome.Lose
            };
        }

        public static RoundResult ApplyTwinTop_ChainSecondLoseToWin(
            RoundResult rr,
            int loseThresholdExclusive,
            RpsColor mainColor,
            RpsColor secondColor,
            List<int> changedIndices,
            out int winFlipCount)
        {
            winFlipCount = 0;
            if (changedIndices != null) changedIndices.Clear();

            if (rr == null) return rr;
            if (rr.PlayerHands == null || rr.Outcomes == null) return rr;

            int n = rr.PlayerHands.Count;
            if (rr.Outcomes.Count < n) n = rr.Outcomes.Count;
            if (n < 3) return rr;

            var outcomes2 = new List<RpsOutcome>(rr.Outcomes);

            // i = 2..n-1 が「3手目（最後）」＝ 123,234,... を見る
            for (int i = 2; i < n; i++)
            {
                var a = rr.PlayerHands[i - 2];
                var b = rr.PlayerHands[i - 1];
                var c = rr.PlayerHands[i];

                // 3手が main/second の交互になっているか
                bool isTripleChain =
                    (a == mainColor   && b == secondColor && c == mainColor) ||
                    (a == secondColor && b == mainColor   && c == secondColor);

                if (!isTripleChain) continue;

                // ★勝敗判定は最後（i）だけ
                if (outcomes2[i] == RpsOutcome.Lose)
                {
                    outcomes2[i] = RpsOutcome.Win;
                    winFlipCount++;

                    if (changedIndices != null)
                        changedIndices.Add(i);
                }
            }

            if (winFlipCount <= 0)
                return rr;

            int losses2 = 0;
            for (int i = 0; i < outcomes2.Count; i++)
                if (outcomes2[i] == RpsOutcome.Lose) losses2++;

            bool clear2 = losses2 < loseThresholdExclusive;

            return new RoundResult(
                rr.HandCount,
                rr.PlayerHands,
                rr.EnemyHands,
                outcomes2,
                losses2,
                clear2,
                rr.MissingColors
            );
        }



        public static RoundResult ApplyBalance_ChargedGaugeReplaceOneHand(
            RoundResult rr,
            int loseThresholdExclusive,
            DeckProfile playerProfile,
            RpsColor replaceColor,
            out bool applied,
            out int changedIndex,
            out RpsColor fromColor,
            out RpsColor toColor,
            out RpsOutcome resultAtIndex,
            out bool createdMissing)
        {
            applied = false;
            changedIndex = -1;
            fromColor = RpsColor.Gu;
            toColor = replaceColor;
            resultAtIndex = RpsOutcome.Tie;
            createdMissing = false;

            if (rr == null) return rr;
            if (rr.PlayerHands == null || rr.EnemyHands == null || rr.Outcomes == null) return rr;

            int handCount = rr.HandCount;
            if (handCount <= 0) return rr;

            int oldLosses = rr.LossCount;
            int oldMissingCount = rr.MissingColors != null ? rr.MissingColors.Count : 0;

            int bestIndex = -1;
            int bestMissingCount = oldMissingCount;
            int bestLossDelta = 0;

            for (int i = 0; i < handCount && i < rr.PlayerHands.Count; i++)
            {
                if (rr.PlayerHands[i] == replaceColor) continue;

                var p2 = new List<RpsColor>(rr.PlayerHands);
                p2[i] = replaceColor;

                var outcomes2 = new List<RpsOutcome>(handCount);
                int losses2 = 0;
                for (int k = 0; k < handCount && k < rr.EnemyHands.Count && k < p2.Count; k++)
                {
                    var o = Judge(p2[k], rr.EnemyHands[k]);
                    outcomes2.Add(o);
                    if (o == RpsOutcome.Lose) losses2++;
                }

                var seen = new HashSet<RpsColor>();
                for (int k = 0; k < handCount && k < p2.Count; k++)
                    seen.Add(p2[k]);

                int missingCount2 = 0;
                foreach (var c in new[] { RpsColor.Gu, RpsColor.Choki, RpsColor.Pa })
                {
                    if (playerProfile.Has(c) && !seen.Contains(c))
                        missingCount2++;
                }

                int lossDelta = oldLosses - losses2;

                bool improvesMissing = (missingCount2 > oldMissingCount);
                bool bestImprovesMissing = (bestMissingCount > oldMissingCount);

                if (improvesMissing && !bestImprovesMissing)
                {
                    bestIndex = i;
                    bestMissingCount = missingCount2;
                    bestLossDelta = lossDelta;
                    continue;
                }

                if (improvesMissing && bestImprovesMissing)
                {
                    bool candSafe = (lossDelta >= 0);
                    bool bestSafe = (bestLossDelta >= 0);

                    if (candSafe && !bestSafe)
                    {
                        bestIndex = i;
                        bestMissingCount = missingCount2;
                        bestLossDelta = lossDelta;
                        continue;
                    }

                    if (candSafe == bestSafe)
                    {
                        if (lossDelta > bestLossDelta)
                        {
                            bestIndex = i;
                            bestMissingCount = missingCount2;
                            bestLossDelta = lossDelta;
                            continue;
                        }
                        if (lossDelta == bestLossDelta && bestIndex >= 0 && i < bestIndex)
                        {
                            bestIndex = i;
                            bestMissingCount = missingCount2;
                            bestLossDelta = lossDelta;
                            continue;
                        }
                    }

                    continue;
                }

                if (!bestImprovesMissing)
                {
                    if (lossDelta > bestLossDelta)
                    {
                        bestIndex = i;
                        bestMissingCount = missingCount2;
                        bestLossDelta = lossDelta;
                        continue;
                    }
                    if (lossDelta == bestLossDelta && bestIndex >= 0 && i < bestIndex)
                    {
                        bestIndex = i;
                        bestMissingCount = missingCount2;
                        bestLossDelta = lossDelta;
                        continue;
                    }
                }
            }

            if (bestIndex < 0)
                return rr;

            // 最終確定の再計算
            var finalP = new List<RpsColor>(rr.PlayerHands);
            fromColor = finalP[bestIndex];
            finalP[bestIndex] = replaceColor;

            var finalOutcomes = new List<RpsOutcome>(handCount);
            int finalLosses = 0;
            var finalSeen = new HashSet<RpsColor>();

            for (int k = 0; k < handCount && k < rr.EnemyHands.Count && k < finalP.Count; k++)
            {
                finalSeen.Add(finalP[k]);
                var o = Judge(finalP[k], rr.EnemyHands[k]);
                finalOutcomes.Add(o);
                if (o == RpsOutcome.Lose) finalLosses++;
            }

            bool finalClear = finalLosses < loseThresholdExclusive;

            var finalMissing = new List<RpsColor>(3);
            foreach (var c in new[] { RpsColor.Gu, RpsColor.Choki, RpsColor.Pa })
            {
                if (playerProfile.Has(c) && !finalSeen.Contains(c))
                    finalMissing.Add(c);
            }

            applied = true;
            changedIndex = bestIndex;
            toColor = replaceColor;
            if (bestIndex < finalOutcomes.Count) resultAtIndex = finalOutcomes[bestIndex];
            createdMissing = (finalMissing.Count > oldMissingCount);

            return new RoundResult(
                handCount,
                finalP,
                rr.EnemyHands,
                finalOutcomes,
                finalLosses,
                finalClear,
                finalMissing
            );
        }

    }
}
