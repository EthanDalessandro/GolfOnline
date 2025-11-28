using UnityEngine;
using PlayerIOClient;
using System.Collections.Generic;

public class NetworkManager : MonoBehaviour {
	public GameObject playerPrefab; // Glissez un Cube ici dans l'inspecteur
	private Connection pioconnection;
	private List<PlayerIOClient.Message> msgQueue = new List<PlayerIOClient.Message>();
	private Dictionary<string, GameObject> players = new Dictionary<string, GameObject>();
	public string myUserId;
	public string currentTurnId = ""; // Public pour que BallController puisse lire

    // Points de départ pour 4 joueurs
    private Vector3[] spawnPoints = new Vector3[] {
        new Vector3(0, 0.5f, 0),    // Joueur 1
        new Vector3(2, 0.5f, 0),    // Joueur 2
        new Vector3(-2, 0.5f, 0),   // Joueur 3
        new Vector3(0, 0.5f, -2)    // Joueur 4
    };

	void Start() {
		Application.runInBackground = true; // Important pour tester avec plusieurs fenêtres

		string generatedUserId = "Guest" + Random.Range(0, 10000);
		PlayerIO.Authenticate(
			"chessonline-2xyyfrmdnuipfyqvw9idpg",
			"public",
			new Dictionary<string, string> { { "userId", generatedUserId } },
			null,
			delegate (Client client) {
				Debug.Log("Connected");
				myUserId = generatedUserId; // On sauvegarde notre ID
				client.Multiplayer.DevelopmentServer = new ServerEndpoint("localhost", 8184);
				client.Multiplayer.CreateJoinRoom(
					"MyRoom",
					"GolfOnline",
					true,
					null,
					null,
					delegate (Connection connection) {
						Debug.Log("Joined Room!");
						pioconnection = connection;
						connection.OnMessage += HandleMessage;
						
						connection.OnDisconnect += delegate(object sender, string reason) {
							Debug.Log("Disconnected: " + reason);
						};
					},
					delegate (PlayerIOError error) { Debug.LogError(error.ToString()); }
				);
			},
			delegate (PlayerIOError error) { Debug.LogError(error.ToString()); }
		);
	}

	void HandleMessage(object sender, PlayerIOClient.Message m) {
		lock(msgQueue) {
			msgQueue.Add(m);
		}
	}

