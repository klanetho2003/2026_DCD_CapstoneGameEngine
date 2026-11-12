using System.Collections.Generic;
using TMPro;
using UnityEngine;
using static Define;

/// <summary>
/// 글자 메뉴(Btn_* 아래 Txt_Label + Img_Cursor)의 선택 표시.
/// 선택된 항목만 흰 글씨 + 화살표, 나머지는 회색(#B4B8C4).
/// 마우스를 올리면(PointerEnter) 그 항목이 선택된다.
///   _menu = new UIMenuSelection(GetObject(...), GetObject(...));
///   _menu.BindHover();
/// </summary>
public class UIMenuSelection
{
    static readonly Color SelectedColor = Color.white;
    static readonly Color UnselectedColor = new Color32(0xB4, 0xB8, 0xC4, 0xFF);

    readonly List<GameObject> _items = new List<GameObject>();

    public int Index { get; private set; } = -1;

    public UIMenuSelection(params GameObject[] items)
    {
        foreach (var item in items)
            if (item != null)
                _items.Add(item);
        Select(0);
    }

    /// <summary>각 항목에 마우스를 올리면 그 항목을 선택</summary>
    public void BindHover()
    {
        for (int i = 0; i < _items.Count; i++)
        {
            int index = i;
            _items[i].BindEvent((evt) => Select(index), UIEvent.PointerEnter);
        }
    }

    public void Select(int index)
    {
        if (index < 0 || index >= _items.Count)
            return;

        Index = index;
        for (int i = 0; i < _items.Count; i++)
        {
            bool selected = i == index;

            var label = _items[i].transform.Find("Txt_Label");
            if (label != null && label.TryGetComponent(out TMP_Text text))
                text.color = selected ? SelectedColor : UnselectedColor;

            var cursor = _items[i].transform.Find("Img_Cursor");
            if (cursor != null)
                cursor.gameObject.SetActive(selected);
        }
    }
}
