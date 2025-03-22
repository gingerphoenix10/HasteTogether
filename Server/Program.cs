using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

class SocketServer
{
    private static Dictionary<Socket, ushort> clients = new();

    static async Task Main()
    {
        int port = 9843; // Change as needed
        IPEndPoint endPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), port);

        using (Socket serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
        {
            serverSocket.Bind(endPoint);
            serverSocket.Listen(10);

            Console.WriteLine($"Server started on port {port}. Waiting for connections...");

            // Accept clients asynchronously
            _ = AcceptClientsAsync(serverSocket);

            // Allow user input to send messages to all clients
            while (true)
            {
                string? input = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(input))
                {
                    BroadcastMessage($"[SERVER]: {input}");
                }
            }
        }
    }

    private static async Task AcceptClientsAsync(Socket serverSocket)
    {
        while (true)
        {
            Socket clientSocket = await serverSocket.AcceptAsync();
            ushort id = 0x0000;
            HashSet<ushort> usedIds = new(clients.Values);
            for (ushort idPossibility = 0x0000; idPossibility <= 0xFFFF; idPossibility++)
            {
                if (!usedIds.Contains(idPossibility))
                {
                    id = idPossibility;
                    break;
                }
            }
            
            clients.Add(clientSocket, id);
            Console.WriteLine($"Client connected: {clientSocket.RemoteEndPoint}. Assigning ID: {id}");

            // Handle client communication
            _ = Task.Run(() => HandleClient(clientSocket));
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

                switch (receivedData[0])
                {
                    case 0x01:
                        byte[] toSend = new byte[receivedData.Length + 2];
                        toSend[0] = receivedData[0];
                        toSend[1] = (byte)(clients[clientSocket] >> 8);
                        toSend[2] = (byte)(clients[clientSocket] & 0xFF);
                        Array.Copy(receivedData, 1, toSend, 3, receivedData.Length - 1);
                        foreach (KeyValuePair<Socket, ushort> client in clients)
                        {
                            if (client.Value != clients[clientSocket] || false) client.Key.Send(toSend);
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
            clients.Remove(clientSocket);
            clientSocket.Close();
            Console.WriteLine($"Client {clientSocket.RemoteEndPoint} disconnected.");
        }
    }

    private static void BroadcastMessage(string message)
    {
        byte[] data = Encoding.UTF8.GetBytes(message);
        foreach (var client in clients.Keys)
        {
            try
            {
                client.Send(data);
            }
            catch
            {
                // Ignore errors if a client disconnects suddenly
            }
        }
    }
}