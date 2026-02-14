// Assets/Scripts/Unity/ResolveHandsSequenceView.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using RpsBuild.Core;

public sealed class ResolveHandsSequenceView : MonoBehaviour
{
    [Header("Enemy / Player Icons (0..6)")]
    [SerializeField] private List<Image> enemyIcons = new();
    [SerializeField] private List<Image> playerIcons = new();

    [Header("Result Texts (0..6)")]
    [SerializeField] private List<TMP_Text> resultTexts = new();

    [Header("Sprites")]
    [SerializeField] private Sprite enemy_gu;
    [SerializeField] private Sprite enemy_choki;
    [SerializeField] private Sprite enemy_pa;
    [SerializeField] private Sprite me_gu;
    [SerializeField] private Sprite me_choki;
    [SerializeField] private Sprite me_pa;

    [Header("Result Colors")]
    [SerializeField] private Color winColor  = new Color(0x5B/255f, 0x8A/255f, 0xE3/255f, 1f); // #5B8AE3
    [SerializeField] private Color tieColor  = new Color(0x80/255f, 0x80/255f, 0x80/255f, 1f); // #808080
    [SerializeField] private Color loseColor = new Color(0xE3/255f, 0x58/255f, 0x58/255f, 1f); // #E35858
    [SerializeField] private Color bonusColor = new(1.0f, 0.85f, 0.25f); // 金系（好みで）


    [Header("Timing")]
    [SerializeField] private float stepDelay = 0.15f;

    private Coroutine _co;

    // ★追加：状態管理
    private bool _isPlaying = false;
    private bool _skipRequested = false;

    // ★追加：外から参照（RoundFlowUIで「スキップなら遷移しない」に使える）
    public bool IsPlaying => _isPlaying;


    private void Start()
    {
        FixResultTextMaterialsOnce();
        ResetView();
    }

    // ★追加：外からスキップ要求（再生中のみ有効）
    public bool RequestSkip()
    {
        if (!_isPlaying) return false;
        _skipRequested = true;
        return true;
    }

    // ===== Public API =====

    public void Play(
        IReadOnlyList<RpsColor> enemyHands,
        IReadOnlyList<RpsColor> playerHands,
        IReadOnlyList<RpsOutcome> outcomes,
        IReadOnlyList<int> highlightIndices = null)
    {
        StopSequence();     // ★ まず必ず止める
        ResetView();        // ★ 表示を完全初期化

        _skipRequested = false;
        _isPlaying = true;

        // HandsPanel が active であることは呼び元（RoundFlowUI）で保証
        _co = StartCoroutine(Sequence(enemyHands, playerHands, outcomes, highlightIndices));
    }

    // ===== Core =====

    private IEnumerator Sequence(
        IReadOnlyList<RpsColor> e,
        IReadOnlyList<RpsColor> p,
        IReadOnlyList<RpsOutcome> o,
        IReadOnlyList<int> highlightIndices)
    {
        int n = Mathf.Min(7,
            e.Count, p.Count, o.Count,
            enemyIcons.Count, playerIcons.Count, resultTexts.Count);

        for (int i = 0; i < n; i++)
        {
            bool highlight = IsHighlighted(i, highlightIndices);
            ShowOne(i, e[i], p[i], o[i], highlight);

            if (_skipRequested)
            {
                for (int j = i + 1; j < n; j++)
                {
                    bool h2 = IsHighlighted(j, highlightIndices);
                    ShowOne(j, e[j], p[j], o[j], h2);
                }
                break;
            }

            yield return new WaitForSeconds(stepDelay);
        }

        _co = null;
        _skipRequested = false;
        _isPlaying = false;
    }


    // ===== Helpers =====

    private void ShowOne(int i, RpsColor e, RpsColor p, RpsOutcome o, bool highlight)
    {
        // Enemy
        if (i < enemyIcons.Count && enemyIcons[i] != null)
        {
            enemyIcons[i].sprite = ToEnemySprite(e);
            enemyIcons[i].color = Color.white; // ★追加
            enemyIcons[i].gameObject.SetActive(true);
        }

        // Player
        if (i < playerIcons.Count && playerIcons[i] != null)
        {
            playerIcons[i].sprite = ToMeSprite(p);
            playerIcons[i].color = Color.white; // ★追加
            playerIcons[i].gameObject.SetActive(true);
        }

        // Result
        var t = resultTexts[i];
        if (t != null)
        {
            ApplyResult(t, o, highlight);
        }
    }

