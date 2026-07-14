using System.Collections;
using UnityEngine;
using UnityEngine.Events;

public class BallController : MonoBehaviour
{
    public enum BallState
    {
        Serving,
        ComingToPlayer,
        WaitingForHit,
        GoingToOpponent,
        BouncingToWall,
        ReturningToPlayer,
        Resetting
    }

    [Header("Ball")]
    [SerializeField] private Rigidbody rb;

    [Header("Serve")]
    [Tooltip("The position on the opponent side where every serve begins.")]
    [SerializeField] private Transform servePoint;

    [Header("Opponent Landing Area")]
    [Tooltip("First corner of the opponent landing area.")]
    [SerializeField] private Transform opponentCornerA;

    [Tooltip("Diagonally opposite corner of the opponent landing area.")]
    [SerializeField] private Transform opponentCornerB;

    [Tooltip("Height of the ball centre above the opponent ground.")]
    [SerializeField] private float opponentGroundOffset = 0.045f;

    [Header("Player Return Area")]
    [Tooltip("First corner of the player receiving area.")]
    [SerializeField] private Transform playerCornerA;

    [Tooltip("Diagonally opposite corner of the player receiving area.")]
    [SerializeField] private Transform playerCornerB;

    [Tooltip(
        "Height above the player ground where the incoming ball should arrive. " +
        "Use approximately 0.8 to 1.2 metres."
    )]
    [SerializeField] private float playerReceiveHeight = 1f;

    [Header("Wall And Net")]
    [Tooltip("Point where the ball should hit the practice wall.")]
    [SerializeField] private Transform wallTarget;

    [Tooltip("Optional point placed at the top-centre of the net.")]
    [SerializeField] private Transform netTop;

    [Header("Racket Assistance")]
    [Tooltip("Assign the racket or right-controller transform.")]
    [SerializeField] private Transform racketTransform;

    [Tooltip("Optional empty GameObject slightly in front of the racket face.")]
    [SerializeField] private Transform racketSweetSpot;

    [SerializeField] private LayerMask racketLayers = ~0;

    [Min(0.01f)]
    [SerializeField] private float racketAssistRadius = 0.12f;

    [Min(0f)]
    [SerializeField] private float racketHitCooldown = 0.15f;

    [Header("Serve Trajectory")]
    [Min(0f)]
    [SerializeField] private float initialServeDelay = 1f;

    [Min(0f)]
    [SerializeField] private float rallyResetDelay = 1.25f;

    [Min(0.1f)]
    [SerializeField] private float serveFlightTime = 1.1f;

    [Min(0.1f)]
    [SerializeField] private float serveArcHeight = 1.4f;

    [Header("Player Shot Trajectory")]
    [Min(0.1f)]
    [SerializeField] private float playerShotArcHeight = 1.4f;

    [Tooltip("Flight duration for a strong racket swing.")]
    [Min(0.1f)]
    [SerializeField] private float minimumPlayerShotTime = 0.65f;

    [Tooltip("Flight duration for a slow racket swing.")]
    [Min(0.1f)]
    [SerializeField] private float maximumPlayerShotTime = 0.95f;

    [Tooltip("Racket speed considered a full-power swing.")]
    [Min(0.1f)]
    [SerializeField] private float fullPowerSwingSpeed = 4f;

    [Tooltip("How much upward racket movement changes the arc.")]
    [Min(0f)]
    [SerializeField] private float upwardSwingArcInfluence = 0.15f;

    [Header("Opponent Bounce To Wall")]
    [Min(0.1f)]
    [SerializeField] private float bounceToWallFlightTime = 0.65f;

    [Min(0.1f)]
    [SerializeField] private float bounceToWallArcHeight = 0.75f;

    [Header("Wall Return")]
    [Min(0.1f)]
    [SerializeField] private float wallReturnFlightTime = 1f;

    [Min(0.1f)]
    [SerializeField] private float wallReturnArcHeight = 1.65f;

    [Header("Net Clearance")]
    [Tooltip("Minimum additional height above Net Top.")]
    [Min(0f)]
    [SerializeField] private float netClearance = 0.25f;

    [Header("Miss Detection")]
    [Tooltip("Time allowed to hit the ball after it arrives on the player side.")]
    [Min(0.1f)]
    [SerializeField] private float waitingForHitDuration = 1.5f;

    [SerializeField] private float outOfBoundsY = -2f;

