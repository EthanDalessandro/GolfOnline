using UnityEngine;

public class Hole : MonoBehaviour {
    void OnTriggerEnter(Collider other) {
        BallController ball = other.GetComponent<BallController>();
        
        // Si c'est une balle et que c'est le joueur local
        if (ball != null && ball.isLocalPlayer && !ball.hasFinished) {
            Debug.Log("Dans le trou !");
            ball.hasFinished = true;
            ball.UpdateTargetPosition(ball.transform.position); // IMPORTANT : On fixe la cible ici pour ne pas qu'elle reparte au spawn quand le tour change !
            ball.rb.linearVelocity = Vector3.zero;
            ball.rb.isKinematic = true; // On fige la balle
            
            // On cache la balle (optionnel, ou on la laisse au fond du trou)
            ball.gameObject.SetActive(false);

            // On prévient le serveur
            NetworkManager nm = FindObjectOfType<NetworkManager>();
            if(nm != null) {
                nm.SendReachedHole();
            }
        }
    }
}
