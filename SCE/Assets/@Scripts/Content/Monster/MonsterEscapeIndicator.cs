using UnityEngine;
using static Define;

/// <summary>추가 UI 프리팹 없이 몬스터 머리 위에 탈출 예고 아이콘과 남은 초를 표시한다.</summary>
public sealed class MonsterEscapeIndicator : MonoBehaviour
{
    /// <summary>표시할 탈출 준비 상태를 제공하는 몬스터다.</summary>
    private MonsterBase _owner;

    /// <summary>예고 표시의 글자 크기와 정렬을 보관하는 재사용 화면 스타일이다.</summary>
    private GUIStyle _style;

    /// <summary>같은 오브젝트의 몬스터를 찾아 표시 대상으로 연결한다.</summary>
    private void Awake()
    {
        _owner = GetComponent<MonsterBase>();
    }

    /// <summary>플레이 중 탈출을 준비하는 몬스터의 월드 위치를 화면 좌표로 바꾸어 예고를 그린다.</summary>
    private void OnGUI()
    {
        if (_owner == null || _owner.MovementController == null || _owner.IsDead ||
            Managers.Stage.Phase != EStagePhase.Playing ||
            _owner.MovementController.State != MonsterMovementState.PreparingEscape)
            return;

        // 월드의 몬스터 위치를 화면으로 투영할 현재 메인 카메라다.
        Camera camera = Camera.main;
        if (camera == null)
            return;
        if (_style == null)
        {
            _style = new GUIStyle(GUI.skin.box);
            _style.fontSize = 18;
            _style.alignment = TextAnchor.MiddleCenter;
            _style.normal.textColor = Color.yellow;
        }

        // 몬스터 머리 위로 띄운 표시 지점의 화면 좌표다.
        Vector3 screen = camera.WorldToScreenPoint(transform.position + Vector3.up * 1.1f);
        if (screen.z <= 0f)
            return;
        // 4, 3, 2, 1 순서로 표시할 남은 초의 올림 값이다.
        int seconds = Mathf.CeilToInt(_owner.MovementController.PreparationRemaining);
        GUI.Box(new Rect(screen.x - 30f, Screen.height - screen.y - 24f, 60f, 30f),
            "! " + seconds, _style);
    }
}
