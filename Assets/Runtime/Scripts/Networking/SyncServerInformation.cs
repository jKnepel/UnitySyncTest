using System.Net;
using System.Text;
using System.Collections.Concurrent;

namespace jKnepel.UnitySync.Networking
{
    public class SyncServerInformation
    {
        public IPAddress IP { get; private set; }
        public string Servername { get; private set; }
        public byte[] ServernameBytes { get; private set; }
        public int NumberConnectedClients { get; set; }

        public SyncServerInformation(IPAddress ip, string servername)
        {
            IP = ip;
            Servername = servername;
            ServernameBytes = Encoding.ASCII.GetBytes(servername);
            NumberConnectedClients = 0;
        }

        public override bool Equals(object obj)
        {
            if ((obj == null) || !this.GetType().Equals(obj.GetType()))
            {
                return false;
            }
            else
            {
                SyncServerInformation server = (SyncServerInformation)obj;
                return server.IP.Equals(IP) && server.Servername.Equals(Servername);
            }
        }

        public override int GetHashCode()
        {
            return IP.GetHashCode();
        }
    }
}
