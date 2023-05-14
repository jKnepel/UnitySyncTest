using jKnepel.UnitySync.Networking;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace jKnepel.UnitySync.SceneSync
{
    internal class ModuledEditorEditorWindow : EditorWindow
    {
        #region private members

        private static ModuledNetSettings _settings;
        private Editor _syncSettingsEditor;

        private Vector2 _serversViewPos;
        private Vector2 _clientsViewPos;
        private Vector2 _messagesViewPos;
        private Color[] _scrollViewColors = new Color[] { new(0.25f, 0.25f, 0.25f), new(0.23f, 0.23f, 0.23f) };
        
        private bool _isAutoscroll = false;

        private bool    _newServerOptionsIsVisible = true;
        private string  _newServerName = "New Server";
        private int     _newServerSceneIndex = 0;

        private Texture2D _texture;
        private GUIStyle _style = new();

        private const float ROW_HEIGHT = 20;

        #endregion

        [MenuItem("Window/ModuledNet/ModuledNet Manager")]
        public static void ShowWindow()
        {
            GetWindow(typeof(ModuledEditorEditorWindow), false, "ModuledNet Manager");
        }

		public void OnEnable()
		{
            _texture = new(1, 1);
            _settings = ModuledNetSettings.GetOrCreateSettings();
            _syncSettingsEditor = Editor.CreateEditor(_settings);
            ModuledNetManager.OnSyncMessageAdded += AddSyncMessage;
            ModuledNetManager.OnServerListChanged += Repaint;
        }

		public void CreateGUI()
		{
        }

        private void OnDestroy()
		{
            ModuledNetManager.OnSyncMessageAdded -= AddSyncMessage;
            ModuledNetManager.OnServerListChanged -= Repaint;
        }

        // TODO : add descriptions to labels, was too lazy
        void OnGUI()
        {
            EditorGUILayout.BeginVertical(EditorStyles.inspectorDefaultMargins);
			{
                GUILayout.Label("UnitySync", EditorStyles.largeLabel);

                // user settings
                _syncSettingsEditor.OnInspectorGUI();
                EditorGUILayout.Space();
                EditorGUILayout.Space();

                if (!ModuledNetManager.IsServerDiscoveryActive)
				{
                    GUILayout.Label($"Client has stopped working!");
                    if (GUILayout.Button(new GUIContent("Reset Client"), GUILayout.ExpandWidth(false)))
					{
                        ModuledNetManager.ResetClient();
                        Repaint();
					}
				}

                if (GUILayout.Button("Reset Client"))
                {
                    ModuledNetManager.ResetClient();
                    Repaint();
                }

                if (!ModuledNetManager.IsConnected)
                {
                    // open servers list
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label($"Open Servers: {ModuledNetManager.OpenServers?.Count}");
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(new GUIContent(_newServerOptionsIsVisible ? "-" : "+", "Create a New Server"),
                        GUILayout.Width(20), GUILayout.Height(20)))
                        _newServerOptionsIsVisible = !_newServerOptionsIsVisible;
                    EditorGUILayout.EndHorizontal();
                    _serversViewPos = EditorGUILayout.BeginScrollView(_serversViewPos,
                        EditorStyles.helpBox, GUILayout.ExpandWidth(true), GUILayout.MaxHeight(150));
                    {
                        if (ModuledNetManager.OpenServers?.Count == 0)
                            GUILayout.Label($"No Servers found!");

                        for (int i = 0; i < ModuledNetManager.OpenServers?.Count; i++)
                        {
                            ServerInformation server = ModuledNetManager.OpenServers[i];
                            EditorGUILayout.BeginHorizontal(GetScrollviewRowStyle(_scrollViewColors[i % 2]));
                            {
                                GUILayout.Label(server.Servername);
                                GUILayout.Label($"#{server.NumberConnectedClients}/{server.MaxNumberConnectedClients}");
                                if (GUILayout.Button(new GUIContent("Connect To Server"), GUILayout.ExpandWidth(false)))
                                    ModuledNetManager.ConnectToServer(server.IP);
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
                            _newServerSceneIndex = EditorGUILayout.Popup("Synced Scene: ", _newServerSceneIndex, sceneNames);
                            _settings.MaxNumberClients = (byte)EditorGUILayout.IntField("Server Discovery Port:", _settings.MaxNumberClients);
                            if (GUILayout.Button(new GUIContent("Create")))
                                ModuledNetManager.CreateServer(_newServerName);
                        }
                        EditorGUILayout.EndVertical();
                    }
                }
                else
                {
                    // server client list
                    GUILayout.Label($"Current Server: {ModuledNetManager.CurrentServer.Servername}");
                    _clientsViewPos = EditorGUILayout.BeginScrollView(_clientsViewPos,
                        EditorStyles.helpBox, GUILayout.ExpandWidth(true), GUILayout.MaxHeight(150));
                    {
                        if (ModuledNetManager.ConnectedClients.Count == 0)
                            GUILayout.Label($"There are no Clients in this server!");

                        Color defaultColor = _style.normal.textColor;
                        _style.alignment = TextAnchor.MiddleLeft;
                        for (int i = 0; i < ModuledNetManager.CurrentServer.NumberConnectedClients; i++)
                        {
                            ClientInformation client = ModuledNetManager.ConnectedClients.ElementAt(i);
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
                        ModuledNetManager.DisconnectFromServer();
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
                    for (int i = 0; i < ModuledNetManager.SyncMessages.Count; i++)
                    {
                        ModuledNetMessage message = ModuledNetManager.SyncMessages.ElementAt(i);
                        EditorGUILayout.BeginHorizontal(GetScrollviewRowStyle(_scrollViewColors[i % 2]));
                        {
                            switch (message.Severity)
							{
                                case ModuledNetMessageSeverity.Log:
                                    _style.normal.textColor = Color.white;
                                    break;
                                case ModuledNetMessageSeverity.LogWarning:
                                    _style.normal.textColor = Color.yellow;
                                    break;
                                case ModuledNetMessageSeverity.LogError:
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
                _messagesViewPos = new(_messagesViewPos.x, ModuledNetManager.SyncMessages.Count * ROW_HEIGHT);
            Repaint();
        }
    }
}
