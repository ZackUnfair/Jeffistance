using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Net;
using System.Threading;
using System.Runtime.Serialization.Formatters.Binary;

namespace Jeffistance.Services
{
    public abstract class ConnectionTcp
    {
        public readonly int PORT_NO;
        public readonly string SERVER_IP;

        public bool IsLocalConnection;

        public CancellationTokenSource CancellationSource;

        public void Stop()
        {
            CancellationSource.Cancel();
        }

        protected ConnectionTcp(string ip, int port)
        {
            PORT_NO = port;
            SERVER_IP = ip;
        }
    }

    public class ServerConnection:ConnectionTcp
    {
        public static implicit operator TcpListener(ServerConnection s) => s.Listener;  // No reason for this, I just thought it was epic
        TcpListener Listener;
        public bool Listening;
        public delegate void ConnectionHandler(object obj, ConnectionArgs args);
        public delegate void MessageReceivedHandler(object obj, MessageReceivedArgs args);
        public event ConnectionHandler OnConnection;
        public event MessageReceivedHandler OnMessageReceived;

        public ClientConnection LatestClient
        {
            set
            {
                ConnectionArgs args = new ConnectionArgs(value);
                OnConnection(this, args);
            }
        }
        List<ClientConnection> Clients = new List<ClientConnection>();

        public ServerConnection(int port):base(NetworkUtilities.GetLocalIPAddress(), port)
        {
            IsLocalConnection = true;
            Listener = new TcpListener(IPAddress.Parse(SERVER_IP), PORT_NO);
        }

        public void Run()
        {
            Listener.Start();
            ListenForConnections();
        }

        private void StopListening()
        {
            Listener.Stop();
            Listening = false;
        }

        public async void ListenForConnections()
        {
            ClientConnection newClient;
            CancellationSource = new CancellationTokenSource();
            Listening = true;
            Console.WriteLine("Listening...");
            using(CancellationSource.Token.Register(StopListening))
            {
                while(Listening)
                {
                    try
                    {
                        newClient = ProcessClient(await AcceptClient(CancellationSource.Token));
                        LatestClient = newClient;
                    }
                    catch(NullReferenceException)
                    {
                        continue;
                    }
                    ListenToClient(newClient);
                }
            }
        }

        private void ListenToClient(ClientConnection client)
        {
            Clients.Add(client);
            Task.Run(() => ListenForMessages(client));
        }

        public async Task<TcpClient> AcceptClient(CancellationToken token)
        {
            try
            {
                TcpClient client = await Listener.AcceptTcpClientAsync(); 
                return client;
            }
            catch(ObjectDisposedException e)
            {
                if(token.IsCancellationRequested)
                {
                    return null;
                }
                throw e;
            }
        }

        private ClientConnection ProcessClient(TcpClient client)
        {
            ClientConnection clientConnection = new ClientConnection(client, SERVER_IP, PORT_NO);
            Console.WriteLine(String.Format("New connection: {0}", client.Client.RemoteEndPoint));
            return clientConnection;
        }
        public void ListenForMessages(ClientConnection client)
        {
            string clientIP = client.IPAddress;
            object message = "";
            while (true)
            {
                try
                {
                    message = ReceiveMessage(client);
                    OnMessageReceived(this, new MessageReceivedArgs(message));
                }
                catch (Exception e) when (e is System.IO.IOException || e is InvalidOperationException)
                {   
                    message = clientIP + " has disconnected.";
                    Clients.Remove(client);
                    if(!(e is InvalidOperationException))
                    {
                        ((TcpClient) client).Close();
                    }
                    break;
                }
            }
        }

        public object ReceiveMessage(ClientConnection client)
        {
            object dataReceived = NetworkUtilities.Receive(client);
            if(dataReceived == "")
            {
                throw new System.InvalidOperationException();
            }
            return dataReceived;
        }

        public void Broadcast(string message)
        {
            foreach (ClientConnection client in this.Clients.ToArray())
            {
                NetworkUtilities.Send(message, client);
            }
        }

        public void Kick(ClientConnection client)
        {
            client.Stop();
        }
    }

    public class ClientConnection:ConnectionTcp
    {
        public static implicit operator TcpClient(ClientConnection c) => c.Client; // No reason for this, I just thought it was epic

        private TcpClient Client;
        public bool Connected;

        public new bool IsLocalConnection()
        {
            return IPAddress == NetworkUtilities.GetLocalIPAddress();
        }

        public string IPAddress
        {
            get
            {
                return Client.Client.RemoteEndPoint.ToString().Split(':')[0];
            }
        }

        public int Port
        {
            get
            {
                return Int32.Parse(Client.Client.RemoteEndPoint.ToString().Split(':')[1]);
            }
        }

        public ClientConnection(string ip, int port):base(ip, port)
        {
            Client = new TcpClient(SERVER_IP, PORT_NO);
            Connected = true;
            Task.Run(() => ListenForMessages());
        }

        public ClientConnection(TcpClient client, string serverIP, int port):base(serverIP, port)
        {
            Client = client;
        }

        private void Close()
        {
            Client.Close();
        }

        public void Send(object message)
        {
            NetworkUtilities.Send(message, Client);
        }

        public void ListenForMessages()
        {
            CancellationSource = new CancellationTokenSource();
            using(CancellationSource.Token.Register(Close))
            {
                while(Connected)
                {
                    Console.WriteLine(ReceiveMessage());
                }
            }
        }

        public object ReceiveMessage()
        {
            object message = NetworkUtilities.Receive(Client);
            if(message == "")
            {
                Stop();
            }
            return message;
        }

        public new void Stop()
        {
            Connected = false;
            Client.Close();
        }
    }


    public static class NetworkUtilities
    {
        public static string GetLocalIPAddress()
        {
            string strHostName = Dns.GetHostName();
            IPHostEntry ipHostEntry = Dns.GetHostEntry(strHostName);
            IPAddress[] address = ipHostEntry.AddressList;
            return address[address.Length-1].ToString();
        }

        public static void Send(object message, TcpClient client)
        {
            BinaryFormatter binaryFormatter = new BinaryFormatter();
            binaryFormatter.Serialize(client.GetStream(), message);
        }

        public static object Receive(TcpClient client, CancellationToken token = new CancellationToken())
        {
            try
            {
                object dataReceived = DeserializeStream(client.GetStream());
                return dataReceived;

            }
            catch(Exception e)
            {
                if(token.IsCancellationRequested || e is System.IO.IOException || e is SocketException || e is System.InvalidOperationException || e is System.Runtime.Serialization.SerializationException)
                {
                    return "";
                }
                throw e;
            }
        }

        private static object DeserializeStream(NetworkStream networkStream)
        {
            BinaryFormatter binaryFormatter = new BinaryFormatter();
            return binaryFormatter.Deserialize(networkStream);
        }
    }

    public class ConnectionArgs : EventArgs
    {
        private ClientConnection client;

        public ConnectionArgs(ClientConnection newClient)
        {
            client = newClient;
        }

        public ClientConnection Client
        {
            get
            {
                return client;
            }
        }
    }

    public class MessageReceivedArgs : EventArgs
    {
        private object message;

        public MessageReceivedArgs(object message)
        {
            this.message = message;
        }

        public object Message
        {
            get
            {
                return message;
            }
        }
    }
}
