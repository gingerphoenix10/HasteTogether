using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

class SocketServer
{
    private static List<Socket> clients = new List<Socket>();
    private static object lockObj = new object();

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
            lock (lockObj) clients.Add(clientSocket);
            Console.WriteLine($"Client connected: {clientSocket.RemoteEndPoint}");
            // give the client an id to differenciate gameobjects / players

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
                        Console.WriteLine($"Received update packet. ");
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
            lock (lockObj) clients.Remove(clientSocket);
            clientSocket.Close();
            Console.WriteLine($"Client {clientSocket.RemoteEndPoint} disconnected.");
        }
    }

    private static void BroadcastMessage(string message)
    {
        byte[] data = Encoding.UTF8.GetBytes(message);
        lock (lockObj)
        {
            foreach (var client in clients)
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
}
