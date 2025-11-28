using System;
using System.Collections.Generic;
using System.Linq;
using PlayerIO.GameLibrary;

namespace GolfOnlineServer {
	public class Player : BasePlayer {
		public float X, Y, Z;
		public bool HasFinished = false;
        public int PlayerIndex = 0;
        public bool HasStarted = false; // Est-ce qu'il a déjà joué au moins une fois ?
	}

	[RoomType("GolfOnline")]
	public class GameCode : Game<Player> {
		private string currentPlayerId = "";

		public override void GameStarted() {
			Console.WriteLine("Game is started: " + RoomId);
		}

		public override void GameClosed() {
			Console.WriteLine("RoomId: " + RoomId);
		}

        public override bool AllowUserJoin(Player player) {
            if (Players.Count() >= 4) return false;
            return true;
        }

		public override void UserJoined(Player player) {
            player.PlayerIndex = Players.Count() - 1;
			Console.WriteLine("User joined: " + player.ConnectUserId + " Index: " + player.PlayerIndex);
			
			foreach(Player p in Players) {
				if(p.ConnectUserId != player.ConnectUserId) {
                    // On envoie l'état HasStarted des autres
					player.Send("PlayerJoined", p.ConnectUserId, p.PlayerIndex, p.HasStarted, p.X, p.Y, p.Z);
				}
			}

            // Le nouveau n'a pas encore commencé (false)
			Broadcast("PlayerJoined", player.ConnectUserId, player.PlayerIndex, false, player.X, player.Y, player.Z);

			if(currentPlayerId == "") {
				currentPlayerId = player.ConnectUserId;
                player.HasStarted = true; // Le premier commence direct
				Broadcast("SetTurn", currentPlayerId);
			} else {
				player.Send("SetTurn", currentPlayerId);
			}
		}

		public override void UserLeft(Player player) {
			Console.WriteLine("User left: " + player.ConnectUserId);
			Broadcast("PlayerLeft", player.ConnectUserId);
			
			// Si le joueur qui avait le tour part, on passe au suivant
			if(player.ConnectUserId == currentPlayerId) {
				NextTurn();
			}
		}

		public override void GotMessage(Player player, Message message) {
			switch(message.Type) {
				case "Move":
					player.X = message.GetFloat(0);
					player.Y = message.GetFloat(1);
					player.Z = message.GetFloat(2);
					Broadcast("PlayerMoved", player.ConnectUserId, player.X, player.Y, player.Z);
					break;

				case "Shoot":
					// Sécurité : on vérifie que c'est bien son tour
					if(player.ConnectUserId != currentPlayerId) return;

					float fx = message.GetFloat(0);
					float fy = message.GetFloat(1);
					float fz = message.GetFloat(2);
					Broadcast("PlayerShot", player.ConnectUserId, fx, fy, fz);
					break;

				case "TurnEnded":
					if(player.ConnectUserId == currentPlayerId) {
						NextTurn();
					}
					break;

				case "UpdateBall":
					// Le joueur actuel a le droit de mettre à jour la position de N'IMPORTE QUI (collisions)
					if(player.ConnectUserId == currentPlayerId) {
						string ownerId = message.GetString(0);
						float px = message.GetFloat(1);
						float py = message.GetFloat(2);
						float pz = message.GetFloat(3);
						
						// On met à jour la position côté serveur (optionnel mais propre)
						Player targetPlayer = Players.FirstOrDefault(p => p.ConnectUserId == ownerId);
						if(targetPlayer != null) {
							targetPlayer.X = px;
							targetPlayer.Y = py;
							targetPlayer.Z = pz;
						}

						Broadcast("UpdateBall", ownerId, px, py, pz);
					}
					break;

				case "ReachedHole":
                    Console.WriteLine("DEBUG: ReachedHole received from " + player.ConnectUserId);
					player.HasFinished = true;
					Broadcast("PlayerFinished", player.ConnectUserId);
					
					if(player.ConnectUserId == currentPlayerId) {
						NextTurn();
					}
					
					CheckLevelCompletion();
					break;
			}
		}

		private void CheckLevelCompletion() {
			int finishedCount = 0;
			int totalPlayers = 0;
			foreach(Player p in Players) {
				totalPlayers++;
				if(p.HasFinished) finishedCount++;
			}
            
            Console.WriteLine("DEBUG: CheckCompletion " + finishedCount + "/" + totalPlayers);

			if(finishedCount >= totalPlayers && totalPlayers > 0) {
				Console.WriteLine("DEBUG: ALL FINISHED! Broadcasting LoadLevel...");
				foreach(Player p in Players) {
					p.HasFinished = false;
                    p.X = 0; p.Y = 0.5f; p.Z = 0;
				}
				
				Broadcast("LoadLevel", 2); 
				
				NextTurn();
			}
		}

		private void NextTurn() {
			List<Player> playerList = new List<Player>(Players);
			if(playerList.Count == 0) {
				currentPlayerId = "";
				return;
			}

			// Trouver l'index du joueur actuel
			int index = -1;
			for(int i = 0; i < playerList.Count; i++) {
				if(playerList[i].ConnectUserId == currentPlayerId) {
					index = i;
					break;
				}
			}

			// Chercher le prochain joueur qui n'a PAS fini
			int attempts = 0;
			do {
				index++;
				if(index >= playerList.Count) index = 0;
				attempts++;
			} while (playerList[index].HasFinished && attempts < playerList.Count);

			// Si tout le monde a fini, on ne fait rien (CheckLevelCompletion s'en chargera)
			if(playerList[index].HasFinished) return;

			currentPlayerId = playerList[index].ConnectUserId;
            playerList[index].HasStarted = true; // Il entre en jeu !
			Broadcast("SetTurn", currentPlayerId);
		}
	}
}
