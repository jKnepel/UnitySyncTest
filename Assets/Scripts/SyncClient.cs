using System;
using System.Linq;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using UnityEngine;
using UnityEditor;

namespace jKnepel.UnitySync
{
    public class SyncClient : IDisposable
    {
        private Socket _client;
        private Thread _senderThread;
        private Thread _receiverThread;

        private static readonly Queue<byte[]> _bytesToSend = new();

        #region lifecycle

        public SyncClient(string ip, int port)
        {
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
            
        }

        #endregion

        #region public methods

        public void AddBytesToSend(byte[] bytes)
        {
            lock (_bytesToSend) { _bytesToSend.Enqueue(bytes); }
        }

        #endregion

        #region private methods

        private void SocketSendThread()
        {
            try
            {
                while (true)
                {
                    lock (_bytesToSend)
                    {
                        if (_bytesToSend.Count == 0) continue;

                        byte[] bytes = _bytesToSend.Dequeue();
                        _client.Send(BitConverter.GetBytes((uint)bytes.Length), SocketFlags.None);
                        _client.Send(bytes, SocketFlags.None);
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
                        SyncManager.AddReceivedBytes(receivedBytes);
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
