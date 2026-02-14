// Assets/Scripts/Unity/SceneFlow.cs
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class SceneFlow : MonoBehaviour
{
    public static int LastScore = 0;

    // Title用：開始
    public void GoToGame()
    {
        SceneManager.LoadScene("Game");
    }

    // Result用：タイトルへ
    public void GoToTitle()
    {
        SceneManager.LoadScene("Title");
    }

    // Gameシーンから呼ばれる
    public static void GoToResult(int score)
    {
        LastScore = score;
        SceneManager.LoadScene("Result");
    }
}
