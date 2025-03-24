using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
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
using TMPro;
using UnityEngine;
using UnityEngine.UI;
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

    public static SocketManager manager;
    public static UpdatePacket lastSent = new();

    public static Transform TogetherUI;
    
    private async void Awake()
    {
        Logger = base.Logger;
        Patcher.PatchAll();
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");

        //IPAddress address = IPAddress.Parse("127.0.0.1");
        IPAddress address = IPAddress.Parse("45.133.89.163");
        IPEndPoint endpoint = new(address, 9843);
        manager = new SocketManager();
        Logger.LogInfo("Connecting to server...");
        
        manager.OnDataReceived += async (byte[] receivedData) =>
        {
            ushort userId;
            NetworkedPlayer plr = null;
            switch (receivedData[0])
            {
                case 0x01:
                    userId = (ushort)((receivedData[1] << 8) | receivedData[2]);
                    foreach (NetworkedPlayer plrPossibility in GameObject.FindObjectsOfType<NetworkedPlayer>())
                    {
                        if (plrPossibility.userId == userId)
                        {
                            plr = plrPossibility;
                            break;
                        }
                    }

                    plr ??= await SetupNetworkedPlayer(userId);

                    if (plr == null) break;

                    byte[] rawTransform = new Byte[15];
                    Array.Copy(receivedData, 3, rawTransform, 0, 15);
                    //Logger.LogInfo($"Received packet: {BitConverter.ToString(receivedData)}");
                    //Logger.LogInfo($"Converted transform: {BitConverter.ToString(rawTransform)}");
                    plr.ApplyTransform(rawTransform);

                    break;
                case 0x02:
                    userId = (ushort)((receivedData[1] << 8) | receivedData[2]);
                    foreach (NetworkedPlayer plrPossibility in GameObject.FindObjectsOfType<NetworkedPlayer>())
                    {
                        if (plrPossibility.userId == userId)
                        {
                            plr = plrPossibility;
                            break;
                        }
                    }

                    if (plr != null)
                        Destroy(plr.gameObject);

                    break;
                case 0x03:
                    userId = (ushort)((receivedData[1] << 8) | receivedData[2]);
                    foreach (NetworkedPlayer plrPossibility in GameObject.FindObjectsOfType<NetworkedPlayer>())
                    {
                        if (plrPossibility.userId == userId)
                        {
                            plr = plrPossibility;
                            break;
                        }
                    }

                    plr ??= await SetupNetworkedPlayer(userId);

                    if (plr == null) break;

                    plr.playerName = Encoding.UTF8.GetString(receivedData, 3, receivedData.Length - 3);
                    plr.playerNameText.text = plr.playerName;

                    break;
            }
            SimpleRunHandler.currentSeed = 0;
        };
        
        _ = manager.StartListening(endpoint);
    }
    
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

    private async Task<NetworkedPlayer> SetupNetworkedPlayer(ushort id = 0xFFFC)
    {
        
        PlayerModel model = GameObject.FindObjectOfType<PlayerModel>();
        if (model != null)
        {
            GameObject newPlayer = Instantiate(model.gameObject);
            Destroy(newPlayer.GetComponent<PlayerModel>());
            newPlayer.transform.position = model.gameObject.transform.position;
            newPlayer.name = $"HasteTogether_{id}";
            NetworkedPlayer networkedPlayer = newPlayer.AddComponent<NetworkedPlayer>();
            networkedPlayer.userId = id;
            networkedPlayer.animator = newPlayer.GetComponentInChildren<Animator>();
            var tcs = new TaskCompletionSource<string>();

            void OnResponseReceived(byte[] data)
            {
                if (data[0] == 0x04)
                {
                    string responseString = System.Text.Encoding.UTF8.GetString(data, 1, data.Length - 1);
                    tcs.TrySetResult(responseString);
                }
            }

            manager.OnDataReceived += OnResponseReceived; 

            new GetPacket(id).Send();

            networkedPlayer.playerName = await tcs.Task;
        
            manager.OnDataReceived -= OnResponseReceived;
            return networkedPlayer;
        }

        Console.WriteLine("[ERROR] Not in a scene where a PlayerModel exists.");
        return null;
    }

}

