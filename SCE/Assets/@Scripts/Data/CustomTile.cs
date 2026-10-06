using UnityEngine;
using UnityEngine.Tilemaps;
using static Define;

/// <summary>
/// 타일에 심는 배치 데이터
/// </summary>
[CreateAssetMenu(menuName = "MyTile", fileName = "Tile_")]
public class CustomTile : Tile
{
    [Tooltip("이 셀에 스폰할 오브젝트 (None이면 스폰 X)")]
    public EObjectType ObjectType;

    [Tooltip("타일 역할")]
    public ETileType TileType;

    [Tooltip("Monster/NPC일 때 스폰할 데이터 ID")]
    public int DataId;

    [Tooltip("식별용 — 런타임 미사용")]
    public string DisplayName;
}