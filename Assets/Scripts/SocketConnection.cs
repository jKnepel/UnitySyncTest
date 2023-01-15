using System;
using System.Threading;
using System.Net.Sockets;

namespace jKnepel.UnitySync
{
    public struct SocketConnection
    {
        public Socket Socket;
        public Thread HandlerThread;
        public string Name;
        public string Ip;

        public SocketConnection(Socket socket)
		{
            Socket = socket;
            HandlerThread = null;
            Name = "";
            Ip = "";
		}
    }
}