    [Header("Tags")]
    [SerializeField] private string racketTag = "Racket";
    [SerializeField] private string opponentGroundTag = "OpponentGround";
    [SerializeField] private string playerGroundTag = "PlayerGround";
    [SerializeField] private string wallTag = "Wall";
    [SerializeField] private string netTag = "Net";

    [Header("Events")]
    public UnityEvent onServe;
    public UnityEvent onPlayerHit;
    public UnityEvent onOpponentBounce;
    public UnityEvent onWallHit;
    public UnityEvent onPlayerMiss;
    public UnityEvent onNetHit;

    [Header("Runtime Debug")]
    [SerializeField] private BallState state;
    [SerializeField] private Vector3 currentTarget;
    [SerializeField] private Vector3 trackedRacketVelocity;

    public BallState CurrentState => state;

    private bool isGuidedFlight;

    private Vector3 guidedStart;
    private Vector3 guidedControl;
    private Vector3 guidedEnd;

    private float guidedTimer;
    private float guidedDuration;

    private float waitingForHitDeadline;
    private float nextAllowedRacketHitTime;

    private Vector3 previousRacketPosition;
    private bool racketPositionInitialized;

    private Coroutine serveRoutine;

    private readonly Collider[] racketDetectionResults = new Collider[16];

    private void Awake()
    {
        if (rb == null)
            rb = GetComponent<Rigidbody>();

        if (rb == null)
        {
            Debug.LogError(
                "BallController requires a Rigidbody.",
                this
            );

            enabled = false;
            return;
        }

        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.useGravity = false;
        rb.isKinematic = true;
    }

    private void Start()
    {
        if (!ReferencesAreValid())
        {
            enabled = false;
            return;
        }

        if (racketTransform != null)
        {
            previousRacketPosition = racketTransform.position;
            racketPositionInitialized = true;
        }

        ResetAndServe(initialServeDelay);
    }

    private void FixedUpdate()
    {
        UpdateRacketVelocity();

        if (state != BallState.Serving &&
            state != BallState.Resetting &&
            rb.position.y < outOfBoundsY)
        {
            PlayerMiss();
            return;
        }

        if (isGuidedFlight)
        {
            MoveAlongGuidedTrajectory();
            return;
        }

        if (state != BallState.WaitingForHit)
            return;

        if (TryDetectNearbyRacket())
            return;

        if (waitingForHitDeadline > 0f &&
            Time.time >= waitingForHitDeadline)
        {
            PlayerMiss();
        }
    }

    private bool ReferencesAreValid()
    {
        bool valid = true;

        valid &= ValidateReference(servePoint, "Serve Point");

        valid &= ValidateReference(
            opponentCornerA,
            "Opponent Corner A"
        );

        valid &= ValidateReference(
            opponentCornerB,
            "Opponent Corner B"
        );

        valid &= ValidateReference(
            playerCornerA,
            "Player Corner A"
        );

        valid &= ValidateReference(
            playerCornerB,
            "Player Corner B"
        );

        valid &= ValidateReference(wallTarget, "Wall Target");

        return valid;
    }

    private bool ValidateReference(
        Object reference,
        string fieldName)
    {
        if (reference != null)
            return true;

        Debug.LogError(
            fieldName + " is not assigned in BallController.",
            this
        );

        return false;
    }

    #region Random Court Positions

    private Vector3 GetRandomOpponentLandingPosition()
    {
        return GetRandomPositionBetweenCorners(
            opponentCornerA,
            opponentCornerB,
            opponentGroundOffset
        );
    }

    private Vector3 GetRandomPlayerReceivePosition()
    {
        return GetRandomPositionBetweenCorners(
            playerCornerA,
            playerCornerB,
            playerReceiveHeight
        );
    }

    /// <summary>
    /// Generates a random position inside the rectangle defined by
    /// two diagonally opposite corner transforms.
    /// </summary>
    private Vector3 GetRandomPositionBetweenCorners(
        Transform cornerA,
        Transform cornerB,
        float heightOffset)
    {
        Vector3 positionA = cornerA.position;
        Vector3 positionB = cornerB.position;

        float minimumX = Mathf.Min(positionA.x, positionB.x);
        float maximumX = Mathf.Max(positionA.x, positionB.x);

        float minimumZ = Mathf.Min(positionA.z, positionB.z);
        float maximumZ = Mathf.Max(positionA.z, positionB.z);

        float randomX = Random.Range(minimumX, maximumX);
        float randomZ = Random.Range(minimumZ, maximumZ);

        float groundY = (positionA.y + positionB.y) * 0.5f;

        return new Vector3(
            randomX,
            groundY + heightOffset,
            randomZ
        );
    }