public class SocketManager
{
    public Socket client;
    private byte[] buffer = new byte[1024];

    public event Action<byte[]> OnDataReceived;

    public SocketManager()
    {
        client = new Socket(
            AddressFamily.InterNetwork,
            SocketType.Stream,
            ProtocolType.Tcp
        );
    }

    public async Task StartListening(IPEndPoint endpoint)
    {
        try
        {
            await client.ConnectAsync(endpoint);
            Plugin.Logger.LogInfo("Connected!");
            new NamePacket("timed out in discord - "+SteamFriends.GetPersonaName()).Send();
            _ = ReceiveLoop(endpoint);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Connection error: {ex.Message}");
            foreach (NetworkedPlayer plr in GameObject.FindObjectsOfType<NetworkedPlayer>()) GameObject.Destroy(plr.gameObject);
            await Task.Delay(3000);
            _ = StartListening(endpoint);
        }
    }

    private async Task ReceiveLoop(IPEndPoint endpoint)
    {
        while (true)
        {
            try
            {
                int bytesRead = await client.ReceiveAsync(buffer, SocketFlags.None);
                if (bytesRead == 0)
                {
                    Console.WriteLine("Disconnected from server.");
                    await Task.Delay(3000);
                    client.Dispose();
                    client = new Socket(
                        AddressFamily.InterNetwork,
                        SocketType.Stream,
                        ProtocolType.Tcp
                    );
                    _ = StartListening(endpoint);
                    break;
                }

                byte[] receivedData = new byte[bytesRead];
                Array.Copy(buffer, receivedData, bytesRead);

                OnDataReceived?.Invoke(receivedData);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Socket error: {ex.Message}");
                foreach (NetworkedPlayer plr in GameObject.FindObjectsOfType<NetworkedPlayer>()) GameObject.Destroy(plr.gameObject);
                await Task.Delay(3000);
                client.Dispose();
                client = new Socket(
                    AddressFamily.InterNetwork,
                    SocketType.Stream,
                    ProtocolType.Tcp
                );
                _ = StartListening(endpoint);
                break;
            }
        }
    }
}

public class ConnectionState : MonoBehaviour
{
    public Image img;
    public Sprite connected;
    public Sprite disconnected;
    void Update()
    {
        if (Plugin.manager == null || Plugin.manager.client == null || connected == null)
            return;
        img.sprite = Plugin.manager.client.Connected ? connected : disconnected;
    }
}

public class NetworkedPlayer : MonoBehaviour
{
    public ushort userId;
    public Animator animator;

    public Vector3 position = new Vector3();
    public Quaternion rotation = new Quaternion();

    private string _playerName;
    public string playerName
    {
        get
        {
            return _playerName;
        }
        set
        {
            _playerName = value;
            playerNameText.text = value;
        }
    }

    public Canvas playerCanvas;
    public TextMeshProUGUI playerNameText;
    
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

        int rawRotY = (transformData[9] << 16) | (transformData[10] << 8) | transformData[11];
        int rawRotW = (transformData[12] << 16) | (transformData[13] << 8) | transformData[14];

        if ((rawRotY & 0x800000) != 0) rawRotY |= unchecked((int)0xFF000000);
        if ((rawRotW & 0x800000) != 0) rawRotW |= unchecked((int)0xFF000000);

        float rotY = rawRotY / 8388607.0f;
        float rotW = rawRotW / 8388607.0f;

        float xzSquared = 1.0f - (rotY * rotY) - (rotW * rotW);
        float rotX = 0.0f, rotZ = 0.0f;
        if (xzSquared > 0.0f)
            rotZ = Mathf.Sqrt(xzSquared);

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
        if (playerCanvas == null)
        {
            GameObject canvasObj = new GameObject();
            canvasObj.transform.SetParent(transform);
            canvasObj.name = "PlayerCanvas";
            playerCanvas = canvasObj.AddComponent<Canvas>();
            playerCanvas.worldCamera = Camera.main;
            playerCanvas.sortingOrder = 1;
            canvasObj.transform.localPosition = new Vector3(0, 2.5f, 0);
            canvasObj.transform.localScale = new Vector3(0.006f, 0.006f, 0.006f);
        }

