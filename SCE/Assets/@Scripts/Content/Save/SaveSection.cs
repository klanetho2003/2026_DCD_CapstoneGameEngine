using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>저장 파일 종류이며 설정은 새 게임을 시작해도 남고 진행은 새 게임에서 처음 상태로 돌아간다.</summary>
public enum ESaveFile
{
    /// <summary>사용자 설정이다. 지금은 등록된 구역이 없어 파일을 쓰지 않는다.</summary>
    Settings,
    /// <summary>게임 상태 등 플레이 진행이다.</summary>
    Progress,
}

/// <summary>파일 하나를 불러온 결과다.</summary>
public enum ESaveLoadResult
{
    /// <summary>본 파일과 백업이 모두 없어 처음 상태로 시작했다.</summary>
    NoFile,
    /// <summary>본 파일을 불러왔다.</summary>
    Loaded,
    /// <summary>본 파일을 읽지 못해 백업을 불러왔다.</summary>
    RestoredFromBackup,
    /// <summary>본 파일과 백업을 모두 읽지 못해 처음 상태로 시작했다.</summary>
    Corrupted,
    /// <summary>이 파일에 등록된 구역이 없어 읽지도 만들지도 않았다.</summary>
    Unused,
}

/// <summary>
/// 저장 파일 안의 구역 하나이며 한 시스템의 저장 데이터를 맡는다. SaveManager가 등록된 구역마다 Capture로 모으고 Restore로 나눠 준다.
/// Key는 파일 안의 구역 이름이라 한 번 정하면 바꾸지 않는다. 데이터 형식을 바꾸면 Version을 올리고 Migrate에 한 단계 변환을 더한다.
/// </summary>
public interface ISaveSection
{
    /// <summary>파일 안에서 이 구역을 찾는 이름이다.</summary>
    string Key { get; }

    /// <summary>이 구역이 들어가는 파일이다.</summary>
    ESaveFile SaveFile { get; }

    /// <summary>지금 코드가 쓰고 읽는 데이터 형식의 버전이며 1부터 시작한다.</summary>
    int Version { get; }

    /// <summary>지금 상태를 저장할 JSON으로 만든다.</summary>
    /// <param name="serializer">SaveManager가 정한 JSON 설정의 직렬화기다.</param>
    JToken Capture(JsonSerializer serializer);

    /// <summary>지금 Version 형식의 데이터를 상태에 입힌다. 입힐 수 없으면 예외를 던진다.</summary>
    /// <param name="data">이주를 마친 구역 데이터다.</param>
    /// <param name="serializer">SaveManager가 정한 JSON 설정의 직렬화기다.</param>
    void Restore(JToken data, JsonSerializer serializer);

    /// <summary>파일이나 구역이 없을 때 처음 상태로 돌린다.</summary>
    void ResetToDefault();

    /// <summary>fromVersion 형식의 데이터를 fromVersion + 1 형식으로 바꾼 새 데이터를 반환하며 그 단계를 모르면 null이다.</summary>
    /// <param name="fromVersion">바꾸기 전 데이터의 버전이다.</param>
    /// <param name="data">바꿀 데이터이며 SaveManager가 복사본을 넘기므로 고쳐도 된다.</param>
    JToken Migrate(int fromVersion, JToken data);
}

/// <summary>저장 데이터 형식(TData)이 정해진 구역이며 JSON과 TData 사이 변환을 맡고 파생 클래스는 상태와 TData 사이만 쓴다.</summary>
public abstract class SaveSection<TData> : ISaveSection where TData : class, new()
{
    public abstract string Key { get; }
    public abstract ESaveFile SaveFile { get; }
    public abstract int Version { get; }

    public JToken Capture(JsonSerializer serializer)
    {
        return JToken.FromObject(CaptureData(), serializer);
    }

    public void Restore(JToken data, JsonSerializer serializer)
    {
        RestoreData(data.ToObject<TData>(serializer) ?? new TData());
    }

    public abstract void ResetToDefault();

    /// <summary>기본은 이주 단계가 없다. 형식을 바꿀 때 파생 클래스가 단계마다 변환을 더한다.</summary>
    public virtual JToken Migrate(int fromVersion, JToken data)
    {
        return null;
    }

    /// <summary>지금 상태를 저장 데이터로 만든다.</summary>
    protected abstract TData CaptureData();

    /// <summary>저장 데이터를 지금 상태에 입힌다. 입힐 수 없으면 예외를 던진다.</summary>
    /// <param name="data">이주를 마친 저장 데이터다.</param>
    protected abstract void RestoreData(TData data);
}
