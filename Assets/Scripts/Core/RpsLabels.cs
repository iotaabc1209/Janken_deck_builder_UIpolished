// Assets/Scripts/Core/RpsLabels.cs
namespace RpsBuild.Core
{
    /// <summary>
    /// UI表示専用の日本語ラベル変換
    /// enum自体は英語のままにする
    /// </summary>
    public static class RpsLabels
    {
        // ---- 手（色） ----
        public static string ToJa(this RpsColor c)
        {
            return c switch
            {
                RpsColor.Gu => "グー",
                RpsColor.Choki => "チョキ",
                RpsColor.Pa => "パー",
                _ => "？"
            };
        }

        // ---- 敵アーキタイプ ----
        public static string ToJa(this EnemyArchetype a)
        {
            return a switch
            {
                EnemyArchetype.Heavy => "偏重",
                EnemyArchetype.Balance => "バランス",
                EnemyArchetype.TwinTop => "２トップ",
                _ => "不明"
            };
        }

        /// <summary>
        /// UI表示用：「手＋アーキタイプ」
        /// 例）グー偏重 / チョキバランス / パーツートップ
        /// </summary>
        public static string ToJaLabel(this EnemyArchetype a, RpsColor mainColor)
        {
            return $"{mainColor.ToJa()}{a.ToJa()}";
        }

        // ---- プレイヤーアーキタイプ ----
        /// <summary>
        /// UI表示用（プレイヤー）
        /// 例）グー偏重 / グーチョキツートップ / バランス
        /// </summary>
        public static string ToJaLabel(this PlayerArchetypeInfo info)
            {
                return info.Archetype switch
                   {
                       PlayerArchetype.Heavy
                           => $"{info.MainColor.ToJa()}偏重",

                       PlayerArchetype.TwinTop
                           => $"{info.MainColor.ToJa()}{info.SecondColor.ToJa()}ツートップ",

                       PlayerArchetype.Balance
                           => "バランス",

                       _ => "不明"
                   };
            }


    }
}
