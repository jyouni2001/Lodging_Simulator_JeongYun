using UnityEngine;
using System.Collections.Generic;
using System.Collections;

public class CounterManager : MonoBehaviour
{
    public static CounterManager Instance { get; private set; }
    
    [Header("Queue Settings")]
    public float queueSpacing = 2f;           // AI 간격
    public float counterServiceDistance = 2f;  // 카운터와 서비스 받는 위치 사이의 거리
    public int maxQueueLength = 10;           // 최대 대기열 길이
    public float serviceTime = 5f;            // 서비스 처리 시간

    // 통합 대기열 - 방 배정과 방 사용완료 보고를 모두 처리
    private Queue<AIAgent> waitingQueue = new Queue<AIAgent>();
    private AIAgent currentServingAgent = null;
    private Vector3 counterFront;
    private Transform counterTransform;
    private bool isProcessingService = false;

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
        counterTransform = transform;
        // 카운터 정면 위치 계산 (카운터의 forward 방향으로 2유닛)
        counterFront = counterTransform.position + counterTransform.forward * counterServiceDistance;
    }

    // 대기열에 합류 요청 (방 배정/방 사용완료 보고 모두 동일 대기열 사용)
    public bool TryJoinQueue(AIAgent agent)
    {
        if (waitingQueue.Count >= maxQueueLength)
        {
            Debug.Log($"대기열이 가득 찼습니다. (현재 {waitingQueue.Count}명)");
            return false;
        }

        waitingQueue.Enqueue(agent);
        UpdateQueuePositions();
        Debug.Log($"AI가 대기열에 합류했습니다. (대기 인원: {waitingQueue.Count}명)");
        return true;
    }

    // AI가 대기열에서 나가기 요청
    public void LeaveQueue(AIAgent agent)
    {
        if (currentServingAgent == agent)
        {
            currentServingAgent = null;
            isProcessingService = false;
        }

        RemoveFromQueue(waitingQueue, agent);
        UpdateQueuePositions();
        Debug.Log($"AI가 대기열에서 나갔습니다. (남은 인원: {waitingQueue.Count}명)");
    }

    private void RemoveFromQueue(Queue<AIAgent> queue, AIAgent agent)
    {
        var tempQueue = new Queue<AIAgent>();
        while (queue.Count > 0)
        {
            var queuedAgent = queue.Dequeue();
            if (queuedAgent != agent)
            {
                tempQueue.Enqueue(queuedAgent);
            }
        }
        while (tempQueue.Count > 0)
        {
            queue.Enqueue(tempQueue.Dequeue());
        }
    }

    // 대기열 위치 업데이트
    private void UpdateQueuePositions()
    {
        int index = 0;
        foreach (var agent in waitingQueue)
        {
            if (agent != null)
            {
                if (agent == currentServingAgent)
                {
                    agent.SetQueueDestination(counterFront);
                }
                else
                {
                    float distance = counterServiceDistance + (index * queueSpacing);
                    Vector3 queuePosition = transform.position + counterTransform.forward * distance;
                    agent.SetQueueDestination(queuePosition);
                }
                index++;
            }
        }
    }

    // 현재 서비스 받을 수 있는지 확인
    public bool CanReceiveService(AIAgent agent)
    {
        return waitingQueue.Count > 0 && waitingQueue.Peek() == agent && !isProcessingService;
    }

    // 서비스 시작
    public void StartService(AIAgent agent)
    {
        if (CanReceiveService(agent))
        {
            currentServingAgent = agent;
            isProcessingService = true;
            agent.SetQueueDestination(counterFront);
            UpdateQueuePositions();
            StartCoroutine(ServiceCoroutine(agent));
            Debug.Log($"서비스가 시작되었습니다.");
        }
    }

    // 서비스 처리 코루틴
    private IEnumerator ServiceCoroutine(AIAgent agent)
    {
        yield return new WaitForSeconds(serviceTime);
        
        if (currentServingAgent == agent)
        {
            // 대기열에서 제거
            if (waitingQueue.Count > 0 && waitingQueue.Peek() == agent)
            {
                waitingQueue.Dequeue();
            }

            currentServingAgent = null;
            isProcessingService = false;
            UpdateQueuePositions();
            agent.OnServiceComplete();
            Debug.Log($"서비스가 완료되었습니다. (남은 대기 인원: {waitingQueue.Count}명)");
        }
    }

    // 대기열 위치 얻기
    public Vector3 GetCounterServicePosition()
    {
        return counterFront;
    }

    void OnDrawGizmos()
    {
        // 에디터에서도 대기열 위치를 시각화
        if (!Application.isPlaying)
        {
            counterFront = transform.position + transform.forward * counterServiceDistance;
        }

        // 서비스 위치 표시 (노란색)
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(counterFront, 0.3f);

        // 대기열 위치 표시 (파란색)
        Gizmos.color = Color.blue;
        for (int i = 0; i < maxQueueLength; i++)
        {
            float distance = counterServiceDistance + (i * queueSpacing);
            Vector3 queuePos = transform.position + transform.forward * distance;
            Gizmos.DrawSphere(queuePos, 0.2f);
            
            // 대기열 라인 표시
            if (i < maxQueueLength - 1)
            {
                float nextDistance = counterServiceDistance + ((i + 1) * queueSpacing);
                Vector3 nextPos = transform.position + transform.forward * nextDistance;
                Gizmos.DrawLine(queuePos, nextPos);
            }
        }
    }
} 