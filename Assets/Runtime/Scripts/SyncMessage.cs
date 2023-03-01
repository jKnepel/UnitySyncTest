using System;

namespace jKnepel.UnitySync
{
	public struct SyncMessage
	{
		public string Message { get; private set; }
		public SyncMessageSeverity Severity { get; private set; }
		public bool IsDebug { get; private set; }
		public DateTime Timestamp { get; private set; }

		public SyncMessage(string message, SyncMessageSeverity severity, bool isDebug = false)
		{
			Message = message;
			Severity = severity;
			IsDebug = isDebug;
			Timestamp = DateTime.Now;
		}

		public SyncMessage(string message, SyncMessageSeverity severity)
			: this(message, severity, false) { }
		
		public SyncMessage(string message, bool isDebug = false)
			: this(message, SyncMessageSeverity.Log, isDebug) { }
	}
}
