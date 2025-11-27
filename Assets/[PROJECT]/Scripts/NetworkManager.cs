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
						if(!players.ContainsKey(id)) {
							GameObject p = Instantiate(playerPrefab);
							p.name = "Player_" + id;
							players.Add(id, p);
							
							// On assigne l'ID au BallController pour qu'il sache à qui il appartient
							BallController bc = p.GetComponent<BallController>();
							if(bc != null) {
								bc.ownerId = id;
								if(id == myUserId) bc.isLocalPlayer = true;
							}
							
							Debug.Log("Spawned player: " + id);
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

					case "PlayerMoved":
						string moveId = m.GetString(0);
						if(players.ContainsKey(moveId)) {
							float x = m.GetFloat(1);
							float y = m.GetFloat(2);
							float z = m.GetFloat(3);
							players[moveId].transform.position = new Vector3(x, y, z);
						}
						break;

					case "PlayerShot":
						string shotId = m.GetString(0);
						// On ne fait plus rien ici pour la physique, car c'est le streaming de position qui gère le mouvement.
						Debug.Log("Player shot: " + shotId);
						break;

					case "SetTurn":
						string turnId = m.GetString(0);
						currentTurnId = turnId; // On mémorise à qui c'est le tour
						Debug.Log("C'est au tour de : " + turnId);
						
						foreach(var kvp in players) {
							BallController bc = kvp.Value.GetComponent<BallController>();
							if(bc != null) {
								// On sauvegarde la position de départ pour tout le monde
								bc.OnTurnStarted();

								if(turnId == myUserId && kvp.Key == myUserId) {
									bc.isMyTurn = true;
								} else {
									bc.isMyTurn = false;
								}
							}
						}
						break;

					case "PlayerPosition":
						string posId = m.GetString(0);
						if(players.ContainsKey(posId)) {
							float px = m.GetFloat(1);
							float py = m.GetFloat(2);
							float pz = m.GetFloat(3);
							
							// On met à jour la cible d'interpolation
							BallController bc = players[posId].GetComponent<BallController>();
							if(bc != null) {
								bc.UpdateTargetPosition(new Vector3(px, py, pz));
							}
						}
						break;

					case "PlayerFinished":
						string finishedId = m.GetString(0);
						if(players.ContainsKey(finishedId)) {
							players[finishedId].SetActive(false); // On cache le joueur
							Debug.Log("Player finished: " + finishedId);
						}
						break;

					case "LoadLevel":
						int levelIndex = m.GetInt(0);
						Debug.Log("Chargement du niveau : " + levelIndex);
						ResetGameForNewLevel();
						break;
				}
			}
			msgQueue.Clear();
		}
	}

	void ResetGameForNewLevel() {
		foreach(var kvp in players) {
			GameObject p = kvp.Value;
			p.SetActive(true);
			p.transform.position = new Vector3(0, 0.5f, 0);
			p.GetComponent<Rigidbody>().isKinematic = false;
			
			BallController bc = p.GetComponent<BallController>();
			if(bc != null) {
				bc.hasFinished = false;
				bc.isMyTurn = false;
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

	public void SendPosition(Vector3 pos) {
		if(pioconnection != null) {
			pioconnection.Send("Position", pos.x, pos.y, pos.z);
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

	void OnGUI() {
		if (pioconnection != null) {
			if (GUI.Button(new Rect(10, 10, 150, 50), "Disconnect")) {
				Disconnect();
			}
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
