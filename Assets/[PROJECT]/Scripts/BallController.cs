using UnityEngine;
using DG.Tweening; 

public class BallController : MonoBehaviour {
    // Paramètres de tir
    public float maxForce = 20f; 
    
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
    public int playerIndex = 0; 

    // Variables internes
    private Vector3 lastValidPosition;
    private bool wasMoving = false;
    private bool hasStartedMoving = false; 
    private bool hasShotThisTurn = false; 
    public float lastRespawnTime = 0f; 
    private float lastShootTime = 0f; // SÉCURITÉ ANTI-BLOCAGE
    
    // Tir
    private float currentPower = 0f; 
    private float chargeStartTime = 0f; 

    // Réseau
    private float lastSendTime = 0f;
    private Vector3 targetPosition; 

    // Light
    private Light turnLight;
    private bool isLightOn = false; 
    [SerializeField] private float maxLightIntensity = 20f; 

    // Prévisualisation
    private LineRenderer lineRenderer;

    void Start() {
        if (rb == null) rb = GetComponent<Rigidbody>();
        maxForce = 20f; 
        
        if (isLocalPlayer && localPlayerMaterial != null) {
            Renderer r = GetComponent<Renderer>();
            if (r != null) r.material = localPlayerMaterial;
        }

        // LUMIÈRE
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

        // TRAIL
        TrailRenderer trail = gameObject.AddComponent<TrailRenderer>();
        trail.startWidth = 0.3f; 
        trail.endWidth = 0.0f;   
        trail.time = 0.3f;       
        trail.material = new Material(Shader.Find("Sprites/Default"));
        trail.startColor = new Color(1f, 1f, 1f, 0.4f); 
        trail.endColor = new Color(1f, 1f, 1f, 0f);     
        trail.minVertexDistance = 0.1f; 

        // CONFIGURATION LINE RENDERER (PREVISUALISATION)
        if (lineRenderer == null) lineRenderer = gameObject.AddComponent<LineRenderer>();
        lineRenderer.startWidth = 0.2f; // Plus large
        lineRenderer.endWidth = 0.05f; 
        
        // Shader de secours
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply");
        
        lineRenderer.material = new Material(shader);
        lineRenderer.startColor = new Color(1f, 1f, 0f, 0.8f); // Jaune
        lineRenderer.endColor = new Color(1f, 0f, 0f, 0f);     // Rouge
        lineRenderer.positionCount = 0;
        lineRenderer.enabled = false;

        lastValidPosition = transform.position;
        targetPosition = transform.position;
    }

