using System.Collections.Generic;
using Data;

/// <summary>
/// 맵 전역 오브젝트(MapData.MapObjects). 맵 수명 동안 존속 —
/// 스테이지 전환/사망 재시작과 무관하다. StageManager가 소유.
/// </summary>
public sealed class MapObjectSpawner
{
    private const string LogContext = "MapObject";

    private readonly List<CreatureBase> _spawned = new();

    public int Count { get { return _spawned.Count; } }

    public void Spawn(MapData map)
    {
        Despawn(); // 방어 — 중복 스폰 방지

        List<SpawnData> objects = map.MapObjects;
        if (objects == null)
            return;

        for (int i = 0; i < objects.Count; i++)
        {
            CreatureBase creature = CreatureSpawnHelper.Spawn(SpawnInfo.From(objects[i]), LogContext);
            if (creature != null)
                _spawned.Add(creature);
        }

        LogPrinter.Log($"[MapObjectSpawner] {map.Name}: 맵 전역 오브젝트 {_spawned.Count}/{objects.Count}개 스폰");
    }

    public void Despawn()
    {
        CreatureSpawnHelper.DespawnAll(_spawned);
    }
}