    #endregion

    #region Serve

    public void ResetAndServe()
    {
        ResetAndServe(rallyResetDelay);
    }

    private void ResetAndServe(float delay)
    {
        if (serveRoutine != null)
            StopCoroutine(serveRoutine);

        serveRoutine = StartCoroutine(
            ServeRoutine(delay)
        );
    }

    private IEnumerator ServeRoutine(float delay)
    {
        state = BallState.Resetting;

        isGuidedFlight = false;
        waitingForHitDeadline = 0f;

        StopBallPhysics();

        rb.position = servePoint.position;
        transform.position = servePoint.position;
        transform.rotation = servePoint.rotation;

        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        Vector3 randomPlayerPosition =
            GetRandomPlayerReceivePosition();

        onServe?.Invoke();

        StartGuidedFlight(
            randomPlayerPosition,
            serveFlightTime,
            serveArcHeight,
            BallState.ComingToPlayer
        );

        serveRoutine = null;
    }

    #endregion

    #region Guided Movement

    private void StartGuidedFlight(
        Vector3 target,
        float duration,
        float arcHeight,
        BallState newState)
    {
        StopBallPhysics();

        state = newState;
        isGuidedFlight = true;

        guidedStart = rb.position;
        guidedEnd = target;

        currentTarget = target;

        guidedDuration = Mathf.Max(0.1f, duration);
        guidedTimer = 0f;

        guidedControl = CalculateControlPoint(
            guidedStart,
            guidedEnd,
            arcHeight
        );
    }

    private void MoveAlongGuidedTrajectory()
    {
        guidedTimer += Time.fixedDeltaTime;

        float t = Mathf.Clamp01(
            guidedTimer / guidedDuration
        );

        Vector3 previousPosition = rb.position;

        Vector3 nextPosition = CalculateQuadraticBezier(
            guidedStart,
            guidedControl,
            guidedEnd,
            t
        );

        if (IsIncomingState(state) &&
            TryDetectRacketBetween(
                previousPosition,
                nextPosition
            ))
        {
            return;
        }

        rb.MovePosition(nextPosition);

        if (t < 1f)
            return;

        rb.position = guidedEnd;
        transform.position = guidedEnd;

        isGuidedFlight = false;

        GuidedFlightFinished();
    }

    private void GuidedFlightFinished()
    {
        switch (state)
        {
            case BallState.ComingToPlayer:
            case BallState.ReturningToPlayer:

                ReleaseBallNearPlayer();
                break;

            case BallState.GoingToOpponent:

                BeginOpponentBounce();
                break;

            case BallState.BouncingToWall:

                BeginWallReturn();
                break;
        }
    }

    private Vector3 CalculateControlPoint(
        Vector3 start,
        Vector3 end,
        float arcHeight)
    {
        Vector3 control = (start + end) * 0.5f;

        float desiredApex =
            Mathf.Max(start.y, end.y) +
            Mathf.Max(0.1f, arcHeight);

        if (netTop != null)
        {
            desiredApex = Mathf.Max(
                desiredApex,
                netTop.position.y + netClearance
            );
        }

        control.y =
            (2f * desiredApex) -
            (0.5f * (start.y + end.y));

        return control;
    }

    private static Vector3 CalculateQuadraticBezier(
        Vector3 start,
        Vector3 control,
        Vector3 end,
        float t)
    {
        float inverseT = 1f - t;

        return
            inverseT * inverseT * start +
            2f * inverseT * t * control +
            t * t * end;
    }

    #endregion

    #region Racket Hit

    private void PlayerHit(Collider racketCollider)
    {
        if (!CanRacketHitCurrentState())
            return;

        if (Time.time < nextAllowedRacketHitTime)
            return;

        nextAllowedRacketHitTime =
            Time.time + racketHitCooldown;

        isGuidedFlight = false;
        waitingForHitDeadline = 0f;

        Vector3 racketVelocity =
            GetRacketHitVelocity(racketCollider);

        StopBallPhysics();

        if (racketSweetSpot != null)
        {
            rb.position = racketSweetSpot.position;
            transform.position = racketSweetSpot.position;
        }

        // A completely new random position is selected
        // every time the racket hits the ball.
        Vector3 randomOpponentPosition =
            GetRandomOpponentLandingPosition();

        float swingStrength = Mathf.InverseLerp(
            0f,
            fullPowerSwingSpeed,
            racketVelocity.magnitude
        );

        float shotDuration = Mathf.Lerp(
            maximumPlayerShotTime,
            minimumPlayerShotTime,
            swingStrength
        );

        float upwardArcChange = Mathf.Clamp(
            racketVelocity.y * upwardSwingArcInfluence,
            -0.3f,
            1f
        );

        float shotArc = Mathf.Max(
            0.6f,
            playerShotArcHeight + upwardArcChange
        );

        onPlayerHit?.Invoke();

        StartGuidedFlight(
            randomOpponentPosition,
            shotDuration,
            shotArc,
            BallState.GoingToOpponent
        );
    }

