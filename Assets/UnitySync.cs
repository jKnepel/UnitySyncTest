using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEditor;
using System.Text.RegularExpressions;

public class UnitySync : EditorWindow
{
    #region public members

    public static string ip = "127.0.0.1";
    public static string port = "8080";
    public static string textMessage = "This is a message";

    #endregion

    #region private members

    private static TcpListener tcpListener;
    private static Thread tcpListenerThread;
    private static TcpClient connectedTcpClient;
    private static TcpClient socketConnection;
    private static bool shouldRun;

    #endregion

    [MenuItem("Window/Unity Sync")]
    public static void ShowWindow()
    {
        EditorWindow.GetWindow(typeof(UnitySync));
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

        textMessage = GUI.TextField(new Rect(5, 100, 200, 30), textMessage, myStyle);
        if (GUI.Button(new Rect(215, 100, 100, 30), new GUIContent("Send Message", "Send Message")))
        {
            SendMessage();
        }
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
            Byte[] bytes = new Byte[1024];
            while (shouldRun)
            {
                using (connectedTcpClient = tcpListener.AcceptTcpClient())
                {
                    using (NetworkStream stream = connectedTcpClient.GetStream())
                    {
                        int length;			
                        while ((length = stream.Read(bytes, 0, bytes.Length)) != 0)
                        {
                            byte[] incommingData = new byte[length];
                            Array.Copy(bytes, 0, incommingData, 0, length);
                            string clientMessage = Encoding.ASCII.GetString(incommingData);
                            Debug.Log("client message received: " + clientMessage);
                        }
                    }
                }
            }
        }
        catch (SocketException socketException)
        {
            Debug.Log("Socket Exception " + socketException.ToString());
        }
    }

    private static void SendMessage()
    {
        try
        {
            socketConnection = new TcpClient(ip, int.Parse(port));
            if (socketConnection == null)
                return;

            try
            {
                NetworkStream stream = socketConnection.GetStream();
                if (stream.CanWrite)
                {
                    string clientMessage = textMessage;
                    byte[] clientMessageAsByteArray = Encoding.ASCII.GetBytes(clientMessage);        
                    stream.Write(clientMessageAsByteArray, 0, clientMessageAsByteArray.Length);
                    Debug.Log("Message send by Client");
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
    }
}
