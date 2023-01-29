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

        private static readonly Queue<byte[]> _bytesToSend = new();
        private static readonly Queue<byte[]> _receivedBytes = new();

        private Scene _scene;

        #region lifecycle

        public SyncClient(string ip, int port, Scene scene)
        {
            _scene = scene;

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

        public void Dispose(bool safeToDispose)
        {
            if (safeToDispose)
            {
                try
                {
                    if (_senderThread != null)
                    {
                        _senderThread.Abort();
                    }
                    if (_receiverThread != null)
                    {
                        _receiverThread.Abort();
                    }
                    if (_client != null)
                    {
                        _client.Shutdown(SocketShutdown.Both);
                        _client.Close();
                    }

                    SyncEvents.onIsConnectedChanged?.Invoke(false);
                    Debug.Log("Connection was closed");
                }
                catch (SocketException ex) { Debug.Log(ex); }
            }
        }

        public void Update()
        {
            lock (_receivedBytes)
			{
                while(_receivedBytes.Count > 0)
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
                while (true)
                {
                    byte[] lengthBytes = new byte[4];
                    _client.Receive(lengthBytes, SocketFlags.None);
                    int dataLength = (int)BitConverter.ToUInt32(lengthBytes);

                    if (dataLength <= 0)
                    {
                        if (!IsConnected(_client))
                        {
                            Dispose(true);
                        }
                        continue;
                    }

                    byte[] receivedBytes = new byte[dataLength];
                    _client.Receive(receivedBytes, SocketFlags.None);
                    Debug.Log("Received " + receivedBytes.Length + " Should have been " + dataLength);

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
            }
            catch (Exception ex)
            {
                if (ex is SocketException)
                {
                    Debug.Log("Socket Exception " + ex.ToString());
                    return;
                }
                if (ex is ThreadAbortException) { }

                ExceptionDispatchInfo.Capture(ex).Throw();
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

                //if (_isDebug) Debug.Log($"GameObject '{createdObject.name}' imported.");
            };

            using MemoryStream memoryStream = new();
            await memoryStream.WriteAsync(hierarchy);
            await DeserializeGameObject(_scene, new ImportOptions(), memoryStream, onLoadComplete);

            //if (_isDebug) Debug.Log($"imported:\n{new GLTFParser().ParseToReadableText(hierarchy)}");

            return;
        }

        private async Task DeserializeGameObject(Scene importScene, ImportOptions importOptions, Stream stream, Action<GameObject, ExceptionDispatchInfo> onLoadComplete)
        {
            GLTFParser.ParseJson(stream, out GLTFRoot gLTFRoot, 0);

            GLTFSceneImporterExtended importer = new(gLTFRoot, stream, importOptions);
            await importer.UpdateOrCreateSceneAsync(GLTFSceneHelper.GetSceneRootComponents(importScene), true, -1, true, onLoadComplete);
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

        private static bool IsConnected(Socket socket)
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
