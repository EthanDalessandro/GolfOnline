using UnityEngine;

public class BallController : MonoBehaviour {
    public float maxForce = 10f;
    public float forceMultiplier = 2f;
    public Rigidbody rb;
    public bool isMyTurn = false; 
    public bool isLocalPlayer = false;
    public bool hasFinished = false;
    public string ownerId; // ID du propriétaire de cette balle

    private Vector3 startPos;
    private Vector3 endPos;
    private Vector3 lastValidPosition;
    private bool isDragging = false;
    private bool wasMoving = false;

    // Variables pour la synchro réseau
    private float lastSendTime = 0f;
    private Vector3 targetPosition; 
    
    // Variable pour savoir si le mouvement a commencé après le tir
    private bool hasStartedMoving = false;
    
    // Variable pour savoir si on a déjà tiré ce tour-ci
    private bool hasShotThisTurn = false;
    
    public float lastRespawnTime = 0f; // Public pour être lu par les autres

    void Start() {
        if (rb == null) rb = GetComponent<Rigidbody>();
        lastValidPosition = transform.position;
        targetPosition = transform.position;
    }

    void Update() {
        NetworkManager nm = FindObjectOfType<NetworkManager>();
        if (nm == null) return;

        // LOGIQUE MAÎTRE DU JEU
        bool amIMaster = (nm.myUserId == nm.currentTurnId);

        if (amIMaster) {
            // Je suis le Maître : La physique est active pour tout le monde sur mon écran
            rb.isKinematic = false;

            // --- 1. Streaming de position (pour TOUTES les balles) ---
            if (IsMoving()) {
                if (Time.time - lastSendTime > 0.05f) { // 20 fois par seconde
                    nm.SendUpdateBall(ownerId, transform.position);
                    lastSendTime = Time.time;
                }
                wasMoving = true;
            } else if (wasMoving) {
                // Elle vient de s'arrêter, on envoie la position finale précise
                nm.SendUpdateBall(ownerId, transform.position);
                wasMoving = false;
            }

            // Vérification Hors-Limites (POUR TOUTES LES BALLES)
            // Si je pousse quelqu'un dehors, il doit respawn
            if (transform.position.y < -5f) {
                ResetToLastPosition();
            }

            // --- 2. Logique de Jeu (UNIQUEMENT sur mon instance locale) ---
            if (isLocalPlayer && isMyTurn && !hasFinished) {
                // Gestion de la Fin du Tour
                if (hasShotThisTurn) {
                    // On détecte le début du mouvement (pour éviter de finir le tour avant même qu'il commence)
                    if (!hasStartedMoving && IsMoving()) {
                        hasStartedMoving = true;
                    }

                    // Si le mouvement a commencé, on attend que TOUT LE MONDE soit arrêté
                    if (hasStartedMoving && AreAllBallsStopped()) {
                        Debug.Log("Tout le monde est arrêté. Fin du tour.");
                        
                        // Envoi fin de tour
                        nm.SendTurnEnded();
                        
                        // Reset des états
                        isMyTurn = false;
                        hasShotThisTurn = false;
                        hasStartedMoving = false;
                    }
                }
                
                // Input (seulement si on n'a pas encore tiré et que tout est calme)
                if (!hasShotThisTurn && AreAllBallsStopped()) {
                     HandleInput();
                }
            }

        } else {
            // Je ne suis pas le Maître : Je suis esclave de la physique du Maître
            rb.isKinematic = true;
            
            // Interpolation vers la position reçue du réseau
            transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * 10f);
        }
    }
    
    // Vérifie si TOUTES les balles en jeu sont arrêtées
    bool AreAllBallsStopped() {
        BallController[] allBalls = FindObjectsOfType<BallController>();
        foreach(var ball in allBalls) {
            if (ball.IsMoving()) return false;
            // Si une balle vient de respawn (il y a moins d'1 seconde), on attend qu'elle se stabilise
            if (Time.time - ball.lastRespawnTime < 1.0f) return false;
        }
        return true;
    }
    
    void HandleInput() {
        if (Input.GetMouseButtonDown(0)) {
            startPos = GetMouseWorldPos();
            isDragging = true;
        }

        if (Input.GetMouseButton(0) && isDragging) {
            // Dessin ligne de visée possible ici
        }

        if (Input.GetMouseButtonUp(0) && isDragging) {
            endPos = GetMouseWorldPos();
            isDragging = false;
            Shoot();
        }
    }

    void Shoot() {
        Vector3 forceVector = startPos - endPos;
        Vector3 clampedForce = Vector3.ClampMagnitude(forceVector * forceMultiplier, maxForce);
        clampedForce.y = 0;

        // En tant que Maître, j'applique la force directement !
        ApplyForce(clampedForce);
        
        // Et j'informe les autres pour qu'ils jouent un son/effet
        NetworkManager nm = FindObjectOfType<NetworkManager>();
        if(nm != null) {
            nm.SendShoot(clampedForce);
        }
        
        hasShotThisTurn = true;
        hasStartedMoving = false; // On reset pour attendre le début du mouvement
    }

    // Cette méthode est appelée par le réseau (pour les effets) ou localement
    public void ApplyForce(Vector3 force) {
        rb.AddForce(force, ForceMode.Impulse);
    }

    // Utilitaire pour obtenir la position de la souris dans le monde 3D
    Vector3 GetMouseWorldPos() {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit)) {
            return hit.point;
        }
        return Vector3.zero;
    }

    public bool IsMoving() {
        // Unity 6 utilise linearVelocity
        return rb.linearVelocity.magnitude > 0.1f;
    }
    
    public void UpdateTargetPosition(Vector3 pos) {
        targetPosition = pos;
    }

    // Appelé au début de CHAQUE tour pour sauvegarder la position "sûre" actuelle
    public void OnTurnStarted() {
        // SÉCURITÉ : On ne sauvegarde pas une position si on est en train de tomber ou trop bas
        if (transform.position.y < -2f) {
            Debug.LogWarning("Position instable détectée pour " + ownerId + " (" + transform.position + "). On garde l'ancienne.");
            return;
        }

        lastValidPosition = transform.position;
        targetPosition = transform.position;
        rb.linearVelocity = Vector3.zero; 
        rb.angularVelocity = Vector3.zero;
    }

    void ResetToLastPosition() {
        Debug.Log("Respawn de " + ownerId + " à " + lastValidPosition);
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.Sleep(); 
        transform.position = lastValidPosition + Vector3.up * 0.1f; 
        targetPosition = transform.position;
        
        lastRespawnTime = Time.time; // On marque le coup pour empêcher la fin du tour immédiate
        
        // En tant que Maître, j'envoie la correction
        NetworkManager nm = FindObjectOfType<NetworkManager>();
        if(nm != null) nm.SendUpdateBall(ownerId, transform.position);
    }
}
