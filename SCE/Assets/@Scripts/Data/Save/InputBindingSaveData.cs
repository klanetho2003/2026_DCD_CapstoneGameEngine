using System;
using Newtonsoft.Json;
using UnityEngine.Scripting;

/// <summary>
/// 키 설정 구역의 저장 형식이며 설정 파일에 들어간다. 값은 InputBindingSaveSection이 적고 받아 쓴다 — 이 형식은 값만 갖는다.
/// Input System이 만든 바인딩 덮어쓰기 JSON을 글 하나로 담는다 — 기본 바인딩은 .inputactions 그대로라 담지 않는다.
/// </summary>
[Serializable]
[Preserve] // JSON 읽기만 만드는 형식 — 빌드의 코드 줄이기에서 생성자가 지워지지 않게
[JsonObject(MemberSerialization.OptIn)]
public sealed class InputBindingSaveData
{
    /// <summary>InputActionAsset.SaveBindingOverridesAsJson의 결과이며 비면 덮어쓰기가 없다.</summary>
    [JsonProperty("overrides")]
    public string Overrides = "";
}
