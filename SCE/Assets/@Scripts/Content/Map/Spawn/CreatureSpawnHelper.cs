using System.Collections.Generic;
using UnityEngine;
using static Define;

/// <summary>
/// 셀 기반 크리처 스폰/디스폰의 단일 경로 (스테이지 오브젝트 / 맵 전역 오브젝트 공용).
/// 스폰 정책(Occupancy, footprint 검사)이 생기면 여기에만 추가한다.
/// </summary>
public static class CreatureSpawnHelper
{
    /// <param name="context">에러 로그용 출처 표기. 호출부에서 1회 만들어 재사용할 것 (스폰마다 문자열 할당 방지).</param>
    public static CreatureBase Spawn(in SpawnInfo info, string context)
    {
        /*Managers.Data.CreatureDataDic.TryGetValue(info.DataId, out var data);
        int footprint = data != null ? data.footprintCells : 0;
        if (Managers.Map.CanMoveTo(info.Cell, footprint, ETraversalMask.Ground) == false)
        {
            LogPrinter.Log($"[{context}] 스폰 불가 셀: {info.Cell} DataId={info.DataId}");
            return null;
        }*/

        // info.DataId → dataId
        // 스폰 요청 단계 — Instantiate 전 (프리팹이 CreatureData에서 결정되므로 Spawn 전에 Hook)
        int dataId = SpawnRequestResolver.Resolve(info.DataId, info.Cell, context);

        Vector2 world = Managers.Map.CellCenterToWorld(info.Cell);
        CreatureBase spawned;
        switch (info.ObjectType)
        {
            case EObjectType.Villager:
                spawned = Managers.Object.Spawn<Villager>(dataId, world);
                break;
            case EObjectType.Monster:
                spawned = Managers.Object.Spawn<MonsterBase>(dataId, world);   
                break;
            case EObjectType.NPC:
                spawned = Managers.Object.Spawn<NPC>(dataId, world);
                break;
            default:
                LogPrinter.LogError($"[{context}] 스폰 미지원 타입 {info.ObjectType} (DataId={info.DataId})");
                return null;
        }

        // Spawned 단계 — 파생 클래스까지 SetInfo가 끝난 뒤, 같은 프레임
        if (spawned != null && spawned.Interaction != null)
            spawned.Interaction.NotifySpawned();

        /*if (spawned != null)
            Managers.Map.Occupancy.UpdatePresence(spawned, info.Cell);*/

        return spawned;
    }

    /// <summary>유효한 대상만 디스폰하고 목록을 비운다.</summary>
    public static void DespawnAll<T>(List<T> list) where T : CreatureBase
    {
        for (int i = 0; i < list.Count; i++)
        {
            T creature = list[i];
            if (creature.IsValid() == false)
                continue;
            if (creature is Villager villager && villager.IsPossessed)
                continue;

            Managers.Object.Despawn(creature);
        }
        list.Clear();
    }
}