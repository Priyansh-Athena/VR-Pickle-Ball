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

        // First required bounce: opponent serve must bounce on player side.
        OpponentServeToPlayerGround,

        // Ball is physically live near the player.
        WaitingForPlayerHit,

        // Second required bounce: player's first return must bounce on opponent side.
        PlayerShotToOpponentGround,

        // Ball is physically bouncing toward the AI.
        OpponentBounceLive,

        // AI intentionally misses this physical bounce.
        AIMissLive,

        // AI has returned the ball toward the player.
        OpponentReturnToPlayer,

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
    [Tooltip("The cube or model that represents the AI opponent.")]
    [SerializeField] private Transform opponentCube;

    [Tooltip("Optional resting position for the opponent.")]
    [SerializeField] private Transform opponentHomePoint;

    [Tooltip("First corner of the opponent court.")]
    [SerializeField] private Transform opponentCornerA;

    [Tooltip("Diagonally opposite corner of the opponent court.")]
    [SerializeField] private Transform opponentCornerB;

    [Tooltip("Height above the opponent court at which the AI meets the bounced ball.")]
    [SerializeField, Min(0.1f)]
    private float opponentBallHitHeight = 0.75f;

    [Range(0f, 1f)]
    [Tooltip("0.2 means the opponent misses 20% of valid player shots.")]
    [SerializeField]
    private float opponentMissChance = 0.2f;

    [Tooltip("How far away from the ball the opponent moves when intentionally missing.")]
    [SerializeField, Min(0.1f)]
    private float opponentMissDistance = 1.25f;

    [Header("Player Court")]
    [Tooltip("First corner of the player court.")]
    [SerializeField] private Transform playerCornerA;

    [Tooltip("Diagonally opposite corner of the player court.")]
    [SerializeField] private Transform playerCornerB;

    [Tooltip("Height above the player court used for an AI return target.")]
    [SerializeField, Min(0.1f)]
    private float playerReceiveHeight = 1f;

    [Header("Court Detection")]
    [Tooltip("Height of the ball's centre above the court at a landing point.")]
    [SerializeField, Min(0f)]
    private float ballGroundOffset = 0.045f;

    [Tooltip("Extra tolerance used by the mathematical ground-plane detector.")]
    [SerializeField, Min(0.001f)]
    private float groundDetectionTolerance = 0.035f;

    [Tooltip("Positive values make the valid court slightly smaller. Negative values expand it.")]
    [SerializeField]
    private float courtBoundsPadding = 0.01f;

    [Header("Net")]
    [Tooltip("Optional point placed at the top-centre of the net.")]
    [SerializeField] private Transform netTop;

    [SerializeField, Min(0f)]
    private float netClearance = 0.25f;

    [Header("Racket Assistance")]
    [Tooltip("Assign the racket or right controller transform.")]
    [SerializeField] private Transform racketTransform;

    [Tooltip("Optional point slightly in front of the racket face.")]
    [SerializeField] private Transform racketSweetSpot;

    [SerializeField] private LayerMask racketLayers = ~0;

    [SerializeField, Min(0.01f)]
    private float racketAssistRadius = 0.12f;

    [SerializeField, Min(0f)]
    private float racketHitCooldown = 0.15f;

    [Header("Opponent Serve")]
    [SerializeField, Min(0f)]
    private float serveDelay = 0.6f;

    [Tooltip("Maximum height above the serve's highest endpoint.")]
    [SerializeField, Min(0.05f)]
    private float serveArcHeight = 1.25f;

    [Header("Player Shot")]
    [Tooltip("Maximum height above the player shot's highest endpoint.")]
    [SerializeField, Min(0.05f)]
    private float playerShotArcHeight = 1.3f;

    [Tooltip("How much the racket's vertical velocity influences the assisted shot arc.")]
    [SerializeField, Min(0f)]
    private float upwardSwingArcInfluence = 0.12f;

    [Header("Opponent Return")]
    [Tooltip("Maximum height above the AI return's highest endpoint.")]
    [SerializeField, Min(0.05f)]
    private float opponentReturnArcHeight = 1.3f;

    [Header("Physical Ground Bounce")]
    [Tooltip("Height reached after the serve bounces on the player court.")]
    [SerializeField, Min(0.05f)]
    private float serveBounceHeight = 0.75f;

    [Tooltip("Height reached after a player shot bounces on the opponent court.")]
    [SerializeField, Min(0.05f)]
    private float opponentBounceHeight = 0.65f;

    [Tooltip("Percentage of horizontal speed retained after a ground bounce.")]
    [SerializeField, Range(0.1f, 1.5f)]
    private float horizontalBounceRetention = 0.82f;

    [Tooltip("Prevents a bounce from appearing almost vertical.")]
    [SerializeField, Min(0.05f)]
    private float minimumBounceHorizontalSpeed = 1.6f;

    [Tooltip("Limits excessively fast post-bounce movement.")]
    [SerializeField, Min(0.1f)]
    private float maximumBounceHorizontalSpeed = 7f;

    [Tooltip("1 uses normal Unity gravity. 0.8 makes the bounce slightly floatier.")]
    [SerializeField, Min(0.1f)]
    private float bounceGravityMultiplier = 1f;

    [Header("Miss Detection")]
    [Tooltip("Safety timeout. Normal misses are detected when the ball reaches the ground.")]
    [SerializeField, Min(0.5f)]
    private float playerHitSafetyTimeout = 5f;

    [SerializeField]
    private float outOfBoundsY = -3f;

    [Header("Score UI")]
    [SerializeField] private TMP_Text playerScoreText;
    [SerializeField] private TMP_Text opponentScoreText;
    [SerializeField] private TMP_Text highScoreText;

    [Tooltip("The button that calls StartGame().")]
    [SerializeField] private Button serveButton;

    [SerializeField] private string playerScorePrefix = "Player: ";
    [SerializeField] private string opponentScorePrefix = "AI: ";
    [SerializeField] private string highScorePrefix = "High Score: ";

    [Tooltip("PlayerPrefs key used to store the player's best score.")]
    [SerializeField]
    private string highScorePlayerPrefsKey = "PickleballPlayerHighScore";

    [SerializeField, Min(0f)]
    private float pointEndDelay = 1f;

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip playerHitClip;
    [SerializeField] private AudioClip opponentHitClip;
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
    [SerializeField] private int highScore;

    [SerializeField] private bool firstServeBounceCompleted;
    [SerializeField] private bool firstReturnBounceCompleted;
    [SerializeField] private bool opponentWillMiss;

    [SerializeField] private Vector3 currentTarget;
    [SerializeField] private Vector3 currentVelocity;
    [SerializeField] private Vector3 currentBounceDirection;

    public BallState CurrentState => state;
    public int PlayerScore => playerScore;
    public int OpponentScore => opponentScore;
    public int HighScore => highScore;

    private LastHitter lastHitter = LastHitter.None;

    private bool gameStarted;
    private bool roundActive;

    // Assisted projectile movement.
    private bool guidedFlightActive;
    private Vector3 guidedStart;
    private Vector3 guidedTarget;
    private Vector3 guidedInitialVelocity;
    private Vector3 guidedGravity;
    private float guidedDuration;
    private float guidedTimer;

    // Physical movement after a bounce or near the player.
    private bool physicalFlightActive;
    private Vector3 previousPhysicsPosition;
    private float ignoreGroundUntil;
    private float playerHitDeadline;

    // True only after the opponent's serve has already made its required
    // first bounce on the player side.
    private bool incomingBallAlreadyBouncedOnPlayerSide;

    // Prevents duplicate collider/plane landing events.
    private float lastGroundResolutionTime;
    private float nextAllowedGroundSoundTime;

    // Opponent movement.
    private bool moveOpponentDuringGuidedFlight;
    private Vector3 opponentMoveStart;
    private Vector3 opponentMoveTarget;
    private float opponentRestingY;

    // Racket tracking.
    private Vector3 previousRacketPosition;
    private bool racketPositionInitialized;
    private Vector3 trackedRacketVelocity;
    private float nextAllowedRacketHitTime;

    private Coroutine serveRoutine;
    private Coroutine pointOverRoutine;
    private Coroutine opponentHitRoutine;

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
                "BallController requires a Rigidbody on the Ball GameObject.",
                this
            );

            enabled = false;
            return;
        }

        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
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

        LoadHighScore();
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

        if (guidedFlightActive)
        {
            MoveGuidedProjectile();
            return;
        }

        if (physicalFlightActive)
        {
            ApplyCustomGravity();
            CheckPhysicalGroundCrossing();

            if (!roundActive)
                return;
        }

        if (!CanPlayerHitCurrentState())
            return;

        if (TryDetectNearbyRacket())
            return;

        if (playerHitDeadline > 0f &&
            Time.time >= playerHitDeadline)
        {
            PlayerMissedValidBall();
        }
    }

    private bool ReferencesAreValid()
    {
        bool valid = true;

        valid &= ValidateReference(servePoint, "Serve Point");
        valid &= ValidateReference(opponentCube, "Opponent Cube");
        valid &= ValidateReference(opponentCornerA, "Opponent Corner A");
        valid &= ValidateReference(opponentCornerB, "Opponent Corner B");
        valid &= ValidateReference(playerCornerA, "Player Corner A");
        valid &= ValidateReference(playerCornerB, "Player Corner B");

        return valid;
    }

    private bool ValidateReference(Object reference, string fieldName)
    {
        if (reference != null)
            return true;

        Debug.LogError(
            fieldName + " is not assigned in BallController.",
            this
        );

        return false;
    }

    #region Public Controls

    /// <summary>
    /// Assign this function to the Start/Serve button.
    /// It starts the first rally or begins the next rally after a point.
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

        StopGameplayCoroutines();

        ShowServeButton(false);
        StartOpponentServe();
    }

    /// <summary>
    /// Resets the current match score but preserves the saved high score.
    /// </summary>
    public void ResetScores()
    {
        playerScore = 0;
        opponentScore = 0;
        UpdateScoreUI();
    }

    /// <summary>
    /// Deletes the saved high score.
    /// </summary>
    public void ResetHighScore()
    {
        highScore = 0;

        PlayerPrefs.DeleteKey(highScorePlayerPrefsKey);
        PlayerPrefs.Save();

        UpdateScoreUI();
    }

    #endregion

    #region Rally Start

    private void StartOpponentServe()
    {
        serveRoutine = StartCoroutine(OpponentServeRoutine());
    }

    private IEnumerator OpponentServeRoutine()
    {
        roundActive = true;
        state = BallState.Resetting;

        firstServeBounceCompleted = false;
        firstReturnBounceCompleted = false;
        opponentWillMiss = false;
        incomingBallAlreadyBouncedOnPlayerSide = false;

        lastHitter = LastHitter.Opponent;

        guidedFlightActive = false;
        physicalFlightActive = false;
        playerHitDeadline = 0f;
        nextAllowedRacketHitTime = 0f;

        StopBallPhysics();
        SetBallAtServePoint();
        MoveOpponentInstantlyToXZ(servePoint.position);

        if (serveDelay > 0f)
            yield return new WaitForSeconds(serveDelay);

        if (!roundActive)
            yield break;

        PlayOpponentHitSound();
        onRallyStarted?.Invoke();

        Vector3 serveLanding =
            GetRandomPlayerGroundPosition();

        StartGuidedProjectile(
            serveLanding,
            serveArcHeight,
            BallState.OpponentServeToPlayerGround
        );

        serveRoutine = null;
    }

    #endregion

    #region Assisted Projectile Movement

    /// <summary>
    /// Calculates a gravity-based projectile that reaches an exact target.
    /// This is used for assisted serves and returns.
    /// </summary>
    private void StartGuidedProjectile(
        Vector3 target,
        float arcHeight,
        BallState newState,
        bool animateOpponent = false,
        Vector3 opponentTarget = default(Vector3))
    {
        StopBallPhysics();

        state = newState;
        guidedFlightActive = true;
        physicalFlightActive = false;

        guidedStart = rb.position;
        guidedTarget = target;
        currentTarget = target;
        guidedTimer = 0f;

        float gravityMagnitude =
            Mathf.Max(0.1f, Mathf.Abs(Physics.gravity.y));

        guidedGravity =
            Vector3.down * gravityMagnitude;

        float apexY =
            Mathf.Max(guidedStart.y, guidedTarget.y) +
            Mathf.Max(0.05f, arcHeight);

        if (netTop != null)
        {
            apexY = Mathf.Max(
                apexY,
                netTop.position.y + netClearance
            );
        }

        float rise =
            Mathf.Max(0.01f, apexY - guidedStart.y);

        float fall =
            Mathf.Max(0.01f, apexY - guidedTarget.y);

        float upwardSpeed =
            Mathf.Sqrt(2f * gravityMagnitude * rise);

        float timeUp =
            upwardSpeed / gravityMagnitude;

        float timeDown =
            Mathf.Sqrt(2f * fall / gravityMagnitude);

        guidedDuration =
            Mathf.Max(0.1f, timeUp + timeDown);

        Vector3 horizontalDisplacement =
            guidedTarget - guidedStart;

        horizontalDisplacement.y = 0f;

        Vector3 horizontalVelocity =
            horizontalDisplacement / guidedDuration;

        guidedInitialVelocity =
            horizontalVelocity +
            Vector3.up * upwardSpeed;

        moveOpponentDuringGuidedFlight =
            animateOpponent &&
            opponentCube != null;

        if (moveOpponentDuringGuidedFlight)
        {
            opponentMoveStart =
                opponentCube.position;

            opponentMoveTarget =
                opponentTarget;
        }
    }

    private void MoveGuidedProjectile()
    {
        guidedTimer += Time.fixedDeltaTime;

        float time =
            Mathf.Min(guidedTimer, guidedDuration);

        float normalizedTime =
            Mathf.Clamp01(time / guidedDuration);

        Vector3 previousPosition =
            rb.position;

        Vector3 nextPosition =
            guidedStart +
            guidedInitialVelocity * time +
            0.5f *
            guidedGravity *
            time *
            time;

        currentVelocity =
            guidedInitialVelocity +
            guidedGravity * time;

        if (CanPlayerHitCurrentState() &&
            TryDetectRacketBetween(
                previousPosition,
                nextPosition
            ))
        {
            return;
        }

        rb.MovePosition(nextPosition);

        if (moveOpponentDuringGuidedFlight)
        {
            float smoothTime =
                Mathf.SmoothStep(
                    0f,
                    1f,
                    normalizedTime
                );

            opponentCube.position =
                Vector3.Lerp(
                    opponentMoveStart,
                    opponentMoveTarget,
                    smoothTime
                );
        }

        if (guidedTimer < guidedDuration)
            return;

        rb.position = guidedTarget;
        transform.position = guidedTarget;

        if (moveOpponentDuringGuidedFlight)
        {
            opponentCube.position =
                opponentMoveTarget;
        }

        guidedFlightActive = false;
        moveOpponentDuringGuidedFlight = false;

        GuidedFlightFinished();
    }

    private void GuidedFlightFinished()
    {
        switch (state)
        {
            case BallState.OpponentServeToPlayerGround:
                ResolveServeLanding();
                break;

            case BallState.PlayerShotToOpponentGround:
                ResolvePlayerShotLanding();
                break;

            case BallState.OpponentReturnToPlayer:
                ReleaseIncomingBallToPlayer();
                break;
        }
    }

    private Vector3 GetGuidedEndVelocity()
    {
        return
            guidedInitialVelocity +
            guidedGravity * guidedDuration;
    }

    #endregion

    #region Court Landing And In/Out Scoring

    private void ResolveServeLanding()
    {
        if (!roundActive ||
            GroundResolutionWasRecent())
        {
            return;
        }

        MarkGroundResolved();

        Vector3 landingPosition =
            rb.position;

        if (!IsInsideCourt(
                landingPosition,
                playerCornerA,
                playerCornerB))
        {
            // The AI served outside the player's court.
            AwardPoint(ScoringSide.Player);
            return;
        }

        PlayGroundHitSound();

        firstServeBounceCompleted = true;
        incomingBallAlreadyBouncedOnPlayerSide = true;

        Vector3 incomingVelocity =
            GetGuidedEndVelocity();

        StartPhysicalBounce(
            incomingVelocity,
            serveBounceHeight,
            BallState.WaitingForPlayerHit
        );

        playerHitDeadline =
            Time.time + playerHitSafetyTimeout;
    }

    private void ResolvePlayerShotLanding()
    {
        if (!roundActive ||
            GroundResolutionWasRecent())
        {
            return;
        }

        MarkGroundResolved();

        Vector3 landingPosition =
            rb.position;

        if (!IsInsideCourt(
                landingPosition,
                opponentCornerA,
                opponentCornerB))
        {
            // The player's shot landed outside the opponent court.
            AwardPoint(ScoringSide.Opponent);
            return;
        }

        PlayGroundHitSound();

        firstReturnBounceCompleted = true;

        Vector3 incomingVelocity =
            GetGuidedEndVelocity();

        if (opponentWillMiss)
        {
            StartPhysicalBounce(
                incomingVelocity,
                opponentBounceHeight,
                BallState.AIMissLive
            );

            MoveOpponentToMissPosition(
                landingPosition
            );

            return;
        }

        StartOpponentPhysicalBounce(
            incomingVelocity
        );
    }

    private void ResolvePlayerSideGroundContact()
    {
        if (!roundActive ||
            GroundResolutionWasRecent())
        {
            return;
        }

        MarkGroundResolved();
        PlayGroundHitSound();

        bool landedInside =
            IsInsideCourt(
                rb.position,
                playerCornerA,
                playerCornerB
            );

        /*
         * If the serve already bounced once, this is the serve's
         * second bounce and the player missed it.
         *
         * If it is a normal AI return, an outside landing gives the
         * point to the player; an inside landing means the player missed.
         */
        if (incomingBallAlreadyBouncedOnPlayerSide)
        {
            // This is the serve's second bounce: the player missed it.
            AwardPoint(ScoringSide.Opponent);
            return;
        }

        // This is the first landing of a normal AI return.
        if (landedInside)
            AwardPoint(ScoringSide.Opponent);
        else
            AwardPoint(ScoringSide.Player);
    }

    private bool IsInsideCourt(
        Vector3 worldPosition,
        Transform cornerA,
        Transform cornerB)
    {
        float minimumX =
            Mathf.Min(
                cornerA.position.x,
                cornerB.position.x
            ) + courtBoundsPadding;

        float maximumX =
            Mathf.Max(
                cornerA.position.x,
                cornerB.position.x
            ) - courtBoundsPadding;

        float minimumZ =
            Mathf.Min(
                cornerA.position.z,
                cornerB.position.z
            ) + courtBoundsPadding;

        float maximumZ =
            Mathf.Max(
                cornerA.position.z,
                cornerB.position.z
            ) - courtBoundsPadding;

        return
            worldPosition.x >= minimumX &&
            worldPosition.x <= maximumX &&
            worldPosition.z >= minimumZ &&
            worldPosition.z <= maximumZ;
    }

    #endregion

    #region Physical Bounce And Gravity

    /// <summary>
    /// Starts a real Rigidbody bounce.
    /// Horizontal direction is copied from the incoming velocity and is
    /// never replaced with a random X/Z target.
    /// </summary>
    private void StartPhysicalBounce(
        Vector3 incomingVelocity,
        float bounceHeight,
        BallState newState)
    {
        StopOpponentHitRoutine();

        guidedFlightActive = false;
        physicalFlightActive = true;
        state = newState;

        Vector3 horizontalVelocity =
            new Vector3(
                incomingVelocity.x,
                0f,
                incomingVelocity.z
            );

        Vector3 horizontalDirection =
            horizontalVelocity.normalized;

        if (horizontalDirection.sqrMagnitude < 0.0001f)
        {
            horizontalDirection =
                guidedTarget - guidedStart;

            horizontalDirection.y = 0f;
            horizontalDirection.Normalize();
        }

        if (horizontalDirection.sqrMagnitude < 0.0001f)
            horizontalDirection = transform.forward;

        currentBounceDirection =
            horizontalDirection;

        float horizontalSpeed =
            horizontalVelocity.magnitude *
            horizontalBounceRetention;

        horizontalSpeed =
            Mathf.Clamp(
                horizontalSpeed,
                minimumBounceHorizontalSpeed,
                maximumBounceHorizontalSpeed
            );

        float gravityMagnitude =
            Mathf.Max(
                0.1f,
                Mathf.Abs(Physics.gravity.y) *
                bounceGravityMultiplier
            );

        float upwardSpeed =
            Mathf.Sqrt(
                2f *
                gravityMagnitude *
                Mathf.Max(0.05f, bounceHeight)
            );

        Vector3 bounceVelocity =
            horizontalDirection *
            horizontalSpeed +
            Vector3.up *
            upwardSpeed;

        rb.isKinematic = false;
        rb.useGravity = false;

        SetRigidbodyVelocity(
            rb,
            bounceVelocity
        );

        rb.angularVelocity =
            Vector3.zero;

        previousPhysicsPosition =
            rb.position;

        currentVelocity =
            bounceVelocity;

        // Avoid immediately processing the same contact that started the bounce.
        ignoreGroundUntil =
            Time.time + 0.1f;
    }

    private void StartOpponentPhysicalBounce(
        Vector3 incomingVelocity)
    {
        StartPhysicalBounce(
            incomingVelocity,
            opponentBounceHeight,
            BallState.OpponentBounceLive
        );

        Vector3 bounceVelocity =
            ReadRigidbodyVelocity(rb);

        float gravityMagnitude =
            Mathf.Max(
                0.1f,
                Mathf.Abs(Physics.gravity.y) *
                bounceGravityMultiplier
            );

        float opponentGroundY =
            GetCourtGroundY(
                opponentCornerA,
                opponentCornerB
            ) + ballGroundOffset;

        float desiredHitY =
            opponentGroundY +
            opponentBallHitHeight;

        float verticalDifference =
            desiredHitY - rb.position.y;

        float verticalSpeed =
            bounceVelocity.y;

        float discriminant =
            verticalSpeed * verticalSpeed -
            2f *
            gravityMagnitude *
            verticalDifference;

        float hitDelay;

        if (discriminant >= 0f)
        {
            // Descending root: the AI meets the ball after the apex.
            hitDelay =
                (
                    verticalSpeed +
                    Mathf.Sqrt(discriminant)
                ) /
                gravityMagnitude;
        }
        else
        {
            // Requested hit height is above the apex.
            hitDelay =
                verticalSpeed /
                gravityMagnitude;
        }

        float totalBounceDuration =
            2f *
            verticalSpeed /
            gravityMagnitude;

        hitDelay =
            Mathf.Clamp(
                hitDelay,
                0.12f,
                Mathf.Max(
                    0.15f,
                    totalBounceDuration - 0.08f
                )
            );

        Vector3 predictedHitPosition =
            rb.position +
            bounceVelocity * hitDelay +
            0.5f *
            Vector3.down *
            gravityMagnitude *
            hitDelay *
            hitDelay;

        predictedHitPosition.y =
            desiredHitY;

        opponentHitRoutine =
            StartCoroutine(
                MoveOpponentAndHitRoutine(
                    predictedHitPosition,
                    hitDelay
                )
            );
    }

    private void ApplyCustomGravity()
    {
        if (!physicalFlightActive ||
            rb.isKinematic)
        {
            return;
        }

        Vector3 velocity =
            ReadRigidbodyVelocity(rb);

        velocity +=
            Physics.gravity *
            bounceGravityMultiplier *
            Time.fixedDeltaTime;

        SetRigidbodyVelocity(
            rb,
            velocity
        );

        currentVelocity =
            velocity;
    }

    private void CheckPhysicalGroundCrossing()
    {
        if (!physicalFlightActive)
            return;

        Vector3 currentPosition =
            rb.position;

        if (Time.time < ignoreGroundUntil)
        {
            previousPhysicsPosition =
                currentPosition;

            return;
        }

        float groundY =
            GetExpectedPhysicalGroundY();

        float contactHeight =
            groundY +
            ballGroundOffset +
            groundDetectionTolerance;

        Vector3 velocity =
            ReadRigidbodyVelocity(rb);

        bool crossedGround =
            previousPhysicsPosition.y >
                contactHeight &&
            currentPosition.y <=
                contactHeight &&
            velocity.y <= 0f;

        previousPhysicsPosition =
            currentPosition;

        if (!crossedGround)
            return;

        ResolvePhysicalGroundContact();
    }

    private float GetExpectedPhysicalGroundY()
    {
        switch (state)
        {
            case BallState.OpponentBounceLive:
            case BallState.AIMissLive:
                return GetCourtGroundY(
                    opponentCornerA,
                    opponentCornerB
                );

            case BallState.WaitingForPlayerHit:
            default:
                return GetCourtGroundY(
                    playerCornerA,
                    playerCornerB
                );
        }
    }

    private void ResolvePhysicalGroundContact()
    {
        if (!roundActive)
            return;

        switch (state)
        {
            case BallState.WaitingForPlayerHit:
                ResolvePlayerSideGroundContact();
                break;

            case BallState.AIMissLive:
                if (GroundResolutionWasRecent())
                    return;

                MarkGroundResolved();
                PlayGroundHitSound();
                AwardPoint(ScoringSide.Player);
                break;

            case BallState.OpponentBounceLive:
                if (GroundResolutionWasRecent())
                    return;

                MarkGroundResolved();
                PlayGroundHitSound();

                // AI failed to reach a valid in-bounds ball before its next bounce.
                AwardPoint(ScoringSide.Player);
                break;
        }
    }

    #endregion

    #region Player Hit

    private void PlayerHitsBall(
        Collider racketCollider)
    {
        if (!roundActive ||
            !CanPlayerHitCurrentState())
        {
            return;
        }

        if (Time.time <
            nextAllowedRacketHitTime)
        {
            return;
        }

        nextAllowedRacketHitTime =
            Time.time + racketHitCooldown;

        StopOpponentHitRoutine();

        guidedFlightActive = false;
        physicalFlightActive = false;
        playerHitDeadline = 0f;

        Vector3 racketVelocity =
            GetRacketHitVelocity(
                racketCollider
            );

        StopBallPhysics();

        if (racketSweetSpot != null)
        {
            rb.position =
                racketSweetSpot.position;

            transform.position =
                racketSweetSpot.position;
        }

        PlayPlayerHitSound();
        onPlayerHit?.Invoke();

        lastHitter =
            LastHitter.Player;

        incomingBallAlreadyBouncedOnPlayerSide = false;

        opponentWillMiss =
            Random.value <
            opponentMissChance;

        Vector3 opponentLanding =
            GetRandomOpponentGroundPosition();

        float upwardArcChange =
            Mathf.Clamp(
                racketVelocity.y *
                upwardSwingArcInfluence,
                -0.25f,
                0.8f
            );

        float shotArc =
            Mathf.Max(
                0.35f,
                playerShotArcHeight +
                upwardArcChange
            );

        Vector3 opponentMovementTarget =
            opponentWillMiss
                ? GetOpponentMissPosition(
                    opponentLanding
                )
                : GetOpponentCubePosition(
                    opponentLanding
                );

        StartGuidedProjectile(
            opponentLanding,
            shotArc,
            BallState.PlayerShotToOpponentGround,
            true,
            opponentMovementTarget
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
            trackedRacketVelocity =
                Vector3.zero;

            return;
        }

        if (!racketPositionInitialized)
        {
            previousRacketPosition =
                racketTransform.position;

            racketPositionInitialized =
                true;

            trackedRacketVelocity =
                Vector3.zero;

            return;
        }

        trackedRacketVelocity =
            (
                racketTransform.position -
                previousRacketPosition
            ) /
            Mathf.Max(
                Time.fixedDeltaTime,
                0.0001f
            );

        previousRacketPosition =
            racketTransform.position;
    }

    #endregion

    #region Opponent Hit And Return

    private IEnumerator MoveOpponentAndHitRoutine(
        Vector3 predictedHitPosition,
        float duration)
    {
        Vector3 startPosition =
            opponentCube.position;

        Vector3 targetPosition =
            new Vector3(
                predictedHitPosition.x,
                opponentRestingY,
                predictedHitPosition.z
            );

        float timer = 0f;

        while (timer < duration)
        {
            if (!roundActive ||
                state != BallState.OpponentBounceLive)
            {
                yield break;
            }

            timer += Time.deltaTime;

            float normalizedTime =
                Mathf.Clamp01(
                    timer / duration
                );

            float smoothTime =
                Mathf.SmoothStep(
                    0f,
                    1f,
                    normalizedTime
                );

            opponentCube.position =
                Vector3.Lerp(
                    startPosition,
                    targetPosition,
                    smoothTime
                );

            yield return null;
        }

        if (!roundActive ||
            state != BallState.OpponentBounceLive)
        {
            yield break;
        }

        opponentCube.position =
            targetPosition;

        // Small correction keeps the assisted hit reliable.
        if (Vector3.Distance(
                rb.position,
                predictedHitPosition) > 0.2f)
        {
            rb.position =
                predictedHitPosition;

            transform.position =
                predictedHitPosition;
        }

        OpponentHitsBall();

        opponentHitRoutine = null;
    }

    private void OpponentHitsBall()
    {
        if (!roundActive)
            return;

        physicalFlightActive = false;
        guidedFlightActive = false;

        StopBallPhysics();

        PlayOpponentHitSound();
        onOpponentHit?.Invoke();

        lastHitter =
            LastHitter.Opponent;

        Vector3 playerGroundPosition =
            GetRandomPlayerGroundPosition();

        Vector3 playerReceiveTarget =
            new Vector3(
                playerGroundPosition.x,
                GetCourtGroundY(
                    playerCornerA,
                    playerCornerB
                ) + playerReceiveHeight,
                playerGroundPosition.z
            );

        StartGuidedProjectile(
            playerReceiveTarget,
            opponentReturnArcHeight,
            BallState.OpponentReturnToPlayer
        );
    }

    private void ReleaseIncomingBallToPlayer()
    {
        state =
            BallState.WaitingForPlayerHit;

        incomingBallAlreadyBouncedOnPlayerSide = false;

        guidedFlightActive = false;
        physicalFlightActive = true;

        rb.isKinematic = false;
        rb.useGravity = false;

        Vector3 endVelocity =
            GetGuidedEndVelocity();

        SetRigidbodyVelocity(
            rb,
            endVelocity
        );

        rb.angularVelocity =
            Vector3.zero;

        previousPhysicsPosition =
            rb.position;

        currentVelocity =
            endVelocity;

        ignoreGroundUntil =
            Time.time + 0.05f;

        playerHitDeadline =
            Time.time + playerHitSafetyTimeout;
    }

    private void MoveOpponentToMissPosition(
        Vector3 ballLanding)
    {
        if (opponentCube == null)
            return;

        opponentCube.position =
            GetOpponentMissPosition(
                ballLanding
            );
    }

    private Vector3 GetOpponentMissPosition(
        Vector3 ballTarget)
    {
        Vector3 randomPosition =
            GetRandomOpponentGroundPosition();

        Vector2 ballXZ =
            new Vector2(
                ballTarget.x,
                ballTarget.z
            );

        Vector2 randomXZ =
            new Vector2(
                randomPosition.x,
                randomPosition.z
            );

        Vector2 direction =
            randomXZ - ballXZ;

        if (direction.sqrMagnitude < 0.01f)
            direction = Vector2.right;

        direction.Normalize();

        Vector2 missXZ =
            ballXZ +
            direction *
            Mathf.Max(
                opponentMissDistance,
                0.1f
            );

        return new Vector3(
            missXZ.x,
            opponentRestingY,
            missXZ.y
        );
    }

    #endregion

    #region Court Position Helpers

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

    private Vector3 GetRandomPositionBetweenCorners(
        Transform cornerA,
        Transform cornerB,
        float heightOffset)
    {
        float minimumX =
            Mathf.Min(
                cornerA.position.x,
                cornerB.position.x
            );

        float maximumX =
            Mathf.Max(
                cornerA.position.x,
                cornerB.position.x
            );

        float minimumZ =
            Mathf.Min(
                cornerA.position.z,
                cornerB.position.z
            );

        float maximumZ =
            Mathf.Max(
                cornerA.position.z,
                cornerB.position.z
            );

        return new Vector3(
            Random.Range(
                minimumX,
                maximumX
            ),
            GetCourtGroundY(
                cornerA,
                cornerB
            ) + heightOffset,
            Random.Range(
                minimumZ,
                maximumZ
            )
        );
    }

    private float GetCourtGroundY(
        Transform cornerA,
        Transform cornerB)
    {
        return
            (
                cornerA.position.y +
                cornerB.position.y
            ) * 0.5f;
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

    #endregion

    #region Racket Detection

    private bool TryDetectRacketBetween(
        Vector3 start,
        Vector3 end)
    {
        int detectedCount;

        if ((end - start).sqrMagnitude <
            0.0001f)
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

        for (int i = 0;
             i < detectedCount;
             i++)
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

            PlayerHitsBall(
                detectedCollider
            );

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

        for (int i = 0;
             i < detectedCount;
             i++)
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

            PlayerHitsBall(
                detectedCollider
            );

            return true;
        }

        return false;
    }

    private bool CanPlayerHitCurrentState()
    {
        return
            state ==
                BallState.WaitingForPlayerHit ||
            state ==
                BallState.OpponentReturnToPlayer;
    }

    #endregion

    #region Collision Handling

    private void OnCollisionEnter(
        Collision collision)
    {
        HandleCollider(
            collision.collider
        );
    }

    private void OnTriggerEnter(
        Collider other)
    {
        HandleCollider(other);
    }

    private void HandleCollider(
        Collider other)
    {
        if (!roundActive ||
            other == null)
        {
            return;
        }

        if (ColliderHasTag(
                other,
                racketTag))
        {
            PlayerHitsBall(other);
            return;
        }

        if (ColliderHasTag(
                other,
                netTag))
        {
            HandleNetFault();
            return;
        }

        if (Time.time <
            ignoreGroundUntil)
        {
            return;
        }

        if (ColliderHasTag(
                other,
                playerGroundTag))
        {
            if (state ==
                BallState.OpponentServeToPlayerGround)
            {
                rb.position =
                    guidedTarget;

                transform.position =
                    guidedTarget;

                ResolveServeLanding();
            }
            else if (state ==
                     BallState.WaitingForPlayerHit)
            {
                ResolvePlayerSideGroundContact();
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
                rb.position =
                    guidedTarget;

                transform.position =
                    guidedTarget;

                ResolvePlayerShotLanding();
            }
            else if (state ==
                     BallState.AIMissLive)
            {
                if (!GroundResolutionWasRecent())
                {
                    MarkGroundResolved();
                    PlayGroundHitSound();
                    AwardPoint(ScoringSide.Player);
                }
            }
            else if (state ==
                     BallState.OpponentBounceLive)
            {
                if (!GroundResolutionWasRecent())
                {
                    MarkGroundResolved();
                    PlayGroundHitSound();
                    AwardPoint(ScoringSide.Player);
                }
            }
        }
    }

    private void HandleNetFault()
    {
        if (!roundActive)
            return;

        if (lastHitter ==
            LastHitter.Player)
        {
            AwardPoint(
                ScoringSide.Opponent
            );
        }
        else
        {
            AwardPoint(
                ScoringSide.Player
            );
        }
    }

    private bool ColliderHasTag(
        Collider targetCollider,
        string requiredTag)
    {
        if (targetCollider.CompareTag(
                requiredTag))
        {
            return true;
        }

        if (targetCollider.attachedRigidbody != null &&
            targetCollider.attachedRigidbody.CompareTag(
                requiredTag))
        {
            return true;
        }

        return
            targetCollider
                .transform
                .root
                .CompareTag(requiredTag);
    }

    #endregion

    #region Scoring

    private void PlayerMissedValidBall()
    {
        if (!roundActive)
            return;

        PlayGroundHitSound();
        AwardPoint(ScoringSide.Opponent);
    }

    private void AwardPoint(
        ScoringSide scoringSide)
    {
        if (!roundActive)
            return;

        roundActive = false;
        guidedFlightActive = false;
        physicalFlightActive = false;
        playerHitDeadline = 0f;

        state =
            BallState.PointOver;

        StopGameplayCoroutines();
        StopBallPhysics();

        if (scoringSide ==
            ScoringSide.Player)
        {
            playerScore++;

            UpdateHighScore();

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

        pointOverRoutine =
            StartCoroutine(
                PointOverRoutine()
            );
    }

    private void AwardPointBasedOnLastHitter()
    {
        if (lastHitter ==
            LastHitter.Player)
        {
            AwardPoint(
                ScoringSide.Opponent
            );
        }
        else
        {
            AwardPoint(
                ScoringSide.Player
            );
        }
    }

    private IEnumerator PointOverRoutine()
    {
        if (pointEndDelay > 0f)
        {
            yield return new WaitForSeconds(
                pointEndDelay
            );
        }

        SetBallAtServePoint();
        ResetOpponentPosition();

        state =
            BallState.Idle;

        ShowServeButton(true);

        pointOverRoutine = null;
    }

    private void LoadHighScore()
    {
        highScore =
            PlayerPrefs.GetInt(
                highScorePlayerPrefsKey,
                0
            );
    }

    private void UpdateHighScore()
    {
        if (playerScore <= highScore)
            return;

        highScore =
            playerScore;

        PlayerPrefs.SetInt(
            highScorePlayerPrefsKey,
            highScore
        );

        PlayerPrefs.Save();
    }

    private void UpdateScoreUI()
    {
        if (playerScoreText != null)
        {
            playerScoreText.text =
                playerScorePrefix +
                playerScore;
        }

        if (opponentScoreText != null)
        {
            opponentScoreText.text =
                opponentScorePrefix +
                opponentScore;
        }

        if (highScoreText != null)
        {
            highScoreText.text =
                highScorePrefix +
                highScore;
        }
    }

    private void ShowServeButton(
        bool show)
    {
        if (serveButton != null)
        {
            serveButton.gameObject.SetActive(
                show
            );
        }
    }

    #endregion

    #region Audio

    private void PlayPlayerHitSound()
    {
        PlayClip(playerHitClip);
    }

    private void PlayOpponentHitSound()
    {
        PlayClip(
            opponentHitClip != null
                ? opponentHitClip
                : playerHitClip
        );
    }

    private void PlayGroundHitSound()
    {
        if (Time.time <
            nextAllowedGroundSoundTime)
        {
            return;
        }

        nextAllowedGroundSoundTime =
            Time.time + 0.08f;

        PlayClip(groundHitClip);
        onGroundHit?.Invoke();
    }

    private void PlayClip(
        AudioClip clip)
    {
        if (audioSource == null ||
            clip == null)
        {
            return;
        }

        audioSource.PlayOneShot(clip);
    }

    #endregion

    #region Opponent Movement

    private void MoveOpponentInstantlyToXZ(
        Vector3 position)
    {
        if (opponentCube == null)
            return;

        opponentCube.position =
            new Vector3(
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

    #region Ground Resolution Helpers

    private bool GroundResolutionWasRecent()
    {
        return
            Time.time -
            lastGroundResolutionTime <
            0.08f;
    }

    private void MarkGroundResolved()
    {
        lastGroundResolutionTime =
            Time.time;
    }

    #endregion

    #region Rigidbody And Coroutine Helpers

    private void SetBallAtServePoint()
    {
        StopBallPhysics();

        rb.position =
            servePoint.position;

        transform.position =
            servePoint.position;

        transform.rotation =
            servePoint.rotation;
    }

    private void StopBallPhysics()
    {
        guidedFlightActive = false;
        physicalFlightActive = false;

        if (!rb.isKinematic)
        {
            SetRigidbodyVelocity(
                rb,
                Vector3.zero
            );

            rb.angularVelocity =
                Vector3.zero;
        }

        rb.useGravity = false;
        rb.isKinematic = true;
    }

    private void StopGameplayCoroutines()
    {
        if (serveRoutine != null)
        {
            StopCoroutine(serveRoutine);
            serveRoutine = null;
        }

        if (pointOverRoutine != null)
        {
            StopCoroutine(pointOverRoutine);
            pointOverRoutine = null;
        }

        StopOpponentHitRoutine();
    }

    private void StopOpponentHitRoutine()
    {
        if (opponentHitRoutine == null)
            return;

        StopCoroutine(
            opponentHitRoutine
        );

        opponentHitRoutine = null;
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

    #endregion

    #region Gizmos

    private void OnDrawGizmosSelected()
    {
        DrawCourtArea(
            playerCornerA,
            playerCornerB,
            ballGroundOffset
        );

        DrawCourtArea(
            opponentCornerA,
            opponentCornerB,
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
                currentTarget,
                0.1f
            );

            Gizmos.DrawLine(
                rb.position,
                rb.position +
                currentBounceDirection
            );
        }
    }

    private void DrawCourtArea(
        Transform cornerA,
        Transform cornerB,
        float heightOffset)
    {
        if (cornerA == null ||
            cornerB == null)
        {
            return;
        }

        Vector3 positionA =
            cornerA.position;

        Vector3 positionB =
            cornerB.position;

        Vector3 centre =
            new Vector3(
                (
                    positionA.x +
                    positionB.x
                ) * 0.5f,
                GetCourtGroundY(
                    cornerA,
                    cornerB
                ) + heightOffset,
                (
                    positionA.z +
                    positionB.z
                ) * 0.5f
            );

        Vector3 size =
            new Vector3(
                Mathf.Abs(
                    positionB.x -
                    positionA.x
                ),
                0.02f,
                Mathf.Abs(
                    positionB.z -
                    positionA.z
                )
            );

        Gizmos.DrawWireCube(
            centre,
            size
        );
    }

    #endregion
}
