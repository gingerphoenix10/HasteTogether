using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

class SocketServer
{
    public static Dictionary<Socket, PlayerInfo> clients = new();

    static async Task Main()
    {
        int port = 9843; // Change as needed
        IPEndPoint endPoint = new IPEndPoint(IPAddress.Any, port);

        using (Socket serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
        {
            serverSocket.Bind(endPoint);
            serverSocket.Listen(10);

            Console.WriteLine($"Server started on port {port}. Waiting for connections...");

            while (true)
            {
                Socket clientSocket = await serverSocket.AcceptAsync();
                ushort id = 0x0000;
                List<ushort> usedIds = PlayerInfo.usedIds();
                for (ushort idPossibility = 0x0000; idPossibility <= 0xFFFF; idPossibility++)
                {
                    if (!usedIds.Contains(idPossibility))
                    {
                        id = idPossibility;
                        break;
                    }
                }

                PlayerInfo info = new PlayerInfo();
                info.userId = id;
            
                clients.Add(clientSocket, info);
                Console.WriteLine($"Client connected: {clientSocket.RemoteEndPoint}. Assigning ID: {id}");

                // Handle client communication
                _ = Task.Run(() => HandleClient(clientSocket));
            }
        }
    }

    private static async Task HandleClient(Socket clientSocket)
    {
        byte[] buffer = new byte[1024];

        try
        {
            while (true)
            {
                int bytesRead = await clientSocket.ReceiveAsync(buffer, SocketFlags.None);
                if (bytesRead == 0)
                {
                    break; // Client disconnected
                }

                byte[] receivedData = new byte[bytesRead];
                Array.Copy(buffer, receivedData, bytesRead);

                //Console.WriteLine(BitConverter.ToString(receivedData));

                byte[] toSend;
                
                switch (receivedData[0])
                {
                    case 0x03:
                        clients[clientSocket].username = Encoding.UTF8.GetString(receivedData, 1, receivedData.Length - 1);
                        Console.WriteLine($"{clients[clientSocket].userId} username set to {clients[clientSocket].username}");
                        
                        toSend = new byte[receivedData.Length + 2];
                        toSend[0] = receivedData[0];
                        toSend[1] = (byte)(clients[clientSocket].userId >> 8);
                        toSend[2] = (byte)(clients[clientSocket].userId & 0xFF);
                        Array.Copy(receivedData, 1, toSend, 3, receivedData.Length - 1); // Copy the username bytes
                        
                        foreach (KeyValuePair<Socket, PlayerInfo> client in clients)
                        {
                            if (client.Value.userId != clients[clientSocket].userId)
                            {
                                client.Key.Send(toSend);
                            }
                        }
                        break;
                    case 0x04:
                        PlayerInfo info = PlayerInfo.FindById((ushort)((receivedData[1] << 8) | receivedData[2]));
                        string username = $"no username for {(ushort)((receivedData[1] << 8) | receivedData[2])}";
                        if (info != null)
                        {
                            username = info.username;
                        }

                        if (username == "") username = "no username sent";
                        byte[] usernameBytes = Encoding.UTF8.GetBytes(username);
                        toSend = new byte[1 + usernameBytes.Length];
                        toSend[0] = receivedData[0];
                        Array.Copy(usernameBytes, 0, toSend, 1, usernameBytes.Length);
                        clientSocket.Send(toSend);
                        break;
                    default:
                        toSend = new byte[receivedData.Length + 2];
                        toSend[0] = receivedData[0];
                        toSend[1] = (byte)(clients[clientSocket].userId >> 8);
                        toSend[2] = (byte)(clients[clientSocket].userId & 0xFF);
                        Array.Copy(receivedData, 1, toSend, 3, receivedData.Length - 1);
                        foreach (KeyValuePair<Socket, PlayerInfo> client in clients)
                        {
                            if (client.Value.userId != clients[clientSocket].userId) client.Key.Send(toSend);
                        }
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error with client {clientSocket.RemoteEndPoint}: {ex.Message}");
        }
        finally
        {
            byte[] disconnectPacket = new byte[3];
            disconnectPacket[0] = 0x02;
            disconnectPacket[1] = (byte)(clients[clientSocket].userId >> 8);
            disconnectPacket[2] = (byte)(clients[clientSocket].userId & 0xFF);
            foreach (KeyValuePair<Socket, PlayerInfo> client in clients)
            {
                if (client.Value.userId != clients[clientSocket].userId) client.Key.Send(disconnectPacket);
            }
            clients.Remove(clientSocket);
            clientSocket.Close();
            Console.WriteLine($"Client {clientSocket.RemoteEndPoint} disconnected.");
        }
    }
}

class PlayerInfo
{
    public ushort userId;
    public string username = "Unknown (s)";

    public static List<ushort> usedIds()
    {
        List<ushort> ids = new();
        foreach (PlayerInfo info in SocketServer.clients.Values)
        {
            ids.Add(info.userId);
        }

        return ids;
    }

    public static PlayerInfo FindById(ushort id)
    {
        foreach (PlayerInfo info in SocketServer.clients.Values)
        {
            if (info.userId == id) return info;
        }
        return null;
    }
}