using System.Collections;
using UnityEngine;

/// <summary>
/// DEV 전용 — 맵 없이 기믹 연출을 시연하는 화면 버튼. DEV_GimmickFx 씬(Tools/DEV/Build Gimmick FX Sandbox)에 놓인다.
/// 실제 게임의 흐름을 흉내 낸다:
/// - 홀로그램: 버튼 Press → 문양 흐려짐 → 박스 등장 → (적 도달 대신) 등장 직후부터 3~5초 뒤 박스 소멸
/// - 붕괴: Crack.json과 같은 순서 — 칸을 나갈 때(TileExit)마다 Step, 밟은 횟수가 3 이상이면 Break
/// </summary>
public sealed class GimmickFxSandbox : MonoBehaviour
{
    [Header("홀로그램")]
    [SerializeField] private GridButton _button;
    [SerializeField] private SpriteFadeFx _spawnMark;
    [SerializeField] private BoxAppearFx _box;

    [SerializeField, Range(0f, 1f), Tooltip("문양이 이만큼 흐려졌을 때 박스 등장을 시작 (0 = 동시, 1 = 다 사라진 뒤)")]
    private float _boxStartAt = 0.5f;

    [SerializeField, Tooltip("등장 뒤 자동으로 사라지게 할지 (지연은 박스의 소멸 지연 범위 사용)")]
    private bool _autoVanish = true;

    [Header("붕괴")]
    [SerializeField] private GridCrumbleTile[] _crumbleTiles;

    [SerializeField, Min(1), Tooltip("Crack.json의 CrumbleTileSteps 값 (이 횟수 이상이면 부서짐)")]
    private int _breakAtSteps = 3;

    /// <summary>박스 등장을 기다리는 코루틴이다.</summary>
    private Coroutine _hologram;
    /// <summary>화면 안내에 보일 마지막 소멸 지연(초)이다.</summary>
    private float _lastVanishDelay;
    /// <summary>소멸 지연을 시작한 시각이다.</summary>
    private float _vanishStartTime = -1f;

    /// <summary>버튼을 누른 것처럼 홀로그램 흐름을 시작한다.</summary>
    public void PressButton()
    {
        if (_button == null || _button.IsPressed)
            return;
        _button.Press();
        if (_spawnMark != null)
            _spawnMark.PlayFadeOut();
        if (_hologram != null)
            StopCoroutine(_hologram);
        _hologram = StartCoroutine(AppearBoxAfter(_spawnMark != null ? _spawnMark.Duration * _boxStartAt : 0f));
    }

    /// <summary>문양이 흐려지는 도중에 박스 등장을 시작한다.</summary>
    private IEnumerator AppearBoxAfter(float delay)
    {
        if (delay > 0f)
            yield return new WaitForSeconds(delay);
        _hologram = null;
        if (_box == null)
            yield break;
        _box.PlayAppear(() =>
        {
            if (!_autoVanish)
                return;
            // 실제 게임에서는 "적이 박스에 도달" 이벤트가 이 호출을 맡는다.
            _lastVanishDelay = _box.RandomVanishDelay;
            _vanishStartTime = Time.time;
            _box.PlayVanishAfter(_lastVanishDelay, () => _vanishStartTime = -1f);
        });
    }

    /// <summary>모든 부서지는 발판에서 플레이어가 한 번 나간 것처럼 처리한다 (Crack.json 순서).</summary>
    public void ExitCrumbleTiles()
    {
        if (_crumbleTiles == null)
            return;
        foreach (GridCrumbleTile tile in _crumbleTiles)
        {
            if (tile == null || tile.IsBroken)
                continue;
            tile.Step();
            if (tile.StepCount >= _breakAtSteps)
                tile.Break(Vector2Int.zero); // 맵이 없으므로 칸 값은 쓰이지 않는다
        }
    }

    /// <summary>처음 상태로 돌린다. 버튼 · 발판은 풀에서 다시 꺼낼 때와 같은 경로(OnEnable)로 초기화한다.</summary>
    public void ResetAll()
    {
        if (_hologram != null)
            StopCoroutine(_hologram);
        _hologram = null;
        _vanishStartTime = -1f;
        if (_button != null)
            Respawn(_button.gameObject);
        if (_spawnMark != null)
            _spawnMark.ResetVisual();
        if (_box != null)
            _box.ResetVisual(false);
        if (_crumbleTiles == null)
            return;
        foreach (GridCrumbleTile tile in _crumbleTiles)
        {
            if (tile != null)
                Respawn(tile.gameObject);
        }
    }

    /// <summary>껐다 켜서 OnEnable 초기화를 다시 탄다.</summary>
    private static void Respawn(GameObject go)
    {
        go.SetActive(false);
        go.SetActive(true);
    }

    /// <summary>시연용 화면 버튼과 상태 안내.</summary>
    private void OnGUI()
    {
        // scale은 고해상도 화면에서도 버튼이 너무 작지 않게 할 배율이다.
        float scale = Mathf.Max(1f, Screen.height / 720f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

        GUILayout.BeginArea(new Rect(12f, 12f, 280f, 400f), GUI.skin.box);
        GUILayout.Label("<b>Gimmick FX Sandbox</b>", new GUIStyle(GUI.skin.label) { richText = true });

        GUILayout.Space(4f);
        GUILayout.Label("[홀로그램]");
        GUI.enabled = _button != null && !_button.IsPressed;
        if (GUILayout.Button("버튼 누르기", GUILayout.Height(32f)))
            PressButton();
        GUI.enabled = true;
        if (_vanishStartTime >= 0f && _box != null && _box.IsVanishPending)
            GUILayout.Label($"박스 소멸까지 {Mathf.Max(0f, _lastVanishDelay - (Time.time - _vanishStartTime)):0.0}초");

        GUILayout.Space(8f);
        GUILayout.Label("[붕괴]");
        if (GUILayout.Button("발판 밟고 나가기 (TileExit)", GUILayout.Height(32f)))
            ExitCrumbleTiles();
        if (_crumbleTiles != null && _crumbleTiles.Length > 0 && _crumbleTiles[0] != null)
        {
            GridCrumbleTile first = _crumbleTiles[0];
            GUILayout.Label(first.IsBroken ? "부서짐" : $"밟은 횟수 {first.StepCount} / {_breakAtSteps}");
        }

        GUILayout.Space(8f);
        if (GUILayout.Button("처음으로", GUILayout.Height(28f)))
            ResetAll();
        GUILayout.EndArea();
    }
}
