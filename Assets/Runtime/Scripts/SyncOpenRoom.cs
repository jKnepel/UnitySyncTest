using System;
using System.Text;
using System.Collections.Concurrent;

namespace jKnepel.UnitySync
{
    public class SyncOpenRoom
    {
        public string Roomname { get; private set; }
        public byte[] RoomnameBytes { get; private set; }
        public ConcurrentDictionary<string, SyncConnectedClient> ConnectedClients { get; private set; }
        public DateTime LastHeartbeat { get; set; }

        public SyncOpenRoom(string roomname)
		{
            Roomname = roomname;
            RoomnameBytes = Encoding.ASCII.GetBytes(roomname);
            ConnectedClients = new();
            LastHeartbeat = DateTime.Now;
		}

		public override bool Equals(object obj)
		{
            if ((obj == null) || !this.GetType().Equals(obj.GetType()))
            {
                return false;
            }
            else
			{
                SyncOpenRoom room = (SyncOpenRoom)obj;
                return room.Roomname.Equals(Roomname);
			}
        }

		public override int GetHashCode()
		{
			return Roomname.GetHashCode();
		}
	}
}
