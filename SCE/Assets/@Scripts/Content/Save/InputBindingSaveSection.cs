using System;
using UnityEngine.InputSystem;

/// <summary>키 설정 구역이며 설정 파일에 들어간다. Input System의 바인딩 덮어쓰기만 저장하고 기본 바인딩은 .inputactions 그대로 둔다.</summary>
public sealed class InputBindingSaveSection : SaveSection<InputBindingSaveData>
{
    public override string Key => "inputBindings";
    public override ESaveFile SaveFile => ESaveFile.Settings;
    public override int Version => 1;

    /// <summary>InputManager가 만든 입력 에셋이며 만들기 전이면 null이다.</summary>
    private static InputActionAsset Asset => Managers.Input?.ActionAsset?.asset;

    public override void ResetToDefault()
    {
        Asset?.RemoveAllBindingOverrides();
    }

    protected override InputBindingSaveData CaptureData()
    {
        InputActionAsset asset = Asset ?? throw new InvalidOperationException("입력 에셋이 없다");
        return new InputBindingSaveData { Overrides = asset.SaveBindingOverridesAsJson() };
    }

    protected override void RestoreData(InputBindingSaveData data)
    {
        InputActionAsset asset = Asset ?? throw new InvalidOperationException("입력 에셋이 없다");
        if (string.IsNullOrEmpty(data.Overrides))
            asset.RemoveAllBindingOverrides();
        else
            asset.LoadBindingOverridesFromJson(data.Overrides);
    }
}
