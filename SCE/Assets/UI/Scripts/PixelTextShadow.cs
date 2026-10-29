using TMPro;
using UnityEngine;

// 도트 폰트(Bitmap 셰이더)는 TMP 그림자를 못 쓴다.
// 그래서 본문 글자(source) 바로 앞 형제로 그림자 글자를 두고, 내용·위치·표시 여부를 따라가게 한다.
// 코드는 본문 글자(Txt_*)만 바꾸면 된다.
[ExecuteAlways]
[RequireComponent(typeof(TMP_Text))]
public class PixelTextShadow : MonoBehaviour
{
    [SerializeField] TMP_Text source;
    [SerializeField] Vector2 offset = new Vector2(1, -1);

    TMP_Text _shadow;

    public void Init(TMP_Text src, Vector2 shadowOffset)
    {
        source = src;
        offset = shadowOffset;
        Sync();
    }

    void LateUpdate()
    {
        Sync();
    }

    // 본문 글자의 내용·위치·표시 여부를 그림자에 맞춘다
    public void Sync()
    {
        if (source == null)
            return;
        if (_shadow == null)
            _shadow = GetComponent<TMP_Text>();

        bool visible = source.isActiveAndEnabled;
        if (_shadow.enabled != visible)
            _shadow.enabled = visible;
        if (!visible)
            return;

        if (_shadow.text != source.text) _shadow.text = source.text;
        if (_shadow.font != source.font) _shadow.font = source.font;
        if (_shadow.fontSize != source.fontSize) _shadow.fontSize = source.fontSize;
        if (_shadow.alignment != source.alignment) _shadow.alignment = source.alignment;

        var color = _shadow.color;
        if (color.a != source.color.a)
        {
            color.a = source.color.a;
            _shadow.color = color;
        }

        var src = (RectTransform)source.transform;
        var rt = (RectTransform)transform;
        rt.anchorMin = src.anchorMin;
        rt.anchorMax = src.anchorMax;
        rt.pivot = src.pivot;
        rt.sizeDelta = src.sizeDelta;
        rt.anchoredPosition = src.anchoredPosition + offset;
    }
}
