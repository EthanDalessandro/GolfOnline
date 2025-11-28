using UnityEngine;
using DG.Tweening; // Nécessaire pour les animations

public class BallController : MonoBehaviour {
    // Paramètres de tir
    public float maxForce = 10f;
    public float forceMultiplier = 2f;

    // FEEDBACK VISUEL
    public Material localPlayerMaterial; 
    public int shotCount = 0; 

    public Rigidbody rb;
    
    // États du jeu
    public bool isMyTurn = false; 
    public bool isCurrentTurn = false; 
    public bool isLocalPlayer = false;
    public bool hasFinished = false;
    public string ownerId; 
    public int playerIndex = 0; // Pour se souvenir du point de spawn 

    // Variables internes
    private Vector3 startPos;
    private Vector3 endPos;
    private Vector3 lastValidPosition;
    private bool isDragging = false;
    private bool wasMoving = false;
    private bool hasStartedMoving = false; 
    private bool hasShotThisTurn = false; 
    public float lastRespawnTime = 0f; 

    // Réseau
    private float lastSendTime = 0f;
    private Vector3 targetPosition; 

    // LineRenderer & Light
    public LineRenderer lineRenderer;
    private Light turnLight;
    private bool isLightOn = false; // Pour suivre l'état de l'animation
    [SerializeField] private float maxLightIntensity = 20f; // Votre réglage d'intensité

    void Start() {
        if (rb == null) rb = GetComponent<Rigidbody>();
        
        // --- FEEDBACK : COULEUR DU JOUEUR LOCAL ---
        if (isLocalPlayer && localPlayerMaterial != null) {
            Renderer r = GetComponent<Renderer>();
            if (r != null) {
                r.material = localPlayerMaterial;
            }
        }

        // --- FEEDBACK : LUMIÈRE DE TOUR (SPOTLIGHT) ---
        GameObject lightObj = new GameObject("TurnIndicatorLight");
        lightObj.transform.parent = transform;
        lightObj.transform.localPosition = new Vector3(0, 5.5f, 0); 
        lightObj.transform.localRotation = Quaternion.Euler(90, 0, 0); 
        
        turnLight = lightObj.AddComponent<Light>();
        turnLight.type = LightType.Spot;
        turnLight.color = Color.blue;
        turnLight.intensity = 0f; 
        turnLight.range = 10f;
        turnLight.spotAngle = 30f;
        turnLight.enabled = true; 

        // Configuration du LineRenderer
        if (lineRenderer == null) {
            lineRenderer = gameObject.AddComponent<LineRenderer>();
            lineRenderer.startWidth = 0.1f;
            lineRenderer.endWidth = 0.05f;
            lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
            lineRenderer.startColor = Color.yellow;
            lineRenderer.endColor = Color.red;
            lineRenderer.enabled = false;
        }

        lastValidPosition = transform.position;
        targetPosition = transform.position;
    }

