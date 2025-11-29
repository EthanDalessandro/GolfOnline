using UnityEngine;
using PlayerIOClient;
using System.Collections.Generic;

public class NetworkManager : MonoBehaviour {
	public GameObject playerPrefab; // Glissez un Cube ici dans l'inspecteur
	private Connection pioconnection;
	private List<PlayerIOClient.Message> msgQueue = new List<PlayerIOClient.Message>();
	private Dictionary<string, GameObject> players = new Dictionary<string, GameObject>();
	public string myUserId;
	public string currentTurnId = ""; 
    public int currentLevelIndex = 1; // Niveau actuel

    // LOBBY
    public bool gameStarted = false; // Public pour que BallController puisse savoir s'il doit afficher son HUD
    private Dictionary<string, bool> readyStatus = new Dictionary<string, bool>();

    // Points de départ pour 4 joueurs
    private Vector3[] spawnPoints = new Vector3[] {
        new Vector3(0, 0.5f, 0),    // Joueur 1
        new Vector3(2, 0.5f, 0),    // Joueur 2
        new Vector3(-2, 0.5f, 0),   // Joueur 3
        new Vector3(0, 0.5f, -2)    // Joueur 4
    };
    
    // Liste des joueurs ayant fini le niveau
    private List<string> finishedPlayers = new List<string>();

    // TIMEOUT DE CONNEXION
    private float connectionStartTime = 0f;
    private bool isConnecting = false;

	void Start() {
		Application.runInBackground = true; 
        ConnectToServer();
    }

    void ConnectToServer() {
        isConnecting = true;
        connectionStartTime = Time.time;

		string generatedUserId = "Player_" + System.Guid.NewGuid().ToString();
        
		PlayerIO.Authenticate(
			"chessonline-2xyyfrmdnuipfyqvw9idpg",
			"public",
			new Dictionary<string, string> { { "userId", generatedUserId } },
			null,
			delegate (Client client) {
				Debug.Log("Authenticated as " + generatedUserId);
				myUserId = generatedUserId; 
                // FIX: Utiliser 127.0.0.1 au lieu de localhost pour éviter les problèmes IPv6
				client.Multiplayer.DevelopmentServer = new ServerEndpoint("127.0.0.1", 8184);
				
                Debug.Log("Attempting to join room...");
				client.Multiplayer.CreateJoinRoom(
					"MyRoom",
					"GolfOnline",
					true,
					null,
					null,
					delegate (Connection connection) {
						Debug.Log(">>> JOIN SUCCESS! Connection object received.");
                        isConnecting = false;
						pioconnection = connection;
						connection.OnMessage += HandleMessage;
						
						connection.OnDisconnect += delegate(object sender, string reason) {
							Debug.LogWarning("Disconnected from server: " + reason);
						};
					},
					delegate (PlayerIOError error) { 
                        isConnecting = false;
                        Debug.LogError("Error Joining Room: " + error.ToString()); 
                    }
				);
			},
			delegate (PlayerIOError error) { 
                isConnecting = false;
                Debug.LogError("Error Authenticating: " + error.ToString()); 
            }
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
                    case "GameStarted":
                        gameStarted = true;
                        Debug.Log("LA PARTIE COMMENCE !");
                        break;

                    case "PlayerReadyStatus":
                        string rId = m.GetString(0);
                        bool isReady = m.GetBoolean(1);
                        if (readyStatus.ContainsKey(rId)) {
                            readyStatus[rId] = isReady;
                        } else {
                            readyStatus.Add(rId, isReady);
                        }
                        break;

					case "PlayerJoined":
						string id = m.GetString(0);
                        int index = m.GetInt(1); 
                        bool hasStarted = m.GetBoolean(2); 

                        // On ajoute le joueur à la liste des statuts s'il n'y est pas
                        if (!readyStatus.ContainsKey(id)) readyStatus.Add(id, false);

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
                                bc.playerIndex = index;
								if(id == myUserId) bc.isLocalPlayer = true;
							}
							
							Debug.Log("Spawned player: " + id + " (Active: " + hasStarted + ")");
						}
						break;

