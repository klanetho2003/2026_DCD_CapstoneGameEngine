using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>별 생성 설정이며 인스펙터에 표시된다.</summary>
[Serializable]
public class StarFieldSettings
{
    [Tooltip("배치용 난수 씨앗. 같은 값이면 항상 같은 배치가 나온다.")]
    public int seed = 12345;

    [Tooltip("만들 별 개수")]
    [Min(0)] public int starCount = 150;

    [Tooltip("별끼리 최소 간격 (픽셀아트 칸 수). 클수록 듬성듬성해진다.")]
    [Min(0)] public int minSpacingInCells = 8;

    [Tooltip("밝기(투명도) 최소값 (0~1)")]
    [Range(0f, 1f)] public float minAlpha = 0.35f;

    [Tooltip("밝기(투명도) 최대값 (0~1)")]
    [Range(0f, 1f)] public float maxAlpha = 1.0f;

    [Tooltip("별 색 목록. 별마다 이 중 하나가 무작위로 골라진다. (투명도는 무시되고 위의 밝기 범위가 쓰인다)")]
    public List<Color> colors = new List<Color>
    {
        Color.white,
        Color.white,
        new Color32(0xE6, 0xED, 0xFF, 0xFF), // 아주 살짝 푸른 흰색
        new Color32(0xCC, 0xDA, 0xFF, 0xFF), // 푸른 흰색
    };

    [Tooltip("자리 하나를 찾을 때 시도 횟수. 별이 다 안 깔리면 늘리거나 최소 간격을 줄이세요.")]
    [Min(1)] public int maxAttemptsPerStar = 30;

    [Tooltip("기본 밝기를 Half Alpha로 고정할 별의 비율 (0~1). 0.2면 전체의 1/5. 0이면 사용하지 않는다.")]
    [Range(0f, 1f)] public float halfAlphaRatio = 0.2f;

    [Tooltip("Half Alpha Ratio 비율만큼 고른 별의 기본 밝기(투명도). 0.5 = 50%")]
    [Range(0f, 1f)] public float halfAlpha = 0.5f;
}

/// <summary>점 별(1칸 크기)을 화면 전체에 무작위로 깔아 UI Image로 만든다.</summary>
public class StarField
{
    /// <summary>생성한 별 목록이다.</summary>
    private readonly List<StarData> _stars = new List<StarData>();

    /// <summary>생성한 별 목록(위치, 기본 밝기, Image)이다.</summary>
    public IReadOnlyList<StarData> Stars => _stars;