    void Update() {
        // LUMIÈRE
        if (turnLight != null) {
            bool shouldBeOn = isCurrentTurn && !isLocalPlayer;
            if (shouldBeOn != isLightOn) {
                isLightOn = shouldBeOn;
                turnLight.DOKill(); 
                if (isLightOn) {
                    turnLight.DOIntensity(maxLightIntensity, 1f).SetEase(Ease.OutQuad);
                } else {
                    turnLight.DOIntensity(0f, 0.5f).SetEase(Ease.InQuad);
                }
            }
        }

        NetworkManager nm = FindObjectOfType<NetworkManager>();
        if (nm == null) return;

        // LOGIQUE MAÎTRE DU JEU
        bool amIMaster = (nm.myUserId == nm.currentTurnId);

        if (amIMaster) {
            if (hasFinished) {
                rb.isKinematic = true;
                rb.linearVelocity = Vector3.zero;
            } else {
                rb.isKinematic = false;
            }

            // Streaming
            if (IsMoving()) {
                if (Time.time - lastSendTime > 0.05f) { 
                    nm.SendUpdateBall(ownerId, transform.position);
                    lastSendTime = Time.time;
                }
                targetPosition = transform.position; 
                wasMoving = true;
            } else if (wasMoving) {
                nm.SendUpdateBall(ownerId, transform.position);
                targetPosition = transform.position;
                wasMoving = false;
            }

            // Respawn
            if (!hasFinished && transform.position.y < -5f) {
                ResetToLastPosition();
            }

            // Logique de jeu locale
            if (isLocalPlayer && isMyTurn && !hasFinished) {
                if (hasShotThisTurn) {
                    if (!hasStartedMoving && IsMoving()) hasStartedMoving = true;

                    // CONDITIONS DE FIN DE TOUR :
                    // 1. On a bougé ET on s'est arrêté
                    // 2. OU BIEN : On a tiré il y a plus de 1s et on ne bouge toujours pas (Bug physique ou mur)
                    bool timeOutReached = (Time.time - lastShootTime > 1.0f) && !IsMoving();

                    if ((hasStartedMoving && AreAllBallsStopped()) || timeOutReached) {
                        Debug.Log("Fin du tour (Normal ou Timeout).");
                        nm.SendTurnEnded();
                        isMyTurn = false;
                        hasShotThisTurn = false;
                        hasStartedMoving = false;
                        currentPower = 0f; 
                    }
                }
                
                if (!hasShotThisTurn && AreAllBallsStopped()) {
                     HandleShootingInput();
                }
            }

        } else {
            // Esclave
            rb.isKinematic = true;
            if (!hasFinished) {
                transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * 10f);
            }
        }
    }

    void HandleShootingInput() {
        Vector3 camForward = Camera.main.transform.forward;
        camForward.y = 0; 
        if (camForward.sqrMagnitude > 0.001f) {
            camForward.Normalize();
        } else {
            camForward = Vector3.forward; 
        }

        if (Input.GetKeyDown(KeyCode.Space)) {
            chargeStartTime = Time.time;
            currentPower = 0f;
            lineRenderer.enabled = true;
        }

        if (Input.GetKey(KeyCode.Space)) {
            float timeCharged = Time.time - chargeStartTime;
            currentPower = Mathf.PingPong(timeCharged * 25f, maxForce);
            
            // DESSIN DE LA TRAJECTOIRE
            Vector3 forceVector = camForward * currentPower;
            DrawTrajectory(forceVector);
        } 

        if (Input.GetKeyUp(KeyCode.Space)) {
            float timeCharged = Time.time - chargeStartTime;
            float finalPower = Mathf.PingPong(timeCharged * 25f, maxForce);
            
            if (finalPower < 2f) finalPower = 2f;

            Debug.Log($"TIR ! Puissance: {finalPower} (Max: {maxForce}) Direction: {camForward}");

            Vector3 shootForce = camForward * finalPower;
            Shoot(shootForce);
            
            currentPower = 0f;
            lineRenderer.enabled = false;

            NetworkManager nm = FindObjectOfType<NetworkManager>();
            if (nm != null) {
                nm.SendShoot(shootForce);
                // On NE PASSE PAS le tour ici !
            }
        }
    }

    void DrawTrajectory(Vector3 force) {
        lineRenderer.enabled = true;
        lineRenderer.positionCount = 50; 
        Vector3 origin = transform.position;
        Vector3 velocity = force / rb.mass; 

        for (int i = 0; i < 50; i++) {
            float time = i * 0.05f; 
            Vector3 point = origin + velocity * time + 0.5f * Physics.gravity * time * time;
            
            if (point.y < -2f) { // On coupe si ça descend trop bas
                lineRenderer.positionCount = i + 1;
                lineRenderer.SetPosition(i, point);
                break;
            }
            lineRenderer.SetPosition(i, point);
        }
    }
    
    bool AreAllBallsStopped() {
        BallController[] allBalls = FindObjectsOfType<BallController>();
        foreach(var ball in allBalls) {
            if (ball.rb.linearVelocity.magnitude > 0.2f) return false;
            if (Time.time - ball.lastRespawnTime < 1.0f) return false;
        }
        return true;
    }

    public void Shoot(Vector3 force) {
        rb.isKinematic = false;
        rb.AddForce(force, ForceMode.Impulse);
        
        shotCount++;
        hasShotThisTurn = true;
        hasStartedMoving = false; 
        lastShootTime = Time.time; 
    }

    public bool IsMoving() {
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
        if (lastValidPosition.y < 0f) {
            Debug.LogWarning($"Position de respawn corrompue ({lastValidPosition}). Reset au centre.");
            lastValidPosition = new Vector3(0, 0.5f, 0);
        }

        Debug.Log("Respawn de " + ownerId + " à " + lastValidPosition);
        
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.Sleep(); 
        
        transform.position = lastValidPosition + Vector3.up * 0.2f; 
        targetPosition = transform.position;
        lastRespawnTime = Time.time; 
        
        NetworkManager nm = FindObjectOfType<NetworkManager>();
        if(nm != null) nm.SendUpdateBall(ownerId, transform.position);
    }

    void OnGUI() {
        NetworkManager nm = FindObjectOfType<NetworkManager>();
        if (nm == null || !nm.gameStarted) return; 

        if (isLocalPlayer) {
            GUIStyle style = new GUIStyle();
            style.fontSize = 20;
            style.normal.textColor = Color.white;
            
            GUI.Label(new Rect(20, 20, 200, 30), "Coups : " + shotCount, style);

            if (isMyTurn) {
                style.normal.textColor = Color.green;
                GUI.Label(new Rect(20, 50, 300, 30), "--> C'EST À TOI DE JOUER !", style);

                if (AreAllBallsStopped()) {
                    GUI.Label(new Rect(20, 80, 400, 30), "Maintenez ESPACE pour charger.", style);
                    
                    if (Input.GetKey(KeyCode.Space)) {
                        float barHeight = 300f;
                        float barWidth = 40f;
                        float xPos = Screen.width - 80f;
                        float yPos = Screen.height / 2f - barHeight / 2f;

                        GUI.Box(new Rect(xPos, yPos, barWidth, barHeight), "");
                        float fillHeight = (currentPower / maxForce) * barHeight;
                        Texture2D texture = new Texture2D(1, 1);
                        texture.SetPixel(0, 0, Color.Lerp(Color.yellow, Color.red, currentPower / maxForce));
                        texture.Apply();
                        GUI.DrawTexture(new Rect(xPos, yPos + (barHeight - fillHeight), barWidth, fillHeight), texture);
                    }
                } else {
                    style.normal.textColor = Color.red;
                    GUI.Label(new Rect(20, 80, 400, 30), "Attente de l'arrêt complet des balles...", style);
                }
            } else {
                style.normal.textColor = Color.yellow;
                GUI.Label(new Rect(20, 50, 300, 30), "En attente des autres...", style);
            }
        }
    }
}