					case "PlayerLeft":
						string leftId = m.GetString(0);
                        if (readyStatus.ContainsKey(leftId)) readyStatus.Remove(leftId);

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
                            if(!finishedPlayers.Contains(finishedId)) finishedPlayers.Add(finishedId);
							Debug.Log("Player finished: " + finishedId);
						}
						break;

                    case "LoadLevel":
                        int levelIdx = m.GetInt(0);
                        currentLevelIndex = levelIdx;
                        Debug.Log("DEBUG: LoadLevel received! Index: " + levelIdx);
                        ResetGameForNewLevel();
                        break;
				}
			}
			msgQueue.Clear();
		}
	}

	void ResetGameForNewLevel() {
        Debug.Log("--- RESET LEVEL ---");
        
        finishedPlayers.Clear();

		foreach(var kvp in players) {
			GameObject p = kvp.Value;
			p.SetActive(true); // On réactive tout le monde au reset
			
            BallController bc = p.GetComponent<BallController>();
            
            // Position de départ simple base sur l'index
            Vector3 startPos = new Vector3(0, 0.5f, 0);
            if (bc != null && bc.playerIndex < spawnPoints.Length) {
                startPos = spawnPoints[bc.playerIndex];
            }
            p.transform.position = startPos;

			p.GetComponent<Rigidbody>().isKinematic = false;
            p.GetComponent<Rigidbody>().linearVelocity = Vector3.zero; 
            p.GetComponent<Rigidbody>().angularVelocity = Vector3.zero;
			
			if(bc != null) {
				bc.hasFinished = false;
				bc.isMyTurn = false;
                
                // FORCE la mise à jour de la cible réseau pour éviter le retour en arrière
                bc.UpdateTargetPosition(p.transform.position);
                bc.OnTurnStarted();
			}
		}
	}

	public void SendReady() {
		if(pioconnection != null) {
			pioconnection.Send("Ready");
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
    
    public void Disconnect() {
        if(pioconnection != null) {
            Debug.Log("Closing connection...");
            pioconnection.Disconnect();
            pioconnection = null;
        }
        isConnecting = false;
    }
    
    void OnDisable() {
        Disconnect();
    }

	void OnApplicationQuit() {
		Disconnect();
	}

	void OnDestroy() {
		Disconnect();
	}

    // INTERFACE DU LOBBY
    void OnGUI() {
        // Message de chargement / Timeout
        if (isConnecting) {
             GUI.Box(new Rect(0, 0, Screen.width, Screen.height), "");
             GUI.Label(new Rect(Screen.width/2 - 100, Screen.height/2, 200, 30), "Connexion en cours...", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } });
             
             if (Time.time - connectionStartTime > 5.0f) {
                 if (GUI.Button(new Rect(Screen.width/2 - 75, Screen.height/2 + 40, 150, 40), "Réessayer")) {
                     Disconnect();
                     ConnectToServer();
                 }
             }
             return; // On n'affiche rien d'autre si on connecte
        }

        if (!gameStarted && pioconnection != null) {
            // Fond noir semi-transparent
            GUI.Box(new Rect(0, 0, Screen.width, Screen.height), "");

            GUILayout.BeginArea(new Rect(Screen.width/2 - 150, Screen.height/2 - 200, 300, 400));
            GUILayout.Label("SALON D'ATTENTE", new GUIStyle(GUI.skin.label) { fontSize = 30, alignment = TextAnchor.MiddleCenter });
            GUILayout.Space(20);

            foreach(var kvp in readyStatus) {
                string status = kvp.Value ? "<color=green>PRÊT</color>" : "<color=red>EN ATTENTE</color>";
                string name = (kvp.Key == myUserId) ? "MOI" : kvp.Key;
                GUILayout.Label(name + " : " + status, new GUIStyle(GUI.skin.label) { richText = true, fontSize = 20 });
            }

            GUILayout.Space(50);

            bool amIReady = readyStatus.ContainsKey(myUserId) && readyStatus[myUserId];
            string btnText = amIReady ? "ANNULER" : "JE SUIS PRÊT !";
            
            if (GUILayout.Button(btnText, new GUIStyle(GUI.skin.button) { fontSize = 25, fixedHeight = 60 })) {
                SendReady();
            }
            
            GUILayout.Label("La partie se lancera quand tout le monde sera prêt.", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter });

            GUILayout.EndArea();
        }
    }
}
