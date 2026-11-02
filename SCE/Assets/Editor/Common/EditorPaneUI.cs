using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 에디터 툴 공용 — JSON 파일 편집 창의 화면 조각 (UI Toolkit). 상태를 갖지 않는다 — 만든 요소는 부르는 쪽이 갖는다.
/// </summary>
public static class EditorPaneUI
{
    public const string RowMainName = "main"; // FileRow 왼쪽 글자의 이름 — 표시 이름 · 배지
    public const string RowFileName = "file"; // FileRow 오른쪽 글자의 이름 — 파일 이름 (흐리게)

    private static readonly Color DividerColor = new Color(0f, 0f, 0f, 0.35f); // 머리글 · 머리줄의 아래 줄

    /// <summary>영역 머리글 — 굵게, 아래 줄.</summary>
    public static Label Header(string text)
    {
        var label = new Label(text);
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        label.style.paddingLeft = 6;
        label.style.paddingTop = 4;
        label.style.paddingBottom = 4;
        label.style.borderBottomWidth = 1;
        label.style.borderBottomColor = DividerColor;
        return label;
    }

    /// <summary>안내 글 — 흐리게, 줄 바꿈.</summary>
    public static Label Hint(string text)
    {
        var label = new Label(text);
        label.style.paddingLeft = 8;
        label.style.paddingTop = 8;
        label.style.opacity = 0.6f;
        label.style.whiteSpace = WhiteSpace.Normal;
        return label;
    }

    /// <summary>파일 목록의 행 (ListView makeItem) — 왼쪽 글자(RowMainName) · 오른쪽 흐린 글자(RowFileName). bindItem이 Q&lt;Label&gt;(이름)으로 찾아 채운다.</summary>
    public static VisualElement FileRow()
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.paddingLeft = 8;
        row.style.paddingRight = 6;

        var main = new Label { name = RowMainName };
        main.style.flexGrow = 1;
        main.style.unityTextAlign = TextAnchor.MiddleLeft;
        row.Add(main);

        var file = new Label { name = RowFileName };
        file.style.opacity = 0.5f;
        file.style.unityTextAlign = TextAnchor.MiddleRight;
        row.Add(file);
        return row;
    }

    /// <summary>목록 위 머리줄 — 굵은 제목(남는 폭을 차지)과 아래 줄. 칸 · 버튼은 부르는 쪽이 줄에 더한다. 제목 글자는 titleLabel로 바꾼다.</summary>
    public static VisualElement HeaderRow(string title, out Label titleLabel)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.borderBottomWidth = 1;
        row.style.borderBottomColor = DividerColor;

        titleLabel = new Label(title);
        titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        titleLabel.style.paddingLeft = 6;
        titleLabel.style.flexGrow = 1;
        row.Add(titleLabel);
        return row;
    }

    /// <summary>
    /// 누르면 그곳으로 가는 목록 행 (ListView makeItem) — 누를 때마다 jump(행 번호). 행 번호는 bindItem이 userData에 int로 넣는다.
    /// selectionChanged는 같은 항목을 다시 누르면 오지 않아서 클릭 이벤트를 쓴다.
    /// </summary>
    public static Label JumpRow(Action<int> jump)
    {
        var label = new Label();
        label.style.unityTextAlign = TextAnchor.MiddleLeft;
        label.RegisterCallback<ClickEvent>(_ =>
        {
            if (label.userData is int row)
                jump(row);
        });
        return label;
    }

    /// <summary>파싱에 실패한 파일의 보기 — 제목(에러 색) · 경로 · 오류(읽기 전용) · 안내 · [텍스트 편집기로 열기](openAsText).</summary>
    public static VisualElement BrokenFileView(string title, string filePath, string error, string hint, Action openAsText)
    {
        var box = new VisualElement();
        box.style.paddingLeft = 8;
        box.style.paddingRight = 8;
        box.style.paddingTop = 8;

        var titleLabel = new Label(title);
        titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        titleLabel.style.color = EditorStatusColors.Error;
        titleLabel.style.whiteSpace = WhiteSpace.Normal;
        box.Add(titleLabel);
        box.Add(new Label(filePath) { style = { opacity = 0.6f, marginBottom = 6 } });

        var errorField = new TextField { value = error, multiline = true, isReadOnly = true };
        errorField.style.whiteSpace = WhiteSpace.Normal;
        box.Add(errorField);

        box.Add(Hint(hint));
        box.Add(new Button(openAsText) { text = "텍스트 편집기로 열기" });
        return box;
    }
}