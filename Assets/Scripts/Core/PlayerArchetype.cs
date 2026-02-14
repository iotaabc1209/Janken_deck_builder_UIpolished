// Assets/Scripts/Core/PlayerArchetype.cs
namespace RpsBuild.Core
{
    // プレイヤー用：敵とは別にしておく（将来ズレても安全）
    public enum PlayerArchetype
    {
        Heavy,   // 偏重（役割集中）
        Balance, // バランス（対面）
        TwinTop  // ツートップ（積みサイクル）
    }

    // 分類結果（UI表示やボーナス判定で使う）
    public readonly struct PlayerArchetypeInfo
    {
        public readonly PlayerArchetype Archetype;
        public readonly RpsColor MainColor;
        public readonly RpsColor SecondColor; // TwinTop時に意味がある。その他はMainと同じでもOK

        public PlayerArchetypeInfo(PlayerArchetype archetype, RpsColor main, RpsColor second)
        {
            Archetype = archetype;
            MainColor = main;
            SecondColor = second;
        }
    }

    /// <summary>
    /// DeckProfile(Gu/Choki/Pa) をプレイヤーアーキタイプに分類する。
    /// しきい値は将来Tuningに移せるよう、引数で渡せる形にしてある（最小差分）。
    /// </summary>
    public static class PlayerArchetypeClassifier
    {
        /// <summary>
        /// 分類して Main/Second を返す。
        /// - Heavy: max >= heavyMin
        /// - TwinTop: top1 >= twinTop1Min && top2 >= twinTop2Min && (top1-top2) <= twinDeltaMax
        /// - else: Balance
        /// </summary>
        public static PlayerArchetypeInfo Classify(
            DeckProfile p,
            int heavyMin = 18,
            int twinTop1Min = 13,
            int twinTop2Min = 11,
            int twinDeltaMax = 4)
        {
            int gu = p.Gu;
            int ch = p.Choki;
            int pa = p.Pa;

            // top1/top2/top3 を決める（タイは色優先順で安定化：Gu > Choki > Pa）
            GetSorted3(gu, ch, pa,
                out var c1, out var n1,
                out var c2, out var n2,
                out var c3, out var n3);

            // Heavy（偏重）
            if (n1 >= heavyMin)
                return new PlayerArchetypeInfo(PlayerArchetype.Heavy, c1, c1);

            // TwinTop（ツートップ）
            if (n1 >= twinTop1Min && n2 >= twinTop2Min && (n1 - n2) <= twinDeltaMax)
                return new PlayerArchetypeInfo(PlayerArchetype.TwinTop, c1, c2);

            // Balance（バランス）
            return new PlayerArchetypeInfo(PlayerArchetype.Balance, c1, c2);
        }

        private static void GetSorted3(
            int gu, int ch, int pa,
            out RpsColor c1, out int n1,
            out RpsColor c2, out int n2,
            out RpsColor c3, out int n3)
        {
            // 初期順（タイの優先順位を固定：Gu->Choki->Pa）
            c1 = RpsColor.Gu;    n1 = gu;
            c2 = RpsColor.Choki; n2 = ch;
            c3 = RpsColor.Pa;    n3 = pa;

            // 3要素の降順ソート（安定：同値なら順序維持）
            if (n2 > n1) Swap(ref c1, ref n1, ref c2, ref n2);
            if (n3 > n2) Swap(ref c2, ref n2, ref c3, ref n3);
            if (n2 > n1) Swap(ref c1, ref n1, ref c2, ref n2);
        }

        private static void Swap(ref RpsColor aC, ref int aN, ref RpsColor bC, ref int bN)
        {
            (aC, bC) = (bC, aC);
            (aN, bN) = (bN, aN);
        }


    }
}