    private Vector3 GetRacketHitVelocity(
        Collider racketCollider)
    {
        if (racketTransform != null)
            return trackedRacketVelocity;

        if (racketCollider != null &&
            racketCollider.attachedRigidbody != null)
        {
            return ReadRigidbodyVelocity(
                racketCollider.attachedRigidbody
            );
        }

        return Vector3.zero;
    }

    private void UpdateRacketVelocity()
    {
        if (racketTransform == null)
        {
            trackedRacketVelocity = Vector3.zero;
            return;
        }

        if (!racketPositionInitialized)
        {
            previousRacketPosition =
                racketTransform.position;

            racketPositionInitialized = true;
            trackedRacketVelocity = Vector3.zero;

            return;
        }

        trackedRacketVelocity =
            (racketTransform.position -
             previousRacketPosition) /
            Mathf.Max(Time.fixedDeltaTime, 0.0001f);

        previousRacketPosition =
            racketTransform.position;
    }

    #endregion

    #region Bounce And Return

    private void BeginOpponentBounce()
    {
        if (state != BallState.GoingToOpponent)
            return;

        onOpponentBounce?.Invoke();

        StartGuidedFlight(
            wallTarget.position,
            bounceToWallFlightTime,
            bounceToWallArcHeight,
            BallState.BouncingToWall
        );
    }

    private void BeginWallReturn()
    {
        if (state != BallState.BouncingToWall)
            return;

        onWallHit?.Invoke();

        // Select a new random player-side target
        // after every wall hit.
        Vector3 randomPlayerPosition =
            GetRandomPlayerReceivePosition();

        StartGuidedFlight(
            randomPlayerPosition,
            wallReturnFlightTime,
            wallReturnArcHeight,
            BallState.ReturningToPlayer
        );
    }

    private void ReleaseBallNearPlayer()
    {
        state = BallState.WaitingForHit;
        isGuidedFlight = false;

        rb.isKinematic = false;
        rb.useGravity = true;

        Vector3 endingVelocity =
            2f * (guidedEnd - guidedControl) /
            Mathf.Max(guidedDuration, 0.1f);

        SetRigidbodyVelocity(rb, endingVelocity);

        waitingForHitDeadline =
            Time.time + waitingForHitDuration;
    }

    #endregion

    #region Assisted Racket Detection

    private bool TryDetectRacketBetween(
        Vector3 start,
        Vector3 end)
    {
        int detectedCount;

        if ((end - start).sqrMagnitude < 0.0001f)
        {
            detectedCount =
                Physics.OverlapSphereNonAlloc(
                    start,
                    racketAssistRadius,
                    racketDetectionResults,
                    racketLayers,
                    QueryTriggerInteraction.Collide
                );
        }
        else
        {
            detectedCount =
                Physics.OverlapCapsuleNonAlloc(
                    start,
                    end,
                    racketAssistRadius,
                    racketDetectionResults,
                    racketLayers,
                    QueryTriggerInteraction.Collide
                );
        }

        for (int i = 0; i < detectedCount; i++)
        {
            Collider detectedCollider =
                racketDetectionResults[i];

            if (detectedCollider == null)
                continue;

            if (!ColliderHasTag(
                    detectedCollider,
                    racketTag))
            {
                continue;
            }

            PlayerHit(detectedCollider);
            return true;
        }

        return false;
    }

    private bool TryDetectNearbyRacket()
    {
        int detectedCount =
            Physics.OverlapSphereNonAlloc(
                rb.position,
                racketAssistRadius,
                racketDetectionResults,
                racketLayers,
                QueryTriggerInteraction.Collide
            );

        for (int i = 0; i < detectedCount; i++)
        {
            Collider detectedCollider =
                racketDetectionResults[i];

            if (detectedCollider == null)
                continue;

            if (!ColliderHasTag(
                    detectedCollider,
                    racketTag))
            {
                continue;
            }

            PlayerHit(detectedCollider);
            return true;
        }

        return false;
    }

    #endregion

    #region Collision Detection

    private void OnCollisionEnter(Collision collision)
    {
        HandleCollider(collision.collider);
    }

