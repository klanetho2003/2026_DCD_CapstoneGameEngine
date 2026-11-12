using UnityEngine;
using UnityEngine.SceneManagement;

public class Managers : MonoBehaviour
{
    public static bool Initialized { get; private set; } = false;

    private static Managers s_instance;
    public static Managers Instance { get { Init(); return s_instance; } }

    #region Contents
    private InteractionManager _interaction = new InteractionManager();
    private CameraManager _camera = new CameraManager();
    private MapManager _map = new MapManager();
    private StageManager _stage = new StageManager();
    private GameStateManager _gameState = new GameStateManager();
    private BehaviorManager _behavior = new BehaviorManager();

    public static InteractionManager Interaction { get { return Instance?._interaction; } }
    public static CameraManager Camera { get { return Instance?._camera; } }
    public static MapManager Map { get { return Instance?._map; } }
    public static StageManager Stage { get { return Instance?._stage; } }
    public static GameStateManager GameState { get { return Instance?._gameState; } }
    public static BehaviorManager Behavior { get { return Instance?._behavior; } }
    #endregion

    #region Core
    private ResourceManager _resource = new ResourceManager();
    private PoolManager _pool = new PoolManager();
    private UIManager _ui = new UIManager();
    private ObjectManager _obj = new ObjectManager();
    private InputManager _input = new InputManager();
    private DataManager _data = new DataManager();
    private SceneManagerEx _scene = new SceneManagerEx();
    private SaveManager _save = new SaveManager();

    public static ResourceManager Resource { get { return Instance?._resource; } } // Nullable 설명을 넣을까말까
    public static PoolManager Pool { get { return Instance?._pool; } }
    public static UIManager UI { get { return Instance?._ui; } }
    public static ObjectManager Object { get { return Instance?._obj; } }
    public static InputManager Input { get { return Instance?._input; } }
    public static DataManager Data { get { return Instance?._data; } }
    public static SceneManagerEx Scene { get { return Instance?._scene; } }
    public static SaveManager Save { get { return Instance?._save; } }
    #endregion

    private void Start()
    {
        Init();
    }

    private void Update()
    {
        s_instance._interaction.OnUpdate();
        s_instance._stage.OnUpdate();
    }

    private static void Init()
    {
        if (s_instance == null && Initialized == false)
        {
            Initialized = true;

            var go = GameObject.Find("@Managers");

            if (go == null)
            {
                go = new GameObject { name = "@Managers" };
                go.AddComponent<Managers>();
            }

            UnityEngine.Object.DontDestroyOnLoad(go);
            s_instance = go.GetComponent<Managers>();

            // Managers Init
            s_instance._input.Init();
            s_instance._obj.Init();
            s_instance._interaction.Init();

            GameEventBus.SetRuleSink(s_instance._gameState);
            s_instance._gameState.Init();

            // 설정 파일은 설정을 쓰는 매니저들이 모두 만들어진 뒤에 읽는다 (등록된 설정 구역이 없으면 아무것도 하지 않는다). 진행 파일은 리소스 로드 뒤 (LoadGameData)
            s_instance._save.Init();
            s_instance._save.Load(ESaveFile.Settings);

            // s_instance._skillInputHandler.Init();
        }
    }

    #region 게임 데이터 로딩
    /// <summary>
    /// 타이틀 화면이 부른다. PreLoad 리소스를 모두 읽으면 시트 데이터 → 진행 파일 순서로 초기화하고 onCompleted를 부른다.
    /// 타이틀 UI가 몇 개든 이 순서는 여기 하나다.
    /// 어느 단계든 실패하면 onCompleted를 부르지 않는다 (시작 화면에 머문다). 이번 실행에서 이미 초기화했으면 리소스만 다시 읽는다.
    /// </summary>
    /// <param name="onCompleted">모든 단계가 끝나면 한 번 불린다.</param>
    public static void LoadGameData(System.Action onCompleted)
    {
        Resource.Clear();
        Data.Clear();

        Resource.LoadAllAsync<UnityEngine.Object>(ResourceManager.PreLoadLabel, (key, count, totalCount) =>
        {
            LogPrinter.Log($"{key} {count}/{totalCount}");

            if (Data.IsDataLoaded == false && count == totalCount)
                OnPreLoadCompleted(onCompleted);
        });
    }

    private static void OnPreLoadCompleted(System.Action onCompleted)
    {
        try
        {
            // (Sheet) Data Init
            Data.InitData();

            // (DB) Data Init
            //Managers.Game.InitGame();
        }
        catch (System.Exception e)
        {
            // 시작 화면에 머무르므로 진행 데이터도 불러오지 않는다 (저장 파일은 그대로 남는다)
            LogPrinter.LogError($"[Managers] 데이터 초기화 실패 — 진행 데이터를 불러오지 않음\n{e}"/*$"[무시된 로딩 에러] 초기화 중 예외가 발생했지만 강제로 통과합니다.\n{e}"*/);
            return;
        }
        finally
        {
            Data.IsDataLoaded = true;
        }

        try
        {
            // (Save) 저장 경로에서 진행 파일을 찾아 불러오고 없으면 새로 만든다
            Save.Load(ESaveFile.Progress);

            // Resource가 필요한 Manager Init
            // _ = Managers.Analytics.InitializeAsync();

            onCompleted?.Invoke();
        }
        catch (System.Exception e)
        {
            LogPrinter.LogError(e.ToString());
        }
    }
    #endregion

    private void Clear()
    {
        s_instance._pool.Clear();
    }
}
