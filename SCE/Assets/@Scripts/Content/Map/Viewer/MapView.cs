using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using static Define;

/// <summary>
/// 스폰된 Stage Prefab(맵 루트)의 표시 담당. MapManager가 소유.
///
/// 책임:
/// - Stage Prefab 스폰 / 해제
/// - 스폰 마커 타일맵(Tilemap_Stage_Object, Tilemap_Map_Object, Tilemap_Wave_N)을 항상 끈다
/// - 라운드 루트(RoundAuthoring) 중 지정한 라운드만 켠다
///
/// 맵 데이터와 좌표 변환은 MapManager, 라운드 진행은 StageManager 책임 — 여기서는 "무엇이 보이는가"만 다룬다.
/// 현재 보이는 라운드는 따로 저장하지 않는다. 각 루트의 activeSelf가 유일한 원본.
/// </summary>
public sealed class MapView
{
    private readonly GameObject[] _roundRoots = new GameObject[ROUND_COUNT]; // 인덱스 = (int)ERound
    private readonly List<Tilemap> _tilemapBuffer = new();                   // 스폰 시 수집용 — 재사용
    private readonly List<RoundAuthoring> _roundBuffer = new();

    private GameObject _root;

    public bool IsSpawned { get { return _root != null; } }

    /// <summary>스폰된 프리팹에 라운드 루트가 하나라도 있는가.</summary>
    public bool HasRoundRoots { get; private set; }

    #region Spawn / Despawn
    public bool Spawn(string prefabKey)
    {
        Despawn(); // 방어 — 이전 맵이 남아 있으면 정리

        if (string.IsNullOrEmpty(prefabKey))
        {
            LogPrinter.LogError("[MapView] Stage Prefab 키가 비어 있음");
            return false;
        }

        GameObject root = Managers.Resource.Instantiate(prefabKey);
        if (root == null)
        {
            LogPrinter.LogError($"[MapView] Stage Prefab 스폰 실패 >> {prefabKey}. Addressable 등록 확인.");
            return false;
        }

        // Export 좌표는 맵 루트가 원점·무회전일 때를 기준으로 구워진다
        root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        _root = root;

        HideSpawnMarkers(root);
        CollectRoundRoots(root);
        HideAllRounds(); // 라운드가 시작되기 전에는 어느 라운드의 것도 보이지 않는다

        return true;
    }

    public void Despawn()
    {
        if (_root != null) // Scene 전환으로 이미 파괴된 경우에도 안전 (Unity의 null 비교)
            Managers.Resource.Destroy(_root);

        _root = null;
        Array.Clear(_roundRoots, 0, _roundRoots.Length);
        HasRoundRoots = false;
    }
    #endregion

    #region Round
    /// <summary>지정한 라운드의 루트만 켜고 나머지는 끈다. 라운드 루트가 없는 맵에서는 아무 일도 하지 않는다.</summary>
    public void ShowRound(ERound round)
    {
        if (_root == null)
            return;

        int target = (int)round;
        if (HasRoundRoots && _roundRoots[target] == null)
            LogPrinter.LogError($"[MapView] Round {round}의 루트가 프리팹에 없음 — 프리팹과 Export 결과가 맞는지 확인");

        for (int i = 0; i < _roundRoots.Length; i++)
            SetRoundActive(i, i == target);
    }

    public void HideAllRounds()
    {
        for (int i = 0; i < _roundRoots.Length; i++)
            SetRoundActive(i, false);
    }

    private void SetRoundActive(int index, bool active)
    {
        GameObject roundRoot = _roundRoots[index];
        if (roundRoot == null)
            return;
        if (roundRoot.activeSelf != active) // 값이 같으면 호출하지 않는다
            roundRoot.SetActive(active);
    }
    #endregion

    #region 스폰 직후 1회 처리
    /// <summary>
    /// 스폰 마커 타일맵을 끈다. 마커는 Exporter가 읽는 저작 데이터이고, 실제 개체는 코드가 스폰한다.
    /// 자신이 비활성이므로 이후 부모(라운드 루트)가 켜져도 보이지 않는다.
    /// </summary>
    private void HideSpawnMarkers(GameObject root)
    {
        root.GetComponentsInChildren(true, _tilemapBuffer);
        for (int i = 0; i < _tilemapBuffer.Count; i++)
        {
            Tilemap tilemap = _tilemapBuffer[i];
            if (IsSpawnMarker(tilemap.name)) // name 접근은 문자열을 할당한다 — 스폰 시 타일맵당 1회뿐
                tilemap.gameObject.SetActive(false);
        }
        _tilemapBuffer.Clear(); // 맵이 파괴된 뒤의 참조를 들고 있지 않도록
    }

    private static bool IsSpawnMarker(string tilemapName)
    {
        return tilemapName == MAP_TILEMAP_STAGE_OBJECT
            || tilemapName == MAP_TILEMAP_MAP_OBJECT
            || tilemapName.StartsWith(MAP_TILEMAP_WAVE_PREFIX, StringComparison.Ordinal);
    }

    private void CollectRoundRoots(GameObject root)
    {
        Array.Clear(_roundRoots, 0, _roundRoots.Length);
        HasRoundRoots = false;

        root.GetComponentsInChildren(true, _roundBuffer);
        for (int i = 0; i < _roundBuffer.Count; i++)
        {
            RoundAuthoring authoring = _roundBuffer[i];
            int index = (int)authoring.Round;

            // 아래 두 경우는 Exporter가 에러로 막는다. Export하지 않은 프리팹이 들어온 경우의 방어
            if ((uint)index >= (uint)_roundRoots.Length)
            {
                LogPrinter.LogError($"[MapView] '{authoring.name}': Round 값 {index}이(가) 범위 밖 — 숨김 처리");
                authoring.gameObject.SetActive(false);
                continue;
            }
            if (_roundRoots[index] != null)
            {
                LogPrinter.LogError($"[MapView] Round {authoring.Round} 중복 — '{_roundRoots[index].name}' 사용, '{authoring.name}' 숨김 처리");
                authoring.gameObject.SetActive(false);
                continue;
            }

            _roundRoots[index] = authoring.gameObject;
            HasRoundRoots = true;
        }
        _roundBuffer.Clear();
    }
    #endregion
}