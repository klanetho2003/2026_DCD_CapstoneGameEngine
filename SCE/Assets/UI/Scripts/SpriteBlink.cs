using UnityEngine;
using UnityEngine.UI;

// 스프라이트 몇 장을 번갈아 보여준다 (예: 행성 선택 링 f0/f1).
// 일시정지 중에도 돌도록 unscaled 시간을 쓴다.
[RequireComponent(typeof(Image))]
public class SpriteBlink : MonoBehaviour
{
    [SerializeField] Sprite[] frames;
    [SerializeField] float interval = 0.5f;

    Image _image;
    float _time;
    int _index;

    public void Init(Sprite[] spriteFrames, float frameInterval)
    {
        frames = spriteFrames;
        interval = frameInterval;
    }

    void Awake()
    {
        _image = GetComponent<Image>();
    }

    void OnEnable()
    {
        _time = 0;
        _index = 0;
        if (_image != null && frames != null && frames.Length > 0)
            _image.sprite = frames[0];
    }

    void Update()
    {
        if (frames == null || frames.Length < 2)
            return;

        _time += Time.unscaledDeltaTime;
        if (_time < interval)
            return;

        _time -= interval;
        _index = (_index + 1) % frames.Length;
        _image.sprite = frames[_index];
    }
}
