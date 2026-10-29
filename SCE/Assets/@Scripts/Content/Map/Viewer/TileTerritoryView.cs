using Data;
using UnityEngine;
using UnityEngine.Tilemaps;
using static Define;

/// <summary>맵 프리팹마다 지정한 스프라이트로 점령 전용 타일맵을 표시한다.</summary>
[DisallowMultipleComponent]
public sealed class TileTerritoryView : MonoBehaviour
{
    /// <summary>기존 맵에서 시각적 바닥을 식별하는 타일맵 이름이다.</summary>
    private const string VISUAL_TILEMAP = "Tilemap_Visual";
    /// <summary>이 스테이지에서 플레이어 점령을 표시할 분할 스프라이트다.</summary>
    [SerializeField, Tooltip("플레이어 점령 타일의 스프라이트")]
    private Sprite _playerSprite;
    /// <summary>이 스테이지에서 몬스터 점령을 표시할 분할 스프라이트다.</summary>
    [SerializeField, Tooltip("몬스터 점령 타일의 스프라이트")]
    private Sprite _monsterSprite;
    /// <summary>점령 표시를 그릴 정렬 레이어이며 일반 캐릭터와 같은 레이어를 사용한다.</summary>
    [SerializeField, Tooltip("점령 표시와 바닥을 그릴 Sorting Layer 이름")]
    private string _sortingLayerName = "Default";
    /// <summary>캐릭터의 기본 순서 0보다 뒤에서 점령 표시를 그릴 순서다.</summary>
    [SerializeField, Tooltip("캐릭터보다 작고 바닥보다 큰 표시 순서")]
    private int _sortingOrder = -1;
    /// <summary>맵 데이터의 원점과 칸 크기를 그대로 사용하는 점령 격자다.</summary>
    private Grid _grid;
    /// <summary>점령한 칸만 표시하며 중립 칸은 비워 두는 전용 타일맵이다.</summary>
    private Tilemap _tilemap;
    /// <summary>모든 플레이어 점령 칸이 공유하는 충돌 없는 표시 타일이다.</summary>
    private Tile _playerTile;
    /// <summary>모든 몬스터 점령 칸이 공유하는 충돌 없는 표시 타일이다.</summary>
    private Tile _monsterTile;

    /// <summary>소유 타일을 그리는 전용 타일맵을 검증과 외부 표시 설정에 제공한다.</summary>
    public Tilemap DisplayTilemap => _tilemap;

    /// <summary>물리 좌표와 일치하는 전용 격자를 만들고 바닥과 캐릭터 사이에 표시한다.</summary>
    /// <param name="spec">맵 데이터에서 읽은 격자 원점과 한 칸 크기다.</param>
    public void Initialize(in GridSpec spec)
    {
        if (_tilemap != null)
            return;
        if (_playerSprite == null || _monsterSprite == null)
            Debug.LogWarning("[Territory] 맵의 플레이어 및 몬스터 점령 스프라이트를 지정하세요.", this);
        // gridObject는 맵의 기존 저작 격자와 분리하여 논리 셀 좌표를 그대로 사용하는 표시 루트다.
        var gridObject = new GameObject("Grid_Territory", typeof(Grid));
        gridObject.transform.SetParent(transform, false);
        gridObject.transform.position = new Vector3(spec.OriginX, spec.OriginY, 0f);
        _grid = gridObject.GetComponent<Grid>();
        _grid.cellSize = new Vector3(spec.CellSize, spec.CellSize, 1f);
        // tilemapObject는 충돌체 없이 점령 표시만 그릴 자식 오브젝트다.
        var tilemapObject = new GameObject("Tilemap_Territory", typeof(Tilemap), typeof(TilemapRenderer));
        tilemapObject.transform.SetParent(gridObject.transform, false);
        _tilemap = tilemapObject.GetComponent<Tilemap>();
        // renderer는 캐릭터를 가리지 않도록 정렬 정보를 적용할 표시 컴포넌트다.
        TilemapRenderer renderer = tilemapObject.GetComponent<TilemapRenderer>();
        renderer.sortingLayerName = _sortingLayerName;
        renderer.sortingOrder = _sortingOrder;
        PlaceGroundBehindTerritory(renderer);
        _playerTile = CreateDisplayTile(_playerSprite);
        _monsterTile = CreateDisplayTile(_monsterSprite);
        _playerTile.transform = Matrix4x4.Scale(new Vector3(spec.CellSize, spec.CellSize, 1f));
        _monsterTile.transform = _playerTile.transform;
    }

    /// <summary>스테이지의 두 진영 스프라이트를 교체하고 이미 표시한 칸도 즉시 갱신한다.</summary>
    /// <param name="playerSprite">플레이어 점령 표시로 사용할 스프라이트다.</param>
    /// <param name="monsterSprite">몬스터 점령 표시로 사용할 스프라이트다.</param>
    public void SetSprites(Sprite playerSprite, Sprite monsterSprite)
    {
        _playerSprite = playerSprite;
        _monsterSprite = monsterSprite;
        if (_tilemap == null)
            return;
        _playerTile.sprite = _playerSprite;
        _monsterTile.sprite = _monsterSprite;
        _tilemap.RefreshAllTiles();
    }