    /// <summary>parent 아래에 별 Image 오브젝트들을 만든다.</summary>
    /// <param name="parent">별을 담을 RectTransform이며 Canvas 기준 해상도 크기, 중앙 피벗이어야 한다.</param>
    /// <param name="settings">별 개수·간격·밝기·색 설정이다.</param>
    /// <param name="grid">칸 좌표를 Canvas 좌표로 바꿀 격자다.</param>
    /// <param name="dotSprite">별 점 스프라이트(star_dot.png)다.</param>
    public void Build(RectTransform parent, StarFieldSettings settings, PixelGrid grid, Sprite dotSprite)
    {
        _stars.Clear();
        if (settings == null || settings.starCount <= 0)
            return;

        // UnityEngine.Random 대신 System.Random을 써서 다른 코드의 영향 없이 항상 같은 배치가 나오게 한다.
        var rng = new System.Random(settings.seed);

        float minA = Mathf.Min(settings.minAlpha, settings.maxAlpha);
        float maxA = Mathf.Max(settings.minAlpha, settings.maxAlpha);
        int spacing = Mathf.Max(0, settings.minSpacingInCells);
        int spacingSqr = spacing * spacing;
        int attempts = Mathf.Max(1, settings.maxAttemptsPerStar);

        var placed = new List<Vector2Int>(settings.starCount);

        for (int i = 0; i < settings.starCount; i++)
        {
            // 1) 다른 별과 최소 간격 이상 떨어진 칸 찾기
            bool found = false;
            Vector2Int cell = default;
            for (int a = 0; a < attempts; a++)
            {
                var candidate = new Vector2Int(rng.Next(0, grid.DesignWidth), rng.Next(0, grid.DesignHeight));
                if (IsFarEnough(candidate, placed, spacingSqr))
                {
                    cell = candidate;
                    found = true;
                    break;
                }
            }
            if (!found)
                continue; // 자리를 못 찾으면 이 별은 건너뜀

            placed.Add(cell);

            // 2) 밝기와 색 정하기
            float alpha = Mathf.Lerp(minA, maxA, (float)rng.NextDouble());
            Color baseColor = PickColor(settings.colors, rng);
            baseColor.a = 1f;

            // 3) Image 오브젝트 생성 (1칸 = pixelScale × pixelScale px, 중앙 피벗)
            int index = _stars.Count;
            var go = new GameObject($"Star_{index:000}", typeof(RectTransform), typeof(Image));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = grid.CellCenter(cell.x, cell.y);
            rect.sizeDelta = new Vector2(grid.PixelScale, grid.PixelScale);

            var image = go.GetComponent<Image>();
            image.sprite = dotSprite;
            image.raycastTarget = false; // 배경이므로 클릭을 막지 않는다 (SCE의 Img_Bg*와 같음)

            var star = new StarData(index, cell, baseColor, alpha, image);
            star.ResetBrightness(); // 기본 색 + 기본 밝기 적용
            _stars.Add(star);
        }

        if (_stars.Count < settings.starCount)
            LogPrinter.LogWarning($"[StarField] 별 {settings.starCount}개 중 {_stars.Count}개만 배치했습니다. 최소 간격을 줄이거나 시도 횟수를 늘려 보세요.");

        ApplyHalfAlpha(settings);
    }

    /// <summary>
    /// 배치가 끝난 별 중 halfAlphaRatio 비율만큼을 무작위로 골라 기본 밝기를 halfAlpha로 바꾼다.
    /// 배치용 난수와 따로 만든 난수를 써서 별 위치와 나머지 별의 밝기·색은 바뀌지 않는다.
    /// </summary>
    /// <param name="settings">비율과 밝기 값을 담은 별 설정이다.</param>
    private void ApplyHalfAlpha(StarFieldSettings settings)
    {
        int count = Mathf.RoundToInt(_stars.Count * Mathf.Clamp01(settings.halfAlphaRatio));
        if (count <= 0)
            return;

        // 같은 seed면 항상 같은 별이 골라지도록 seed에서 파생한 별도 난수를 쓴다.
        var rng = new System.Random(unchecked(settings.seed * 31 + 7));

        var indices = new List<int>(_stars.Count);
        for (int i = 0; i < _stars.Count; i++)
            indices.Add(i);

        // 앞에서부터 count개를 무작위로 섞어 고른다 (Fisher-Yates)
        for (int i = 0; i < count; i++)
        {
            int j = rng.Next(i, indices.Count);
            (indices[i], indices[j]) = (indices[j], indices[i]);

            var old = _stars[indices[i]];
            var star = new StarData(old.Index, old.Cell, old.BaseColor, Mathf.Clamp01(settings.halfAlpha), old.Image);
            star.ResetBrightness(); // 새 기본 밝기 적용
            _stars[indices[i]] = star;
        }
    }

    /// <summary>후보 칸이 이미 놓인 모든 별과 최소 간격 이상 떨어져 있는지 확인한다.</summary>
    private static bool IsFarEnough(Vector2Int candidate, List<Vector2Int> placed, int spacingSqr)
    {
        for (int i = 0; i < placed.Count; i++)
        {
            if ((placed[i] - candidate).sqrMagnitude < spacingSqr)
                return false;
        }
        return true;
    }

    /// <summary>색 목록에서 하나를 무작위로 고른다. 목록이 비어 있으면 흰색이다.</summary>
    private static Color PickColor(List<Color> colors, System.Random rng)
    {
        if (colors == null || colors.Count == 0)
            return Color.white;
        return colors[rng.Next(0, colors.Count)];
    }
}
