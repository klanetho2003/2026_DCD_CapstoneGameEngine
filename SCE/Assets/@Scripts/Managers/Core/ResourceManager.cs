using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using static LogPrinter;
using Object = UnityEngine.Object;

public class ResourceManager
{
    // Addressable에서 Load한 모든 대상들은 담는다 //(key값은 Addresable에서 지정한 string값이고, value는 Load한 대상)
    Dictionary<string, Object> _resources = new Dictionary<string, Object>();

    public T Load<T>(string key) where T : Object
    {
        if (_resources.TryGetValue(key, out Object resource))                   // 있으면 대상을 return
            return resource as T;

        if (typeof(T) == typeof(Sprite) && key.Contains(".sprite") == false)    // sprite는 Address 내부에서 Texture로 변환해서 저장하는 경우가 있어서 특별 취급 -> ".sprite" 힌트를 붙임
        {
            if (_resources.TryGetValue($"{key}.sprite", out resource))
                return resource as T;
        }

        return null;                                                            // 없으면 null return
    }

    public GameObject Instantiate(string key, Transform parent = null, bool pooling = false)
    {
        GameObject prefab = Load<GameObject>($"{key}");
        if (prefab == null)                                     // Load해서 없으면 null return
        {
            Debug.Log($"Failed to load prefab : {key}");
            return null;
        }

        //pooling // 풀링할 객체인지 확인 후, Pool Manager로 인계
        if (pooling)
        {
            var poolable = Managers.Pool.Pop(prefab);
            poolable.transform.SetParent(parent);
            return poolable;
        }

        GameObject go = Object.Instantiate(prefab, parent);     // 풀링할 대상이 아니면 Unity에서 기본 제공하는 Instantiate Method 사용
        go.name = prefab.name;
        return go;
    }

    public void Destroy(GameObject go)
    {
        if (go == null)                                         // 방어 코드
            return;

        if (Managers.Pool.Push(go))                             // 풀링할 객체인지 확인 후, Pool Manager로 인계
            return;

        Object.Destroy(go);                                     // 풀링할 대상이 아니면 Unity에서 기본 제공하는 Destroy Method 사용
    }

    public void Clear()
    {
        // Addressable에서 할당한 Memory를 Release함수로 해제
        foreach (var resource in _resources.Values)
            Addressables.Release(resource);

        _resources.Clear();
    }

    #region Addressable

    // key : Addresable에서 지정한 string값 // callback : Resource Load가 완료된 후에 호출할 함수
    public void LoadAsync<T>(string key, Action<T> callback = null) where T : UnityEngine.Object
    {
        // 이미 로드된 리소스라면 즉시 콜백
        if (_resources.TryGetValue(key, out Object resource))
        {
            callback?.Invoke(resource as T);
            return;
        }

        // sprite만 예외 처리
        // sprite는 Addressable 내부에서 Texture로 변환해서 저장하는 경우가 있어서 특별 취급 -> ".sprite" 힌트를 붙임
        string loadKey = key;
        if (key.Contains(".sprite"))                                    // key 내에 ".sprite"가 포함되어 있으면
            loadKey = $"{key}[{key.Replace(".sprite", "")}]";           // sprite 형식으로 저장되도록 key값을 변환 >> Ex. "AddressableTest.sprite[AddressableTest]" + 주의.Project창에서의 이름과 Addressable에서의 이름이 같아야 함.

        var asyncOperation = Addressables.LoadAssetAsync<T>(loadKey);
        asyncOperation.Completed += (op) =>
        {
            // 중복 키 체크
            if (_resources.ContainsKey(key) == false)
            {
                // 비동기 완료 시점에 다른 작업에 의해 이미 추가되었을 수 있으므로, 중복 키 체크 후 추가
                _resources.Add(key, op.Result);
            }
            else
            {
                // 이미 존재한다면 기존 것을 사용하도록 중복 Load된 리소스는 해제 처리
                Addressables.Release(op);
            }

            callback?.Invoke(_resources[key] as T);
        };
    }

    // LoadAsync Method를 반복 호출해서 모든 Resource Load
    // label : Addressable에서 지정한 label(ex. PreLoad) string값 // callback : LoadAsync 호출할 때 전달할 완료 처리 함수
    public void LoadAllAsync<T>(string label, Action<string, int, int> callback = null) where T : UnityEngine.Object
    {
        // Addressable에서 지정한 label(ex. PreLoad) 불러오기
        var operationHandle = Addressables.LoadResourceLocationsAsync(label, typeof(T));

        // label(ex. PreLoad)에 해당하는 대상들 시작
        operationHandle.Completed += (op) =>
        {
            int loadCount = 0;
            int totalCount = op.Result.Count;

            // Load할 Resource가 없는 경우 대응
            if (totalCount == 0)
            {
                callback?.Invoke(null, 0, 0);
                return;
            }

            // label(ex. PreLoad)에 해당하는 대상들 반복하면서 LoadAsync 호출
            foreach (var result in op.Result)
            {
                string key = result.PrimaryKey;

                // sprite만 예외 처리
                // sprite는 Addressable 내부에서 Texture로 변환해서 저장하는 경우가 있어서 특별 취급 -> ".sprite" 힌트를 붙임
                if (key.Contains(".sprite"))
                {
                    LoadAsync<Sprite>(key, (obj) =>
                    {
                        loadCount++;
                        callback?.Invoke(key, loadCount, totalCount);
                    });
                }
                else // sprite가 아닌 경우 일반적으로 LoadAsync 호출
                {
                    LoadAsync<T>(key, (obj) =>
                    {
                        loadCount++;
                        callback?.Invoke(key, loadCount, totalCount);
                    });
                }
            }
        };
    }
    #endregion
}
