using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using GLTF;
using GLTF.Schema;
using UnityGLTF;
using Toph.GLTFSerializer.Components;

namespace jKnepel.UnitySync
{
    [InitializeOnLoad()]
    public static class SyncManager
    {
        #region public members

        public static string Ip { get; private set; }
        public static int Port { get; private set; }
        public static string ExportSceneName { get; private set; }
        public static string ImportSceneName { get; private set; }
        public static bool IsDebug { get; private set; }

        #endregion

        #region private members

        private static Scene _importScene;

        #endregion

        #region thread handlers

        private static Socket _listener;
        private static Socket _client;
        private static List<SocketConnection> _connectedClients = new();
        private static int _maximumNumberConnections = 10;
        private static Thread _listenerThread;
        private static readonly object _lock = new();
        private static readonly Queue<Action> _mainThreadActions = new();

		#endregion

		#region unity cycle

        static SyncManager()
		{
            IsDebug = true;
            SyncEvents.onIpChanged += (string value) => Ip = value;
            SyncEvents.onPortChanged += (int value) => Port = value;
            SyncEvents.onExportSceneNameChanged += (string value) => ExportSceneName = value;
            SyncEvents.onImportSceneNameChanged += (string value) => ImportSceneName = value;

            SyncEvents.onOpenTCPSocket += OpenTCPSocket;
            SyncEvents.onCloseTCPSocket += CloseTCPSocket;
            SyncEvents.onConnectClient += ConnectClient;
            SyncEvents.onCloseClient += CloseClient;
            
            SyncEvents.onExportScene += ExportScene;

            EditorApplication.update += Update;
        }

        private static void Update()
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

        private static void AddMainThreadAction(Action action)
        {
            _mainThreadActions.Enqueue(action);
        }

		#endregion

		#region sync manager

		#region server logic

		private static void OpenTCPSocket()
        {
            IPHostEntry host = Dns.GetHostEntry(Ip);
            IPAddress ipAddress = host.AddressList[0];
            IPEndPoint localEndPoint = new(ipAddress, Port);

            _listener = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            _listener.Bind(localEndPoint);
            _listener.Listen(_maximumNumberConnections);
            _listener.Blocking = true;
            SyncEvents.onIsOpenChanged?.Invoke(true);

            Debug.Log("Server is listening");

            _listenerThread = new(() => ListenForIncomingConnections()) { IsBackground = true };
            _listenerThread.Start();
        }

        private static void CloseTCPSocket()
        {
            try
			{
                lock (_connectedClients)
				{
                    for (int i = _connectedClients.Count - 1; i >= 0; i--)
				    {
                        _connectedClients.ElementAt(i).HandlerThread.Abort();
                        _connectedClients.RemoveAt(i);
                    }
				}

                if (_listener != null)
                {
                    _listenerThread.Abort();
                    _listener.Close();
                    SyncEvents.onIsOpenChanged?.Invoke(false);
                }
                Debug.Log("Server was closed");
            }
            catch (SocketException ex) { Debug.Log(ex); }
        }

        private static void ListenForIncomingConnections()
        {
            try
            {
                while (true)
                {
                    Socket handler = _listener.Accept();
                    handler.Blocking = true;

                    SocketConnection connection = new(handler);
                    Thread handlerThread = new(() => HandleSocketConnection(connection)) { IsBackground = true };
                    connection.HandlerThread = handlerThread;
                    lock (_connectedClients) { _connectedClients.Add(connection); }
                    handlerThread.Start();
                }
            }
            catch (SocketException ex)
            {
                Debug.Log("Socket Exception " + ex);
            }
        }

        private static void HandleSocketConnection(SocketConnection connection)
        {
            Socket handler = connection.Socket;

            try
            {
                while (true)
                {
                    byte[] lengthBytes = new byte[4];
                    handler.Receive(lengthBytes, SocketFlags.None);
                    int dataLength = (int)BitConverter.ToUInt32(lengthBytes);

                    if (dataLength <= 0)
                    {
                        if (!IsConnected(handler))
                        {
                            connection.HandlerThread.Abort();
                        }
                        continue;
                    }

                    byte[] receivedBytes = new byte[dataLength];
                    handler.Receive(receivedBytes, SocketFlags.None);
                    Debug.Log("Received " + receivedBytes.Length + " Should have been " + dataLength);

                    if (receivedBytes.Length == dataLength)
                    {
                        string hashedBytes = GetHashedBytes(receivedBytes);
                        handler.Send(ParseStringToByteArray(hashedBytes), SocketFlags.None);
                        Debug.Log("Send Hash: " + hashedBytes);

                        AddMainThreadAction(() => ImportScene(receivedBytes));
                    }
                }
            }
            catch (SocketException ex)
            {
                Debug.Log("Socket Exception " + ex.ToString());
            }
            catch (ThreadAbortException)
            {
                if (handler != null)
                {
                    handler.Shutdown(SocketShutdown.Both);
                    handler.Close();
                }
                Debug.Log("Connection was closed");
            }
        }

        #endregion

        #region client logic

