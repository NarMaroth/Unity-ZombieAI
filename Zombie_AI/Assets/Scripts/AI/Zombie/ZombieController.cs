using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using AI.Helper;
using AI.Zombie.Helper;
using System;

public class ZombieController : MonoBehaviour
{
    #region variables
    [SerializeField]    [Min(1)]private float FovDistance = 1f;
    [SerializeField]    [Range(0,360)] private float FovRange;
    [SerializeField]    [Range(1, 10)] private float stopDistance = 1f;
    [SerializeField]    private LayerMask _obstructViewLayers;

    private float refreshTime = 0.2f;

    private bool _chasingPlayer;
    public bool ChasingPlayer 
    {
        get { return _chasingPlayer; }
    }

    private ZombieAnimController _zAnimController;
    public ZombieAnimController ZombieAnimController
    {
        get { return _zAnimController; }
    }
    public bool Agro
    {
        get { return _zAnimController.Agro; }
    }

    private TargetPriorityLevel _currentTargetPriority;
    private Vector3 _currentTargetPosition;
    private NavMeshPath _currentPath;

    private Coroutine _agroZombieIE;
    private Coroutine _goTowardsTargetPositionIE;
    private Coroutine _chasePlayerIE;
    private Coroutine _lostTrakOfPlayer;

    private List<GameObject> _playersInRange = new List<GameObject>();
    #endregion

    private void Awake()
    {
        #region init
        _chasingPlayer = false;
        _currentTargetPosition = Vector3.zero;
        _currentTargetPriority = TargetPriorityLevel.none;

        _currentPath = new NavMeshPath();
        _zAnimController = new ZombieAnimController(GetComponent<Animator>());
        #endregion
    }

    private void Start()
    {
        StartCoroutine(ZombieLogic());
    }

    private IEnumerator ZombieLogic() 
    {
        while (true) 
        {

            GetPlayersInsideFOV(_playersInRange);
            print(_chasingPlayer);

            if (_playersInRange.Count >= 1)
            {
                _currentTargetPosition = ClosestPlayerPosition(_playersInRange);

                if (_chasingPlayer == false)
                {
                    _chasingPlayer = true;
                    _currentTargetPriority = TargetPriorityLevel.player;

                    if (_goTowardsTargetPositionIE != null)
                    {
                        StopCoroutine(_goTowardsTargetPositionIE);

                        _chasePlayerIE = StartCoroutine(ChasePlayer());
                    }
                    else 
                    {
                        _agroZombieIE = StartCoroutine(AgroZombie());
                    }

                }
                else 
                {
                    if (_lostTrakOfPlayer != null) 
                    {
                        StopCoroutine(_lostTrakOfPlayer);

                        _chasePlayerIE = StartCoroutine(ChasePlayer());
                    }
                }
            }
            else
            {
                if (_chasingPlayer) // if lost tack of player
                {
                    _chasingPlayer = false;
                    _lostTrakOfPlayer = StartCoroutine( LostTrackOfPlayerIE() );
                }
            }

            yield return new WaitForSeconds(refreshTime);
        }
        
    }

    private IEnumerator LostTrackOfPlayerIE()
    {
        StopCoroutine(_chasePlayerIE);

        _goTowardsTargetPositionIE = StartCoroutine(GoTowardsTargetPosition()); // go toward last seen player position
        yield return _goTowardsTargetPositionIE;

        _zAnimController.Agro = false;
        _currentTargetPosition = Vector3.zero;
        _currentTargetPriority = TargetPriorityLevel.none;
    }

    public void AgroZombieBySound(Vector3 soundPosition,TargetPriorityLevel priorityLevel)
    {
        if (_chasingPlayer || !ValidateNewTarget(soundPosition, priorityLevel)) 
        {
           return;
        }

        _currentTargetPosition = soundPosition;

        _agroZombieIE = StartCoroutine( AgroZombie() );
    }

    private IEnumerator AgroZombie() 
    {
        transform.LookAt(_currentTargetPosition);

        _zAnimController.Agro = true;
        yield return new WaitForSeconds(2); // Wait until the startAgro anim. end

        if (_chasingPlayer)
        {
            _chasePlayerIE = StartCoroutine(ChasePlayer());
        }
        else 
        {
            _goTowardsTargetPositionIE = StartCoroutine( GoTowardsTargetPosition() );
        }
    }

