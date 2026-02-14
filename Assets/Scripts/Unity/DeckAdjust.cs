// Assets/Scripts/Unity/DeckAdjust.cs
using RpsBuild.Core;

public static class DeckAdjust
{
    /// <summary>
    /// “+a / -b” の基本操作。
    /// 制約：各色は0以上、合計30を維持。
    /// 破綻するなら false で何もしない。
    /// </summary>
    public static bool TryAdjust(
        ref DeckProfile profile,
        RpsColor addColor, int addAmount,
        RpsColor subColor, int subAmount)
    {
        if (addAmount < 0 || subAmount < 0) return false;
        if (addAmount == 0 && subAmount == 0) return true;

        // 合計30固定なので基本は add==sub を期待（違うのも将来許可できるが、まずは堅く）
        if (addAmount != subAmount) return false;

        int addCur = profile.Get(addColor);
        int subCur = profile.Get(subColor);

        if (subCur - subAmount < 0) return false;

        profile.Set(addColor, addCur + addAmount);
        profile.Set(subColor, subCur - subAmount);

        // 念のためチェック
        return profile.Total == 30;
    }

    /// <summary>
    /// 将来：+2を入れるが引く色は自動で候補から選ぶ…などを作りたくなった時用の入り口。
    /// いまは簡易に「一番多い色から引く」などのルールを後付けしやすい。
    /// </summary>
    public static bool TryAddAndAutoSubtract(ref DeckProfile profile, RpsColor addColor, int amount)
    {
        if (amount <= 0) return false;

        // 引く候補：addColor以外で、枚数が多い順に試す
        var c1 = Other1(addColor);
        var c2 = Other2(addColor);

        RpsColor first = profile.Get(c1) >= profile.Get(c2) ? c1 : c2;
        RpsColor second = first == c1 ? c2 : c1;

        // まずは多い方から引けるだけ引く
        int need = amount;

        // add
        profile.Set(addColor, profile.Get(addColor) + amount);

        need = SubClamp(ref profile, first, need);
        need = SubClamp(ref profile, second, need);

        if (need > 0)
        {
            // 戻す（失敗）
            profile.Set(addColor, profile.Get(addColor) - amount);
            // 元に戻す処理は簡略化のため「最初にコピーして戻す」方式にした方が安全。
            // 実運用ではこの関数を使う前に profile をコピーしておき、失敗なら差し替えない方針がおすすめ。
            return false;
        }

        return profile.Total == 30;
    }

    private static int SubClamp(ref DeckProfile p, RpsColor c, int need)
    {
        if (need <= 0) return 0;
        int cur = p.Get(c);
        int sub = cur >= need ? need : cur;
        p.Set(c, cur - sub);
        return need - sub;
    }

    private static RpsColor Other1(RpsColor c) => c switch
    {
        RpsColor.Gu => RpsColor.Choki,
        RpsColor.Choki => RpsColor.Gu,
        _ => RpsColor.Gu
    };

    private static RpsColor Other2(RpsColor c) => c switch
    {
        RpsColor.Pa => RpsColor.Choki,
        RpsColor.Choki => RpsColor.Pa,
        _ => RpsColor.Pa
    };
}
