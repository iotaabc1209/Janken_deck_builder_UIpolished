// Assets/Scripts/Unity/ResultView.cs
using UnityEngine;
using TMPro;

public sealed class ResultView : MonoBehaviour
{
    [SerializeField] private TMP_Text resultText;

    private void Start()
    {
        resultText.text =
            $"RESULT\n\nScore: {SceneFlow.LastScore}\n\nPress Enter";
    }
}
