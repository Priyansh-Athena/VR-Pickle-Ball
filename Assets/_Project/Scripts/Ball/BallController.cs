using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class BallController : MonoBehaviour
{
    public enum BallState
    {
        Idle,
        Resetting,

        // First required bounce:
        OpponentServeToGround,
        ServeBounceToPlayer,

        WaitingForPlayerHit,

        // Second required bounce:
        PlayerShotToOpponentGround,
        OpponentBounceToHit,

        // Normal rally after the two-bounce rule:
        PlayerShotToOpponentHit,
        OpponentReturnToPlayer,

        AIMissBounce,
        PointOver
    }

    private enum LastHitter
    {
        None,
        Player,
        Opponent
    }

    private enum ScoringSide
    {
        Player,
        Opponent
    }

    [Header("Ball")]
    [SerializeField] private Rigidbody rb;

    [Tooltip("Position from which the opponent serves.")]
    [SerializeField] private Transform servePoint;

    [Header("Opponent")]
    [Tooltip("The cube that represents the AI opponent.")]
    [SerializeField] private Transform opponentCube;

    [Tooltip("Optional resting position for the opponent cube.")]
    [SerializeField] private Transform opponentHomePoint;

    [Tooltip("First corner of the opponent court area.")]
    [SerializeField] private Transform opponentCornerA;

    [Tooltip("Diagonally opposite corner of the opponent court area.")]
    [SerializeField] private Transform opponentCornerB;

    [Tooltip("Height at which the AI cube hits the ball.")]
    [SerializeField] private float opponentBallHitHeight = 0.8f;

    [Tooltip("Height of the ball centre above the ground.")]
    [SerializeField] private float ballGroundOffset = 0.045f;

    [Range(0f, 1f)]
    [Tooltip("0.2 means that the AI misses 20% of player shots.")]
    [SerializeField] private float opponentMissChance = 0.2f;

    [Tooltip("Minimum distance between the AI and the missed ball.")]
    [SerializeField] private float opponentMissDistance = 1f;

    [Header("Player Court")]
    [Tooltip("First corner of the player court area.")]
    [SerializeField] private Transform playerCornerA;

    [Tooltip("Diagonally opposite corner of the player court area.")]
    [SerializeField] private Transform playerCornerB;

    [Tooltip("Height at which the ball should arrive near the player.")]
    [SerializeField] private float playerReceiveHeight = 1f;

    [Header("Net")]
    [Tooltip("Optional transform placed at the top-centre of the net.")]
    [SerializeField] private Transform netTop;

    [SerializeField] private float netClearance = 0.25f;

    [Header("Racket")]
    [Tooltip("Assign the racket or right controller transform.")]
    [SerializeField] private Transform racketTransform;

    [Tooltip("Optional point slightly in front of the racket face.")]
    [SerializeField] private Transform racketSweetSpot;

    [SerializeField] private LayerMask racketLayers = ~0;

    [SerializeField] private float racketAssistRadius = 0.12f;
    [SerializeField] private float racketHitCooldown = 0.15f;

    [Header("Serve")]
    [SerializeField] private float serveDelay = 0.6f;
    [SerializeField] private float serveFlightTime = 1.1f;
    [SerializeField] private float serveArcHeight = 1.4f;

    [Header("Serve Bounce")]
    [SerializeField] private float serveBounceFlightTime = 0.55f;
    [SerializeField] private float serveBounceArcHeight = 0.65f;

    [Header("Player Shot")]
    [SerializeField] private float playerShotArcHeight = 1.4f;

    [Tooltip("Duration of a powerful player shot.")]
    [SerializeField] private float minimumPlayerShotTime = 0.65f;

    [Tooltip("Duration of a slow player shot.")]
    [SerializeField] private float maximumPlayerShotTime = 0.95f;

    [SerializeField] private float fullPowerSwingSpeed = 4f;
    [SerializeField] private float upwardSwingArcInfluence = 0.15f;

    [Header("Opponent Bounce And Hit")]
    [SerializeField] private float opponentBounceToHitTime = 0.45f;
    [SerializeField] private float opponentBounceToHitArcHeight = 0.45f;

    [Header("Opponent Return")]
    [SerializeField] private float opponentReturnFlightTime = 1f;
    [SerializeField] private float opponentReturnArcHeight = 1.5f;

    [Header("AI Miss")]
    [SerializeField] private float aiMissBounceTime = 0.55f;
    [SerializeField] private float aiMissBounceHeight = 0.45f;

    [Header("Player Miss")]
    [Tooltip("Time the player has to hit the ball after it reaches them.")]
    [SerializeField] private float waitingForHitDuration = 1.5f;

    [SerializeField] private float incomingReleaseVelocityMultiplier = 0.3f;
    [SerializeField] private float outOfBoundsY = -2f;

    [Header("Score UI")]
    [SerializeField] private TMP_Text playerScoreText;
    [SerializeField] private TMP_Text opponentScoreText;

    [Tooltip("Button near the player that calls StartGame().")]
    [SerializeField] private Button serveButton;

    [SerializeField] private string playerScorePrefix = "Player: ";
    [SerializeField] private string opponentScorePrefix = "AI: ";

    [SerializeField] private float pointEndDelay = 1f;

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;

    [Tooltip("Played when the player racket hits the ball.")]
    [SerializeField] private AudioClip playerHitClip;

    [Tooltip("Played when the AI hits or serves the ball.")]
    [SerializeField] private AudioClip opponentHitClip;

    [Tooltip("Played whenever the ball hits the court.")]
    [SerializeField] private AudioClip groundHitClip;

    [SerializeField] private AudioClip playerScoreClip;
    [SerializeField] private AudioClip opponentScoreClip;

    [Header("Tags")]
    [SerializeField] private string racketTag = "Racket";
    [SerializeField] private string playerGroundTag = "PlayerGround";
    [SerializeField] private string opponentGroundTag = "OpponentGround";
    [SerializeField] private string netTag = "Net";

    [Header("Events")]
    public UnityEvent onRallyStarted;
    public UnityEvent onPlayerHit;
    public UnityEvent onOpponentHit;
    public UnityEvent onGroundHit;
    public UnityEvent onPlayerScored;
    public UnityEvent onOpponentScored;

    [Header("Runtime Debug")]
    [SerializeField] private BallState state = BallState.Idle;
    [SerializeField] private int playerScore;
    [SerializeField] private int opponentScore;
    [SerializeField] private bool serveBounceCompleted;
    [SerializeField] private bool returnBounceCompleted;
    [SerializeField] private bool opponentWillMiss;
    [SerializeField] private Vector3 currentBallTarget;
    [SerializeField] private Vector3 trackedRacketVelocity;

    public BallState CurrentState => state;
    public int PlayerScore => playerScore;
    public int OpponentScore => opponentScore;

    private LastHitter lastHitter = LastHitter.None;

    private bool gameStarted;
    private bool roundActive;
    private bool isGuidedFlight;

    private Vector3 guidedStart;
    private Vector3 guidedControl;
    private Vector3 guidedEnd;

    private float guidedTimer;
    private float guidedDuration;

    private bool moveOpponentDuringFlight;
    private Vector3 opponentMoveStart;
    private Vector3 opponentMoveTarget;

    private Vector3 previousRacketPosition;
    private bool racketPositionInitialized;

    private float waitingForHitDeadline;
    private float nextAllowedRacketHitTime;
    private float nextAllowedGroundSoundTime;

    private float opponentRestingY;

    private Coroutine serveRoutine;
    private Coroutine pointOverRoutine;

    private readonly Collider[] racketDetectionResults = new Collider[16];

    private void Awake()
    {
        if (rb == null)
            rb = GetComponent<Rigidbody>();

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        if (rb == null)
        {
            Debug.LogError(
                "BallController requires a Rigidbody on the ball.",
                this
            );

            enabled = false;
            return;
        }

        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.useGravity = false;
        rb.isKinematic = true;

        if (opponentCube != null)
            opponentRestingY = opponentCube.position.y;
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

        state = BallState.Idle;
        roundActive = false;

        SetBallAtServePoint();
        ResetOpponentPosition();

        UpdateScoreUI();
        ShowServeButton(true);
    }

    private void FixedUpdate()
    {
        UpdateRacketVelocity();

        if (!roundActive)
            return;

        if (rb.position.y < outOfBoundsY)
        {
            AwardPointBasedOnLastHitter();
            return;
        }

        if (isGuidedFlight)
        {
            MoveAlongGuidedTrajectory();
            return;
        }

        if (state != BallState.WaitingForPlayerHit)
            return;

        if (TryDetectNearbyRacket())
            return;

        if (waitingForHitDeadline > 0f &&
            Time.time >= waitingForHitDeadline)
        {
            PlayerMissedBall();
        }
    }

    private bool ReferencesAreValid()
    {
        bool valid = true;

        valid &= ValidateReference(servePoint, "Serve Point");
        valid &= ValidateReference(opponentCube, "Opponent Cube");

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

        return valid;
    }

    private bool ValidateReference(
        Object reference,
        string fieldName)
    {
        if (reference != null)
            return true;

        Debug.LogError(
            fieldName + " is not assigned.",
            this
        );

        return false;
    }

    #region Public Game Functions

    /// <summary>
    /// Assign this function to the serve button.
    ///
    /// On the first click, it begins the game.
    /// After a point, it begins the next rally.
    /// Scores are preserved between rallies.
    /// </summary>
    public void StartGame()
    {
        if (roundActive)
            return;

        if (!gameStarted)
        {
            gameStarted = true;
            playerScore = 0;
            opponentScore = 0;

            UpdateScoreUI();
        }

        if (pointOverRoutine != null)
        {
            StopCoroutine(pointOverRoutine);
            pointOverRoutine = null;
        }

        ShowServeButton(false);
        StartOpponentServe();
    }

    public void ResetScores()
    {
        playerScore = 0;
        opponentScore = 0;

        UpdateScoreUI();
    }

    #endregion

    #region Rally Start

    private void StartOpponentServe()
    {
        if (serveRoutine != null)
            StopCoroutine(serveRoutine);

        serveRoutine = StartCoroutine(OpponentServeRoutine());
    }

    private IEnumerator OpponentServeRoutine()
    {
        roundActive = true;
        state = BallState.Resetting;

        serveBounceCompleted = false;
        returnBounceCompleted = false;
        opponentWillMiss = false;

        waitingForHitDeadline = 0f;
        nextAllowedRacketHitTime = 0f;

        isGuidedFlight = false;
        lastHitter = LastHitter.Opponent;

        StopBallPhysics();
        SetBallAtServePoint();

        MoveOpponentInstantlyToXZ(servePoint.position);

        if (serveDelay > 0f)
            yield return new WaitForSeconds(serveDelay);

        if (!roundActive)
            yield break;

        PlayOpponentHitSound();

        Vector3 randomPlayerGroundPosition =
            GetRandomPlayerGroundPosition();

        onRallyStarted?.Invoke();

        StartGuidedFlight(
            randomPlayerGroundPosition,
            serveFlightTime,
            serveArcHeight,
            BallState.OpponentServeToGround
        );

        serveRoutine = null;
    }

    #endregion

    #region Random Court Positions

    private Vector3 GetRandomPlayerGroundPosition()
    {
        return GetRandomPositionBetweenCorners(
            playerCornerA,
            playerCornerB,
            ballGroundOffset
        );
    }

    private Vector3 GetRandomOpponentGroundPosition()
    {
        return GetRandomPositionBetweenCorners(
            opponentCornerA,
            opponentCornerB,
            ballGroundOffset
        );
    }

    private Vector3 GetPlayerReceivePosition(
        Vector3 groundPosition)
    {
        float groundY = GetCourtGroundY(
            playerCornerA,
            playerCornerB
        );

        return new Vector3(
            groundPosition.x,
            groundY + playerReceiveHeight,
            groundPosition.z
        );
    }

    private Vector3 GetOpponentHitPosition(
        Vector3 groundPosition)
    {
        float groundY = GetCourtGroundY(
            opponentCornerA,
            opponentCornerB
        );

        return new Vector3(
            groundPosition.x,
            groundY + opponentBallHitHeight,
            groundPosition.z
        );
    }

    private Vector3 GetOpponentCubePosition(
        Vector3 targetPosition)
    {
        return new Vector3(
            targetPosition.x,
            opponentRestingY,
            targetPosition.z
        );
    }

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

        float groundY = GetCourtGroundY(
            cornerA,
            cornerB
        );

        return new Vector3(
            Random.Range(minimumX, maximumX),
            groundY + heightOffset,
            Random.Range(minimumZ, maximumZ)
        );
    }

    private float GetCourtGroundY(
        Transform cornerA,
        Transform cornerB)
    {
        return (
            cornerA.position.y +
            cornerB.position.y
        ) * 0.5f;
    }

    #endregion

    #region Guided Ball Movement

    private void StartGuidedFlight(
        Vector3 target,
        float duration,
        float arcHeight,
        BallState newState,
        bool animateOpponent = false,
        Vector3 newOpponentTarget = default)
    {
        StopBallPhysics();

        state = newState;
        isGuidedFlight = true;

        guidedStart = rb.position;
        guidedEnd = target;

        currentBallTarget = target;

        guidedDuration = Mathf.Max(0.1f, duration);
        guidedTimer = 0f;

        guidedControl = CalculateControlPoint(
            guidedStart,
            guidedEnd,
            arcHeight
        );

        moveOpponentDuringFlight =
            animateOpponent &&
            opponentCube != null;

        if (moveOpponentDuringFlight)
        {
            opponentMoveStart = opponentCube.position;
            opponentMoveTarget = newOpponentTarget;
        }
    }

    private void MoveAlongGuidedTrajectory()
    {
        guidedTimer += Time.fixedDeltaTime;

        float t = Mathf.Clamp01(
            guidedTimer / guidedDuration
        );

        Vector3 previousBallPosition = rb.position;

        Vector3 nextBallPosition =
            CalculateQuadraticBezier(
                guidedStart,
                guidedControl,
                guidedEnd,
                t
            );

        if (CanPlayerHitCurrentState() &&
            TryDetectRacketBetween(
                previousBallPosition,
                nextBallPosition
            ))
        {
            return;
        }

        rb.MovePosition(nextBallPosition);

        if (moveOpponentDuringFlight)
        {
            float smoothT = Mathf.SmoothStep(
                0f,
                1f,
                t
            );

            opponentCube.position = Vector3.Lerp(
                opponentMoveStart,
                opponentMoveTarget,
                smoothT
            );
        }

        if (t < 1f)
            return;

        rb.position = guidedEnd;
        transform.position = guidedEnd;

        if (moveOpponentDuringFlight)
            opponentCube.position = opponentMoveTarget;

        isGuidedFlight = false;
        moveOpponentDuringFlight = false;

        GuidedFlightFinished();
    }

    private void GuidedFlightFinished()
    {
        switch (state)
        {
            case BallState.OpponentServeToGround:

                HandleServeGroundBounce();
                break;

            case BallState.ServeBounceToPlayer:

                ReleaseBallNearPlayer();
                break;

            case BallState.PlayerShotToOpponentGround:

                HandleOpponentGroundBounce();
                break;

            case BallState.OpponentBounceToHit:

                OpponentHitsBall();
                break;

            case BallState.PlayerShotToOpponentHit:

                OpponentHitsBall();
                break;

            case BallState.OpponentReturnToPlayer:

                ReleaseBallNearPlayer();
                break;

            case BallState.AIMissBounce:

                PlayGroundHitSound();
                AwardPoint(ScoringSide.Player);
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

    #region Two-Bounce Rule

    private void HandleServeGroundBounce()
    {
        if (state != BallState.OpponentServeToGround)
            return;

        PlayGroundHitSound();

        serveBounceCompleted = true;

        Vector3 playerReceivePosition =
            GetPlayerReceivePosition(guidedEnd);

        StartGuidedFlight(
            playerReceivePosition,
            serveBounceFlightTime,
            serveBounceArcHeight,
            BallState.ServeBounceToPlayer
        );
    }

    private void HandleOpponentGroundBounce()
    {
        if (state != BallState.PlayerShotToOpponentGround)
            return;

        PlayGroundHitSound();

        // This is the second required bounce:
        // 1. Opponent serve bounced on the player side.
        // 2. Player return bounced on the opponent side.
        returnBounceCompleted = true;

        Vector3 groundPosition = guidedEnd;

        if (opponentWillMiss)
        {
            StartAIMissBounce(groundPosition);
            return;
        }

        Vector3 opponentHitPosition =
            GetOpponentHitPosition(groundPosition);

        StartGuidedFlight(
            opponentHitPosition,
            opponentBounceToHitTime,
            opponentBounceToHitArcHeight,
            BallState.OpponentBounceToHit
        );
    }

    #endregion

    #region Player Hit

    private void PlayerHitsBall(
        Collider racketCollider)
    {
        if (!roundActive)
            return;

        if (!CanPlayerHitCurrentState())
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

        PlayPlayerHitSound();
        onPlayerHit?.Invoke();

        lastHitter = LastHitter.Player;

        opponentWillMiss =
            Random.value < opponentMissChance;

        Vector3 randomOpponentGroundPosition =
            GetRandomOpponentGroundPosition();

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
            racketVelocity.y *
            upwardSwingArcInfluence,
            -0.3f,
            1f
        );

        float shotArc = Mathf.Max(
            0.6f,
            playerShotArcHeight + upwardArcChange
        );

        /*
         * Before the two-bounce rule is complete,
         * the player's first return must bounce.
         *
         * If the AI is going to miss, the ball also
         * travels toward the ground so the miss is visible.
         */
        if (!returnBounceCompleted ||
            opponentWillMiss)
        {
            Vector3 opponentMovementTarget;

            if (opponentWillMiss)
            {
                opponentMovementTarget =
                    GetOpponentMissPosition(
                        randomOpponentGroundPosition
                    );
            }
            else
            {
                opponentMovementTarget =
                    GetOpponentCubePosition(
                        randomOpponentGroundPosition
                    );
            }

            StartGuidedFlight(
                randomOpponentGroundPosition,
                shotDuration,
                shotArc,
                BallState.PlayerShotToOpponentGround,
                true,
                opponentMovementTarget
            );

            return;
        }

        // After the two-bounce rule, the AI may volley.
        Vector3 opponentHitPosition =
            GetOpponentHitPosition(
                randomOpponentGroundPosition
            );

        Vector3 opponentCubeTarget =
            GetOpponentCubePosition(
                opponentHitPosition
            );

        StartGuidedFlight(
            opponentHitPosition,
            shotDuration,
            shotArc,
            BallState.PlayerShotToOpponentHit,
            true,
            opponentCubeTarget
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

    #region Opponent Hit And Miss

    private void OpponentHitsBall()
    {
        if (!roundActive)
            return;

        if (opponentWillMiss)
        {
            StartAIMissBounce(rb.position);
            return;
        }

        PlayOpponentHitSound();
        onOpponentHit?.Invoke();

        lastHitter = LastHitter.Opponent;

        Vector3 randomPlayerGroundPosition =
            GetRandomPlayerGroundPosition();

        Vector3 playerReceivePosition =
            GetPlayerReceivePosition(
                randomPlayerGroundPosition
            );

        StartGuidedFlight(
            playerReceivePosition,
            opponentReturnFlightTime,
            opponentReturnArcHeight,
            BallState.OpponentReturnToPlayer
        );
    }

    private void StartAIMissBounce(
        Vector3 startingGroundPosition)
    {
        Vector3 missLandingPosition =
            GetRandomOpponentGroundPosition();

        StartGuidedFlight(
            missLandingPosition,
            aiMissBounceTime,
            aiMissBounceHeight,
            BallState.AIMissBounce
        );
    }

    private Vector3 GetOpponentMissPosition(
        Vector3 ballTarget)
    {
        Vector3 randomWrongPosition =
            GetRandomOpponentGroundPosition();

        Vector2 ballXZ = new Vector2(
            ballTarget.x,
            ballTarget.z
        );

        Vector2 wrongXZ = new Vector2(
            randomWrongPosition.x,
            randomWrongPosition.z
        );

        if (Vector2.Distance(ballXZ, wrongXZ) <
            opponentMissDistance)
        {
            Vector2 direction =
                wrongXZ - ballXZ;

            if (direction.sqrMagnitude < 0.01f)
                direction = Vector2.right;

            direction.Normalize();

            wrongXZ =
                ballXZ +
                direction * opponentMissDistance;
        }

        return new Vector3(
            wrongXZ.x,
            opponentRestingY,
            wrongXZ.y
        );
    }

    #endregion

    #region Player Receiving And Missing

    private void ReleaseBallNearPlayer()
    {
        state = BallState.WaitingForPlayerHit;
        isGuidedFlight = false;

        rb.isKinematic = false;
        rb.useGravity = true;

        Vector3 endingVelocity =
            2f * (guidedEnd - guidedControl) /
            Mathf.Max(guidedDuration, 0.1f);

        endingVelocity *=
            incomingReleaseVelocityMultiplier;

        SetRigidbodyVelocity(
            rb,
            endingVelocity
        );

        waitingForHitDeadline =
            Time.time + waitingForHitDuration;
    }

    private void PlayerMissedBall()
    {
        if (!roundActive)
            return;

        PlayGroundHitSound();
        AwardPoint(ScoringSide.Opponent);
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

            PlayerHitsBall(detectedCollider);
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

            PlayerHitsBall(detectedCollider);
            return true;
        }

        return false;
    }

    #endregion

    #region Collision Detection

    private void OnCollisionEnter(
        Collision collision)
    {
        HandleCollider(collision.collider);
    }

    private void OnTriggerEnter(
        Collider other)
    {
        HandleCollider(other);
    }

    private void HandleCollider(
        Collider other)
    {
        if (!roundActive || other == null)
            return;

        if (ColliderHasTag(other, racketTag))
        {
            PlayerHitsBall(other);
            return;
        }

        if (ColliderHasTag(other, netTag))
        {
            HandleNetFault();
            return;
        }

        if (ColliderHasTag(
                other,
                playerGroundTag))
        {
            if (state == BallState.WaitingForPlayerHit ||
                state == BallState.ServeBounceToPlayer ||
                state == BallState.OpponentReturnToPlayer)
            {
                PlayerMissedBall();
            }

            return;
        }

        if (ColliderHasTag(
                other,
                opponentGroundTag))
        {
            if (state ==
                BallState.PlayerShotToOpponentGround)
            {
                isGuidedFlight = false;
                HandleOpponentGroundBounce();
            }
        }
    }

    private void HandleNetFault()
    {
        if (!roundActive)
            return;

        if (lastHitter == LastHitter.Player)
        {
            AwardPoint(ScoringSide.Opponent);
        }
        else
        {
            AwardPoint(ScoringSide.Player);
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

    #region Scoring

    private void AwardPoint(
        ScoringSide scoringSide)
    {
        if (!roundActive)
            return;

        roundActive = false;
        isGuidedFlight = false;

        waitingForHitDeadline = 0f;
        state = BallState.PointOver;

        StopBallPhysics();

        if (scoringSide == ScoringSide.Player)
        {
            playerScore++;

            PlayClip(playerScoreClip);
            onPlayerScored?.Invoke();
        }
        else
        {
            opponentScore++;

            PlayClip(opponentScoreClip);
            onOpponentScored?.Invoke();
        }

        UpdateScoreUI();

        if (pointOverRoutine != null)
            StopCoroutine(pointOverRoutine);

        pointOverRoutine =
            StartCoroutine(PointOverRoutine());
    }

    private void AwardPointBasedOnLastHitter()
    {
        if (lastHitter == LastHitter.Player)
        {
            AwardPoint(ScoringSide.Opponent);
        }
        else
        {
            AwardPoint(ScoringSide.Player);
        }
    }

    private IEnumerator PointOverRoutine()
    {
        if (pointEndDelay > 0f)
            yield return new WaitForSeconds(pointEndDelay);

        SetBallAtServePoint();
        ResetOpponentPosition();

        state = BallState.Idle;

        // The user must click the button to begin
        // the opponent's next serve.
        ShowServeButton(true);

        pointOverRoutine = null;
    }

    private void UpdateScoreUI()
    {
        if (playerScoreText != null)
        {
            playerScoreText.text =
                playerScorePrefix + playerScore;
        }

        if (opponentScoreText != null)
        {
            opponentScoreText.text =
                opponentScorePrefix + opponentScore;
        }
    }

    private void ShowServeButton(bool show)
    {
        if (serveButton != null)
            serveButton.gameObject.SetActive(show);
    }

    #endregion

    #region Audio

    private void PlayPlayerHitSound()
    {
        PlayClip(playerHitClip);
    }

    private void PlayOpponentHitSound()
    {
        if (opponentHitClip != null)
            PlayClip(opponentHitClip);
        else
            PlayClip(playerHitClip);
    }

    private void PlayGroundHitSound()
    {
        // Prevents duplicate sounds when a guided endpoint and
        // a collider event happen almost simultaneously.
        if (Time.time < nextAllowedGroundSoundTime)
            return;

        nextAllowedGroundSoundTime =
            Time.time + 0.08f;

        PlayClip(groundHitClip);
        onGroundHit?.Invoke();
    }

    private void PlayClip(AudioClip clip)
    {
        if (audioSource == null || clip == null)
            return;

        audioSource.PlayOneShot(clip);
    }

    #endregion

    #region Opponent Movement

    private void MoveOpponentInstantlyToXZ(
        Vector3 position)
    {
        if (opponentCube == null)
            return;

        opponentCube.position = new Vector3(
            position.x,
            opponentRestingY,
            position.z
        );
    }

    private void ResetOpponentPosition()
    {
        if (opponentCube == null)
            return;

        if (opponentHomePoint != null)
        {
            opponentCube.position =
                opponentHomePoint.position;

            opponentRestingY =
                opponentCube.position.y;
        }
    }

    #endregion

    #region Rigidbody Helpers

    private void SetBallAtServePoint()
    {
        StopBallPhysics();

        rb.position = servePoint.position;
        transform.position = servePoint.position;
        transform.rotation = servePoint.rotation;
    }

    private void StopBallPhysics()
    {
        if (!rb.isKinematic)
        {
            SetRigidbodyVelocity(
                rb,
                Vector3.zero
            );

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

    private bool CanPlayerHitCurrentState()
    {
        // The player cannot hit OpponentServeToGround,
        // because the serve must bounce first.
        return
            state == BallState.ServeBounceToPlayer ||
            state == BallState.OpponentReturnToPlayer ||
            state == BallState.WaitingForPlayerHit;
    }

    #endregion

    #region Gizmos

    private void OnDrawGizmosSelected()
    {
        DrawCourtArea(
            opponentCornerA,
            opponentCornerB,
            ballGroundOffset
        );

        DrawCourtArea(
            playerCornerA,
            playerCornerB,
            ballGroundOffset
        );

        if (servePoint != null)
        {
            Gizmos.DrawWireSphere(
                servePoint.position,
                0.08f
            );
        }

        if (Application.isPlaying)
        {
            Gizmos.DrawWireSphere(
                currentBallTarget,
                0.1f
            );
        }
    }

    private void DrawCourtArea(
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

    #endregion
}