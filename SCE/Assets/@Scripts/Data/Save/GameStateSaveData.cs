using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine.Scripting;

/// <summary>
/// 게임 상태(플래그, 카운터, 일차) 구역의 저장 형식이며 진행 파일에 들어간다. 값은 GameStateSaveSection이 적고 받아 쓴다 — 이 형식은 값만 갖는다.
/// 모든 상태 키를 이름으로 저장한다 — 정의의 차례가 바뀌어도 그대로 읽히고, 정의에서 지운 키는 불러올 때 그 칸만 버린다.
/// 예: { "values": { "sys.day": 3, "met_merchant": 1 } }
/// </summary>
[Serializable]
[Preserve] // JSON 읽기만 만드는 형식 — 빌드의 코드 줄이기에서 생성자가 지워지지 않게
[JsonObject(MemberSerialization.OptIn)]
public sealed class GameStateSaveData
{
    /// <summary>상태 키 이름에서 값으로 가는 표다.</summary>
    [JsonProperty("values")]
    public Dictionary<string, int> Values = new Dictionary<string, int>();
}
