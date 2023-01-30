using System;
using System.Linq;
using System.Globalization;
using System.Net.Sockets;
using System.Threading;
using System.Security.Cryptography;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using UnityEngine;
using UnityEditor;

namespace jKnepel.UnitySync
{
    public class SyncClientHandler : IDisposable
    {
        private Socket _client;
        private Thread _senderThread;
        private Thread _receiverThread;

        private readonly ConcurrentQueue<byte[]> _bytesToSend = new();

        private Action<SyncClientHandler, ExceptionDispatchInfo> _connectionAbolished;
        private int _disposeCount;

        #region lifecycle

        public SyncClientHandler(Socket handler, Action<SyncClientHandler, ExceptionDispatchInfo> connectionAbolished)
		{
            _client = handler;
            _connectionAbolished = connectionAbolished;

            _senderThread = new(() => SocketSendThread()) { IsBackground = true };
            _senderThread.Start();
            _receiverThread = new(() => SocketReceiveThread()) { IsBackground = true };
            _receiverThread.Start();

            Debug.Log("Client registered with Server");
        }

		~SyncClientHandler()
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

                    Debug.Log("Clients connection was closed");
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

        #endregion

        #region public methods

        public void AddBytesToSend(byte[] bytes)
        {
            _bytesToSend.Enqueue(bytes);
        }

		#endregion

		#region private methods

		private void SocketSendThread()
		{
            try
            {
                while (IsConnected())
                {
                    if (_bytesToSend.Count == 0) continue;

                    bool success = _bytesToSend.TryDequeue(out byte[] bytes);
                    if (!success) continue;
                    _client.Send(BitConverter.GetBytes((uint)bytes.Length), SocketFlags.None);
                    _client.Send(bytes, SocketFlags.None);
                }
                if (_disposeCount == 0) _connectionAbolished?.Invoke(this, null);
            }
            catch (Exception ex)
            {
                if (_disposeCount == 0) _connectionAbolished?.Invoke(this, ExceptionDispatchInfo.Capture(ex));
            }
        }

        private void SocketReceiveThread()
        {
            
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

        private bool IsConnected()
        {
            try
            {
                return !(_client.Poll(1, SelectMode.SelectRead) && _client.Available == 0);
            }
            catch (SocketException) { return false; }
        }

        #endregion

    }
}
