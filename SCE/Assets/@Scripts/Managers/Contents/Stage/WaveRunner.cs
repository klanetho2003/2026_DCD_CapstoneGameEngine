using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 한 스테이지의 웨이브 순차 진행. Stage가 소유, 스폰 실행은 Stage 콜백에 위임.
/// 
/// 진행: 웨이브 스폰 > 생존 집합 추적 > 전멸(Count 0) > 다음 웨이브 > 마지막 클리어 시 OnAllWavesCleared
/// </summary>
public class WaveRunner
{
    private readonly HashSet<CombatCreature> _alive = new();
    private readonly Action<CombatCreature> _onCreatureDied;


    private StageSpawnData _data;
    private int _currentWaveIndex = -1;

    private Func<IReadOnlyList<SpawnInfo>, IReadOnlyList<CombatCreature>> _spawnWave;
    private Action _onAllCleared;
    private Action<int> _onWaveStarted;

    /// <summary>진행 중(또는 Stop 직전까지 진행하던) 웨이브. Stop 후에도 유지.</summary>
    public int CurrentWaveIndex { get { return _currentWaveIndex; } }
    public int WaveCount { get { return _data != null ? _data.Waves.Count : 0; } }
    public bool IsRunning { get; private set; }
    public bool AllCleared { get; private set; }

    public WaveRunner()
    {
        _onCreatureDied = OnCreatureDied;
    }

    /// <param name="onWaveStarted">웨이브가 스폰되어 전멸 대기에 들어갈 때 인덱스와 함께 호출. 빈 웨이브는 통지하지 않는다.</param>
    public void SetInfo(StageSpawnData data,
        Func<IReadOnlyList<SpawnInfo>, IReadOnlyList<CombatCreature>> spawnWave,
        Action onAllCleared,
        Action<int> onWaveStarted = null)
    {
        _data = data;
        _spawnWave = spawnWave;
        _onAllCleared = onAllCleared;
        _onWaveStarted = onWaveStarted;
    }

    /// <summary>첫 웨이브 시작 — Stage 진입 시.</summary>
    public void Begin(int startWaveIndex = 0)
    {
        Stop(); // 방어 — 이전 실행의 리스너가 남아 있으면 해제
        AllCleared = false;
        IsRunning = true;

        if (WaveCount == 0 || startWaveIndex >= WaveCount)
        {
            CompleteAll();
            return;
        }

        _currentWaveIndex = Math.Max(startWaveIndex, 0) - 1; // AdvanceWave가 +1
        AdvanceWave();
    }

    /// <summary>
    /// 중단 — Stage Unload/맵 전환. 생존 집합의 리스너를 해제한다 (디스폰 후 유령 콜백 방지).
    /// CurrentWaveIndex는 유지 — 호출 측이 진행도로 읽는다.
    /// </summary>
    public void Stop()
    {
        foreach (CombatCreature creature in _alive) // HashSet 열거자는 struct — 할당 없음
        {
            if (creature.IsValid())
                creature.SetDeathListener(null);
        }
        _alive.Clear();
        IsRunning = false;
    }

    private void AdvanceWave()
    {
        _alive.Clear();

        // 빈 웨이브는 건너뛴다. 재귀 대신 반복 — 빈 웨이브가 연속돼도 스택이 쌓이지 않는다
        while (true)
        {
            _currentWaveIndex++;
            if (_currentWaveIndex >= _data.Waves.Count)
            {
                CompleteAll();
                return;
            }

            IReadOnlyList<CombatCreature> spawned = _spawnWave(_data.Waves[_currentWaveIndex]);
            for (int i = 0; i < spawned.Count; i++)
            {
                CombatCreature creature = spawned[i];
                if (creature.IsValid() == false)
                    continue;
                if (_alive.Add(creature))
                    creature.SetDeathListener(_onCreatureDied); // Spawn의 SetInfo가 리스너를 리셋한 뒤이므로 여기서 주입
            }

            if (_alive.Count > 0)
            {
                LogPrinter.Log($"[Wave] {_currentWaveIndex + 1}/{_data.Waves.Count} 시작 (생존 {_alive.Count})");
                _onWaveStarted?.Invoke(_currentWaveIndex);
                return;
            }
        }
    }

    private void OnCreatureDied(CombatCreature creature)
    {
        if (IsRunning == false)
            return;

        // 중복 사망 통지 / 이미 제거된 대상 >> 무시. 웨이브를 건너뛰기 방지
        if (_alive.Remove(creature) == false)
            return;

        creature.SetDeathListener(null);

        if (_alive.Count == 0)
            AdvanceWave();
    }

    private void CompleteAll()
    {
        _alive.Clear();
        AllCleared = true;
        IsRunning = false;
        _onAllCleared?.Invoke();
    }
}