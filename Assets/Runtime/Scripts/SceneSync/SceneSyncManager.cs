using System;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace jKnepel.UnitySync.SceneSync
{
	public static class SceneSyncManager
	{
		private readonly static SyncSettings _settings = SyncManager.GetSyncSettings();

		private static ClientVisualiserModule _clientVisualiserModule;
		private static GLTFHierarchyModule _gLTFHierarchyModule;

		public static bool IsSceneSyncManagerConnected { get; private set; }

		private static bool _visualiseClients = true;
		public static bool VisualiseClients
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
				else if (value && _clientVisualiserModule == null)
				{
					if (_settings.SyncParentContainer == null)
					{
						_settings.SyncParentContainer = new GameObject().transform;
						_settings.SyncParentContainer.name = "SceneSync Visualiser Parent";
						_settings.SyncParentContainer.gameObject.hideFlags = HideFlags.HideInHierarchy;
					}
					_clientVisualiserModule = new(_settings.SyncParentContainer);
				}
			}
		}

#if UNITY_EDITOR
		private static bool _syncSceneAutomatically;
		public static bool SyncSceneAutomatically 
		{
			get => _syncSceneAutomatically;
			set
			{
				if (value == _syncSceneAutomatically)
					return;

				_syncSceneAutomatically = value;
				if (IsSceneSyncManagerConnected)
				{
					if (value)
						ObjectChangeEvents.changesPublished += ChangesPublished;
					else 
						ObjectChangeEvents.changesPublished -= ChangesPublished;
				}
			}
		}
#endif

		#region lifecycle

		static SceneSyncManager()
		{
			if (_settings.SyncParentContainer != null)
			{
#if UNITY_EDITOR
				GameObject.DestroyImmediate(_settings.SyncParentContainer.gameObject);
#else
				GameObject.Destroy(_settings.SyncParentContainer.gameObject);
#endif
				_settings.SyncParentContainer = null;
			}
		}

        public static void Init() { }

		#endregion

		#region public methods

		public static bool ConnectToRoom(string roomname, string scenename, bool createroom = false)
		{
			if (SyncManager.IsConnectedToRoom)
			{
				Debug.LogError($"The client is already connected!");
				return false;
			}

#if UNITY_EDITOR
			if (EditorApplication.isPlaying)
			{
				Debug.LogError($"Can't connect to a Room while the Editor is in Playmode!");
				return false;
			}
#endif

			// create/save scene if room is joined/created
			Scene scene = SceneManager.GetSceneByName(scenename);
			if (createroom)
			{
				if (!scene.IsValid() && !scene.isLoaded)
				{
					Debug.LogError($"The Scene {scenename} is not valid!");
					return false;
				}
				_settings.SyncScene = scene;
			}
			else
			{
				if (scene.IsValid() || scene.isLoaded)
				{
					Debug.LogError($"The Scene {scenename} already exists!");
					return false;
				}

#if UNITY_EDITOR
				_settings.SyncScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
				_settings.SyncScene.name = scenename;
#else
				_settings.SyncScene = SceneManager.CreateScene(scenename);
#endif
			}
			SceneManager.SetActiveScene(_settings.SyncScene);

			// if create/join room in network is unsuccessful reverse scene creation
			if (!SyncManager.ConnectToRoom(roomname, createroom))
			{
				if (!SyncManager.IsRoomOwner && !_settings.KeepSyncScene && _settings.SyncScene.IsValid())
				{
#if UNITY_EDITOR
					EditorSceneManager.UnloadSceneAsync(_settings.SyncScene);
#else
					SceneManager.UnloadSceneAsync(_settings.SyncScene);
#endif
				}
				return false;
			}

			_settings.SyncParentContainer = new GameObject().transform;
			_settings.SyncParentContainer.name = "SceneSync Visualiser Parent";
			_settings.SyncParentContainer.gameObject.hideFlags = HideFlags.HideInHierarchy;

			if (_visualiseClients)
				_clientVisualiserModule = new(_settings.SyncParentContainer);
			_gLTFHierarchyModule = new();

			IsSceneSyncManagerConnected = true;

#if UNITY_EDITOR
			//Selection.selectionChanged += SelectionChanged;
			if (SyncSceneAutomatically)
				ObjectChangeEvents.changesPublished += ChangesPublished;
			EditorApplication.playModeStateChanged += PreventPlayMode;
#endif
			return true;
		}

        public static void DisconnectFromRoom()
		{
			if (!SyncManager.IsConnectedToRoom)
				return;

#if UNITY_EDITOR
			//Selection.selectionChanged -= SelectionChanged;
			if (SyncSceneAutomatically)
				ObjectChangeEvents.changesPublished -= ChangesPublished;
			EditorApplication.playModeStateChanged -= PreventPlayMode;
#endif

			if (!SyncManager.IsRoomOwner && !_settings.KeepSyncScene && _settings.SyncScene.IsValid())
			{
#if UNITY_EDITOR
				EditorSceneManager.UnloadSceneAsync(_settings.SyncScene);
#else
                SceneManager.UnloadSceneAsync(_settings.SyncScene);
#endif
			}

			if (_visualiseClients)
			{
				_clientVisualiserModule.Dispose();
				_clientVisualiserModule = null;
			}
			_gLTFHierarchyModule.Dispose();
			_gLTFHierarchyModule = null;

			IsSceneSyncManagerConnected = false;

			if (_settings.SyncParentContainer != null)
			{
#if UNITY_EDITOR
				GameObject.DestroyImmediate(_settings.SyncParentContainer.gameObject);
#else
				GameObject.Destroy(_settings.SyncParentContainer.gameObject);
#endif
				_settings.SyncParentContainer = null;
			}

			SyncManager.DisconnectFromRoom();
		}

		public static void SyncHierarchy(GameObject go, Action<bool> onDataSend = null)
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
		private static void ChangesPublished(ref ObjectChangeEventStream stream)
		{
			if (!IsSceneSyncManagerConnected)
				return;

			ChangeGameObjectOrComponentPropertiesEventArgs obj;
			stream.GetChangeGameObjectOrComponentPropertiesEvent(0, out obj);
			if (obj.scene == _settings.SyncScene)
			{
				UnityEngine.Object o = EditorUtility.InstanceIDToObject(obj.instanceId);
				GameObject go;
				try { go = ((Component)o).gameObject; }
				catch (InvalidCastException) { go = (GameObject)o; }
				SyncHierarchy(go); // TODO : replace with functionality for only sending gameobject/component
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
