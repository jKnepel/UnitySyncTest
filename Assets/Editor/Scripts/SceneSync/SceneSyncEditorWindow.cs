using jKnepel.UnitySync.Networking;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace jKnepel.UnitySync.SceneSync
{
    internal class SceneSyncEditorWindow : EditorWindow
    {
        #region private members

        private static SyncSettings _settings;
        private Editor _syncSettingsEditor;

        private static SceneSyncDecorator _sceneSync;

        private Vector2 _serversViewPos;
        private Vector2 _clientsViewPos;
        private Vector2 _messagesViewPos;
        private Color[] _scrollViewColors = new Color[] { new(0.25f, 0.25f, 0.25f), new(0.23f, 0.23f, 0.23f) };
        
        private bool _isAutoscroll = false;
        private bool _newServerOptionsIsVisible = true;
        private string _newServerName = "New Server";
        private int _newServerDropdownIndex = 0;
        private int NewServerDropdownIndex
		{
            get => _newServerDropdownIndex;
            set
            {
                _newServerDropdownIndex = value;
                _sceneSync.ExportSceneName = value < EditorSceneManager.sceneCount
                    ? EditorSceneManager.GetSceneAt(value).name
                    : "";
			}
		}

        private Texture2D _texture;
        private GUIStyle _style = new();

        private const float ROW_HEIGHT = 20;

        #endregion

        [MenuItem("Window/Unity Sync/Scene Sync Manager")]
        public static void ShowWindow()
        {
            GetWindow(typeof(SceneSyncEditorWindow), false, "Scene Sync Manager");
        }

		public void OnEnable()
		{
            _texture = new(1, 1);
            _settings = SyncSettings.GetOrCreateSettings();
            _syncSettingsEditor = Editor.CreateEditor(_settings);
            _sceneSync = new();
            SyncManager.OnSyncMessageAdded += AddSyncMessage;
		}

		public void CreateGUI()
		{
        }

        private void OnDestroy()
		{
            SyncManager.OnSyncMessageAdded -= AddSyncMessage;
        }

        // TODO : add descriptions to labels, was too lazy
        void OnGUI()
        {
            EditorGUILayout.BeginVertical(EditorStyles.inspectorDefaultMargins);
			{
                GUILayout.Label("UnitySync", EditorStyles.largeLabel);

                // user settings
                _sceneSync.KeepSyncScene = EditorGUILayout.Toggle("Keep ImportScene:", _sceneSync.KeepSyncScene);
                _sceneSync.VisualiseClients = EditorGUILayout.Toggle(new GUIContent("Visualise Clients:"), _sceneSync.VisualiseClients);
                _sceneSync.SyncSceneAutomatically = EditorGUILayout.Toggle(new GUIContent("Sync Scene Automatically:"), _sceneSync.SyncSceneAutomatically);
                _sceneSync.ImportSceneName = EditorGUILayout.TextField("ImportScene Name:", _sceneSync.ImportSceneName);
                _syncSettingsEditor.OnInspectorGUI();
                EditorGUILayout.Space();
                EditorGUILayout.Space();

                if (!SyncManager.IsServerDiscoveryActive)
				{
                    GUILayout.Label($"Client has stopped working!");
                    if (GUILayout.Button(new GUIContent("Reset Client"), GUILayout.ExpandWidth(false)))
					{
                        SyncManager.ResetClient();
                        Repaint();
					}
				}

                if (GUILayout.Button("Reset Client"))
                {
                    SyncManager.ResetClient();
                    Repaint();
                }

                if (!SyncManager.IsConnected)
                {
                    // open servers list
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label($"Open Servers: {SyncManager.OpenServers?.Count}");
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(new GUIContent(_newServerOptionsIsVisible ? "-" : "+", "Create a New Server"),
                        GUILayout.Width(20), GUILayout.Height(20)))
                        _newServerOptionsIsVisible = !_newServerOptionsIsVisible;
                    EditorGUILayout.EndHorizontal();
                    _serversViewPos = EditorGUILayout.BeginScrollView(_serversViewPos,
                        EditorStyles.helpBox, GUILayout.ExpandWidth(true), GUILayout.MaxHeight(150));
                    {
                        if (SyncManager.OpenServers?.Count == 0)
                            GUILayout.Label($"No Servers found!");

                        for (int i = 0; i < SyncManager.OpenServers?.Count; i++)
                        {
                            SyncServerInformation server = SyncManager.OpenServers[i];
                            EditorGUILayout.BeginHorizontal(GetScrollviewRowStyle(_scrollViewColors[i % 2]));
                            {
                                GUILayout.Label(server.Servername);
                                GUILayout.Label($"#{server.NumberConnectedClients}");
                                if (GUILayout.Button(new GUIContent("Connect To Server"), GUILayout.ExpandWidth(false)))
                                    SyncManager.ConnectToServer(server.Servername, false);
                            }
                            EditorGUILayout.EndHorizontal();
                        }
                    }
                    EditorGUILayout.EndScrollView();

                    // new server options
                    if (_newServerOptionsIsVisible)
                    {
                        int sceneCount = EditorSceneManager.sceneCount;
                        string[] sceneNames = new string[sceneCount];
                        for (int i = 0; i < sceneCount; i++)
                            sceneNames[i] = EditorSceneManager.GetSceneAt(i).name;
                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                        {
                            GUILayout.Label("Create a New Server", EditorStyles.boldLabel);
                            _newServerName = EditorGUILayout.TextField("Servername:", _newServerName);
                            NewServerDropdownIndex = EditorGUILayout.Popup("Synced Scene: ", NewServerDropdownIndex, sceneNames);
                            if (GUILayout.Button(new GUIContent("Create")))
                                SyncManager.ConnectToServer(_newServerName, true);
                        }
                        EditorGUILayout.EndVertical();
                    }
                }
                else
                {
                    // server client list
                    GUILayout.Label($"Current Server: {SyncManager.CurrentServer.Servername}");
                    _clientsViewPos = EditorGUILayout.BeginScrollView(_clientsViewPos,
                        EditorStyles.helpBox, GUILayout.ExpandWidth(true), GUILayout.MaxHeight(150));
                    {
                        if (SyncManager.CurrentServer.ConnectedClients.Count == 0)
                            GUILayout.Label($"There are no Clients in this server!");

                        Color defaultColor = _style.normal.textColor;
                        _style.alignment = TextAnchor.MiddleLeft;
                        for (int i = 0; i < SyncManager.CurrentServer.NumberConnectedClients; i++)
                        {
                            SyncConnectedClient client = SyncManager.CurrentServer.ConnectedClients.ElementAt(i).Value;
                            EditorGUILayout.BeginHorizontal(GetScrollviewRowStyle(_scrollViewColors[i % 2]));
                            {
                                _style.normal.textColor = client.Color;
                                GUILayout.Label(client.Username, _style);
                            }
                            EditorGUILayout.EndHorizontal();
                        }
                        _style.normal.textColor = defaultColor;
                    }
                    EditorGUILayout.EndScrollView();

                    if (GUILayout.Button(new GUIContent("Leave Server")))
                        SyncManager.DisconnectFromServer();
                }
                EditorGUILayout.Space();
                EditorGUILayout.Space();

                // sync messages
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label($"Sync Messages:");
                GUILayout.FlexibleSpace();
                _isAutoscroll = EditorGUILayout.Toggle(new GUIContent(" ", "Is Autoscrolling Messages"), _isAutoscroll);
                EditorGUILayout.EndHorizontal();
                _messagesViewPos = EditorGUILayout.BeginScrollView(_messagesViewPos,
                    EditorStyles.helpBox, GUILayout.ExpandWidth(true), GUILayout.MaxHeight(200));
				{
                    Color defaultColor = _style.normal.textColor;
                    for (int i = 0; i < SyncManager.SyncMessages.Count; i++)
                    {
                        SyncMessage message = SyncManager.SyncMessages.ElementAt(i);
                        EditorGUILayout.BeginHorizontal(GetScrollviewRowStyle(_scrollViewColors[i % 2]));
                        {
                            switch (message.Severity)
							{
                                case SyncMessageSeverity.Log:
                                    _style.normal.textColor = Color.white;
                                    break;
                                case SyncMessageSeverity.LogWarning:
                                    _style.normal.textColor = Color.yellow;
                                    break;
                                case SyncMessageSeverity.LogError:
                                    _style.normal.textColor = Color.red;
                                    break;
							}
                            GUILayout.Label($"[{message.Timestamp:H:mm:ss}]  {message.Message}", _style);
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                    _style.normal.textColor = defaultColor;
                }
                EditorGUILayout.EndScrollView();

            }
            EditorGUILayout.EndVertical();
        }


        private GUIStyle GetScrollviewRowStyle(Color color)
        {
            _texture.SetPixel(0, 0, color);
            _texture.Apply();
            GUIStyle style = new();
            style.normal.background = _texture;
            style.fixedHeight = ROW_HEIGHT;
            return style;
        }

        private void AddSyncMessage()
		{
            if (_isAutoscroll) 
                _messagesViewPos = new(_messagesViewPos.x, SyncManager.SyncMessages.Count * ROW_HEIGHT);
            Repaint();
        }
    }
}