    private Sprite ToEnemySprite(RpsColor c) => c switch
    {
        RpsColor.Gu    => enemy_gu,
        RpsColor.Choki => enemy_choki,
        RpsColor.Pa    => enemy_pa,
        _ => null
    };

    private Sprite ToMeSprite(RpsColor c) => c switch
    {
        RpsColor.Gu    => me_gu,
        RpsColor.Choki => me_choki,
        RpsColor.Pa    => me_pa,
        _ => null
    };


    // ===== Helpers =====

    private void ResetView()
    {
        // Enemy icons
        for (int i = 0; i < enemyIcons.Count; i++)
        {
            var img = enemyIcons[i];
            if (img == null) continue;

            img.sprite = null;                 // ★追加：絵を消す
            img.color = new Color(1f,1f,1f,0f); // ★追加：透明にする（子構造対策）
            img.gameObject.SetActive(true);    // ★レイアウト安定（好みでfalseでもOK）
        }

        // Player icons
        for (int i = 0; i < playerIcons.Count; i++)
        {
            var img = playerIcons[i];
            if (img == null) continue;

            img.sprite = null;
            img.color = new Color(1f,1f,1f,0f);
            img.gameObject.SetActive(true);
        }

        // Result texts
        for (int i = 0; i < resultTexts.Count; i++)
        {
            var t = resultTexts[i];
            if (t == null) continue;

            t.text = "";
            t.color = Color.white;
            t.alpha = 0f;
        }
    }


    private void StopSequence()
    {
        if (_co != null)
        {
            StopCoroutine(_co);
            _co = null;
        }
        _isPlaying = false;
        _skipRequested = false;
    }

    void ApplyResult(TMP_Text t, RpsOutcome o, bool highlight)
    {
        if (t == null) return;

        t.enableVertexGradient = false;
        t.richText = false;                 // <color>タグが混ざってると上書きされる
        t.color = Color.white;              // いったん白でリセット

        if (highlight)
            {
                // outcomeに合わせて文字だけ変える（BalanceでDもあり得るので）
                t.text = o switch
                {
                    RpsOutcome.Win  => "★W",
                    RpsOutcome.Tie  => "★D",
                    _               => "★L"
                };
                t.color = bonusColor;
                t.alpha = 1f;
                return;
            }

        switch (o)
        {
            case RpsOutcome.Win:
                t.text = "W"; t.color = winColor; break;
            case RpsOutcome.Tie:
                t.text = "D"; t.color = tieColor; break;
            case RpsOutcome.Lose:
                t.text = "L"; t.color = loseColor; break;
        }

        t.alpha = 1f; // ★ ここで初めて見せる
    }


    private static bool IsHighlighted(int index, IReadOnlyList<int> list)
    {
        if (list == null) return false;
        for (int i = 0; i < list.Count; i++)
            if (list[i] == index) return true;
        return false;
    }

    private bool _fixedTmpMaterials = false;

    private void FixResultTextMaterialsOnce()
    {
        if (_fixedTmpMaterials) return;
        _fixedTmpMaterials = true;

        for (int i = 0; i < resultTexts.Count; i++)
        {
            var t = resultTexts[i];
            if (t == null) continue;

            // 共有マテリアルを壊さないように複製して差し替え
            var src = t.fontMaterial;
            if (src == null) continue;

            var mat = new Material(src);
            // FaceColor が白じゃないと t.color が乗算されて暗くなる
            mat.SetColor(ShaderUtilities.ID_FaceColor, Color.white);

            // （任意）アウトライン色が原因で暗く見える場合の保険
            // mat.SetColor(ShaderUtilities.ID_OutlineColor, Color.black);

            t.fontMaterial = mat;

            // ついでに、グラデONだと色が変わるので切る
            t.enableVertexGradient = false;
        }
    }


}
