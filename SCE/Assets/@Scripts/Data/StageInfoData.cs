using System;
using System.Collections.Generic;

namespace Data
{
    /// <summary>Stage 선택 UI의 한 항목 = Stage Prefab 1개.</summary>
    [Serializable]
    public class StageInfoData
    {
        /// <summary>목록 안에서 유일. 저장 데이터나 해금 조건의 키로 쓸 값이므로 한 번 정하면 바꾸지 않는다.</summary>
        public int StageId;

        /// <summary>선택 UI에 표시할 이름. 비우면 MapDataKey로 대신한다.</summary>
        public string DisplayName = "";

        /// <summary>Stage Prefab(맵 루트)의 Addressable 키.</summary>
        public string MapPrefabKey = "";

        /// <summary>MapExporter가 만든 JSON의 Addressable 키 (= 맵 루트 이름).</summary>
        public string MapDataKey = "";
    }

    /// <summary>StageCatalog.json의 루트.</summary>
    [Serializable]
    public class StageCatalogData
    {
        public List<StageInfoData> Stages = new();
    }
}