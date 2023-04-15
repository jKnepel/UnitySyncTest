using System.Net;

namespace jKnepel.UnitySync
{
    public abstract class SyncManagerDecorator
    {
		public SyncManagerDecorator()
		{
			SyncManager.OnAwake += Awake;
			SyncManager.OnStart += Start;
			SyncManager.OnUpdate += Update;

			SyncManager.OnSyncMessageAdded += SyncMessageAdded;
			SyncManager.OnServerDiscoveryActivated += ServerDiscoveryActivated;
			SyncManager.OnServerDiscoveryDeactivated += ServerDiscoveryDeactivated;
			SyncManager.OnLocalClientConnected += ConnectedToServer;
			SyncManager.OnLocalClientDisconnected += DisconnectedFromServer;
			SyncManager.OnClientConnected += ClientConnected;
			SyncManager.OnClientDisconnected += ClientDisconnected;
		}

		~SyncManagerDecorator()
		{
			Dispose(false);
		}

		public virtual void Dispose()
		{
			Dispose(true);
		}

		private void Dispose(bool disposing)
		{
			if (disposing)
			{
				SyncManager.OnAwake -= Awake;
				SyncManager.OnStart -= Start;
				SyncManager.OnUpdate -= Update;

				SyncManager.OnSyncMessageAdded -= SyncMessageAdded;
				SyncManager.OnServerDiscoveryActivated -= ServerDiscoveryActivated;
				SyncManager.OnServerDiscoveryDeactivated -= ServerDiscoveryDeactivated;
				SyncManager.OnLocalClientConnected -= ConnectedToServer;
				SyncManager.OnLocalClientDisconnected -= DisconnectedFromServer;
				SyncManager.OnClientConnected -= ClientConnected;
				SyncManager.OnClientDisconnected -= ClientDisconnected;
			}
		}

		protected virtual void Awake() { }
		protected virtual void Start() { }
		protected virtual void Update() { }

		protected virtual void SyncMessageAdded() { }
		protected virtual void ServerDiscoveryActivated() { }
		protected virtual void ServerDiscoveryDeactivated() { }
		protected virtual void ConnectedToServer() { }
		protected virtual void DisconnectedFromServer() { }
		protected virtual void ClientConnected(IPAddress ip) { }
		protected virtual void ClientDisconnected(IPAddress ip) { }
	}
}
