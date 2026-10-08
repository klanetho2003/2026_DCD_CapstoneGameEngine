using System;
using System.Collections.Generic;
using UnityEngine;
using static Define;

namespace Data
{
    /// <summary>한 라운드의 스폰 데이터. 라운드 루트(RoundAuthoring) 아래 Tilemap_Wave_N이 변환된 것.</summary>
    [Serializable]
    public class RoundData
    {
        public ERound Round;
        public List<List<SpawnData>> Waves = new(); // [i] = 이 라운드의 i번 웨이브
    }

    /// <summary>
    /// 전장 맵 구조. BattleMapExporter가 생성, MapManager가 소비.
    /// </summary>
    [Serializable]
    public class MapData
    {
        public string Name;
        public GridSpec Grid;
        public List<string> CollisionRows;
        public List<StageData> Stages = new();

        /// <summary>맵 전역 오브젝트 (맵 루트 Tilemap_Object). 스테이지 Load/Unload와 무관하게 맵 수명 동안 존속.</summary>
        public List<SpawnData> MapObjects = new();

        /// <summary>
        /// 셀 >> Stage Key 영역 그리드.
        /// - 크기: Grid.Width * Grid.Height
        /// - '.' = 통로, 그 외 글자는 StageRegionLegend로 Stage Key 조회
        /// - Key == STAGE_REGION_NONE(-1): 어느 스테이지도 아님 (통로)
        /// 인코딩/디코딩은 StageRegionCodec만 사용한다.
        /// </summary>
        public List<string> StageRegionRows = new();

        /// <summary>기호(1글자) > Stage Key. 디코딩 원본.</summary>
        public Dictionary<string, int> StageRegionLegend = new();
    }

    /// <summary>
    /// 전장 격자의 순수 데이터
    /// 
    /// 좌표 규약:
    /// - (OriginX, OriginZ) >> cell (0,0)의 월드 최소 모서리 (XZ 평면)
    /// - cell (x,z)의 중심 >> Origin + (x + 0.5, z + 0.5) * CellSize
    /// </summary>
    [Serializable]
    public struct GridSpec
    {
        public float OriginX;
        public float OriginY;
        public float CellSize;
        public int Width;    // X축 셀 개수
        public int Height;   // Z축 셀 개수
    }

    /// <summary>Tilemap_Object의 CustomTile 한 개가 변환된 스폰 정보.</summary>
    [Serializable]
    public class SpawnData
    {
        public EObjectType ObjectType;
        public int DataId;
        public int CellX, CellY;
    }

    [Serializable]
    public class StageSettingsData
    {
        [Tooltip("구역 종류 — 입력 ActionMap 등 구역별 처리의 분기 기준")]
        public EZoneType ZoneType = EZoneType.Battlefield;

        [Tooltip("표시 이름 — UI/로그용. 비워도 됨")]
        public string DisplayName = "";
    }

    public class StageData
    {
        public int Key;
        public StageSettingsData Settings = new();

        public bool HasPlayerSpawn;
        public SpawnInfo PlayerSpawnInfo;

        public List<SpawnData> AlwaysSpawn = new();     // NPC 등 상시
        public List<List<SpawnData>> Waves = new();     // [i] = i번 웨이브

        /// <summary>A → B → C 순서. 비어 있으면 라운드를 쓰지 않는 구역 (기존 맵 — JSON에 없으면 초기값 유지).</summary>
        public List<RoundData> Rounds = new();
    }
}