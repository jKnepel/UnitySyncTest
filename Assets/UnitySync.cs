using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using UnityEngine;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEditor;
using System.Text.RegularExpressions;
using GLTF;
using GLTF.Schema;
using UnityGLTF;
using Toph.GLTFSerializer.Components;



public class UnitySync : EditorWindow
{
    #region public members

    public static string ip = "127.0.0.1";
    public static string port = "8080";
    public static string exportSceneName = "ExportScene";
    public static string importSceneName = "ImportScene";

    public static bool debug = false;

    #endregion

    #region private members

    private static TcpListener tcpListener;
    private static Thread tcpListenerThread;
    private static TcpClient socketConnection;

    #endregion

    #region thread handlers

    private static bool shouldRun;
    private static readonly object _lock = new object();
    private static readonly Queue<Action> _mainThreadActions = new Queue<Action>();

    #endregion

    [MenuItem("Window/Unity Sync")]
    public static void ShowWindow()
    {
        EditorWindow.GetWindow(typeof(UnitySync));
    }

	public void Update()
	{
        lock (_lock)
        {
            while (_mainThreadActions.Count > 0)
            {
                var action = _mainThreadActions.Dequeue();
                action?.Invoke();
            }
        }
    }

	void OnGUI()
    {
        GUILayout.Label("Unity Sync", EditorStyles.boldLabel);
        GUIStyle myStyle = new GUIStyle(GUI.skin.textField);
        myStyle.alignment = TextAnchor.MiddleLeft;

        GUI.Label(new Rect(5, 30, 100, 20), "IP:");
        ip = GUI.TextField(new Rect(30, 30, 100, 20), ip, myStyle);
        GUI.Label(new Rect(140, 30, 100, 20), "Port:");
        port = GUI.TextField(new Rect(180, 30, 60, 20), port, myStyle);
        port = Regex.Replace(port, @"[^a-zA-Z0-9 ]", "");

        if (GUI.Button(new Rect(5, 60, 100, 30), new GUIContent("Open Socket", "Open Socket")))
        {
            OpenTCPSocket();
        }

        if (GUI.Button(new Rect(115, 60, 100, 30), new GUIContent("Close Socket", "Close Socket")))
        {
            CloseTCPSocket();
        }

        exportSceneName = GUI.TextField(new Rect(5, 100, 200, 30), exportSceneName, myStyle);
        if (GUI.Button(new Rect(215, 100, 100, 30), new GUIContent("Export Scene", "Export Scene")))
        {
            ExportScene();
        }

        importSceneName = GUI.TextField(new Rect(5, 140, 200, 30), importSceneName, myStyle);

    }

    private static void OpenTCPSocket()
    {
        shouldRun = true;
        tcpListenerThread = new Thread(new ThreadStart(ListenForIncommingRequests));
        tcpListenerThread.IsBackground = true;
        tcpListenerThread.Start();
    }

    private static void CloseTCPSocket()
    {
        shouldRun = false;
        tcpListener.Server.Close();
    }

    private static void ListenForIncommingRequests()
    {
        try
        {
            tcpListener = new TcpListener(IPAddress.Parse(ip), int.Parse(port));
            tcpListener.Start();
            Debug.Log("Server is listening");
            List<byte[]> allBytes = new List<byte[]>();
            while (shouldRun)
            {
				using TcpClient connectedTcpClient = tcpListener.AcceptTcpClient();
				using NetworkStream stream = connectedTcpClient.GetStream();

                bool shouldImport = false;
                byte[] lengthBytes = new byte[4];
				int length;
				while ((length = stream.Read(lengthBytes, 0, lengthBytes.Length)) != 0)
				{
                    shouldImport = true;
                    byte[] data = new byte[BitConverter.ToUInt32(lengthBytes)];
                    int dataLength = stream.Read(data, 0, data.Length);
                    allBytes.Add(data);
                    Debug.Log("Received " + data.Length);
				}
                if (shouldImport)
				{
                    _mainThreadActions.Enqueue(() => ImportScene(allBytes));
				}
            }
        }
        catch (SocketException socketException)
        {
            Debug.Log("Socket Exception " + socketException.ToString());
        }
    }

    private async static void ExportScene()
    {
        var exportScene = SceneManager.GetSceneByName(exportSceneName);
        if (exportScene.IsValid() is false || exportScene.isLoaded is false)
        {
            Debug.LogError($"Export scene with name '{exportSceneName}' can not be found or is not valid!");
            return;
        }

        var rootGameObjects = GLTFUnitySceneHelper.GetSceneRootGameObjects(exportScene).ToList();
        var rootComponents = GLTFUnitySceneHelper.AddOrGetSceneRootComponents(rootGameObjects);
        rootComponents.ForEach(r => r.Init());

        var byteArrays = new List<byte[]>();
        foreach (var rootComponent in rootComponents)
        {
            var byteArray = await rootComponent.SerializeGameObject(new ExportOptions());
            byteArrays.Add(byteArray);

            if (debug) Debug.Log($"GameObject '{rootComponent.name}' exported:\n{new GLTFParser().ParseToReadableText(byteArray)}");
        }

        try
        {
            socketConnection = new TcpClient(ip, int.Parse(port));
            if (socketConnection == null)
			{
                Debug.Log("Socket Exception: couldn't connect to server");
                return;
			}

            try
            {
                NetworkStream stream = socketConnection.GetStream();
                if (stream.CanWrite)
                {
                    foreach (byte[] bytes in byteArrays)
					{
                        stream.Write(BitConverter.GetBytes((uint)bytes.Length), 0, 4);
                        stream.Write(bytes, 0, bytes.Length);
                        Debug.Log("Send " + bytes.Length);
					}
                }
                socketConnection.Close();
            }
            catch (SocketException socketException)
            {
                Debug.Log("Socket Exception: " + socketException);
            }
        }
        catch (SocketException socketException)
        {
            Debug.Log("Socket Exception: " + socketException);
        }

        if (debug) Debug.Log($"Scene '{exportScene.name}' exported.");
    }

    private async static void ImportScene(List<byte[]> scene)
	{
        var importScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        importScene.name = importSceneName;
        Action<GameObject, ExceptionDispatchInfo> onLoadComplete = (createdObject, e) =>
        {
            if (createdObject is null)
            {
                return;
            }
            SceneManager.MoveGameObjectToScene(createdObject, importScene);

            if (debug) Debug.Log($"GameObject '{createdObject.name}' imported.");
        };

        foreach (var byteArray in scene)
        {
            using (var memoryStream = new MemoryStream())
            {
                await memoryStream.WriteAsync(byteArray);
                await DeserializeGameObject(importScene, new ImportOptions(), memoryStream, onLoadComplete);
            }
        }
        if (debug) Debug.Log($"Scene '{importScene.name}' imported.");
    }

    private async static Task DeserializeGameObject(Scene importScene, ImportOptions importOptions, Stream stream,
        Action<GameObject, ExceptionDispatchInfo> onLoadComplete)
    {
        GLTFParser.ParseJson(stream, out GLTFRoot gLTFRoot, 0);

        var importer = new GLTFSceneImporterExtended(gLTFRoot, stream, importOptions);
        await importer.UpdateOrCreateSceneAsync(GLTFUnitySceneHelper.GetSceneRootComponents(importScene), debug, -1, true, onLoadComplete); // ToDo get progress using IProgress
    }
}
