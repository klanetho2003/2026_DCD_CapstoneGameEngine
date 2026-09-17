using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Play 모드에서 선택한 NPC의 조건별 평가 결과를 실시간 표시한다.
/// 편집 모드나 Watch 대상이 없으면 안내만 띄운다.
/// </summary>
public sealed class InteractionDebugPanel : VisualElement
{
    private static readonly Color TrueColor = new Color(0.45f, 0.85f, 0.5f);
    private static readonly Color FalseColor = new Color(0.9f, 0.45f, 0.45f);

    private readonly Label _status = new Label();
    private readonly VisualElement _body = new VisualElement();
    private CreatureBase _watchedOwner;
    private InteractionComponent _watchedComponent;


    public InteractionDebugPanel()
    {
        var header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.Add(new Label("Play 디버그") { style = { unityFontStyleAndWeight = FontStyle.Bold, paddingLeft = 6, flexGrow = 1 } });
        header.Add(new Button(WatchSelection) { text = "선택 NPC 감시" });
        header.Add(new Button(StopWatch) { text = "해제" });
        Add(header);

        _status.style.paddingLeft = 8;
        _status.style.opacity = 0.6f;
        Add(_status);

        _body.style.paddingLeft = 8;
        Add(_body);

        // 툴 창이 비활성이어도 값이 갱신되도록 에디터 업데이트에 붙인다
        schedule.Execute(Refresh).Every(100);
    }

    private void WatchSelection()
    {
        if (EditorApplication.isPlaying == false)
        {
            _status.text = "Play 모드에서만 동작합니다";
            return;
        }

        GameObject go = Selection.activeGameObject;
        InteractionComponent component = go != null ? go.GetComponent<InteractionComponent>() : null;
        if (component == null)
        {
            _status.text = "Hierarchy에서 InteractionComponent가 붙은 오브젝트를 선택해 주세요";
            return;
        }

        _watchedOwner = go.GetComponent<CreatureBase>();
        _watchedComponent = component;
        InteractionDiagnostics.Watch(component);
        _status.text = $"감시 중: {go.name}";
    }

    private void StopWatch()
    {
        _watchedOwner = null;
        InteractionDiagnostics.StopWatching();
        _body.Clear();
        _status.text = "";
    }

    private void Refresh()
    {
        if (EditorApplication.isPlaying == false || _watchedOwner == null)
        {
            if (_body.childCount > 0) _body.Clear();
            return;
        }

        InteractionComponent component = _watchedOwner.Interaction;
        InteractionDiagnostics.Record record = InteractionDiagnostics.Current;

        _body.Clear();
        for (int i = 0; i < component.RuntimeCount; i++)
        {
            InteractionRuntime rt = component.GetRuntime(i);
            if (rt == null) continue;

            bool pass = i < record.Pass.Count && record.Pass[i];

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop = 2;

            var title = new Label($"{(pass ? "●" : "○")} {rt.Definition.Id}  [{rt.Definition.Trigger}]");
            title.style.color = pass ? TrueColor : FalseColor;
            title.style.width = 220;
            row.Add(title);

            InteractionCondition[] conditions = rt.Definition.Conditions;
            bool[] results = i < record.ConditionResults.Count ? record.ConditionResults[i] : null;
            for (int c = 0; c < conditions.Length; c++)
            {
                bool value = results != null && c < results.Length && results[c];
                var chip = new Label(ShortName(conditions[c]));
                chip.style.color = value ? TrueColor : FalseColor;
                chip.style.marginRight = 6;
                row.Add(chip);
            }

            if (rt.IsActive)
                row.Add(new Label("ACTIVE") { style = { opacity = 0.7f, marginLeft = 6 } });
            if (rt.ActivationCount > 0)
                row.Add(new Label($"×{rt.ActivationCount}") { style = { opacity = 0.5f, marginLeft = 4 } });

            _body.Add(row);
        }

        _status.text = $"감시 중: {_watchedOwner.name}  (frame {record.LastFrame})";
    }

    private static string ShortName(InteractionCondition condition)
    {
        return InteractionNodeRegistry.TryGetKey(condition.GetType(), out string key) ? key : condition.GetType().Name;
    }
}