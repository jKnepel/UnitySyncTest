using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace jKnepel.UnitySync
{
    public class SyncEditorWindow : EditorWindow
    {
        #region

        private Editor _syncSettingsEditor;

        private Vector2 _roomsViewPos;
        private Vector2 _clientsViewPos;
        private Vector2 _messagesViewPos;
        private Color[] _scrollViewColors = new Color[] { new(0.25f, 0.25f, 0.25f), new(0.23f, 0.23f, 0.23f) };

        private readonly List<SyncMessage> _syncMessages = new();
        
        private bool _newRoomOptionsIsVisible = false;
        private string _newRoomName = "New Room";
        private int _newRoomDropdownIndex = 0;
        private bool _isAutoscroll = false;

        private Texture2D _texture;
        private GUIStyle _style = new();

        private const float ROW_HEIGHT = 20;

        #endregion

        [MenuItem("Window/Unity Sync")]
        public static void ShowWindow()
        {
            GetWindow(typeof(SyncEditorWindow), false, "UnitySync");
        }

		public void OnEnable()
		{
            _texture = new(1, 1);
            _syncSettingsEditor = Editor.CreateEditor(SyncSettings.Instance);
            SyncEvents.OnSyncMessageAdded += AddSyncMessage;
		}

		public void CreateGUI()
		{
        }

        private void OnDestroy()
		{
            SyncEvents.OnSyncMessageAdded -= AddSyncMessage;
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

                if (!SyncManager.IsSearchingRooms)
				{
                    GUILayout.Label($"Searching for Rooms is stopped!");
                    if (GUILayout.Button(new GUIContent("Retry Search"), GUILayout.ExpandWidth(false)))
                        SyncManager.RetryRoomDiscovery();
                    EditorGUILayout.EndVertical();
                    return;
				}

                if (!SyncManager.IsConnected)
				{
                    // open rooms list
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label($"Open Rooms: {SyncManager.OpenRooms.Count}");
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(new GUIContent("+", "Create a New Room"), GUILayout.ExpandWidth(false)))
                        _newRoomOptionsIsVisible = !_newRoomOptionsIsVisible;
                    EditorGUILayout.EndHorizontal();
                    _roomsViewPos = EditorGUILayout.BeginScrollView(_roomsViewPos, 
                        EditorStyles.helpBox, GUILayout.ExpandWidth(true), GUILayout.MaxHeight(150));
				    {
                        if (SyncManager.OpenRooms.Count == 0)
                            GUILayout.Label($"No Rooms found!");

                        for (int i = 0; i < SyncManager.OpenRooms.Count; i++)
					    {
                            SyncOpenRoom room = SyncManager.OpenRooms[i];
                            EditorGUILayout.BeginHorizontal(GetScrollviewRowStyle(_scrollViewColors[i % 2]));
                            {
                                GUILayout.Label(room.Roomname);
                                GUILayout.Label($"#{room.ConnectedClients.Values.Count}");
                                if (GUILayout.Button(new GUIContent("Connect To Room"), GUILayout.ExpandWidth(false)))
                                    SyncManager.ConnectToRoom(room.Roomname, SyncSettings.Instance.ImportSceneName);
                            }
                            EditorGUILayout.EndHorizontal();
					    }
				    }
                    EditorGUILayout.EndScrollView();

                    // new room options
                    if (_newRoomOptionsIsVisible)
					{
                        int sceneCount = EditorSceneManager.sceneCount;
                        string[] sceneNames = new string[sceneCount];
                        for (int i = 0; i < sceneCount; i++)
                            sceneNames[i] = EditorSceneManager.GetSceneAt(i).name;
                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
						{
                            GUILayout.Label("Create a New Room", EditorStyles.boldLabel);
                            _newRoomName = EditorGUILayout.TextField("Roomname:", _newRoomName);
                            _newRoomDropdownIndex = EditorGUILayout.Popup("Synced Scene: ", _newRoomDropdownIndex, sceneNames);
                            if (GUILayout.Button(new GUIContent("Create")))
                                SyncManager.ConnectToRoom(_newRoomName, sceneNames[_newRoomDropdownIndex], true);
                        }
                        EditorGUILayout.EndVertical();
                    }
                }
                else
				{
                    // room client list
                    GUILayout.Label($"Current Room: {SyncManager.CurrentRoom.Roomname}");
                    _clientsViewPos = EditorGUILayout.BeginScrollView(_clientsViewPos,
                        EditorStyles.helpBox, GUILayout.ExpandWidth(true), GUILayout.MaxHeight(150));
                    {
                        if (SyncManager.CurrentRoom.ConnectedClients.Count == 0)
                            GUILayout.Label($"There are no Clients in this room!");

						Color defaultColor = _style.normal.textColor;
                        _style.alignment = TextAnchor.MiddleLeft;
						for (int i = 0; i < SyncManager.CurrentRoom.ConnectedClients.Count; i++)
                        {
                            SyncConnectedClient client = SyncManager.CurrentRoom.ConnectedClients.ElementAt(i).Value;
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

                    if (GUILayout.Button(new GUIContent("Leave Room")))
                        SyncManager.DisconnectFromRoom();
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
                    for (int i = 0; i < _syncMessages.Count; i++)
                    {
                        SyncMessage message = _syncMessages.ElementAt(i);
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
                            GUILayout.Label($"[{message.Timestamp.ToString("H:mm:ss")}]  {message.Message}", _style);
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

        private void AddSyncMessage(SyncMessage message)
		{
            _syncMessages.Add(message);
            if (_isAutoscroll) 
                _messagesViewPos = new(_messagesViewPos.x, _syncMessages.Count * ROW_HEIGHT);
            Repaint();
        }
    }
}
