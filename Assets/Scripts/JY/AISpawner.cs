using UnityEngine;
using System.Collections.Generic;

public class AISpawner : MonoBehaviour
{
    public GameObject aiPrefab;
    public float spawnInterval = 2f;
    public int poolSize = 200;

    private float nextSpawnTime;
    private int spawnCount = 0;
    private Queue<GameObject> aiPool;
    private List<GameObject> activeAIs;

    // 싱글톤 인스턴스
    public static AISpawner Instance { get; private set; }

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        InitializePool();
        nextSpawnTime = Time.time + spawnInterval;
    }

    void InitializePool()
    {
        aiPool = new Queue<GameObject>();
        activeAIs = new List<GameObject>();

        // 풀에 AI 오브젝트들을 미리 생성
        for (int i = 0; i < poolSize; i++)
        {
            GameObject ai = Instantiate(aiPrefab, transform.position, Quaternion.identity);
            ai.name = $"AI_{i}";
            ai.SetActive(false);
            ai.transform.parent = transform; // 계층구조 정리를 위해 spawner의 자식으로 설정
            aiPool.Enqueue(ai);
            
            // AIAgent 컴포넌트에 spawner 참조 설정
            AIAgent aiAgent = ai.GetComponent<AIAgent>();
            if (aiAgent != null)
            {
                aiAgent.SetSpawner(this);
            }
        }
    }

    void Update()
    {
        if (Time.time >= nextSpawnTime)
        {
            SpawnAI();
            nextSpawnTime = Time.time + spawnInterval;
        }
    }

    void SpawnAI()
    {
        if (aiPool.Count <= 0)
        {
            Debug.LogWarning("풀에 사용 가능한 AI가 없습니다!");
            return;
        }

        GameObject ai = aiPool.Dequeue();
        ai.transform.position = transform.position;
        ai.transform.rotation = Quaternion.identity;
        
        // AI 컴포넌트 초기화
        AIAgent aiAgent = ai.GetComponent<AIAgent>();
        if (aiAgent != null)
        {
            aiAgent.SetSpawner(this);
        }
        
        // 활성화 전에 위치 설정
        ai.transform.position = transform.position;
        ai.SetActive(true);
        activeAIs.Add(ai);
        
        spawnCount++;
        Debug.Log($"{ai.name} 활성화됨 (현재 활성화된 AI: {activeAIs.Count}개)");
    }

    // AI 오브젝트를 풀로 반환
    public void ReturnToPool(GameObject ai)
    {
        if (ai == null) return;

        ai.SetActive(false);
        activeAIs.Remove(ai);
        aiPool.Enqueue(ai);
        ai.transform.position = transform.position; // 스포너 위치로 이동
        Debug.Log($"{ai.name} 비활성화됨 (남은 풀 개수: {aiPool.Count}개)");
    }

    // 모든 활성화된 AI를 풀로 반환
    public void ReturnAllToPool()
    {
        foreach (var ai in activeAIs.ToArray())
        {
            ReturnToPool(ai);
        }
        activeAIs.Clear();
    }
} 