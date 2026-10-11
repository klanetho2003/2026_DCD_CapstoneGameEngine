using System;
using Data;
using UnityEngine.EventSystems;

/// <summary>
/// Stage 선택 목록의 버튼 1개 = Stage 1개. UI_StageSelect가 목록의 행 수만큼 생성한다.
/// 마우스 클릭과 패드·키보드 Submit이 같은 Select로 모인다.
/// 프리팹 규약: 최상위에 Button + Image, 하위에 Text_StageName(TMP_Text).
/// </summary>
public class UI_StageSelectItem : UI_FocusItem
{
    private enum Texts
    {
        Text_StageName,
    }

    private StageInfoData _info;
    private Action<StageInfoData> _onSelected;

    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        BindTexts(typeof(Texts));

        BindEvent(gameObject, OnClicked); // 마우스 클릭
        Submitted += Select;              // 패드·키보드 (UIFocusNavigator → Submit)

        return true;
    }

    public void SetInfo(StageInfoData info, Action<StageInfoData> onSelected)
    {
        Init(); // Awake보다 먼저 불려도 안전하도록

        _info = info;
        _onSelected = onSelected;
        GetText(Texts.Text_StageName).text = info.DisplayName;
    }

    private void OnClicked(PointerEventData _)
    {
        Select();
    }

    private void Select()
    {
        if (_info != null)
            _onSelected?.Invoke(_info);
    }
}