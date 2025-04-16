using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

public class AutoNavMeshBaker : MonoBehaviour
{
    [Header("NavMesh 설정")]
    [Tooltip("NavMesh를 생성할 태그들")]
    public string[] tagsToBake;
    
    [Tooltip("NavMesh 생성 시 사용할 높이")]
    public float agentHeight = 2f;
    
    [Tooltip("NavMesh 생성 시 사용할 반경")]
    public float agentRadius = 0.5f;
    
    [Tooltip("NavMesh 생성 시 사용할 경사")]
    public float agentSlope = 45f;
    
    [Tooltip("NavMesh 생성 시 사용할 스텝 높이")]
    public float agentStepHeight = 0.4f;
    
    [Tooltip("NavMesh 생성 시 사용할 최대 거리")]
    public float maxJumpDistance = 2f;
    
    [Tooltip("NavMesh 생성 시 사용할 최소 거리")]
    public float minJumpDistance = 0.5f;

    [Header("자동 업데이트 설정")]
    [Tooltip("실행 중에 NavMesh를 자동으로 업데이트할지 여부")]
    public bool autoUpdate = true;
    
    [Tooltip("자동 업데이트 간격 (초)")]
    public float updateInterval = 1f;

    [Header("디버그 설정")]
    [Tooltip("디버그 로그를 표시할지 여부")]
    public bool showDebugLogs = true;

    private NavMeshData navMeshData;
    private NavMeshDataInstance navMeshInstance;
    private float nextUpdateTime;
    private Dictionary<string, List<GameObject>> tagObjectCache = new Dictionary<string, List<GameObject>>();
    private bool isInitialized = false;
    private bool isBaking = false;

    void Start()
    {
        InitializeNavMesh();
    }

    void InitializeNavMesh()
    {
        if (tagsToBake == null || tagsToBake.Length == 0)
        {
            Debug.LogError("NavMesh를 생성할 태그가 설정되지 않았습니다!");
            return;
        }

        // 태그별 오브젝트 캐시 초기화
        CacheTaggedObjects();

        // NavMesh 설정 초기화
        var settings = CreateNavMeshSettings();

        // NavMesh 생성
        if (BakeNavMesh(settings))
        {
            isInitialized = true;
        }
    }

    NavMeshBuildSettings CreateNavMeshSettings()
    {
        var settings = new NavMeshBuildSettings();
        settings.agentTypeID = 0; // Humanoid
        settings.agentHeight = agentHeight;
        settings.agentRadius = agentRadius;
        settings.agentSlope = agentSlope;
        settings.agentClimb = agentStepHeight;
        settings.minRegionArea = 2f;
        settings.overrideVoxelSize = false;
        settings.overrideTileSize = false;
        settings.tileSize = 256;
        settings.voxelSize = 0.3f;
        settings.buildHeightMesh = true;
        return settings;
    }

    void CacheTaggedObjects()
    {
        tagObjectCache.Clear();

        foreach (var tag in tagsToBake)
        {
            if (string.IsNullOrEmpty(tag)) continue;
            
            var taggedObjects = GameObject.FindGameObjectsWithTag(tag);
            tagObjectCache[tag] = new List<GameObject>(taggedObjects);

            if (showDebugLogs)
            {
                Debug.Log($"태그 '{tag}'에 {taggedObjects.Length}개의 오브젝트가 캐시되었습니다.");
            }
        }
    }

    void Update()
    {
        if (!isInitialized) return;

        if (autoUpdate && Time.time >= nextUpdateTime && !isBaking)
        {
            var settings = CreateNavMeshSettings();
            BakeNavMesh(settings);
            nextUpdateTime = Time.time + updateInterval;
        }
    }

    bool BakeNavMesh(NavMeshBuildSettings settings)
    {
        if (isBaking) return false;
        isBaking = true;

        try
        {
            var sources = new List<NavMeshBuildSource>();
            
            foreach (var tag in tagsToBake)
            {
                if (string.IsNullOrEmpty(tag) || !tagObjectCache.ContainsKey(tag)) continue;

                foreach (var obj in tagObjectCache[tag])
                {
                    if (obj == null) continue;

                    var meshFilter = obj.GetComponent<MeshFilter>();
                    var meshRenderer = obj.GetComponent<MeshRenderer>();
                    
                    if (meshFilter != null && meshFilter.sharedMesh != null)
                    {
                        var source = new NavMeshBuildSource();
                        source.shape = NavMeshBuildSourceShape.Mesh;
                        source.sourceObject = meshFilter.sharedMesh;
                        source.transform = obj.transform.localToWorldMatrix;
                        source.area = 0; // Walkable area
                        sources.Add(source);
                    }
                    else if (meshRenderer != null)
                    {
                        var source = new NavMeshBuildSource();
                        source.shape = NavMeshBuildSourceShape.Terrain;
                        source.sourceObject = obj;
                        source.transform = obj.transform.localToWorldMatrix;
                        source.area = 0; // Walkable area
                        sources.Add(source);
                    }
                }
            }

            if (sources.Count == 0)
            {
                Debug.LogWarning("NavMesh를 생성할 수 있는 Mesh가 없습니다. 태그 설정을 확인해주세요.");
                isBaking = false;
                return false;
            }

            var bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
            navMeshData = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, transform.position, transform.rotation);

            if (navMeshData == null)
            {
                Debug.LogError("NavMesh 생성에 실패했습니다.");
                isBaking = false;
                return false;
            }

            if (navMeshInstance.valid)
            {
                NavMesh.RemoveNavMeshData(navMeshInstance);
            }

            navMeshInstance = NavMesh.AddNavMeshData(navMeshData);
            
            if (showDebugLogs)
            {
                Debug.Log($"NavMesh가 성공적으로 생성되었습니다. (소스 개수: {sources.Count})");
            }
            isBaking = false;
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"NavMesh 생성 중 오류 발생: {e.Message}");
            isBaking = false;
            return false;
        }
    }

    void OnDestroy()
    {
        if (navMeshInstance.valid)
        {
            NavMesh.RemoveNavMeshData(navMeshInstance);
        }
        if (navMeshData != null)
        {
            navMeshData = null;
        }
        tagObjectCache.Clear();
    }
}