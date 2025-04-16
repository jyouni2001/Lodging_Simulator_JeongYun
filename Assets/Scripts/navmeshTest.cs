using UnityEngine;
using UnityEngine.AI;

public class navmeshTest : MonoBehaviour
{
    private UnityEngine.AI.NavMeshAgent agent;       // NavMeshAgent 컴포넌트
        private Animator animator;        // Animator 컴포넌트
        private Camera mainCamera;        // 마우스 클릭 위치 계산용 카메라
    
        void Start()
        {
            // 필요한 컴포넌트 가져오기
            agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
            animator = GetComponent<Animator>();
            mainCamera = Camera.main; // 메인 카메라 참조
        }
    
        void Update()
        {
            // 마우스 우클릭 감지
            if (Input.GetMouseButtonDown(1)) // 1은 우클릭
            {
                MoveToClickPosition();
            }
    
            // 캐릭터가 움직이는 중인지 체크
            UpdateMovementAnimation();
        }
    
        void MoveToClickPosition()
        {
            // 마우스 위치를 화면에서 월드 좌표로 변환
            Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;
    
            // NavMesh 위에 클릭한 위치를 찾기
            if (Physics.Raycast(ray, out hit))
            {
                // NavMesh에 유효한 위치인지 확인
                UnityEngine.AI.NavMeshHit navHit;
                if (UnityEngine.AI.NavMesh.SamplePosition(hit.point, out navHit, 1.0f, UnityEngine.AI.NavMesh.AllAreas))
                {
                    agent.SetDestination(navHit.position); // 목적지 설정
                    animator.SetBool("Moving", true);      // 이동 시작 시 애니메이션 활성화
                }
            }
        }
    
        void UpdateMovementAnimation()
        {
            // 목적지에 거의 도달했는지 확인 (remainingDistance 사용)
            if (agent.remainingDistance <= agent.stoppingDistance && !agent.pathPending)
            {
                animator.SetBool("Moving", false); // 도착 시 애니메이션 비활성화
            }
            else if (agent.hasPath) // 경로가 있고 움직이는 중일 때
            {
                animator.SetBool("Moving", true);
            }
        }
}
