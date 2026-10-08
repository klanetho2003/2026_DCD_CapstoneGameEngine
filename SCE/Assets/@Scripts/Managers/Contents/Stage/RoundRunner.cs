using System;
using System.Collections.Generic;
using static Define;

/// <summary>
/// 라운드 1개의 실행. 인스턴스 1개를 모든 라운드가 재사용한다.
///
/// 시작 조건: Begin 호출 즉시.
/// 종료 조건: 누적 시간이 duration에 도달 (Tick이 판정).
/// 종료 처리: 웨이브 중단 → 남은 몬스터 디스폰 → onRoundEnded 통지 1회.
///
/// 이 클래스는 자신이 몇 번째 라운드인지 모른다 (ERound를 참조하지 않는다).
/// 라운드마다 달라지는 것은 Begin에 넘기는 content뿐이다.
/// 웨이브 진행(전멸 → 다음 웨이브)은 기존 WaveRunner를 그대로 쓴다.
/// </summary>
public sealed class RoundRunner
{
    private const string LOG_CONTEXT = "Round"; // 스폰 에러 로그의 출처 표기 — 상수라 스폰마다 문자열을 만들지 않는다

    private readonly WaveRunner _waveRunner = new();
    private readonly List<CombatCreature> _waveSpawns = new(); // 현재 웨이브의 몬스터 — 재사용

    // WaveRunner에 넘길 delegate. 생성 시 1회만 만든다 (메서드 그룹을 매번 넘기면 그때마다 할당된다)
    private readonly Func<IReadOnlyList<SpawnInfo>, IReadOnlyList<CombatCreature>> _spawnWave;
    private readonly Action _onAllWavesCleared;

    private Action _onRoundEnded;
    private float _duration;
    private double _elapsed; // 수천 프레임에 걸쳐 더하므로 double — float 누적은 반올림 오차가 쌓인다

    public bool IsRunning { get; private set; }

    /// <summary>남은 시간(초). 실행 중이 아니면 0.</summary>
    public float RemainingSeconds
    {
        get { return IsRunning ? (float)Math.Max(0.0, _duration - _elapsed) : 0f; }
    }

    public RoundRunner()
    {
        _spawnWave = SpawnWave;
        _onAllWavesCleared = OnAllWavesCleared;
    }

    /// <param name="onRoundEnded">시간 종료로 라운드가 끝났을 때 1회. Stop으로 중단한 경우에는 부르지 않는다.</param>
    public void SetInfo(Action onRoundEnded)
    {
        _onRoundEnded = onRoundEnded;
    }

    #region Begin / Tick / Stop
    /// <summary>라운드 시작. 실행 중에 다시 부르면 이전 라운드를 통지 없이 정리하고 처음부터 시작한다.</summary>
    public void Begin(StageSpawnData content, float duration)
    {
        Stop(); // 방어 — 이전 라운드의 몬스터·리스너 정리

        if (content == null)
        {
            LogPrinter.LogError("[RoundRunner] 라운드 내용이 null — 시작하지 않음");
            return;
        }

        // 순서: 자기 상태를 먼저 맞춘다. 웨이브가 0개면 아래 Begin 안에서 OnAllWavesCleared가 곧바로 불린다
        _duration = duration;
        _elapsed = 0.0;
        IsRunning = true;

        _waveRunner.SetInfo(content, _spawnWave, _onAllWavesCleared);
        _waveRunner.Begin();
    }

    /// <summary>시간을 누적하고 종료를 판정한다. 소유자(StageManager)가 매 프레임 호출. 비용 = 덧셈 1회 + 비교 1회.</summary>
    public void Tick(float deltaTime)
    {
        if (IsRunning == false)
            return;

        _elapsed += deltaTime;
        if (_elapsed < _duration)
            return;

        // 순서: 정리 → 통지. 통지를 받은 쪽이 곧바로 Begin을 불러도 안전하다
        Cleanup();
        _onRoundEnded?.Invoke();
    }

    /// <summary>중단 — 사망·Stage 이탈·씬 전환. 통지 없이 정리만 한다. 여러 번 불러도 안전.</summary>
    public void Stop()
    {
        Cleanup();
    }

    private void Cleanup()
    {
        IsRunning = false;
        _waveRunner.Stop();                              // 리스너 해제가 먼저 — 디스폰 후 유령 콜백 방지
        CreatureSpawnHelper.DespawnAll(_waveSpawns);     // 유효한 것만 디스폰하고 목록을 비운다
    }
    #endregion

    #region WaveRunner 콜백
    /// <summary>WaveRunner가 호출. 이전 웨이브는 전멸 상태이므로 목록만 비운다.</summary>
    private IReadOnlyList<CombatCreature> SpawnWave(IReadOnlyList<SpawnInfo> infos)
    {
        _waveSpawns.Clear();
        for (int i = 0; i < infos.Count; i++)
        {
            SpawnInfo info = infos[i];
            if (info.ObjectType != EObjectType.Monster) // Exporter가 막지만 손으로 고친 JSON 방어
            {
                LogPrinter.LogError($"[{LOG_CONTEXT}] 웨이브에 {info.ObjectType}(DataId={info.DataId}) — Monster만 허용, 무시");
                continue;
            }

            if (CreatureSpawnHelper.Spawn(info, LOG_CONTEXT) is CombatCreature combat)
                _waveSpawns.Add(combat);
        }
        return _waveSpawns;
    }

    /// <summary>
    /// 시간이 남았는데 스폰할 웨이브가 더 없는 경우. 지금 정책은 "추가 스폰 없이 시간 종료까지 대기".
    /// 반복 스폰·시간표 스폰으로 바꿀 때 고치는 유일한 지점.
    /// 주의: 여기서 곧바로 _waveRunner.Begin()을 다시 부르면 웨이브가 0개인 라운드에서 무한 재귀가 된다 — 다음 Tick으로 미룰 것.
    /// </summary>
    private void OnAllWavesCleared()
    {
        LogPrinter.Log($"[{LOG_CONTEXT}] 남은 웨이브 없음 — 시간 종료까지 대기 (남은 {RemainingSeconds:F1}s)");
    }
    #endregion
}