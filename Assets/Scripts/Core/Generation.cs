// Assets/Scripts/Core/Generation.cs
using System;
using System.Collections.Generic;

namespace RpsBuild.Core
{
    public enum EnemyArchetype
    {
        Heavy,      // 偏重（主軸1色が厚い）
        Balance,    // 均等寄り
        TwinTop     // 上位2色が厚い
    }

    [Serializable]
    public struct ArchetypeWeights
    {
        public float Heavy;
        public float Balance;
        public float TwinTop;

        public float Sum => Heavy + Balance + TwinTop;
    }

    public sealed class EnvironmentState
    {
        public readonly ArchetypeWeights Weights;
        public readonly string SeedTag; // 表示用。将来Seedを入れるならここに。

        public EnvironmentState(ArchetypeWeights weights, string seedTag = "")
        {
            Weights = weights;
            SeedTag = seedTag;
        }
    }

    public static class EnvironmentGenerator
    {
        public static EnvironmentState CreateDefault(ArchetypeWeights weights)
        {
            return new EnvironmentState(weights, seedTag: "");
        }

        public static EnemyArchetype RollArchetype(EnvironmentState env, IRng rng)
        {
            float sum = env.Weights.Sum;
            if (sum <= 0f) return EnemyArchetype.Heavy;

            float r = rng.Value01() * sum;
            if (r < env.Weights.Heavy) return EnemyArchetype.Heavy;
            r -= env.Weights.Heavy;
            if (r < env.Weights.Balance) return EnemyArchetype.Balance;
            return EnemyArchetype.TwinTop;
        }

        public static ArchetypeWeights ShuffleWeightsAcrossArchetypes(
            ArchetypeWeights w,
            IRng rng)
        {
            float[] ws = { w.Heavy, w.Balance, w.TwinTop };

            // Fisher–Yates shuffle
            for (int i = ws.Length - 1; i > 0; i--)
            {
                int j = rng.Range(0, i + 1);
                (ws[i], ws[j]) = (ws[j], ws[i]);
            }

            return new ArchetypeWeights
            {
                Heavy = ws[0],
                Balance = ws[1],
                TwinTop = ws[2]
            };
        }

    }

    /// <summary>
    /// 「型→内訳生成」を固定するジェネレータ。
    /// “60%枠はHeavy固定（主軸グー固定でもOK）”などはConfig側で制御しやすい。
    /// </summary>
    public sealed class ArchetypeDeckGenerator
    {
        // ここは後で調整弁にしやすいように public に置いておく
        public int HeavyMain = 20;    // 主軸色
        public int HeavySub1 = 5;     // 残り
        public int HeavySub2 = 5;

        public int BalanceEach = 10;

        public int TwinTopA = 14;
        public int TwinTopB = 14;
        public int TwinTopC = 2;

        public bool UseRangeTuning = false;

        // ---- Heavy ranges ----
        public int HeavyMainMin = 16;
        public int HeavyMainMax = 24;

        // subは「残りを2分割」するが、将来縛れるように範囲も用意
        public int HeavySubMin = 0;
        public int HeavySubMax = 14;

        // ---- Balance ranges ----
        public int BalanceMin = 8;
        public int BalanceMax = 14;

        // ---- TwinTop ranges ----
        public int TwinTopMainMin = 12;
        public int TwinTopMainMax = 20;

        // TopA - TopB の差（あなたの予定：1〜6）
        public int TwinTopDeltaMin = 1;
        public int TwinTopDeltaMax = 6;

        // TopC の許容範囲（任意：今は広め）
        public int TwinTopCMin = 0;
        public int TwinTopCMax = 10;


        public RpsColor DefaultHeavyMainColor = RpsColor.Gu; // ひとまず主軸グー固定

        public DeckProfile Generate(EnemyArchetype archetype, IRng rng)
        {
            return archetype switch
            {
                EnemyArchetype.Heavy => GenHeavy(rng),
                EnemyArchetype.Balance => GenBalance(rng),
                EnemyArchetype.TwinTop => GenTwinTop(rng),
                _ => GenHeavy(rng)
            };
        }


        private DeckProfile GenHeavy(IRng rng, RpsColor? forcedMainColor = null)
        {
            var main = forcedMainColor ?? DefaultHeavyMainColor;

            // サブ2色の割当
            var others = OtherTwo(main);

            if (!UseRangeTuning)
            {
                int a = HeavySub1;
                int b = HeavySub2;
                if (rng.Range(0, 2) == 1) (a, b) = (b, a);
                return MakeProfile(main, HeavyMain, others[0], a, others[1], b);
            }

            int mainCount = rng.Range(HeavyMainMin, HeavyMainMax + 1);
            int rem = 30 - mainCount;

            // rem を (subA, subB) に分ける。範囲で縛れるようにする。
            // subA を先に決め、subB は残り。
            // うまくいかなければ数回リトライ。
            for (int attempt = 0; attempt < 20; attempt++)
            {
                int subMin = Math.Max(0, Math.Min(HeavySubMin, rem));
                int subMax = Math.Max(0, Math.Min(HeavySubMax, rem));
                int subA = rng.Range(subMin, subMax + 1);
                int subB = rem - subA;

                if (subB < HeavySubMin || subB > HeavySubMax) continue;

                // subの割当順をランダムに
                if (rng.Range(0, 2) == 1)
                    return MakeProfile(main, mainCount, others[0], subA, others[1], subB);
                else
                    return MakeProfile(main, mainCount, others[0], subB, others[1], subA);
            }

            // フォールバック（安全）
            int fallbackA = rem / 2;
            int fallbackB = rem - fallbackA;
            return MakeProfile(main, mainCount, others[0], fallbackA, others[1], fallbackB);
        }