        if (playerNameText == null)
        {
            GameObject nameObj = new GameObject();
            nameObj.transform.SetParent(playerCanvas.transform);
            nameObj.name = "Nametag";
            nameObj.transform.localPosition = new Vector3(0, 0, 0);
            nameObj.transform.localScale = new Vector3(1, 1, 1);
            playerNameText = nameObj.AddComponent<TextMeshProUGUI>();
            playerNameText.text = playerName;
            playerNameText.enableWordWrapping = false;
            playerNameText.alignment = TextAlignmentOptions.Center;
            
            playerNameText.fontStyle = FontStyles.Bold;
            
            playerNameText.fontSharedMaterial.shader = Shader.Find("TextMeshPro/Distance Field");
            playerNameText.outlineWidth = 0.35f;
            playerNameText.outlineColor = Color.black;
        }
        
        playerCanvas.transform.forward = Camera.main.transform.forward;
    }
}

public abstract class Packet
{
    public abstract byte PacketID();
    public abstract byte[] Serialize();

    public void Send()
    {
        if (Plugin.manager.client == null || !Plugin.manager.client.Connected) return;//throw new Exception("Not connected to a server!");
        byte[] data = Serialize();
        byte[] toSend = new byte[data.Length+1];
        toSend[0] = PacketID();
        Buffer.BlockCopy(data, 0, toSend, 1, data.Length);
        Plugin.manager.client.Send(toSend);
    }
}

public class UpdatePacket : Packet
{
    public override byte PacketID() => 0x01;
    public override byte[] Serialize() => Plugin.SerializeTransform(position, rotation);

    private Vector3 position;
    private Quaternion rotation;
    
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

public class NamePacket : Packet
{
    private string name;
    public override byte PacketID() => 0x03;
    public override byte[] Serialize()
    {
        return Encoding.UTF8.GetBytes(name);
    }

    public NamePacket(string name)
    {
        this.name = name;
    }
}

public class GetPacket : Packet
{
    private ushort userId;
    public override byte PacketID() => 0x04;

    public override byte[] Serialize()
    {
        byte[] toSend = new byte[2];
        toSend[0] = (byte)(userId >> 8);
        toSend[1] = (byte)(userId & 0xFF);
        return toSend;
    }
    
    public GetPacket(ushort userId)
    {
        this.userId = userId;
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

[HarmonyPatch(typeof(PersistentObjects))]
internal static class PersistentObjectsPatch
{
    [HarmonyPatch(nameof(PersistentObjects.Awake))]
    [HarmonyPrefix]
    internal static bool Awake(PersistentObjects __instance)
    {
        if (PersistentObjects.instance != null)
        {
            GameObject.DestroyImmediate(__instance.gameObject);
        }
        else
        {
            PersistentObjects.instance = __instance;
            __instance.transform.SetParent(null);
            GameObject.DontDestroyOnLoad(__instance.gameObject);
            Transform persistent = __instance.transform.Find("UI_Persistent");
            
            Plugin.TogetherUI = new GameObject("TogetherUI").transform;
            Plugin.TogetherUI.SetParent(persistent);
            
            Transform TogetherConnectionImg = new GameObject("Connection").transform;
            TogetherConnectionImg.SetParent(Plugin.TogetherUI);
            ConnectionState state = TogetherConnectionImg.gameObject.AddComponent<ConnectionState>();
            state.img = TogetherConnectionImg.gameObject.AddComponent<Image>();
            state.img.transform.localPosition = new Vector2(1780, 100);
            state.img.transform.localScale = new Vector2(2.5f, 2.5f);
            state.img.preserveAspect = true;
            state.connected = fromResource("HasteTogether.Graphics.HasteTogether_Connected.png");
            state.disconnected = fromResource("HasteTogether.Graphics.HasteTogether_Disconnected.png");
        }
        return false;
    }

    public static Sprite fromResource(string name)
    {
        Texture2D texture = new Texture2D(1,1);
        Stream imgStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        byte[] buffer = new byte[16*1024];
        using (MemoryStream ms = new MemoryStream())
        {
            int read;
            while ((read = imgStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                ms.Write(buffer, 0, read);
            }

            texture.LoadImage(ms.ToArray());
            return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.zero);
            
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