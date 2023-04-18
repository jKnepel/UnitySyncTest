using System;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace jKnepel.UnitySync.SceneSync
{
    public class SceneSyncDecorator : SyncManagerDecorator
    {
		#region private fields

		private ClientVisualiserModule _clientVisualiserModule;
		private GLTFHierarchyModule _gLTFHierarchyModule;
		private GLTFComponentModule _gltfComponentModule;

		private Scene _syncScene;
		private bool _wasRoomOwner;

		private string _importSceneName = "SyncImportScene";
		private string _exportSceneName;

		private bool _visualiseClients = true;
#if UNITY_EDITOR
		private bool _syncSceneAutomatically = true;
#endif

		#endregion

		#region properties

		/// <summary>
		/// The Scene Name that will be used for the synced Scene once a Room is joined.
		/// </summary>
		public string ImportSceneName { get => _importSceneName; set => _importSceneName = value; }

		/// <summary>
		/// The Scene Name that will be used for the synced Scene once a Room is created.
		/// </summary>
		public string ExportSceneName { get => _exportSceneName; set => _exportSceneName = value; }

		/// <summary>
		/// Wether or not the Scene Sync Decorator is subscribed to the SyncManager and has a an active synced Scene.
		/// </summary>
		public bool IsSceneSyncManagerConnected { get; private set; }

		/// <summary>
		/// Wether or not to keep the sync Scene in the Hierarchy upon leaving the Room.
		/// The Scene will always be kept if the local Client was the Room Owner.
		/// </summary>
		public bool KeepSyncScene { get; set; }

		/// <summary>
		/// Wether or not a Visualisation of other Clients should be shown
		/// and the Position of the local Client should be send to other Clients.
		/// </summary>
		public bool VisualiseClients
		{
			get => _visualiseClients;
			set
			{
				if (value == _visualiseClients)
					return;

				_visualiseClients = value;
				if (!value && _clientVisualiserModule != null)
				{
					_clientVisualiserModule.Dispose();
					_clientVisualiserModule = null;
				}
				else if (value && _clientVisualiserModule == null && IsSceneSyncManagerConnected)
				{
					_clientVisualiserModule = new();
				}
			}
		}

#if UNITY_EDITOR
		/// <summary>
		/// Wether or not Objects in the synced Scene should be synced automatically on changes.
		/// </summary>
		/// <remarks>Automatic Scene Synchronisation is only supported in the Editor!</remarks>
		public bool SyncSceneAutomatically
		{
			get => _syncSceneAutomatically;
			set
			{
				if (value == _syncSceneAutomatically)
					return;

				_syncSceneAutomatically = value;
				if (!value)
					ObjectChangeEvents.changesPublished -= ChangesPublished;
				else if (SyncSceneAutomatically)
					ObjectChangeEvents.changesPublished += ChangesPublished;
			}
		}
#endif

		#endregion

		#region lifecycle

		public override void Dispose()
		{
			base.Dispose();
			DisconnectedFromRoom();
		}

		#endregion

		#region protected methods

		protected override void ConnectedToRoom()
		{
#if UNITY_EDITOR
			if (EditorApplication.isPlaying)
			{
				Debug.LogError($"Can't connect to a Room while the Editor is in Playmode!");
				SyncManager.DisconnectFromRoom();
				return;
			}
#endif

			// create/save scene if room is joined/created
			if (SyncManager.IsRoomOwner)
			{
				Scene scene = SceneManager.GetSceneByName(ExportSceneName);
				if (!scene.IsValid() && !scene.isLoaded)
				{
					Debug.LogError($"The Scene {ImportSceneName} is not valid!");
					SyncManager.DisconnectFromRoom();
					return;
				}
				_syncScene = scene;
			}
			else
			{
				Scene scene = SceneManager.GetSceneByName(ImportSceneName);
				if (scene.IsValid() || scene.isLoaded)
				{
					Debug.LogError($"The Scene {ImportSceneName} already exists!");
					SyncManager.DisconnectFromRoom();
					return;
				}

#if UNITY_EDITOR
				_syncScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
				_syncScene.name = ImportSceneName;
#else
				_syncScene = SceneManager.CreateScene(scenename);
#endif
			}
			SceneManager.SetActiveScene(_syncScene);

			if (_visualiseClients)
				_clientVisualiserModule = new();
			_gLTFHierarchyModule = new(_syncScene);
			_gltfComponentModule = new(_syncScene);


			//Selection.selectionChanged += SelectionChanged;

#if UNITY_EDITOR
			if (SyncSceneAutomatically)
				ObjectChangeEvents.changesPublished += ChangesPublished;
			EditorApplication.playModeStateChanged += PreventPlayMode;
#endif

			_wasRoomOwner = SyncManager.IsRoomOwner;
			IsSceneSyncManagerConnected = true;
		}

		protected override void DisconnectedFromRoom()
		{
			IsSceneSyncManagerConnected = false;

			if (_clientVisualiserModule != null)
			{
				_clientVisualiserModule.Dispose();
				_clientVisualiserModule = null;
			}

			if (_gLTFHierarchyModule != null)
			{
				_gLTFHierarchyModule.Dispose();
				_gLTFHierarchyModule = null;
			}

			if (_gltfComponentModule != null)
			{
				_gltfComponentModule.Dispose();
				_gltfComponentModule = null;
			}

			//Selection.selectionChanged -= SelectionChanged;

#if UNITY_EDITOR
			if (SyncSceneAutomatically)
				ObjectChangeEvents.changesPublished -= ChangesPublished;
			EditorApplication.playModeStateChanged -= PreventPlayMode;
#endif

			if (!_wasRoomOwner && !KeepSyncScene && _syncScene.IsValid())
			{
#if UNITY_EDITOR
				EditorSceneManager.UnloadSceneAsync(_syncScene);
#else
                SceneManager.UnloadSceneAsync(_syncScene);
#endif
			}
		}

		#endregion

		#region public methods

		/// <summary>
		/// Deep Syncs all the Child Objects of the given GameObjects Root Object.
		/// </summary>
		/// <param name="go">The GameObject of which its Root GameObject's Hierarchy should be synced</param>
		/// <param name="onDataSend">Callback invoked upon un-/successful syncing of the hierarchy</param>
		public void SyncHierarchy(GameObject go, Action<bool> onDataSend = null)
		{
			_gLTFHierarchyModule.SyncHierarchy(go, onDataSend);
		}

		// TODO : add delete hierarchy

		// TODO : add sync gameobject

		// TODO : add delete gameobject

		// TODO : add sync component

		// TODO : add delete component

		#endregion

		#region private methods

#if UNITY_EDITOR
		private void ChangesPublished(ref ObjectChangeEventStream stream)
		{
			if (!IsSceneSyncManagerConnected)
				return;

			ChangeGameObjectOrComponentPropertiesEventArgs obj;
			stream.GetChangeGameObjectOrComponentPropertiesEvent(0, out obj);
			if (obj.scene == _syncScene)
			{
				UnityEngine.Object o = EditorUtility.InstanceIDToObject(obj.instanceId);
				try { 
					Component co = ((Component)o); 
					_gltfComponentModule.SyncHierarchy(co, (a) => { });
				}
				catch (InvalidCastException) { 
					GameObject go = (GameObject)o; 
					SyncHierarchy(go); // TODO : replace with functionality for only sending gameobject/component
				}
			}

			// TODO : add functionality for sending hierarchy only on hierarchy changes
			// TODO : support all kinds of changesPublished
		}
#endif

		#endregion

		#region helper methods

#if UNITY_EDITOR
		private static void PreventPlayMode(PlayModeStateChange state)
		{
			if (state == PlayModeStateChange.ExitingEditMode)
				EditorApplication.isPlaying = false;
		}
#endif

		#endregion
	}
}