    private void OnTriggerEnter(Collider other)
    {
        HandleCollider(other);
    }

    private void HandleCollider(Collider other)
    {
        if (other == null)
            return;

        if (ColliderHasTag(other, racketTag))
        {
            PlayerHit(other);
            return;
        }

        if (ColliderHasTag(other, netTag))
        {
            NetHit();
            return;
        }

        if (ColliderHasTag(other, playerGroundTag))
        {
            if (IsIncomingState(state) ||
                state == BallState.WaitingForHit)
            {
                PlayerMiss();
            }

            return;
        }

        if (ColliderHasTag(
                other,
                opponentGroundTag))
        {
            if (state == BallState.GoingToOpponent)
            {
                isGuidedFlight = false;
                BeginOpponentBounce();
            }

            return;
        }

        if (ColliderHasTag(other, wallTag))
        {
            if (state == BallState.BouncingToWall)
            {
                isGuidedFlight = false;
                BeginWallReturn();
            }
        }
    }

    private bool ColliderHasTag(
        Collider targetCollider,
        string requiredTag)
    {
        if (targetCollider.CompareTag(requiredTag))
            return true;

        if (targetCollider.attachedRigidbody != null &&
            targetCollider.attachedRigidbody.CompareTag(
                requiredTag))
        {
            return true;
        }

        return targetCollider.transform.root.CompareTag(
            requiredTag
        );
    }

    #endregion

    #region Miss And Reset

    private void NetHit()
    {
        if (state == BallState.Serving ||
            state == BallState.Resetting)
        {
            return;
        }

        onNetHit?.Invoke();
        ResetAndServe(rallyResetDelay);
    }

    private void PlayerMiss()
    {
        if (state == BallState.Serving ||
            state == BallState.Resetting)
        {
            return;
        }

        onPlayerMiss?.Invoke();
        ResetAndServe(rallyResetDelay);
    }

    #endregion

    #region Rigidbody Helpers

    private void StopBallPhysics()
    {
        if (!rb.isKinematic)
        {
            SetRigidbodyVelocity(rb, Vector3.zero);
            rb.angularVelocity = Vector3.zero;
        }

        rb.useGravity = false;
        rb.isKinematic = true;
    }

    private static Vector3 ReadRigidbodyVelocity(
        Rigidbody targetRigidbody)
    {
#if UNITY_6000_0_OR_NEWER
        return targetRigidbody.linearVelocity;
#else
        return targetRigidbody.velocity;
#endif
    }

    private static void SetRigidbodyVelocity(
        Rigidbody targetRigidbody,
        Vector3 velocity)
    {
#if UNITY_6000_0_OR_NEWER
        targetRigidbody.linearVelocity = velocity;
#else
        targetRigidbody.velocity = velocity;
#endif
    }

    private bool CanRacketHitCurrentState()
    {
        return
            state == BallState.ComingToPlayer ||
            state == BallState.ReturningToPlayer ||
            state == BallState.WaitingForHit;
    }

    private static bool IsIncomingState(
        BallState targetState)
    {
        return
            targetState == BallState.ComingToPlayer ||
            targetState == BallState.ReturningToPlayer;
    }

    #endregion

    private void OnDrawGizmosSelected()
    {
        DrawRandomArea(
            opponentCornerA,
            opponentCornerB,
            opponentGroundOffset
        );

        DrawRandomArea(
            playerCornerA,
            playerCornerB,
            playerReceiveHeight
        );

        if (servePoint != null)
            Gizmos.DrawWireSphere(servePoint.position, 0.08f);

        if (wallTarget != null)
            Gizmos.DrawWireSphere(wallTarget.position, 0.08f);

        if (Application.isPlaying)
            Gizmos.DrawWireSphere(currentTarget, 0.12f);
    }

    private void DrawRandomArea(
        Transform cornerA,
        Transform cornerB,
        float heightOffset)
    {
        if (cornerA == null || cornerB == null)
            return;

        Vector3 positionA = cornerA.position;
        Vector3 positionB = cornerB.position;

        Vector3 center = new Vector3(
            (positionA.x + positionB.x) * 0.5f,
            ((positionA.y + positionB.y) * 0.5f) +
            heightOffset,
            (positionA.z + positionB.z) * 0.5f
        );

        Vector3 size = new Vector3(
            Mathf.Abs(positionB.x - positionA.x),
            0.02f,
            Mathf.Abs(positionB.z - positionA.z)
        );

        Gizmos.DrawWireCube(center, size);
    }
}