    void Update() {
        // GESTION DE LA LUMIÈRE AVEC DOTWEEN
        if (turnLight != null) {
            bool shouldBeOn = isCurrentTurn && !isLocalPlayer;
            
            // Si l'état désiré change, on lance l'animation
            if (shouldBeOn != isLightOn) {
                isLightOn = shouldBeOn;
                turnLight.DOKill(); // On arrête les animations en cours pour éviter les conflits
                
                if (isLightOn) {
                    // Allumage progressif (1 seconde)
                    turnLight.DOIntensity(maxLightIntensity, 1f).SetEase(Ease.OutQuad);
                } else {
                    // Extinction progressive (0.5 seconde)
                    turnLight.DOIntensity(0f, 0.5f).SetEase(Ease.InQuad);
                }
            }
        }

        NetworkManager nm = FindObjectOfType<NetworkManager>();
        if (nm == null) return;

        // LOGIQUE MAÎTRE DU JEU
        bool amIMaster = (nm.myUserId == nm.currentTurnId);

        if (amIMaster) {
            // Si j'ai fini, je ne suis plus soumis à la physique (je reste au fond du trou)
            if (hasFinished) {
                rb.isKinematic = true;
                rb.linearVelocity = Vector3.zero;
            } else {
                rb.isKinematic = false;
            }

            // Streaming de position
            if (IsMoving()) {
                if (Time.time - lastSendTime > 0.05f) { 
                    nm.SendUpdateBall(ownerId, transform.position);
                    lastSendTime = Time.time;
                }
                targetPosition = transform.position; // On garde la cible à jour localement aussi !
                wasMoving = true;
            } else if (wasMoving) {
                nm.SendUpdateBall(ownerId, transform.position);
                targetPosition = transform.position;
                wasMoving = false;
            }

            // Respawn si tombé (seulement si on n'a pas fini !)
            if (!hasFinished && transform.position.y < -5f) {
                ResetToLastPosition();
            }

            // Logique de jeu locale
            if (isLocalPlayer && isMyTurn && !hasFinished) {
                if (hasShotThisTurn) {
                    if (!hasStartedMoving && IsMoving()) {
                        hasStartedMoving = true;
                    }

                    if (hasStartedMoving && AreAllBallsStopped()) {
                        Debug.Log("Fin du tour.");
                        nm.SendTurnEnded();
                        isMyTurn = false;
                        hasShotThisTurn = false;
                        hasStartedMoving = false;
                    }
                }
                
                if (!hasShotThisTurn && AreAllBallsStopped()) {
                     HandleInput();
                }
            }

        } else {
            // Esclave
            rb.isKinematic = true;
            
            // Si j'ai fini, je ne bouge plus ! (J'ignore les vieux messages réseau qui pourraient me sortir du trou)
            if (!hasFinished) {
                transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * 10f);
            }
        }
    }
    
    bool AreAllBallsStopped() {
        BallController[] allBalls = FindObjectsOfType<BallController>();
        foreach(var ball in allBalls) {
            if (ball.IsMoving()) return false;
            if (Time.time - ball.lastRespawnTime < 1.0f) return false;
        }
        return true;
    }
    
    void HandleInput() {
        if (Input.GetMouseButtonDown(0)) {
            startPos = GetMouseWorldPos();
            isDragging = true;
            lineRenderer.enabled = true;
        }

        if (Input.GetMouseButton(0) && isDragging) {
            Vector3 currentMousePos = GetMouseWorldPos();
            Vector3 forceVector = startPos - currentMousePos;
            Vector3 clampedForce = Vector3.ClampMagnitude(forceVector * forceMultiplier, maxForce);
            
            lineRenderer.SetPosition(0, transform.position);
            lineRenderer.SetPosition(1, transform.position + new Vector3(clampedForce.x, 0, clampedForce.z));
        }

        if (Input.GetMouseButtonUp(0) && isDragging) {
            endPos = GetMouseWorldPos();
            isDragging = false;
            lineRenderer.enabled = false;
            Shoot();
        }
    }

    void Shoot() {
        Vector3 forceVector = startPos - endPos;
        Vector3 clampedForce = Vector3.ClampMagnitude(forceVector * forceMultiplier, maxForce);
        clampedForce.y = 0;

        ApplyForce(clampedForce);
        
        // --- FEEDBACK : COMPTEUR DE TIRS ---
        shotCount++;

        NetworkManager nm = FindObjectOfType<NetworkManager>();
        if(nm != null) {
            nm.SendShoot(clampedForce);
        }
        
        hasShotThisTurn = true;
        hasStartedMoving = false; 
    }

    public void ApplyForce(Vector3 force) {
        rb.AddForce(force, ForceMode.Impulse);
    }

    Vector3 GetMouseWorldPos() {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit)) {
            return hit.point;
        }
        return Vector3.zero;
    }

    public bool IsMoving() {
        // Unity 6
        return rb.linearVelocity.magnitude > 0.1f;
    }
    
    public void UpdateTargetPosition(Vector3 pos) {
        targetPosition = pos;
    }

    public void OnTurnStarted() {
        if (transform.position.y < -2f) return;

        lastValidPosition = transform.position;
        targetPosition = transform.position;
        rb.linearVelocity = Vector3.zero; 
        rb.angularVelocity = Vector3.zero;
    }

    void ResetToLastPosition() {
        Debug.Log("Respawn de " + ownerId);
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.Sleep(); 
        transform.position = lastValidPosition + Vector3.up * 0.1f; 
        targetPosition = transform.position;
        lastRespawnTime = Time.time; 
        
        NetworkManager nm = FindObjectOfType<NetworkManager>();
        if(nm != null) nm.SendUpdateBall(ownerId, transform.position);
    }

    // --- FEEDBACK : INTERFACE UTILISATEUR (HUD) ---
    void OnGUI() {
        NetworkManager nm = FindObjectOfType<NetworkManager>();
        if (nm == null || !nm.gameStarted) return; // On n'affiche rien tant que le lobby est là

        if (isLocalPlayer) {
            // Style simple pour le TP
            GUIStyle style = new GUIStyle();
            style.fontSize = 20;
            style.normal.textColor = Color.white;
            
            // Affichage du compteur
            GUI.Label(new Rect(20, 20, 200, 30), "Coups : " + shotCount, style);

            // Affichage de l'état du tour
            if (isMyTurn) {
                style.normal.textColor = Color.green;
                GUI.Label(new Rect(20, 50, 300, 30), "--> C'EST À TOI DE JOUER !", style);
            } else {
                style.normal.textColor = Color.yellow;
                GUI.Label(new Rect(20, 50, 300, 30), "En attente des autres...", style);
            }
        }
    }
}
