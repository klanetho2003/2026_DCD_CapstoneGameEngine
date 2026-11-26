using System;
using UnityEngine;

/// <summary>
/// 연출 한 순간에 곁들일 소리와 이펙트 자리. 비워 두면 아무 일도 하지 않는다.
/// 소리는 카메라 거리와 상관없이 같은 크기로 들리도록 2D(spatialBlend 0)로 재생한다.
/// </summary>
[Serializable]
public struct GimmickFxSlot
{
    [Tooltip("재생할 효과음. 비우면 소리 없음")]
    public AudioClip Sound;

    [Range(0f, 1f), Tooltip("효과음 크기")]
    public float Volume;

    [Tooltip("그 위치에 생성할 이펙트 프리팹 (파티클 등). 비우면 없음")]
    public GameObject EffectPrefab;

    [Min(0f), Tooltip("이펙트를 지우기까지의 시간(초). 0이면 지우지 않음 — 프리팹이 스스로 정리할 때")]
    public float EffectLifetime;

    /// <summary>소리와 이펙트를 월드 위치에서 한 번 재생한다.</summary>
    /// <param name="position">이펙트를 놓을 월드 위치다.</param>
    public void Play(Vector3 position)
    {
        if (Sound != null)
        {
            // go는 효과음 하나를 재생하고 끝나면 사라질 임시 오브젝트다.
            GameObject go = new GameObject($"Sfx_{Sound.name}");
            go.transform.position = position;
            AudioSource source = go.AddComponent<AudioSource>();
            source.spatialBlend = 0f;
            source.playOnAwake = false;
            source.PlayOneShot(Sound, Volume <= 0f ? 1f : Volume);
            UnityEngine.Object.Destroy(go, Sound.length + 0.1f);
        }

        if (EffectPrefab != null)
        {
            // effect는 연출 위치에 생성한 이펙트 인스턴스다.
            GameObject effect = UnityEngine.Object.Instantiate(EffectPrefab, position, Quaternion.identity);
            if (EffectLifetime > 0f)
                UnityEngine.Object.Destroy(effect, EffectLifetime);
        }
    }
}