        private DeckProfile GenBalance(IRng rng, RpsColor? bannerColor = null)
        {
            if (!UseRangeTuning)
                return new DeckProfile(BalanceEach, BalanceEach, BalanceEach);

            // 2色を範囲で引いて、3色目を残りで決める（範囲外ならリトライ）
            for (int attempt = 0; attempt < 50; attempt++)
            {
                int a = rng.Range(BalanceMin, BalanceMax + 1);
                int b = rng.Range(BalanceMin, BalanceMax + 1);
                int c = 30 - a - b;
                if (c < BalanceMin || c > BalanceMax) continue;

                // どの色に a/b/c を割り当てるかは “看板色” があればそれを優先して見やすくする
                // bannerColor を a にする（残り2色に b/c を割当）
                if (bannerColor.HasValue)
                {
                    var main = bannerColor.Value;
                    var others = OtherTwo(main);
                    return MakeProfile(main, a, others[0], b, others[1], c);
                }

                // 看板色なしなら固定順
                return new DeckProfile(a, b, c);
            }

            return new DeckProfile(10, 10, 10);
        }

        private DeckProfile GenTwinTop(IRng rng, RpsColor mainColor, RpsColor secondColor)
        {
            var thirdColor = OtherTwo(mainColor)[0] == secondColor ? OtherTwo(mainColor)[1] : OtherTwo(mainColor)[0];

            if (!UseRangeTuning)
                return MakeProfile(mainColor, TwinTopA, secondColor, TwinTopB, thirdColor, TwinTopC);

            for (int attempt = 0; attempt < 50; attempt++)
            {
                int topA = rng.Range(TwinTopMainMin, TwinTopMainMax + 1);
                int delta = rng.Range(TwinTopDeltaMin, TwinTopDeltaMax + 1);
                int topB = topA - delta;
                if (topB < 0) continue;

                int topC = 30 - topA - topB;
                if (topC < TwinTopCMin || topC > TwinTopCMax) continue;

                return MakeProfile(mainColor, topA, secondColor, topB, thirdColor, topC);
            }

            // フォールバック
            int fa = 14, fb = 14, fc = 2;
            return MakeProfile(mainColor, fa, secondColor, fb, thirdColor, fc);
        }

        private DeckProfile GenTwinTop(IRng rng)
        {
            // 上位2色をランダムに選ぶ（旧挙動）
            var colors = new List<RpsColor> { RpsColor.Gu, RpsColor.Choki, RpsColor.Pa };
            for (int i = colors.Count - 1; i > 0; i--)
            {
                int j = rng.Range(0, i + 1);
                (colors[i], colors[j]) = (colors[j], colors[i]);
            }

            var main = colors[0];
            var second = colors[1];

            // 新シグネチャ版へ委譲
            return GenTwinTop(rng, main, second);
        }


        public DeckProfile GenerateHeavyFixed(IRng rng, RpsColor mainColor)
        {
            return GenHeavy(rng, forcedMainColor: mainColor);
        }

        public DeckProfile GenerateBalanceFixed(IRng rng, RpsColor bannerColor)
        {
            return GenBalance(rng, bannerColor: bannerColor);
        }

        public DeckProfile GenerateTwinTopFixed(IRng rng, RpsColor mainColor, RpsColor secondColor)
        {
            return GenTwinTop(rng, mainColor, secondColor);
        }


        private static DeckProfile MakeProfile(RpsColor c1, int n1, RpsColor c2, int n2, RpsColor c3, int n3)
        {
            int gu = 0, choki = 0, pa = 0;
            void Add(RpsColor c, int n)
            {
                switch (c)
                {
                    case RpsColor.Gu: gu += n; break;
                    case RpsColor.Choki: choki += n; break;
                    case RpsColor.Pa: pa += n; break;
                }
            }
            Add(c1, n1); Add(c2, n2); Add(c3, n3);

            var p = new DeckProfile(gu, choki, pa);
            if (p.Total != 30) throw new InvalidOperationException($"Generated profile must total 30, got {p.Total}");
            return p;
        }

        private static RpsColor[] OtherTwo(RpsColor main)
        {
            return main switch
            {
                RpsColor.Gu => new[] { RpsColor.Choki, RpsColor.Pa },
                RpsColor.Choki => new[] { RpsColor.Gu, RpsColor.Pa },
                _ => new[] { RpsColor.Gu, RpsColor.Choki }
            };
        }
    }
}
