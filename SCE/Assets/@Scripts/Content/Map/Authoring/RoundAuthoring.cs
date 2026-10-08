using UnityEngine;
using static Define;

/// <summary>
/// 라운드 루트 GameObject에 부착하는 저작 데이터. MapExporter가 읽어 RoundData로 기록한다.
/// 구역(StageAuthoring) 루트의 직계 자식이어야 하며, 이 오브젝트의 직계 자식 Tilemap_Wave_N이 이 라운드의 웨이브다.
/// 자체 동작은 없다. 런타임에는 MapView가 라운드 루트를 찾는 표식으로 쓴다. GameObject 이름은 규약과 무관한 자유 라벨.
/// </summary>
[DisallowMultipleComponent]
public sealed class RoundAuthoring : MonoBehaviour
{
    [SerializeField, Tooltip("이 오브젝트가 담당하는 라운드. 한 구역 안에 A·B·C가 하나씩 있어야 한다")]
    private ERound _round;

    public ERound Round { get { return _round; } }
}