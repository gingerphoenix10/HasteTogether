using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.Mathematics;
using Zorro.ControllerSupport;
using Zorro.Settings;
using Zorro.Settings.DebugUI;
using FloatSettingUI = Zorro.Settings.UI.FloatSettingUI;
using Logger = UnityEngine.Logger;

namespace HasteTogether;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;
    private static readonly Harmony Patcher = new(MyPluginInfo.PLUGIN_GUID);

    public static Socket client;
    
    public static byte[] SerializeTransform(Vector3 position, Quaternion rotation)
    {
        byte[] buffer = new byte[13]; // 9 bytes for position, 4 for rotation

        // Position: Convert each coordinate to 3 bytes
        int x = (int)((position.x + 8388607.5f) * 256); // Shift to unsigned 24-bit
        int y = (int)((position.y + 8388607.5f) * 256);
        int z = (int)((position.z + 8388607.5f) * 256);

        buffer[0] = (byte)(x >> 16);
        buffer[1] = (byte)(x >> 8);
        buffer[2] = (byte)x;

        buffer[3] = (byte)(y >> 16);
        buffer[4] = (byte)(y >> 8);
        buffer[5] = (byte)y;

        buffer[6] = (byte)(z >> 16);
        buffer[7] = (byte)(z >> 8);
        buffer[8] = (byte)z;

        // Quaternion: Convert to 4-byte format
        byte largestIndex = 0;
        float largestValue = Math.Abs(rotation.w);
        float[] quat = { rotation.x, rotation.y, rotation.z, rotation.w };

        for (byte i = 0; i < 4; i++)
        {
            if (Math.Abs(quat[i]) > largestValue)
            {
                largestIndex = i;
                largestValue = Math.Abs(quat[i]);
            }
        }

        byte sign = (quat[largestIndex] < 0) ? (byte)0x80 : (byte)0x00;
        int a = (int)((quat[(largestIndex + 1) % 4] + 1) * 1023.5f);
        int b = (int)((quat[(largestIndex + 2) % 4] + 1) * 1023.5f);
        int c = (int)((quat[(largestIndex + 3) % 4] + 1) * 1023.5f);

        buffer[9] = (byte)(sign | (largestIndex << 6) | (a >> 4));
        buffer[10] = (byte)(((a & 0xF) << 4) | (b >> 6));
        buffer[11] = (byte)(((b & 0x3F) << 2) | (c >> 8));
        buffer[12] = (byte)(c & 0xFF);

        return buffer;
    }

    public static UpdatePacket lastSent = new();
    
    private async void Awake()
    {
        Logger = base.Logger;
        Patcher.PatchAll();
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
        
        IPAddress address = IPAddress.Parse("127.0.0.1");
        IPEndPoint endpoint = new(address, 9843);
        client = new(
            AddressFamily.InterNetwork,
            SocketType.Stream,
            ProtocolType.Tcp
        );
        Logger.LogInfo("Connecting to server...");
        await client.ConnectAsync(endpoint);
        Logger.LogInfo("Connected!");
        
        byte[] buffer = new byte[1024];
        
        while (true)
        {
            int bytesRead = await client.ReceiveAsync(buffer, SocketFlags.None);
            if (bytesRead == 0)
            {
                Console.WriteLine("Disconnected from server.");
                break;
            }

            string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            Console.WriteLine($"Received: {message}");
        }
    }
}

public class NetworkedPlayer : MonoBehaviour
{
    public string userId;
}

public abstract class Packet
{
    public abstract byte PacketID();
    public abstract byte[] Serialize();

    public void Send()
    {
        if (Plugin.client == null || !Plugin.client.Connected) throw new Exception("Not connected to a server!");
        byte[] data = Serialize();
        byte[] toSend = new byte[data.Length+1];
        toSend[0] = PacketID();
        Buffer.BlockCopy(data, 0, toSend, 1, data.Length);
        Plugin.client.Send(toSend);
    }
}

public class UpdatePacket : Packet
{
    public override byte PacketID() => 0x01;
    public override byte[] Serialize() => Plugin.SerializeTransform(position, rotation);

    public Vector3 position;
    public Quaternion rotation;
    
    public bool Equals( UpdatePacket obj )
    {
        return (position == obj.position && rotation == obj.rotation);
    }

    public UpdatePacket(Transform player = null)
    {
        this.position = player != null ? player.position : new Vector3();
        this.rotation = player != null ? player.rotation : new Quaternion();
    }
}

[HarmonyPatch(typeof(PlayerCharacter))]
internal static class PlayerCharacterPatch
{
    [HarmonyPatch(nameof(PlayerCharacter.Update))]
    [HarmonyPostfix]
    internal static void Update(PlayerCharacter __instance)
    {
        UpdatePacket packet = new UpdatePacket(__instance.transform);
        if (!Plugin.lastSent.Equals(packet))
        {
            packet.Send();
            Plugin.lastSent = packet;
        }
    }
}