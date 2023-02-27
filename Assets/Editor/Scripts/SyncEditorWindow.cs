using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace jKnepel.UnitySync
{
    public class SyncEditorWindow : EditorWindow
    {
        #region

        private bool _userSettingsIsVisible = true;
        private bool _newRoomOptionsIsVisible = false;
        private string _newRoomName = "New Room";
        private int _newRoomDropdownIndex = 0;
        
        private Vector2 _roomsViewPos;
        private Vector2 _clientsViewPos;
        private Color[] _scrollViewColors = new Color[] { new(0.15f, 0.15f, 0.15f), new(0.2f, 0.2f, 0.2f) };
        
        private static Texture2D _texture;
        private static GUIStyle _style = new();

        #endregion

        [MenuItem("Window/Unity Sync")]
        public static void ShowWindow()
        {
            GetWindow(typeof(SyncEditorWindow), false, "UnitySync");
        }

		public void OnEnable()
		{
            _texture = new(1, 1);
		}

		public void CreateGUI()
		{
        }

        private void OnDestroy()
		{
        }

		void OnGUI()
        {
            EditorGUILayout.BeginVertical(EditorStyles.inspectorDefaultMargins);
			{
                GUILayout.Label("UnitySync", EditorStyles.largeLabel);

                // user settings
                // TODO : replace with custom editor for SyncSettings
                _userSettingsIsVisible = EditorGUILayout.BeginFoldoutHeaderGroup(_userSettingsIsVisible, "User Settings", 
                    EditorStyles.foldoutHeader);
                if (_userSettingsIsVisible)
				{
                    EditorGUI.indentLevel++;
                    SyncSettings.Instance.Port = EditorGUILayout.IntField("Port:", SyncSettings.Instance.Port);
                    SyncSettings.Instance.ImportSceneName = EditorGUILayout.TextField("ImportScene Name:", 
                        SyncSettings.Instance.ImportSceneName);
                    SyncSettings.Instance.Username = EditorGUILayout.TextField("Username:", SyncSettings.Instance.Username);
                    SyncSettings.Instance.Color = EditorGUILayout.ColorField("Color:", SyncSettings.Instance.Color);
                    EditorGUI.indentLevel--;
				}
                EditorGUILayout.EndFoldoutHeaderGroup();

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
                    GUILayout.Label($"Open Rooms: {SyncManager.OpenRooms.Count}");
                    _roomsViewPos = EditorGUILayout.BeginScrollView(_roomsViewPos, 
                        EditorStyles.helpBox, GUILayout.ExpandWidth(true), GUILayout.MaxHeight(150));
				    {
                        if (SyncManager.OpenRooms.Count == 0)
                            GUILayout.Label($"No Rooms found!");

                        for (int i = 0; i < SyncManager.OpenRooms.Count; i++)
					    {
                            SyncOpenRoom room = SyncManager.OpenRooms[i];
                            EditorGUILayout.BeginHorizontal(GetBackgroundColorStyle(_scrollViewColors[i % 2]));
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
                    if (GUILayout.Button(new GUIContent("+", "Create a New Room"), GUILayout.ExpandWidth(false)))
                        _newRoomOptionsIsVisible = !_newRoomOptionsIsVisible;
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
                        for (int i = 0; i < SyncManager.CurrentRoom.ConnectedClients.Count; i++)
                        {
                            SyncConnectedClient client = SyncManager.CurrentRoom.ConnectedClients.ElementAt(i).Value;
                            EditorGUILayout.BeginHorizontal(GetBackgroundColorStyle(_scrollViewColors[i % 2]));
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
			}
            EditorGUILayout.EndVertical();
        }


        private static GUIStyle GetBackgroundColorStyle(Color color)
        {
            _texture.SetPixel(0, 0, color);
            _texture.Apply();
            GUIStyle style = new();
            style.normal.background = _texture;
            return style;
        }
    }
}
