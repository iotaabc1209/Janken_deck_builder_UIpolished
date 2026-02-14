using UnityEngine;

public static class TutorialSessionGate
{
    // このページを開いている間だけ生きるフラグ（WebGLのページ更新でリセット）
    public static bool Shown { get; private set; }

    public static bool TryConsume()
    {
        if (Shown) return false;
        Shown = true;
        return true;
    }

    // デバッグ用（必要なら）
    public static void ResetForDebug()
    {
        Shown = false;
    }

    // 起動時に確実に初期化（保険）
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Init()
    {
        Shown = false;
    }
}