	void Update() {
		lock(msgQueue) {
			foreach(var m in msgQueue) {
				switch(m.Type) {
					case "PlayerJoined":
						string id = m.GetString(0);
                        int index = m.GetInt(1); // Index (0-3)
                        bool hasStarted = m.GetBoolean(2); // Est-ce qu'il a déjà joué ?

						if(!players.ContainsKey(id)) {
                            Vector3 spawnPos = spawnPoints[index % spawnPoints.Length];

							GameObject p = Instantiate(playerPrefab, spawnPos, Quaternion.identity);
							p.name = "Player_" + id;
                            
                            // Si le joueur n'a pas encore commencé, on le cache !
                            if (!hasStarted) {
                                p.SetActive(false);
                            }
                            
							players.Add(id, p);
							
							BallController bc = p.GetComponent<BallController>();
							if(bc != null) {
								bc.ownerId = id;
								if(id == myUserId) bc.isLocalPlayer = true;
							}
							
							Debug.Log("Spawned player: " + id + " (Active: " + hasStarted + ")");
						}
						break;

					case "PlayerLeft":
						string leftId = m.GetString(0);
						if(players.ContainsKey(leftId)) {
							Destroy(players[leftId]);
							players.Remove(leftId);
							Debug.Log("Removed player: " + leftId);
						}
						break;

					case "PlayerShot":
						string shotId = m.GetString(0);
						Debug.Log("Player shot: " + shotId);
						break;

					case "SetTurn":
						string turnId = m.GetString(0);
						currentTurnId = turnId; 
						Debug.Log("C'est au tour de : " + turnId);
                        
                        // Si c'est le tour de quelqu'un, on s'assure qu'il est visible (Spawn !)
                        if(players.ContainsKey(turnId)) {
                            GameObject p = players[turnId];
                            if(!p.activeSelf) {
                                p.SetActive(true);
                                Debug.Log("Le joueur " + turnId + " entre en jeu !");
                            }
                        }
						
						foreach(var kvp in players) {
                            if (!kvp.Value.activeSelf) continue; // On ignore les joueurs cachés

							BallController bc = kvp.Value.GetComponent<BallController>();
							if(bc != null) {
								bc.OnTurnStarted();

                                // Mise à jour de l'indicateur global (pour la lumière)
                                if (kvp.Key == turnId) {
                                    bc.isCurrentTurn = true;
                                } else {
                                    bc.isCurrentTurn = false;
                                }

                                // Mise à jour de l'indicateur local (pour savoir si JE peux jouer)
								if(turnId == myUserId && kvp.Key == myUserId) {
									bc.isMyTurn = true;
								} else {
									bc.isMyTurn = false;
								}
							}
						}
						break;

					case "UpdateBall":
						string ballOwnerId = m.GetString(0);
						if(players.ContainsKey(ballOwnerId)) {
							float px = m.GetFloat(1);
							float py = m.GetFloat(2);
							float pz = m.GetFloat(3);
							
							// On met à jour la cible d'interpolation
							BallController bc = players[ballOwnerId].GetComponent<BallController>();
							if(bc != null) {
								bc.UpdateTargetPosition(new Vector3(px, py, pz));
							}
						}
						break;

					case "PlayerFinished":
						string finishedId = m.GetString(0);
						if(players.ContainsKey(finishedId)) {
							players[finishedId].SetActive(false); // On cache le joueur qui a fini
							Debug.Log("Player finished: " + finishedId);
						}
						break;

					case "LoadLevel":
						int levelIndex = m.GetInt(0);
						Debug.Log("DEBUG: LoadLevel received! Index: " + levelIndex);
						ResetGameForNewLevel();
						break;
				}
			}
			msgQueue.Clear();
		}
	}

	void ResetGameForNewLevel() {
        Debug.Log("--- RESET LEVEL ---");
		foreach(var kvp in players) {
			GameObject p = kvp.Value;
			p.SetActive(true);
			p.transform.position = new Vector3(0, 0.5f, 0); 
			p.GetComponent<Rigidbody>().isKinematic = false;
            p.GetComponent<Rigidbody>().linearVelocity = Vector3.zero; // Stop physics
			
			BallController bc = p.GetComponent<BallController>();
			if(bc != null) {
				bc.hasFinished = false;
				bc.isMyTurn = false;
                
                // FORCE la mise à jour de la cible réseau pour éviter le retour en arrière
                bc.UpdateTargetPosition(p.transform.position);
                bc.OnTurnStarted();
			}
		}
	}

	public void SendShoot(Vector3 force) {
		if(pioconnection != null) {
			pioconnection.Send("Shoot", force.x, force.y, force.z);
		}
	}

	public void SendTurnEnded() {
		if(pioconnection != null) {
			pioconnection.Send("TurnEnded");
		}
	}

	public void SendUpdateBall(string ownerId, Vector3 pos) {
		if(pioconnection != null) {
			pioconnection.Send("UpdateBall", ownerId, pos.x, pos.y, pos.z);
		}
	}

	public void SendReachedHole() {
		if(pioconnection != null) {
			pioconnection.Send("ReachedHole");
		}
	}
	void OnApplicationQuit() {
		Disconnect();
	}

	void OnApplicationPause(bool pauseStatus) {
		if (pauseStatus) Disconnect();
	}

	void OnDestroy() {
		Disconnect();
	}

	void Disconnect() {
		if(pioconnection != null) {
			Debug.Log("Attempting to disconnect...");
			pioconnection.Disconnect();
			pioconnection = null;
			Debug.Log("Disconnected from server");
		}
	}
}
