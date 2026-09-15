using UnityEngine;
using UnityEngine.SceneManagement;

public class Managers : MonoBehaviour
{
    public static bool Initialized { get; private set; } = false;

    private static Managers s_instance;
    public static Managers Instance { get { Init(); return s_instance; } }

    #region Contents
    private InteractionManager _interaction = new InteractionManager();
    public static InteractionManager Interaction { get { return Instance?._interaction; } }
    #endregion

    #region Core
    private ResourceManager _resource = new ResourceManager();
    private PoolManager _pool = new PoolManager();
    private UIManager _ui = new UIManager();
    private ObjectManager _obj = new ObjectManager();
    private InputManager _input = new InputManager();
    private DataManager _data = new DataManager();
    private SceneManagerEx _scene = new SceneManagerEx();

    public static ResourceManager Resource { get { return Instance?._resource; } } // Nullable 설명을 넣을까말까
    public static PoolManager Pool { get { return Instance?._pool; } }
    public static UIManager UI { get { return Instance?._ui; } }
    public static ObjectManager Object { get { return Instance?._obj; } }
    public static InputManager Input { get { return Instance?._input; } }
    public static DataManager Data { get { return Instance?._data; } }
    public static SceneManagerEx Scene { get { return Instance?._scene; } }
    #endregion

    private void Start()
    {
        Init();
    }

    private void Update()
    {
        s_instance._interaction.OnUpdate();
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
            // s_instance._skillInputHandler.Init();
        }
    }

    private void Clear()
    {
        s_instance._pool.Clear();
    }
}
