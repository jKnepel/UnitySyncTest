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
		private static SyncSettings _settings = SyncManager.GetSyncSettings();
        private static ClientVisualizerModule _clientVisualizerModule;

        public static void Init() { }

		#region public methods

		public static bool ConnectToRoom(string roomname, string scenename, bool createroom = false)
		{
			if (SyncManager.IsConnectedToRoom)
			{
				Debug.LogWarning($"The client is already connected!");
				return false;
			}

#if UNITY_EDITOR
			if (EditorApplication.isPlaying)
			{
				Debug.LogWarning($"Can't connect to a Room while the Editor is in Playmode!");
				return false;
			}
#endif

			// create/save scene if room is joined/created
			Scene scene = SceneManager.GetSceneByName(scenename);
			if (createroom)
			{
				if (!scene.IsValid() && !scene.isLoaded)
				{
					Debug.LogWarning($"The Scene {scenename} is not valid!");
					return false;
				}
				_settings.SyncScene = scene;
			}
			else
			{
				if (scene.IsValid() || scene.isLoaded)
				{
					Debug.LogWarning($"The Scene {scenename} already exists!");
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

			_clientVisualizerModule = new();

#if UNITY_EDITOR
			//Selection.selectionChanged += SelectionChanged;
			//ObjectChangeEvents.changesPublished += ChangesPublished;
			EditorApplication.playModeStateChanged += PreventPlayMode;
#endif
			return true;
		}

        public static void DisconnectFromRoom()
		{
#if UNITY_EDITOR
			//Selection.selectionChanged -= SelectionChanged;
			//ObjectChangeEvents.changesPublished -= ChangesPublished;
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
			_clientVisualizerModule.Dispose();
			_clientVisualizerModule = null;

			SyncManager.DisconnectFromRoom();
			return;
		}

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
