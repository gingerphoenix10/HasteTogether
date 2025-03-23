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
using Landfall.Haste;
using Landfall.Haste.Steam;
using MonoMod.Utils;
using Steamworks;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.Mathematics;
using Zorro.ControllerSupport;
using Zorro.Settings;
using Zorro.Settings.DebugUI;
using FloatSettingUI = Zorro.Settings.UI.FloatSettingUI;
using Logger = UnityEngine.Logger;
using PlatformSelector = Landfall.Haste.PlatformSelector;

namespace HasteTogether;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;
    private static readonly Harmony Patcher = new(MyPluginInfo.PLUGIN_GUID);

    public static Socket client;
    
    public static byte[] SerializeTransform(Vector3 position, Quaternion rotation)
    {
        byte[] buffer = new byte[15];
        
        int x = (int)((position.x + 32767.5f) * 256);
        int y = (int)((position.y + 32767.5f) * 256);
        int z = (int)((position.z + 32767.5f) * 256);

        buffer[0] = (byte)(x >> 16);
        buffer[1] = (byte)(x >> 8);
        buffer[2] = (byte)x;

        buffer[3] = (byte)(y >> 16);
        buffer[4] = (byte)(y >> 8);
        buffer[5] = (byte)y;

        buffer[6] = (byte)(z >> 16);
        buffer[7] = (byte)(z >> 8);
        buffer[8] = (byte)z;
        
        // Scale y and w from [-1,1] to [-8388607,8388607]
        int rotY = Mathf.RoundToInt(rotation.y * 8388607.0f);
        int rotW = Mathf.RoundToInt(rotation.w * 8388607.0f);

        // Store y
        buffer[9]  = (byte)((rotY >> 16) & 0xFF);
        buffer[10] = (byte)((rotY >> 8) & 0xFF);
        buffer[11] = (byte)(rotY & 0xFF);

        // Store w
        buffer[12] = (byte)((rotW >> 16) & 0xFF);
        buffer[13] = (byte)((rotW >> 8) & 0xFF);
        buffer[14] = (byte)(rotW & 0xFF);

        //Logger.LogInfo($"Serialized {rotation.y}, {rotation.w} to {buffer[9]:X2} {buffer[10]:X2} {buffer[11]:X2} | {buffer[12]:X2} {buffer[13]:X2} {buffer[14]:X2}");
        
        return buffer;
    }

    public static UpdatePacket lastSent = new();
    
    private async void Awake()
    {
        Logger = base.Logger;
        Patcher.PatchAll();
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");

        //IPAddress address = IPAddress.Parse("127.0.0.1");
        IPAddress address = IPAddress.Parse("45.133.89.163");
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

            byte[] receivedData = new byte[bytesRead];
            Array.Copy(buffer, receivedData, bytesRead);

            switch (receivedData[0])
            {
                case 0x01:
                    ushort userId = (ushort)((receivedData[1] << 8) | receivedData[2]);
                    NetworkedPlayer plr = null;
                    foreach (NetworkedPlayer plrPossibility in GameObject.FindObjectsOfType<NetworkedPlayer>())
                    {
                        if (plrPossibility.userId == userId)
                        {
                            plr = plrPossibility;
                            break;
                        }
                    }
                    plr ??= SetupNetworkedPlayer(userId);

                    if (plr == null) continue;
                    
                    byte[] rawTransform = new Byte[15];
                    Array.Copy(receivedData, 3, rawTransform, 0, 15);
                    //Logger.LogInfo($"Received packet: {BitConverter.ToString(receivedData)}");
                    //Logger.LogInfo($"Converted transform: {BitConverter.ToString(rawTransform)}");
                    plr.ApplyTransform(rawTransform);
                    
                    break;
            }
        }
    }

    private NetworkedPlayer SetupNetworkedPlayer(ushort id = 0xFFFC)
    {
        PlayerModel model = GameObject.FindObjectOfType<PlayerModel>();
        if (model != null)
        {
            GameObject newPlayer = Instantiate(model.gameObject);
            newPlayer.transform.position = model.gameObject.transform.position;
            newPlayer.name = $"HasteTogether_{id}";
            NetworkedPlayer networkedPlayer = newPlayer.AddComponent<NetworkedPlayer>();
            networkedPlayer.userId = id;
            networkedPlayer.animator = newPlayer.GetComponentInChildren<Animator>();
            Destroy(newPlayer.GetComponent<PlayerModel>());
            return networkedPlayer;
        }
        Console.WriteLine("[ERROR] Not in a scene where a PlayerModel exists.");
        return null;
    }
}

