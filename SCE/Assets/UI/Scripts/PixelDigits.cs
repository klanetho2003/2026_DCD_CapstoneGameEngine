using UnityEngine;
using UnityEngine.UI;

// digits_s*_*.png 시트(0 1 2 3 4 5 6 7 8 9 s /)로 숫자를 그린다.
// 코드는 Text 만 바꾸면 된다. 예: GetComponent<PixelDigits>().Text = "21";
// 색을 바꿀 때(예: 10초 이하 빨강)는 SetGlyphs 로 다른 색 시트를 넣는다.
[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class PixelDigits : MonoBehaviour
{
    const string Chars = "0123456789s/";

    [SerializeField] Sprite[] glyphs = new Sprite[12];
    [SerializeField] int advance = 12;
    [SerializeField] string text = "0";

    public string Text
    {
        get => text;
        set
        {
            if (text == value)
                return;
            text = value;
            Rebuild();
        }
    }

    public void SetGlyphs(Sprite[] sheet, int glyphAdvance)
    {
        glyphs = sheet;
        advance = glyphAdvance;
        Rebuild();
    }

    void OnEnable()
    {
        Rebuild();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        // 프리팹 원본(에셋)에서는 다시 그리지 않는다. 컴파일할 때마다 원본 파일이 다시 저장되는 것을 막는다
        if (UnityEditor.PrefabUtility.IsPartOfPrefabAsset(this))
            return;
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null)
                Rebuild();
        };
    }
#endif

    public void Rebuild()
    {
        if (glyphs == null || glyphs.Length < Chars.Length || glyphs[0] == null)
            return;

        var cell = glyphs[0].rect.size;
        var rt = (RectTransform)transform;
        rt.sizeDelta = new Vector2(Mathf.Max(text.Length, 1) * advance, cell.y);

        for (int i = 0; i < Mathf.Max(text.Length, transform.childCount); i++)
        {
            var image = GetGlyphImage(i);
            int index = i < text.Length ? Chars.IndexOf(text[i]) : -1;
            image.gameObject.SetActive(index >= 0);
            if (index < 0)
                continue;

            image.sprite = glyphs[index];
            var grt = image.rectTransform;
            grt.anchorMin = grt.anchorMax = Vector2.zero;
            grt.pivot = Vector2.zero;
            grt.sizeDelta = cell;
            grt.anchoredPosition = new Vector2(i * advance, 0);
        }

        if (transform.parent is RectTransform parent)
            LayoutRebuilder.MarkLayoutForRebuild(parent);
    }

    Image GetGlyphImage(int i)
    {
        if (i < transform.childCount)
            return transform.GetChild(i).GetComponent<Image>();

        var go = new GameObject("Glyph_" + i, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.layer = gameObject.layer;
        go.transform.SetParent(transform, false);
        var image = go.GetComponent<Image>();
        image.raycastTarget = false;
        return image;
    }
}