    private IEnumerator GoTowardsTargetPosition()
    {
        while (true)
        {
            NavMesh.CalculatePath(transform.position, _currentTargetPosition, -1, _currentPath);
            AI_Helper.DebugPath(_currentPath);

            if (_currentPath.corners.Length  <= 2) // the last 2 corners (the transform pos. and the target pos.)
            {
                if (Vector3.Distance(transform.position, _currentPath.corners[1]) <= stopDistance)
                {
                    _zAnimController.StopAgro();
                    _currentTargetPosition = Vector3.zero; // Clear current target;
                    _currentTargetPriority = TargetPriorityLevel.none; 
                    yield break;
                }
            }

            transform.LookAt(_currentPath.corners[1]);

            yield return new WaitForSeconds(refreshTime);
        }
    }

    private IEnumerator ChasePlayer()
    {
        while (true)
        {
            NavMesh.CalculatePath(transform.position, _currentTargetPosition, -1, _currentPath);
            AI_Helper.DebugPath(_currentPath, Color.red);
            if (_currentPath.corners.Length <= 0)
                continue;
            if (_currentPath.corners.Length <= 2) // the last 2 corners (the transform pos. and the target pos.)
            {
                    if (Vector3.Distance(transform.position, _currentPath.corners[1]) <= stopDistance)
                    {
                        _zAnimController.Attack = true;
                    }
                    else
                    {
                        _zAnimController.Attack = false;
                    }
            }

            transform.LookAt(_currentPath.corners[1]);

            yield return new WaitForSeconds(refreshTime);
        }
    }

    #region PlayerViewMethods

    void GetPlayersInsideFOV(List<GameObject> players) 
    {
        players.Clear();

        Collider[] colliders = Physics.OverlapSphere(transform.position, FovDistance);
        if(colliders.Length != 0) 
        {
            foreach (Collider target in colliders) 
            {
                if (target.CompareTag("Player")) 
                {
                    Vector3 targetDirection = (target.transform.position - transform.position).normalized;
                    bool insideRange = Vector3.Angle(transform.forward, targetDirection) < FovRange / 2 ;
                    if (insideRange) 
                    {
                        float targetDistance = Vector3.Distance(transform.position, target.transform.position);
                        bool nothingBetween = !Physics.Raycast(transform.position, targetDirection, targetDistance, _obstructViewLayers);
                        if (nothingBetween) 
                        {
                            players.Add(target.gameObject);
                        }
                    }
                }
            }
        }
    }

    Vector3 ClosestPlayerPosition(List<GameObject> players) 
    {
        Vector3 _closestPlayerPosition = players[0].transform.position;

        for (int i = 1; i < players.Count-1; i++)
        {
            if (Vector3.Distance(transform.position, players[i].transform.position) < Vector3.Distance(transform.position, _closestPlayerPosition)) 
            {
                _closestPlayerPosition = players[i].transform.position;
            }
        }

        return _closestPlayerPosition;
    }

    #endregion

    #region TargetValidationMethods

    private bool ValidateNewTarget(Vector3 targetPosition, TargetPriorityLevel priorityLevel)
    {
        return ValidateSound(targetPosition) && ValidatePriority(priorityLevel);
    }

    private bool ValidateSound(Vector3 targetPosition)
    {
        if (_currentTargetPosition == Vector3.zero)
        {
            return true;
        }
        else if (_currentTargetPosition == targetPosition)
        {
            return false;
        }
        else
            return (Vector3.Distance(transform.position, targetPosition) < Vector3.Distance(transform.position, _currentTargetPosition));
    }

    private bool ValidatePriority(TargetPriorityLevel priorityLevel) {
        return priorityLevel < _currentTargetPriority;
    }

    #endregion

    #region Gizmos 
    private void OnDrawGizmos()
    {
        Vector3 viewAngle01 = DirectionFromAngle(transform.eulerAngles.y, -FovRange / 2);
        Vector3 viewAngle02 = DirectionFromAngle(transform.eulerAngles.y, FovRange / 2);

        Gizmos.color = Color.black;
        Gizmos.DrawLine(transform.position, transform.position + viewAngle01 * FovDistance);
        Gizmos.DrawLine(transform.position, transform.position + viewAngle02 * FovDistance);
    }

    private Vector3 DirectionFromAngle(float eulerY, float angleInDegrees)
    {
        angleInDegrees += eulerY;

        return new Vector3(Mathf.Sin(angleInDegrees * Mathf.Deg2Rad), 0, Mathf.Cos(angleInDegrees * Mathf.Deg2Rad));
    }
    #endregion

    
}