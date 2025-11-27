using System;
using System.Collections.Generic;
using System.Linq;
using PlayerIO.GameLibrary;

namespace GolfOnlineServer {
	public class Player : BasePlayer {
		public float X, Y, Z;
		public bool HasFinished = false;
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

		public override void UserJoined(Player player) {
			Console.WriteLine("User joined: " + player.ConnectUserId);
			
			foreach(Player p in Players) {
				if(p.ConnectUserId != player.ConnectUserId) {
					player.Send("PlayerJoined", p.ConnectUserId, p.X, p.Y, p.Z);
				}
			}

			Broadcast("PlayerJoined", player.ConnectUserId, player.X, player.Y, player.Z);

			// Si c'est le premier joueur, on lui donne le tour tout de suite
			if(currentPlayerId == "") {
				currentPlayerId = player.ConnectUserId;
				Broadcast("SetTurn", currentPlayerId);
			} else {
				// Sinon on informe le nouveau de qui est en train de jouer
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

						Broadcast("PlayerPosition", ownerId, px, py, pz);
					}
					break;

				case "ReachedHole":
					player.HasFinished = true;
					Broadcast("PlayerFinished", player.ConnectUserId); // Optionnel : pour afficher un score
					
					// Si c'était son tour, on passe au suivant
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

			if(finishedCount >= totalPlayers && totalPlayers > 0) {
				// Tout le monde a fini !
				// On reset les statuts
				foreach(Player p in Players) {
					p.HasFinished = false;
				}
				
				// On dit à tout le monde de charger le niveau suivant
				Broadcast("LoadLevel", 2); 
				
				// On redonne la main au premier joueur
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
			Broadcast("SetTurn", currentPlayerId);
		}
	}
}
