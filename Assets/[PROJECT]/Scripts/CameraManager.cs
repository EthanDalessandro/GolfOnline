using UnityEngine;

public class CameraManager : MonoBehaviour {
    [Header("Paramètres de Suivi")]
    public float distance = 8.0f;       // Distance derrière la balle
    public float height = 4.0f;         // Hauteur de la caméra
    
    [Header("Fluidité Indépendante")]
    [Tooltip("Temps de transition entre deux joueurs (Position). Plus grand = plus lent.")]
    public float transitionSmoothTime = 1.5f;

    [Tooltip("Temps de lissage de la souris (Rotation). Plus petit = plus réactif.")]
    public float rotationSmoothTime = 0.1f;

    [Header("Paramètres de Rotation")]
    public float rotationSpeed = 5.0f;  // Sensibilité de la souris

    // Cibles
    private Transform target;           
    
    // Variables de Position (Focus Point)
    private Vector3 currentFocusPosition; // Le point virtuel qu'on regarde/suit
    private Vector3 focusVelocity;        // Pour SmoothDamp

    // Variables de Rotation
    private float targetYaw = 0f;      // Angle voulu (Input)
    private float currentYaw = 0f;     // Angle affiché (Lissé)
    private float yawVelocity;         // Pour SmoothDampAngle
    
    private float currentPitch = 20f;  // Pitch fixe pour l'instant

    void Start() {
        // Initialisation au centre
        currentFocusPosition = Vector3.zero;
    }

    void Update() {
        NetworkManager nm = FindObjectOfType<NetworkManager>();
        if (nm == null) return;

        // 1. Trouver la cible (Joueur actif)
        string currentTurnId = nm.currentTurnId;
        GameObject activePlayerObj = GameObject.Find("Player_" + currentTurnId);
        
        if (activePlayerObj != null) {
            target = activePlayerObj.transform;
        } else {
            target = null;
        }

        // 2. Gestion de la Souris (Rotation)
        if (nm.gameStarted) {
            if (Input.GetMouseButtonDown(0)) Cursor.lockState = CursorLockMode.Locked;
        }
        if (Input.GetKeyDown(KeyCode.Escape)) Cursor.lockState = CursorLockMode.None;

        if (Cursor.lockState == CursorLockMode.Locked) {
            // On modifie l'angle CIBLE
            targetYaw += Input.GetAxis("Mouse X") * rotationSpeed;
        }
    }

    void LateUpdate() {
        if (Camera.main == null) return;

        // 3. Calcul du Point de Focus (Lissé)
        Vector3 targetPos = (target != null) ? target.position : Vector3.zero;
        
        // On déplace le "point de focus" doucement vers la cible réelle
        // C'est ça qui gère la vitesse de "vol" entre les joueurs
        currentFocusPosition = Vector3.SmoothDamp(currentFocusPosition, targetPos, ref focusVelocity, transitionSmoothTime);

        // 4. Calcul de la Rotation (Lissée indépendamment)
        // On lisse l'angle actuel vers l'angle cible
        currentYaw = Mathf.SmoothDampAngle(currentYaw, targetYaw, ref yawVelocity, rotationSmoothTime);

        Quaternion rotation = Quaternion.Euler(currentPitch, currentYaw, 0);

        // 5. Calcul Final de la Position Caméra
        // La caméra se place par rapport au Focus Point LISSÉ, avec la Rotation LISSÉE
        Vector3 desiredPosition = currentFocusPosition - (rotation * Vector3.forward * distance) + Vector3.up * height;
        
        Camera.main.transform.position = desiredPosition;

        // 6. Orientation
        // La caméra regarde le Focus Point (avec un petit offset hauteur)
        Vector3 lookAtPoint = currentFocusPosition + Vector3.up * 0.5f;
        Camera.main.transform.LookAt(lookAtPoint);
    }
}