        private static void ConnectClient()
		{
            try
			{
                IPHostEntry host = Dns.GetHostEntry(Ip);
                IPAddress ipAddress = host.AddressList[0];
                IPEndPoint remoteEP = new(ipAddress, Port);

                _client = new(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                _client.Connect(remoteEP);
                SyncEvents.onIsConnectedChanged?.Invoke(true);
			} catch (SocketException ex)
			{
                Debug.Log("Socket Exception " + ex);
            }
        }

        private static void CloseClient()
		{
            _client.Shutdown(SocketShutdown.Both);
            _client.Close();
            SyncEvents.onIsConnectedChanged?.Invoke(false);
        }

        private static void SendRequest(List<byte[]> byteArrays)
        {
            try
            {
                foreach (byte[] bytes in byteArrays)
                {
                    _client.Send(BitConverter.GetBytes((uint)bytes.Length), SocketFlags.None);
                    _client.Send(bytes, SocketFlags.None);
                    /*
                    byte[] receivedHashedBytes = new byte[20];
                    Debug.Log("1");
                    sender.Receive(receivedHashedBytes, SocketFlags.None);
                    Debug.Log("2");
                    if (ByteArrayCompare(receivedHashedBytes, GetHashedBytes(bytes)))
                    {
                        Debug.Log("Hashes are not equal");
					}
                    */

                    Debug.Log("Send " + bytes.Length);
                }
            }
            catch (SocketException ex)
            {
                Debug.Log("Socket Exception: " + ex);
            }
        }

		#endregion

		#endregion

		#region synchronizer

		private async static void ExportScene()
        {
            var exportScene = SceneManager.GetSceneByName(ExportSceneName);
            if (exportScene.IsValid() is false || exportScene.isLoaded is false)
            {
				Debug.LogError($"Export scene with name '{ExportSceneName}' can not be found or is not valid!");
                return;
            }

            var rootGameObjects = GLTFSceneHelper.GetSceneRootGameObjects(exportScene).ToList();
            var rootComponents = GLTFSceneHelper.AddOrGetSceneRootComponents(rootGameObjects);
            rootComponents.ForEach(r => r.Init());

            var byteArrays = new List<byte[]>();
            foreach (var rootComponent in rootComponents)
            {
                var byteArray = await rootComponent.SerializeGameObject(new ExportOptions());
                byteArrays.Add(byteArray);

                if (IsDebug) Debug.Log($"GameObject '{rootComponent.name}' exported:\n{new GLTFParser().ParseToReadableText(byteArray)}");
            }

            SendRequest(byteArrays);
        }

        private async static void ImportScene(byte[] scene)
        {
            if (_importScene.IsValid() is false || _importScene.isLoaded is false)
			{
                _importScene = SceneManager.GetSceneByName(ImportSceneName);
                if (_importScene.IsValid() is false || _importScene.isLoaded is false)
				{
                    _importScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                    _importScene.name = ImportSceneName;
				}
				SceneManager.SetActiveScene(_importScene);
			}

            Action<GameObject, ExceptionDispatchInfo> onLoadComplete = (createdObject, e) =>
            {
                if (createdObject is null)
                {
                    return;
                }
                SceneManager.MoveGameObjectToScene(createdObject, _importScene);

                if (IsDebug) Debug.Log($"GameObject '{createdObject.name}' imported.");
            };

            using (var memoryStream = new MemoryStream())
            {
                if (IsDebug) Debug.Log($"imported:\n{new GLTFParser().ParseToReadableText(scene)}");
                await memoryStream.WriteAsync(scene);
                await DeserializeGameObject(_importScene, new ImportOptions(), memoryStream, onLoadComplete);

            }
            if (IsDebug) Debug.Log($"Scene '{_importScene.name}' imported.");

            return;
        }

        private async static Task DeserializeGameObject(Scene importScene, ImportOptions importOptions, Stream stream,
            Action<GameObject, ExceptionDispatchInfo> onLoadComplete)
        {
            GLTFParser.ParseJson(stream, out GLTFRoot gLTFRoot, 0);

            var importer = new GLTFSceneImporterExtended(gLTFRoot, stream, importOptions);
            await importer.UpdateOrCreateSceneAsync(GLTFSceneHelper.GetSceneRootComponents(importScene), IsDebug, -1, true, onLoadComplete);
        }

		#endregion

		#region helper methods

		private static string GetHashedBytes(byte[] bytes)
		{
			using var sha1 = new SHA1CryptoServiceProvider();
			string hashedBytes = string.Concat(sha1.ComputeHash(bytes).Select(x => x.ToString("X2")));
            return hashedBytes;
        }

        private static byte[] ParseStringToByteArray(string bytes)
		{
            byte[] arr = new byte[bytes.Length / 2];
            for (int index = 0; index < arr.Length; index++)
            {
                string byteValue = bytes.Substring(index * 2, 2);
                arr[index] = byte.Parse(byteValue, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }

            return arr;
        }

        private static bool ByteArrayCompare(ReadOnlySpan<byte> a1, ReadOnlySpan<byte> a2)
        {
            return a1.SequenceEqual(a2);
        }

        private static bool IsConnected(this Socket socket)
        {
            try
            {
                return !(socket.Poll(1, SelectMode.SelectRead) && socket.Available == 0);
            }
            catch (SocketException) { return false; }
        }

        #endregion
    }
}
