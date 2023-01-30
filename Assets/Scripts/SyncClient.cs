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
    public class SyncClient : IDisposable
    {
        private Socket _client;
        private Thread _senderThread;
        private Thread _receiverThread;

        private readonly Queue<Action> _mainThreadActions = new();
        private readonly Queue<byte[]> _bytesToSend = new();
        private readonly Queue<byte[]> _receivedBytes = new();

        private Scene _scene;

        private Action<ExceptionDispatchInfo> _connectionAbolished;
        private int _disposeCount;

        #region lifecycle

        public SyncClient(string ip, int port, string sceneName, Action<ExceptionDispatchInfo> connectionAbolished)
        {
            try
			{
                _connectionAbolished = connectionAbolished;

                _scene = Application.isEditor
                    ? EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive)
                    : SceneManager.CreateScene(sceneName);
                _scene.name = sceneName;
                SceneManager.SetActiveScene(_scene);

                IPHostEntry host = Dns.GetHostEntry(ip);
                if (ip == "localhost") { host = Dns.GetHostEntry("127.0.01"); }
                IPAddress ipAddress = host.AddressList[0];
                IPEndPoint remoteEP = new(ipAddress, port);

                _client = new(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                _client.Connect(remoteEP);

                _senderThread = new(() => SocketSendThread()) { IsBackground = true };
                _senderThread.Start();
                _receiverThread = new(() => SocketReceiveThread()) { IsBackground = true };
                _receiverThread.Start();
            }
            catch (Exception ex)
            {
                _connectionAbolished?.Invoke(ExceptionDispatchInfo.Capture(ex));
                return;
            }

            EditorApplication.update += Update;
            SyncEvents.onIsConnectedChanged?.Invoke(true);
            Debug.Log("Client connected to Server");
        }

        ~SyncClient()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
        }

        public void Dispose(bool disposing)
        {
            if (Interlocked.Increment(ref _disposeCount) == 1)
            {
                if (disposing)
                {
                    if (_senderThread != null)
                    {
                        _senderThread = null;
                    }
                    if (_receiverThread != null)
                    {
                        _receiverThread = null;
                    }

                    EditorApplication.update -= Update;
                    SyncEvents.onIsConnectedChanged?.Invoke(false);
                    Debug.Log("Connection was closed");
                }

                try
                {
                    if (_client != null)
                    {
                        _client.Shutdown(SocketShutdown.Both);
                        _client.Close();
                    }
                }
                catch (ObjectDisposedException) { }
            }
        }

        public void Update()
        {
            lock (_mainThreadActions)
            {
                while (_mainThreadActions.Count > 0)
                {
                    Action action = _mainThreadActions.Dequeue();
                    action?.Invoke();
                }
            }

            lock (_receivedBytes)
			{
                while(_receivedBytes.Count > 0 && _scene.isLoaded)
				{
                    byte[] bytes = _receivedBytes.Dequeue();
                    ImportHierarchy(bytes);
				}
			}
        }

        #endregion

        #region public methods

        public void AddBytesToSend(byte[] bytes)
        {
            lock (_bytesToSend) { _bytesToSend.Enqueue(bytes); }
        }

        private void AddReceivedBytes(byte[] bytes)
		{
            lock (_receivedBytes) { _receivedBytes.Enqueue(bytes); }
		}

        #endregion

        #region private methods

        private void SocketSendThread()
        {
            
        }

        private void SocketReceiveThread()
        {
            try
            {
                while (IsSocketConnected() && _disposeCount == 0)
                {
                    byte[] lengthBytes = new byte[4];
                    _client.Receive(lengthBytes, SocketFlags.None);
                    int dataLength = (int)BitConverter.ToUInt32(lengthBytes);

                    if (dataLength <= 0) continue;

                    byte[] receivedBytes = new byte[dataLength];
                    _client.Receive(receivedBytes, SocketFlags.None);
                    if (SyncManager.IsDebug) Debug.Log("Received " + receivedBytes.Length + " Should have been " + dataLength);

                    if (receivedBytes.Length == dataLength)
                    {
                        /*
                        string hashedBytes = GetHashedBytes(receivedBytes);
                        _client.Send(ParseStringToByteArray(hashedBytes), SocketFlags.None);
                        Debug.Log("Send Hash: " + hashedBytes);
                        */
                        AddReceivedBytes(receivedBytes);
                    }
                }
                if (_disposeCount == 0) _connectionAbolished?.Invoke(null);
            }
            catch (Exception ex)
            {
                if (_disposeCount == 0) _connectionAbolished?.Invoke(ExceptionDispatchInfo.Capture(ex));
            }
        }

        private async void ImportHierarchy(byte[] hierarchy)
        {
            Action<GameObject, ExceptionDispatchInfo> onLoadComplete = (createdObject, e) =>
            {
                if (createdObject is null)
                {
                    return;
                }
                SceneManager.MoveGameObjectToScene(createdObject, _scene);

                if (SyncManager.IsDebug) Debug.Log($"GameObject '{createdObject.name}' imported.");
            };

            using MemoryStream memoryStream = new();
            await memoryStream.WriteAsync(hierarchy);
            await DeserializeGameObject(_scene, new ImportOptions(), memoryStream, onLoadComplete);

            if (SyncManager.IsDebug) Debug.Log($"imported:\n{new GLTFParser().ParseToReadableText(hierarchy)}");

            return;
        }

        private async Task DeserializeGameObject(Scene importScene, ImportOptions importOptions, Stream stream, Action<GameObject, ExceptionDispatchInfo> onLoadComplete)
        {
            GLTFParser.ParseJson(stream, out GLTFRoot gLTFRoot, 0);

            GLTFSceneImporterExtended importer = new(gLTFRoot, stream, importOptions);
            await importer.UpdateOrCreateSceneAsync(GLTFSceneHelper.GetSceneRootComponents(importScene), SyncManager.IsDebug, -1, true, onLoadComplete);
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

        private bool IsSocketConnected()
        {
            try
            {
                return !(_client.Poll(1, SelectMode.SelectRead) && _client.Available == 0);
            }
            catch (Exception) { return false; }
        }

        #endregion

    }
}