public class NetworkedPlayer : MonoBehaviour
{
    public ushort userId;
    public Animator animator;

    public Vector3 position = new Vector3();
    public Quaternion rotation = new Quaternion();
    
    public void ApplyTransform(byte[] transformData)
    {
        int x = (transformData[0] << 16) | (transformData[1] << 8) | transformData[2];
        int y = (transformData[3] << 16) | (transformData[4] << 8) | transformData[5];
        int z = (transformData[6] << 16) | (transformData[7] << 8) | transformData[8];

        Vector3 targetPosition = new Vector3(
            (x / 256.0f) - 32767.5f,
            (y / 256.0f) - 32767.5f,
            (z / 256.0f) - 32767.5f
        );

        // Reconstruct 24-bit signed integers
        int rawRotY = (transformData[9] << 16) | (transformData[10] << 8) | transformData[11];
        int rawRotW = (transformData[12] << 16) | (transformData[13] << 8) | transformData[14];

        // Sign extension for 24-bit integers
        if ((rawRotY & 0x800000) != 0) rawRotY |= unchecked((int)0xFF000000);
        if ((rawRotW & 0x800000) != 0) rawRotW |= unchecked((int)0xFF000000);

        // Convert back to float range [-1,1]
        float rotY = rawRotY / 8388607.0f;
        float rotW = rawRotW / 8388607.0f;

        // Recalculate x and z to maintain unit quaternion
        float xzSquared = 1.0f - (rotY * rotY) - (rotW * rotW);
        float rotX = 0.0f, rotZ = 0.0f;
        if (xzSquared > 0.0f) 
            rotZ = Mathf.Sqrt(xzSquared); // Assume positive Z for consistency

        // Apply the new quaternion
        Quaternion targetRotation = new Quaternion(rotX, rotY, rotZ, rotW);
        rotation = targetRotation;
        position = targetPosition;

        //Plugin.Logger.LogInfo($"Deserialized {transformData[9]:X2} {transformData[10]:X2} {transformData[11]:X2} | {transformData[12]:X2} {transformData[13]:X2} {transformData[14]:X2} to Quaternion({rotX}, {rotY}, {rotZ}, {rotW})");
        
        SetAnimation("New_Courier_Idle");
    }

    public void SetAnimation(string animName)
    {
        animator.Play(animName);
    }
    
    private float interpolationSpeed = 10.0f;
    private void Update()
    {
        transform.position = Vector3.Lerp(transform.position, position, Time.deltaTime * interpolationSpeed);
        transform.rotation = Quaternion.Slerp(transform.rotation, rotation, Time.deltaTime * interpolationSpeed);
    }
}

public abstract class Packet
{
    public abstract byte PacketID();
    public abstract byte[] Serialize();

    public void Send()
    {
        if (Plugin.client == null || !Plugin.client.Connected) return;//throw new Exception("Not connected to a server!");
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
        this.rotation = player != null ? player.GetComponent<PlayerVisualRotation>().visual.rotation : new Quaternion();
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

[HarmonyPatch(typeof(SteamAPI))]
internal static class SteamAPIPatch
{
    [HarmonyPatch(nameof(SteamAPI.RestartAppIfNecessary))]
    [HarmonyPrefix]
    internal static bool RestartAppIfNecessary(ref bool __result)
    {
        __result = false;
        return false;
    }
}