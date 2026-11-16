using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Define;

/// <summary>
/// 설정 (Popup). 돌아가기 / ESC 로 닫힘.
/// - 배경음 / 효과음: 볼륨 칸 10개 (칸 하나 = 10). 칸을 누르면 거기까지 켜진다. 선택된 행에서 ←/→ 로 10씩.
///   값은 SoundSettings 에 저장 (PlayerPrefs) — 효과음은 바로 UISound 음량에 반영, 배경음은 배경음악을 넣으면 그대로 쓴다.
/// - 메뉴: 마우스를 올리거나 ↑/↓ 로 행 선택 (흰 글씨 + 화살표), Enter 로 "돌아가기".
/// - 화면 모드 / 해상도 값 처리는 아직 (선택 표시만).
/// </summary>
public class UI_Panel_Settings : UI_Popup
{
    private enum GameObjects
    {
        Row_BGM,
        Row_SFX,
        Row_ScreenMode,
        Row_Resolution,
        Btn_Back,
    }

    private const int CELL_COUNT = 10;
    private const int MENU_BACK = 4; // 메뉴 순서에서 "돌아가기" 자리

    [Header("볼륨 칸 그림 (Settings/volume_cell_on, volume_cell_off)")]
    [SerializeField] private Sprite _cellOn;
    [SerializeField] private Sprite _cellOff;

    /// <summary>설정창이 떠 있는지 (일시정지가 ESC 를 양보할 때 씀)</summary>
    public static bool IsOpen { get; private set; }

    private class VolumeRow
    {
        public bool IsSfx;
        public readonly List<Image> Cells = new List<Image>();
        public TMP_Text Value;
    }

    private readonly VolumeRow[] _volumeRows = new VolumeRow[2]; // 0 배경음, 1 효과음
    private UIMenuSelection _menu;

    private void OnEnable()
    {
        IsOpen = true;
        if (_init)
        {
            RefreshVolumes();
            _menu?.Select(0);
        }
    }

    private void OnDisable() { IsOpen = false; }

    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        BindObjects(typeof(GameObjects));

        GameObject back = GetObject(GameObjects.Btn_Back);
        if (back != null) BindEvent(back, (evt) => Close());

        _volumeRows[0] = BuildVolumeRow(GetObject(GameObjects.Row_BGM), false, 0);
        _volumeRows[1] = BuildVolumeRow(GetObject(GameObjects.Row_SFX), true, 1);

        // 행 글자에 마우스가 닿아야 선택되므로 글자도 클릭을 받게
        foreach (GameObjects row in new[] { GameObjects.Row_BGM, GameObjects.Row_SFX, GameObjects.Row_ScreenMode, GameObjects.Row_Resolution, GameObjects.Btn_Back })
            EnableRaycast(GetObject(row), "Txt_Label");

        _menu = new UIMenuSelection(
            GetObject(GameObjects.Row_BGM),
            GetObject(GameObjects.Row_SFX),
            GetObject(GameObjects.Row_ScreenMode),
            GetObject(GameObjects.Row_Resolution),
            back);
        _menu.BindHover();

        RefreshVolumes();
        return true;
    }

    private VolumeRow BuildVolumeRow(GameObject rowGo, bool isSfx, int menuIndex)
    {
        var row = new VolumeRow { IsSfx = isSfx };
        if (rowGo == null)
            return row;

        for (int i = 1; i <= CELL_COUNT; i++)
        {
            Transform cell = rowGo.transform.Find("Img_Cell_" + i);
            Image image = cell != null ? cell.GetComponent<Image>() : null;
            row.Cells.Add(image);
            if (image == null)
                continue;

            image.raycastTarget = true;
            int level = i;
            BindEvent(image.gameObject, (evt) =>
            {
                _menu?.Select(menuIndex);
                // 이미 첫 칸만 켜져 있을 때 첫 칸을 다시 누르면 0 (소리 끄기)
                int current = GetVolume(row);
                SetVolume(row, (level == 1 && current == 10) ? 0 : level * 10);
            });
        }

        // 그림이 연결 안 됐으면 프리팹에 놓인 칸 그림에서 가져온다 (첫 칸 = 켜짐, 마지막 칸 = 꺼짐)
        if (_cellOn == null && row.Cells[0] != null) _cellOn = row.Cells[0].sprite;
        if (_cellOff == null && row.Cells[CELL_COUNT - 1] != null) _cellOff = row.Cells[CELL_COUNT - 1].sprite;

        Transform value = rowGo.transform.Find("Txt_Value");
        if (value != null) row.Value = value.GetComponent<TMP_Text>();
        return row;
    }

    private static void EnableRaycast(GameObject root, string childName)
    {
        if (root == null) return;
        Transform t = root.transform.Find(childName);
        if (t != null && t.TryGetComponent(out Graphic g)) g.raycastTarget = true;
    }

    private static int GetVolume(VolumeRow row) => row.IsSfx ? SoundSettings.Sfx : SoundSettings.Bgm;

    private void SetVolume(VolumeRow row, int value)
    {
        int before = GetVolume(row);
        if (row.IsSfx) SoundSettings.Sfx = value;
        else SoundSettings.Bgm = value;

        RefreshVolumes();

        // 효과음은 바뀐 크기로 확인음 한 번
        if (row.IsSfx && GetVolume(row) != before)
            UISound.Play(Sfx.UiSelect);
    }

    private void RefreshVolumes()
    {
        foreach (VolumeRow row in _volumeRows)
        {
            if (row == null) continue;
            int on = GetVolume(row) / 10;
            for (int i = 0; i < row.Cells.Count; i++)
            {
                if (row.Cells[i] == null) continue;
                Sprite s = i < on ? _cellOn : _cellOff;
                if (s != null) row.Cells[i].sprite = s;
            }
            if (row.Value != null) row.Value.text = GetVolume(row).ToString();
        }
    }

    private void Update()
    {
        if (UIPanelInput.ConsumeEsc())
        {
            Close();
            return;
        }

        if (_menu == null)
            return;

        Vector2Int arrow = UIPanelInput.ArrowPressed();

        // ↑/↓: 행 선택
        if (arrow.y != 0)
        {
            int next = Mathf.Clamp(_menu.Index + arrow.y, 0, MENU_BACK);
            if (next != _menu.Index)
            {
                UISound.Play(Sfx.UiHover);
                _menu.Select(next);
            }
        }

        // ←/→: 선택된 볼륨 행을 10씩
        if (arrow.x != 0 && _menu.Index >= 0 && _menu.Index < _volumeRows.Length && _volumeRows[_menu.Index] != null)
        {
            VolumeRow row = _volumeRows[_menu.Index];
            SetVolume(row, GetVolume(row) + arrow.x * 10);
        }

        // Enter: "돌아가기"가 선택돼 있으면 닫기
        if (_menu.Index == MENU_BACK && UIPanelInput.ConfirmPressed())
            Close();
    }

    private void Close()
    {
        UISound.Play(Sfx.UiBack);
        Managers.UI.ClosePopupUI(this);
    }
}