    /// <summary>소유권이 바뀐 한 칸만 갱신하고 중립은 표시를 지워 원래 바닥을 드러낸다.</summary>
    /// <param name="cell">논리 격자에서 점령 표시를 바꿀 타일 좌표다.</param>
    /// <param name="owner">표시할 소유 진영이다.</param>
    public void SetOwner(Vector2Int cell, TileOwner owner)
    {
        if (_tilemap == null)
            return;
        _tilemap.SetTile(new Vector3Int(cell.x, cell.y, 0),
            owner == TileOwner.Player ? _playerTile : owner == TileOwner.Monster ? _monsterTile : null);
    }

    /// <summary>다음 라운드에 이전 점령 색상이 남지 않도록 표시 타일을 전부 비운다.</summary>
    public void Clear()
    {
        if (_tilemap != null)
            _tilemap.ClearAllTiles();
    }

    /// <summary>한 진영의 모든 점령 칸에서 재사용하는 표시 전용 타일을 만든다.</summary>
    /// <param name="sprite">표시 타일에 사용할 분할 스프라이트다.</param>
    private static Tile CreateDisplayTile(Sprite sprite)
    {
        // tile은 기존 충돌 및 스폰 타일과 독립적인 런타임 표시 에셋이다.
        Tile tile = ScriptableObject.CreateInstance<Tile>();
        tile.hideFlags = HideFlags.DontSave;
        tile.sprite = sprite;
        tile.colliderType = Tile.ColliderType.None;
        return tile;
    }

    /// <summary>바닥 타일맵끼리의 앞뒤 관계를 유지하면서 점령 표시 뒤로 이동시킨다.</summary>
    /// <param name="territoryRenderer">기존 바닥의 재질과 앞뒤 관계를 연결할 점령 표시 컴포넌트다.</param>
    private void PlaceGroundBehindTerritory(TilemapRenderer territoryRenderer)
    {
        // renderers는 비활성 라운드를 포함하여 한 번 수집하는 기존 타일맵 표시 목록이다.
        TilemapRenderer[] renderers = GetComponentsInChildren<TilemapRenderer>(true);
        // highestOrder는 기존 바닥 중 가장 앞에 있던 표시 순서다.
        int highestOrder = int.MinValue;
        // groundMaterial은 현재 렌더 파이프라인에 맞는 기존 바닥의 공유 재질이다.
        Material groundMaterial = null;
        // renderer는 벽과 스폰 마커를 제외하고 바닥 이름으로 검사할 표시 컴포넌트다.
        foreach (TilemapRenderer renderer in renderers)
        {
            if (IsGround(renderer) && renderer.sortingLayerID == territoryRenderer.sortingLayerID)
            {
                highestOrder = Mathf.Max(highestOrder, renderer.sortingOrder);
                if (groundMaterial == null)
                    groundMaterial = renderer.sharedMaterial;
            }
        }
        if (groundMaterial != null)
            territoryRenderer.sharedMaterial = groundMaterial;
        if (highestOrder < _sortingOrder)
            return;
        // offset은 기존 바닥의 상대적인 표시 순서를 보존하는 공통 이동량이다.
        int offset = _sortingOrder - 1 - highestOrder;
        // renderer는 점령 표시보다 뒤로 옮길 기존 바닥 표시 컴포넌트다.
        foreach (TilemapRenderer renderer in renderers)
        {
            if (IsGround(renderer) && renderer.sortingLayerID == territoryRenderer.sortingLayerID)
                renderer.sortingOrder += offset;
        }
    }

    /// <summary>기존 맵 규약에서 시각적 바닥과 라운드 영역 바닥만 골라낸다.</summary>
    /// <param name="renderer">바닥 여부를 검사할 타일맵 표시 컴포넌트다.</param>
    private static bool IsGround(TilemapRenderer renderer)
    {
        return renderer.name == VISUAL_TILEMAP || renderer.name == MAP_TILEMAP_TERRAIN;
    }

    /// <summary>맵이 파괴되면 런타임에 만든 두 표시 타일의 메모리도 해제한다.</summary>
    private void OnDestroy()
    {
        ReleaseTile(_playerTile);
        ReleaseTile(_monsterTile);
    }

    /// <summary>실행 중과 에디터 검증 모두에서 임시 타일 에셋을 안전하게 해제한다.</summary>
    /// <param name="tile">맵 이탈 시 해제할 런타임 표시 타일이다.</param>
    private static void ReleaseTile(Tile tile)
    {
        if (tile == null)
            return;
        if (Application.isPlaying)
            Destroy(tile);
        else
            DestroyImmediate(tile);
    }
}
