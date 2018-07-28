using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System;
using VRage.Collections;
using VRage.Game.Components;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Game;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        // This file contains your actual script.
        //
        // You can either keep all your code here, or you can create separate
        // code files to make your program easier to navigate while coding.
        //
        // In order to add a new utility class, right-click on your project, 
        // select 'New' then 'Add Item...'. Now find the 'Space Engineers'
        // category under 'Visual C# Items' on the left hand side, and select
        // 'Utility Class' in the main area. Name it in the box below, and
        // press OK. This utility class will be merged in with your code when
        // deploying your final script.
        //
        // You can also simply create a new utility class manually, you don't
        // have to use the template if you don't want to. Just do so the first
        // time to see what a utility class looks like.
        #region ProgrammableBlock

        static Action<string, bool> LogImpl;

        static void Log(string text, bool append = true)
        {
            LogImpl(text, append);
        }
        #region BaseService
        public interface IShipService
        {
            void update(float dt);
            void update10();
            void update100();
            void update500();
            void incomeMessage(string message);
        }

        public class BaseShipService : IShipService
        {
            protected string groupName;
            virtual public void update(float dt) { }
            virtual public void update10() { }
            virtual public void update100() { }
            virtual public void update500() { }
            virtual public void incomeMessage(string message) { }
        }
        #endregion //BaseService
        //------------ship services-------------
        #region NetworkService
        #region NetworkPacket
        public class NetworkPacket
        {
            const char delimiter = '#';
            public NetworkPacket(string rawData)
            {
                string[] splitData = rawData.Split(delimiter);
                if (splitData.Count() == 6)
                {
                    int.TryParse(splitData[0], out id);
                    Enum.TryParse(splitData[1], out type);
                    to = splitData[2];
                    from = splitData[3];
                    Enum.TryParse(splitData[4], out code);
                    data = splitData[5];
                }
                else
                {
                    code = NetworkStatusCode.WRONG_PACKET;
                }
            }

            public NetworkPacket(int id, NetworkPacketType type, string to, string from, NetworkStatusCode code, string data)
            {
                this.id = id;
                this.type = type;
                this.to = to;
                this.from = from;
                this.code = code;
                this.data = data;
            }

            public string serialize()
            {
                return id.ToString() + delimiter + type.ToString() + delimiter + to + delimiter + from + delimiter + code.ToString() + delimiter + data;
            }

            public int id;
            public NetworkPacketType type;
            public string to;
            public string from;
            public NetworkStatusCode code;
            public string data;
        }

        public enum NetworkStatusCode
        {
            OK,
            TIMEOUT,
            WRONG_PACKET
        }

        public enum NetworkPacketType
        {
            BROADCAST,
            BROADCAST_RESPONSE,
            PING,
            PING_RESPONSE,
            REQUEST,
            RESPONSE,
            UDP
        }

        #endregion //#NetworkPacket

        public interface IRequestListener
        {
            void request(NetworkPacket packet);
        }

        public class NetworkService : BaseShipService
        {
            IMyRadioAntenna antenna;
            string networkName;
            
            bool isScanningNetwork = false;

            public List<string> network;
            private List<IRequestListener> requestListeners;
            private Dictionary<int, Action<NetworkPacket>> responseListeners;
            private Dictionary<int, int> responseTimers;
            private List<NetworkPacket> packetsToSend;
            private int idGenerator;
            private Action onScanNetworkComplete;
            private int ticksToCompleteScan = 0;

            public NetworkService(string networkName)
            {
                this.networkName = networkName;
                network = new List<string>();
                groupName = "[Network Service]";
                requestListeners = new List<IRequestListener>();
                responseListeners = new Dictionary<int, Action<NetworkPacket>>();
                responseTimers = new Dictionary<int, int>();
                packetsToSend = new List<NetworkPacket>();
                init();
            }

            void init()
            {
                findRequiredBlocks();
                renameBlocks();
                //scanNetwork(()=>{ });
            }

            public void registerRequestListener(IRequestListener listener)
            {
                requestListeners.Add(listener);
            }

            public override void update(float dt)
            {
                if (antenna != null && packetsToSend.Count > 0)
                {
                    var packet = packetsToSend.First();
                    antenna.TransmitMessage(packet.serialize(), MyTransmitTarget.Everyone);
                    //Log("Send packet: " + packet.serialize());
                    packetsToSend.Remove(packet);
                }
            }

            override public void update500()
            {
                //reinit
                init();
            }

            public override void update10()
            {
                if(isScanningNetwork)
                {
                    ticksToCompleteScan--;
                    if (ticksToCompleteScan <= 0)
                    {
                        isScanningNetwork = false;
                        ScanNetworkComplete();
                    }
                }
            }

            public override void update100()
            {
                List<int> timersToRemove = new List<int>();
                foreach(var key in responseTimers.Keys.ToList())
                {
                    responseTimers[key]--;
                    int timer = responseTimers[key];
                    if(timer <= 0)
                    {
                        responseListeners[key].Invoke(new NetworkPacket(key, NetworkPacketType.RESPONSE, "", "", NetworkStatusCode.TIMEOUT, ""));
                        responseListeners.Remove(key);
                        timersToRemove.Add(key);
                        break;
                    }
                }

                foreach (int key in timersToRemove)
                {
                    responseTimers.Remove(key);
                }
            }

            public override void incomeMessage(string message)
            {
                NetworkPacket packet = new NetworkPacket(message);

                if (packet.to == networkName || packet.type == NetworkPacketType.BROADCAST)
                {
                    //Log("Recieved packed:" + message);
                }

                if(packet.type == NetworkPacketType.BROADCAST)
                {
                    sendPacket(generateId(), NetworkPacketType.BROADCAST_RESPONSE, packet.from);
                }

                if (packet.to == networkName)
                {
                    if (packet.type == NetworkPacketType.PING)
                    {
                        sendPacket(generateId(), NetworkPacketType.PING_RESPONSE, packet.from);
                    }
                    else if (packet.type == NetworkPacketType.BROADCAST_RESPONSE)
                    {
                        if (network.Contains(packet.from) == false)
                        {
                            network.Add(packet.from);
                        }
                    }
                    else if (packet.type == NetworkPacketType.REQUEST)
                    {
                        resendToListeners(packet);
                    }
                    else if (packet.type == NetworkPacketType.RESPONSE)
                    {
                        if (responseListeners.ContainsKey(packet.id))
                        {
                            responseListeners[packet.id].Invoke(packet);
                            responseListeners.Remove(packet.id);
                            responseTimers.Remove(packet.id);
                        }
                    }
                }
            }

            private void resendToListeners(NetworkPacket packet)
            {
                foreach(var listener in requestListeners)
                {
                    listener.request(packet);
                }
            }

            public void scanNetwork(Action onComplete)
            {
                network.Clear();
                int TICKS_COMPLETE_SCAN = 2;
                ticksToCompleteScan = TICKS_COMPLETE_SCAN;
                onScanNetworkComplete += onComplete;
                isScanningNetwork = true;
                sendPacket(generateId(), NetworkPacketType.BROADCAST);
            }

            void ScanNetworkComplete()
            {
                onScanNetworkComplete?.Invoke();
                onScanNetworkComplete = null;
            }

            void sendPacket(int id, NetworkPacketType packetType, string to = "", string data = "")
            {
                NetworkPacket packet = new NetworkPacket(id, packetType, to, networkName, NetworkStatusCode.OK, data);
                packetsToSend.Add(packet);
            }

            void findRequiredBlocks()
            {
                IMyBlockGroup groupBlocks = GTS.GetBlockGroupWithName(groupName);

                List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
                groupBlocks?.GetBlocksOfType<IMyRadioAntenna>(blocks);

                blocks.ForEach(b => antenna = b as IMyRadioAntenna);
                //if(antenna==null)
                //{
                //    Log(groupName + ": Can't find antenna");
                //}
            }

            void renameBlocks()
            {
                if (antenna != null)
                {
                    antenna.CustomName = groupName + " Antenna (" + networkName + ")";
                }
            }

            public void sendRequest(string to, string data, Action<NetworkPacket> response)
            {
                int id = generateId();
                responseListeners.Add(id, response);
                int TIMEOUT = 2;
                
                responseTimers.Add(id, TIMEOUT);
                
                sendPacket(id, NetworkPacketType.REQUEST, to, data);
            }

            public void sendResponse(int requestId, string to, string data)
            {
                sendPacket(requestId, NetworkPacketType.RESPONSE, to, data);
            }

            int generateId()
            {
                return idGenerator++;
            }
        }

        public class TestListener : IRequestListener
        {
            NetworkService networkService;

            public TestListener(NetworkService networkService)
            {
                this.networkService = networkService;
                networkService.registerRequestListener(this);
            }

            public void request(NetworkPacket packet)
            {
                networkService.sendResponse(packet.id, packet.from, "<<RESPONCE DATA ID:" + packet.id +">>");
            }
        }

        TestListener testListener;

        #endregion //NetworkService

        #region FuelService
        public class FuelService : BaseShipService
        {
            List<IMyGasGenerator> gasGenerators = new List<IMyGasGenerator>();
            List<IMyReactor> reactors = new List<IMyReactor>();

            public FuelService()
            {
                groupName = "[Fuel Service]";
                init();
            }

            void init()
            {
                findRequiredBlocks();
                renameBlocks();
            }

            override public void update100()
            {

                init();
                if (gasGenerators != null && gasGenerators.Count > 1)
                {
                    sortOreOfType(InvetoryItems.Ores.Ice, gasGenerators);
                }

                if (reactors != null && reactors.Count > 1)
                {
                    sortOreOfType(InvetoryItems.Ingots.UraniumIngot, reactors);
                }
            }

            void findRequiredBlocks()
            {
                IMyBlockGroup groupBlocks = GTS.GetBlockGroupWithName(groupName);
                groupBlocks?.GetBlocksOfType(gasGenerators);
                groupBlocks?.GetBlocksOfType(reactors);
            }

            void renameBlocks()
            {
                if (gasGenerators != null)
                {
                    for (int i = 0; i < gasGenerators.Count; i++)
                    {
                        IMyGasGenerator gasGenerator = gasGenerators[i];
                        gasGenerator.CustomName = groupName + " Gas Generator " + (i + 1);
                    }
                }

                if (reactors != null)
                {
                    for (int i = 0; i < reactors.Count; i++)
                    {
                        IMyReactor reactor = reactors[i];
                        reactor.CustomName = groupName + " Reactor " + (i + 1);
                    }
                }
            }
        }
        #endregion //FuelService

        #region ParkingService
        public class ParkingService : BaseShipService
        {
            float maxSensorDistance = 10f;
            IMyLightingBlock lightingBlock = null;
            IMyCameraBlock cameraSensor = null;

            public ParkingService()
            {
                groupName = "[Parking Service]";
                init();
            }

            void init()
            {
                findRequiredBlocks();
                renameBlocks();
                if (cameraSensor != null)
                {
                    cameraSensor.EnableRaycast = true;
                    if (cameraSensor.CustomData != String.Empty)
                    {
                        float.TryParse(cameraSensor.CustomData, out maxSensorDistance);
                    }
                }
            }

            override public void update500()
            {
                //reinit
                init();
            }

            override public void update10()
            {
                if (cameraSensor != null)
                {
                    MyDetectedEntityInfo lastDetectedEntityInfo = cameraSensor.Raycast(maxSensorDistance);
                    //Log(lastDetectedEntityInfo.Type.ToString());
                    if (lastDetectedEntityInfo.Type != MyDetectedEntityType.None && lightingBlock != null)
                    {
                        lightingBlock.Enabled = true;
                    }
                    else
                    {
                        lightingBlock.Enabled = false;
                    }
                    Vector3 sensorPosition = cameraSensor.WorldMatrix.Translation;
                    float distance = float.PositiveInfinity;
                    if (lastDetectedEntityInfo.HitPosition != null)
                    {
                        distance = Vector3.Distance(sensorPosition, lastDetectedEntityInfo.HitPosition.Value);
                        changeLightColor(distance);
                    }

                    //Log("Distance: " + distance);
                }
            }

            void findRequiredBlocks()
            {
                IMyBlockGroup groupBlocks = GTS.GetBlockGroupWithName(groupName);

                List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
                groupBlocks?.GetBlocksOfType<IMyCameraBlock>(blocks);

                blocks.ForEach(b => cameraSensor = b as IMyCameraBlock);

                groupBlocks?.GetBlocksOfType<IMyLightingBlock>(blocks);
                blocks.ForEach(b => lightingBlock = b as IMyLightingBlock);
            }

            void changeLightColor(float distance)
            {
                Vector3 redColor = new Vector3(1f, 0f, 0f);
                Vector3 greenColor = new Vector3(0f, 1f, 0f);
                Vector3 color = Vector3.Lerp(redColor, greenColor, distance / maxSensorDistance);
                if (lightingBlock != null)
                {
                    lightingBlock.Color = new Color(color);
                }
            }

            void renameBlocks()
            {
                if (cameraSensor != null)
                {
                    cameraSensor.CustomName = groupName + " Camera Ground Sensor";
                }

                if (lightingBlock != null)
                {
                    lightingBlock.CustomName = groupName + " Light Distance Meter";
                }
            }
        }
        #endregion //Parking Service

        #region InfoService
        public class InfoService : BaseShipService
        {
            Dictionary<string, float> inventoryItems;
            List<IMyTextPanel> panelsToShowInventory;

            Dictionary<IMyTextPanel, List<KeyValuePair<InfoType, ProgressBar>>> progressBars = new Dictionary<IMyTextPanel, List<KeyValuePair<InfoType, ProgressBar>>>();

            Dictionary<IMyTextPanel, string[]> drawBuffers = new Dictionary<IMyTextPanel, string[]>();

            enum InfoType
            {
                Energy,
                Cargo,
                Hydrogen,
                Fuel
            }

            public InfoService()
            {
                groupName = "[Info Service]";
                inventoryItems = new Dictionary<string, float>();
                panelsToShowInventory = new List<IMyTextPanel>();

                init();
            }

            private void createDrawBuffer(IMyTextPanel textPanel)
            {
                if (!drawBuffers.ContainsKey(textPanel))
                {
                    var drawBuffer = new string[43];
                    for (int i = 0; i < drawBuffer.Length; i++)
                    {
                        drawBuffer[i] = new string(SColor.getColor(0, 0, 0), 90);
                    }

                    drawBuffers.Add(textPanel, drawBuffer);
                }
            }

            void init()
            {
                findRequiredBlocks();
            }

            override public void update500()
            {
                //reinit
                init();
            }

            override public void update100()
            {
                updateCargoInfo();
                updateShipInfo();
            }

            private void updateShipInfo()
            {
                float energy = getEnergyValue();
                float cargo = getShipCargo();
                float fuel = getShipFuel();
                float hydrogen = getHydrogen();

                foreach (var textPanel in progressBars.Keys)
                {
                    foreach(var pb in progressBars[textPanel])
                    {
                        if (pb.Key == InfoType.Energy)
                        {
                            pb.Value.setPercent(energy);
                        }
                        else if(pb.Key == InfoType.Cargo)
                        {
                            pb.Value.setPercent(cargo);
                        }
                        else if(pb.Key == InfoType.Fuel)
                        {
                            pb.Value.setPercent(fuel);
                        }
                        else if(pb.Key == InfoType.Hydrogen)
                        {
                            pb.Value.setPercent(hydrogen);
                        }
                    }

                    textPanel.WritePublicText(String.Empty);
                    foreach (var line in drawBuffers[textPanel])
                    {
                        textPanel.WritePublicText(line + "\n", true);
                    }
                }
            }

            private float getHydrogen()
            {
                var tanks = GetBlocksOfType<IMyGasTank>();
                float fillRatio = 0f;
                foreach(var tank in tanks)
                {
                    fillRatio += (float)tank.FilledRatio;
                }

                float count = tanks.Count > 0 ? tanks.Count : 1f;

                return fillRatio / count;
            }

            private float getShipFuel()
            {
                var gasGenerators = GetBlocksOfType<IMyGasGenerator>();
                float maxCapacity = 0.00000001f;
                float currentCapacity = 0f;
                foreach(var generator in gasGenerators)
                {
                    currentCapacity += (float)generator.GetInventory().CurrentVolume;
                    maxCapacity += (float)generator.GetInventory().MaxVolume;
                }

                return currentCapacity / maxCapacity;
            }

            private float getShipCargo()
            {
                var drills = GetBlocksOfType<IMyShipDrill>();
                var containers = GetBlocksOfType<IMyCargoContainer>();
                var connectors = GetBlocksOfType<IMyShipConnector>();

                float maxVolume = 0f;
                float currentVolume = 0f;

                foreach(var drill in drills)
                {
                    currentVolume += (float)drill.GetInventory().CurrentVolume;
                    maxVolume += (float)drill.GetInventory().MaxVolume;
                }

                foreach (var container in containers)
                {
                    currentVolume += (float)container.GetInventory().CurrentVolume;
                    maxVolume += (float)container.GetInventory().MaxVolume;
                }

                foreach (var connector in connectors)
                {
                    currentVolume += (float)connector.GetInventory().CurrentVolume;
                    maxVolume += (float)connector.GetInventory().MaxVolume;
                }

                return currentVolume / maxVolume;
            }

            private float getEnergyValue()
            {
                var batteries = GetBlocksOfType<IMyBatteryBlock>();
                float maxCapacity = 0f;
                float currentCapacity = 0f;
                foreach(var battery in batteries)
                {
                    maxCapacity += battery.MaxStoredPower;
                    currentCapacity += battery.CurrentStoredPower;
                }

                if (maxCapacity == 0f)
                    return 1f;

                return currentCapacity / maxCapacity;
            }

            private void updateCargoInfo()
            {
                inventoryItems.Clear();
                getInventoryItems<IMyCargoContainer>(inventoryItems);
                getInventoryItems<IMyShipDrill>(inventoryItems);
                getInventoryItems<IMyShipGrinder>(inventoryItems);
                getInventoryItems<IMyShipController>(inventoryItems);
                getInventoryItems<IMyShipConnector>(inventoryItems);

                foreach (var textPanel in panelsToShowInventory)
                {
                    if(textPanel.CustomData.Contains("All"))
                    {
                        bool append = false;
                        foreach(var kv in inventoryItems)
                        {
                            textPanel.WritePublicText(getShortItemName(kv.Key) + ":" + (int)kv.Value + "\n", append);
                            append = true;
                        }
                    }

                    if (textPanel.CustomData.Contains("Ores"))
                    {
                        bool append = false;
                        foreach (var kv in inventoryItems)
                        {
                            if (isOre(kv.Key))
                            {
                                textPanel.WritePublicText(getShortItemName(kv.Key) + ":" + (int)kv.Value + "\n", append);
                                append = true;
                            }
                        }
                    }

                    if (textPanel.CustomData.Contains("Ingots"))
                    {
                        bool append = false;
                        foreach (var kv in inventoryItems)
                        {
                            if (isIngot(kv.Key))
                            {
                                textPanel.WritePublicText(getShortItemName(kv.Key) + ":" + (int)kv.Value + "\n", append);
                                append = true;
                            }
                        }
                    }
                }
            }

            private bool isIngot(string itemName)
            {
                return itemName.Contains("Ingot");
            }

            private bool isOre(string itemName)
            {
                return itemName.Contains("Ore");
            }

            void findRequiredBlocks()
            {
                panelsToShowInventory.Clear();
                progressBars.Clear();
                var allTextPanels = GetBlocksOfType<IMyTextPanel>();
                foreach(var textPanel in allTextPanels)
                {
                    if (textPanel.CustomData.Contains("Ores") ||
                       textPanel.CustomData.Contains("Ingots") ||
                       textPanel.CustomData.Contains("All"))
                    {
                        panelsToShowInventory.Add(textPanel);
                    }
                    else
                    {
                        var list = new List<KeyValuePair<InfoType, ProgressBar>>();
                        createDrawBuffer(textPanel);
                        if (textPanel.CustomData.Contains("Energy"))
                        {
                            string[] drawBuffer = drawBuffers[textPanel];
                            var energyProgressBar = new ProgressBar(0, list.Count * 11, ref drawBuffer, Icons.Energy, 59);
                            energyProgressBar.setDrawColor(0, 7, 0);
                            energyProgressBar.setPBColor(new SColor(7, 0, 0), new SColor(0, 7, 0));
                            KeyValuePair<InfoType, ProgressBar> pair = new KeyValuePair<InfoType, ProgressBar>(InfoType.Energy, energyProgressBar);
                            list.Add(pair);
                        }

                        if (textPanel.CustomData.Contains("Fuel"))
                        {
                            string[] drawBuffer = drawBuffers[textPanel];
                            var fuelProgressBar = new ProgressBar(0, list.Count * 11, ref drawBuffer, Icons.Fuel, 59);
                            fuelProgressBar.setDrawColor(0, 5, 5);
                            fuelProgressBar.setPBColor(new SColor(7, 0, 0), new SColor(0, 5, 5));
                            KeyValuePair<InfoType, ProgressBar> pair = new KeyValuePair<InfoType, ProgressBar>(InfoType.Fuel, fuelProgressBar);
                            list.Add(pair);
                        }

                        if (textPanel.CustomData.Contains("Hydrogen"))
                        {
                            string[] drawBuffer = drawBuffers[textPanel];
                            var hydrogenPB = new ProgressBar(0, list.Count * 11, ref drawBuffer, Icons.Hydrogen, 59);
                            hydrogenPB.setDrawColor(0, 1, 5);
                            hydrogenPB.setPBColor(new SColor(0, 0, 2), new SColor(0, 1, 5));
                            KeyValuePair<InfoType, ProgressBar> pair = new KeyValuePair<InfoType, ProgressBar>(InfoType.Hydrogen, hydrogenPB);
                            list.Add(pair);
                        }

                        if (textPanel.CustomData.Contains("Cargo"))
                        {
                            string[] drawBuffer = drawBuffers[textPanel];
                            var cargoPB = new ProgressBar(0, list.Count * 11, ref drawBuffer, Icons.Cargo, 59);
                            cargoPB.setDrawColor(7, 7, 0);
                            cargoPB.setPBColor(new SColor(7, 7, 0), new SColor(7, 0, 0));
                            KeyValuePair<InfoType, ProgressBar> pair = new KeyValuePair<InfoType, ProgressBar>(InfoType.Cargo, cargoPB);
                            list.Add(pair);
                        }

                        if (progressBars.ContainsKey(textPanel))
                        {
                            progressBars[textPanel] = list;
                        }
                        else
                        {
                            progressBars.Add(textPanel, list);
                        }
                    }

                }
            }

            void renameBlocks()
            {

            }
        }
        #endregion //Parking Service

        #region DebugScreenService
        class DebugScreenService : BaseShipService
        {
            IMyTextPanel debugTextPanel;

            public DebugScreenService()
            {
                groupName = "[DebugScreenService]";
                init();
                debugTextPanel?.WritePublicText("", false);
            }

            public void Log(string text, bool append)
            {
                debugTextPanel?.WritePublicText(text + "\n", append);
            }

            void init()
            {
                findRequiredBlocks();
                renameBlocks();
            }

            public override void update500()
            {
                //reinit
                init();
            }

            void findRequiredBlocks()
            {
                List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
                IMyBlockGroup groupBlocks = GTS.GetBlockGroupWithName(groupName);
                groupBlocks?.GetBlocksOfType<IMyTextPanel>(blocks);
                blocks.ForEach(b => debugTextPanel = b as IMyTextPanel);
            }

            void renameBlocks()
            {
                if (debugTextPanel != null)
                {
                    debugTextPanel.CustomName = groupName + " LCD";
                }
            }
        }
        #endregion //DebugScreenService

        #region PilotingService
        public class PilotingService : BaseShipService
        {
            float raycastDistance = 2000f;
            IMyCameraBlock camera = null;
            List<IMyGyro> gyroscopes;
            Vector3D destination = new Vector3D();

            public PilotingService()
            {
                groupName = "[Piloting Service]";
                init();
            }

            public void SetDestination(Vector3D destination)
            {
                this.destination = destination;
            }

            void init()
            {
                findRequiredBlocks();
                renameBlocks();
                if (camera != null)
                {
                    camera.EnableRaycast = true;
                    if (camera.CustomData != String.Empty)
                    {
                        float.TryParse(camera.CustomData, out raycastDistance);
                    }

                    if (gyroscopes != null && gyroscopes.Count > 0)
                    {
                        var gyro = gyroscopes[0];
                        //gyro.GyroOverride = true;
                    }
                }
            }

            override public void update500()
            {
                //reinit
                init();
            }

            override public void update(float dt)
            {
                rotateTowardDestination();
            }

            void rotateTowardDestination()
            {
                if (gyroscopes.Count > 0 && camera!=null)
                {
                    Vector3D forward = getForwardVector();
                    Vector3D up = getUpVector();
                    Vector3D right = getRightVector();
                    right.Normalize();
                    up.Normalize();
                    Vector3D directionTowardDestination = destination - getPosition();

                    directionTowardDestination.Normalize();

                    Vector3D upForwardProjection = Vector3D.ProjectOnPlane(ref directionTowardDestination, ref right);
                    Vector3D rightForwardProjection = Vector3D.ProjectOnPlane(ref directionTowardDestination, ref up);

                    forward.Normalize();
                    upForwardProjection.Normalize();
                    rightForwardProjection.Normalize();

                    double elevation = getAngleBetweenVectors(forward, upForwardProjection) * getPitchSign(upForwardProjection, forward, right);
                    double azimuth = getAngleBetweenVectors(forward, rightForwardProjection) * getYawSign(rightForwardProjection, forward, up);

                    float maxRPM = 2f;

                    float azimuthIntent = Math.Sign(azimuth) * (float)Math.Min(maxRPM, Math.Abs(azimuth * 2f));
                    float elevationIntent = Math.Sign(elevation) * (float)Math.Min(maxRPM, Math.Abs(elevation * 2f));


                    //Log("Elevation : " + (int)radiansToDegrees(elevation), false);
                    //Log("Azimuth : " + (int)radiansToDegrees(azimuth));

                    //Log("Yaw " + gyroscopes[0].Yaw);
                    //Log("Pitch " + gyroscopes[0].Pitch);
                    //Log("Roll " + gyroscopes[0].Roll);

                    setShipAzimuthAndElevationIntents(azimuthIntent, elevationIntent);
                }
            }

            struct Angles
            {
                public float Yaw;
                public float Pitch;
                public float Roll;
            }

            private void setShipAzimuthAndElevationIntents(float azimuthIntent, float elevationIntent)
            {
                Angles angles = new Angles();
                angles.Pitch = elevationIntent;
                angles.Yaw = azimuthIntent;
                angles.Roll = 0;

                angles = convertAnglesByOrientation(camera.Orientation, angles);


                foreach (IMyGyro gyro in gyroscopes)
                {
                    Angles convertedAngles = convertAnglesByOrientation(gyro.Orientation, angles);
                    gyro.Yaw = convertedAngles.Yaw;
                    gyro.Pitch = convertedAngles.Pitch;
                    gyro.Roll = convertedAngles.Roll;
                }
            }

            private Angles convertAnglesByOrientation(MyBlockOrientation orientation, Angles angles)
            {
                Angles converted = new Angles();

                if (orientation.Forward == Base6Directions.Direction.Forward)
                {
                    converted.Roll = angles.Roll;
                }
                else if(orientation.Forward == Base6Directions.Direction.Backward)
                {
                    converted.Roll = -angles.Roll;
                }
                else if(orientation.Forward == Base6Directions.Direction.Left)
                {
                    converted.Roll = angles.Pitch;
                }
                else if (orientation.Forward == Base6Directions.Direction.Right)
                {
                    converted.Roll = -angles.Pitch;
                }
                else if (orientation.Forward == Base6Directions.Direction.Up)
                {
                    converted.Roll = -angles.Yaw;
                }
                else if (orientation.Forward == Base6Directions.Direction.Down)
                {
                    converted.Roll = angles.Yaw;
                }
                //--------------------------------------------------------------------
                if (orientation.Left == Base6Directions.Direction.Forward)
                {
                    converted.Pitch = angles.Roll;
                }
                else if (orientation.Left == Base6Directions.Direction.Backward)
                {
                    converted.Pitch = -angles.Roll;
                }
                else if (orientation.Left == Base6Directions.Direction.Left)
                {
                    converted.Pitch = angles.Pitch;
                }
                else if (orientation.Left == Base6Directions.Direction.Right)
                {
                    converted.Pitch = -angles.Pitch;
                }
                else if (orientation.Left == Base6Directions.Direction.Up)
                {
                    converted.Pitch = -angles.Yaw;
                }
                else if (orientation.Left == Base6Directions.Direction.Down)
                {
                    converted.Pitch = angles.Yaw;
                }
                //--------------------------------------------------------------------
                if (orientation.Up == Base6Directions.Direction.Forward)
                {
                    converted.Yaw = angles.Roll;
                }
                else if (orientation.Up == Base6Directions.Direction.Backward)
                {
                    converted.Yaw = -angles.Roll;
                }
                else if (orientation.Up == Base6Directions.Direction.Left)
                {
                    converted.Yaw = -angles.Pitch;
                }
                else if (orientation.Up == Base6Directions.Direction.Right)
                {
                    converted.Yaw = angles.Pitch;
                }
                else if (orientation.Up == Base6Directions.Direction.Up)
                {
                    converted.Yaw = angles.Yaw;
                }
                else if (orientation.Up == Base6Directions.Direction.Down)
                {
                    converted.Yaw = -angles.Yaw;
                }


                return converted;
            }

            private Vector3D getRightVector()
            {
                return camera.WorldMatrix.GetDirectionVector(Base6Directions.Direction.Right);
            }

            private Vector3D getUpVector()
            {
                return camera.WorldMatrix.GetDirectionVector(Base6Directions.Direction.Up);
            }

            private Vector3D getForwardVector()
            {
                return camera.WorldMatrix.GetDirectionVector(Base6Directions.Direction.Forward);
            }

            Vector3D getPosition()
            {
                return camera.WorldMatrix.Translation;
            }

            void findRequiredBlocks()
            {
                IMyBlockGroup groupBlocks = GTS.GetBlockGroupWithName(groupName);

                List<IMyCameraBlock> cameraBlocks = new List<IMyCameraBlock>();
                groupBlocks?.GetBlocksOfType(cameraBlocks);

                cameraBlocks.ForEach(b => camera = b);

                gyroscopes = GetBlocksOfTypeInGroup<IMyGyro>(groupName);

            }

            void renameBlocks()
            {
                if (camera != null)
                {
                    camera.CustomName = groupName + " Forward Camera";
                }

                if(gyroscopes!=null)
                {
                    for(int i = 0; i < gyroscopes.Count; i++)
                    {
                        IMyGyro gyro = gyroscopes[i];
                        gyro.CustomName = groupName + " Gyro " + (i + 1);
                    }
                }
            }

            double getAngleBetweenVectors(Vector3D Va, Vector3D Vb)
            {
                double angle = Math.Acos(Vector3D.Dot(Va, Vb));
                return angle;
            }

            int getYawSign(Vector3D rightForwardProjection, Vector3D forward, Vector3D up)
            {
                return Math.Sign(Vector3D.Dot(up, Vector3D.Cross(rightForwardProjection, forward)));
            }

            int getPitchSign(Vector3D upForwardProjection, Vector3D forward, Vector3D right)
            {
                return Math.Sign(Vector3D.Dot(right, Vector3D.Cross(upForwardProjection, forward)));
            }

            double radiansToDegrees(double radians)
            {
                return radians * 180.0 / Math.PI;
            }
        }
        #endregion //Piloting Service
        //--------------------------------------


        /// ////////////////////////////////////////////////////PROGRAMM////////////////////////////////////////////////////////////////////

        static IMyGridTerminalSystem GTS;
        static IMyProgrammableBlock Self;
        List<IShipService> shipServices = new List<IShipService>();

        NetworkService networkService = null;

        public Program()
        {
            try
            {
                GTS = GridTerminalSystem;
                Self = Me;
                DebugScreenService debugService = new DebugScreenService();
                LogImpl = (string text, bool append) => { debugService.Log(text, append); };
                //networkService = new NetworkService(generateUniqueNetworkName());
                //shipServices.Add(networkService);
                shipServices.Add(debugService);
                shipServices.Add(new FuelService());
                shipServices.Add(new ParkingService());
                //var pilotingService = new PilotingService();
                //pilotingService.SetDestination(new Vector3D(0, 5000, 5000));
                //shipServices.Add(pilotingService);

                //testListener = new TestListener(networkService);
                shipServices.Add(new InfoService());

                Runtime.UpdateFrequency = UpdateFrequency.Update1 | UpdateFrequency.Update10 | UpdateFrequency.Update100;
            }
            catch (Exception e)
            {
                Echo(e.StackTrace);
            }
        }

        public void Save()
        {
            
        }
        
        string generateUniqueNetworkName()
        {
            return Math.Abs(Me.WorldMatrix.Translation.ToString().GetHashCode()).ToString();
        }

        int executionCounter = 0;

        public void Main(string argument, UpdateType updateSource)
        {
            try
            {
                executionCounter++;
                foreach (IShipService service in shipServices)
                {
                    if ((updateSource & UpdateType.Update1) != 0)
                    {
                        service.update((float)Runtime.TimeSinceLastRun.TotalSeconds);
                    }

                    if ((updateSource & UpdateType.Update10) != 0)
                    {
                        service.update10();
                    }

                    if ((updateSource & UpdateType.Update100) != 0)
                    {
                        service.update100();
                    }

                    if (executionCounter % 500 == 0)
                    {
                        service.update500();
                    }

                    if (updateSource == UpdateType.Antenna)
                    {
                        service.incomeMessage(argument);
                    }
                }

                if (updateSource == UpdateType.Trigger && argument == "request")
                {
                    Log("Scanning network...");
                    networkService?.scanNetwork(()=> {
                        Log("Scan completed");
                        foreach (string name in networkService.network)
                        {
                            Log(name);
                        }
                    });

                    if (networkService.network.Count > 0)
                    {
                        string to = networkService.network[0];
                        Log("Try to send request to: " + to);
                        Log("Waiting responce...");
                        networkService.sendRequest(to, "request to ", (NetworkPacket packet) =>
                        {
                            Log("Responce: " + packet.data + " Code:" + packet.code);
                        });
                    }
                }
            }
            catch(Exception e)
            {
                Log(e.StackTrace);
                Echo(e.StackTrace);
            }
        }

        /// ////////////////////////////////////////////////////PROGRAMM END////////////////////////////////////////////////////////////////////
        
#region Libraries

        static VRage.MyFixedPoint getSumAmountItemsOfType<TBlockType>(string subtype, List<TBlockType> blocks) where TBlockType : class, IMyTerminalBlock
        {
            VRage.MyFixedPoint amount = 0;
            blocks.ForEach(block =>
            {
                List<IMyInventoryItem> items = new List<IMyInventoryItem>();
                items = block.GetInventory().GetItems();

                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    string typeName = item.GetDefinitionId().ToString();
                    //Log("Typename" + typeName);
                    if (typeName.Contains(subtype))
                    {
                        amount += item.Amount;
                    }
                    //Echo(typeName); 
                }
            });
            return amount;
        }

        static VRage.MyFixedPoint getAmountItemsOfType(string subtype, IMyTerminalBlock block)
        {
            VRage.MyFixedPoint amount = 0;

            List<IMyInventoryItem> items = new List<IMyInventoryItem>();
            items = block.GetInventory().GetItems();

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                string typeName = item.GetDefinitionId().ToString();
                if (typeName == subtype)
                {
                    amount += item.Amount;
                }
                //Echo(typeName); 
            }

            return amount;
        }

        static int getItemIndex(string subtype, IMyTerminalBlock block)
        {
            List<IMyInventoryItem> items = new List<IMyInventoryItem>();
            items = block.GetInventory(0).GetItems();


            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                string typeName = item.GetDefinitionId().ToString();
                if (typeName == subtype)
                {
                    return i;
                }
                //Echo(typeName); 
            }

            return 0;
        }

        public class InvetoryItems
        {
            public class Ingots
            {
                static public string UraniumIngot = "MyObjectBuilder_Ingot/Uranium";
            }

            public class Ores
            {
                static public string Ice = "MyObjectBuilder_Ore/Ice";
                public static string Iron = "MyObjectBuilder_Ore/Iron";
                internal static string Nickel = "MyObjectBuilder_Ore/Nickel";
                internal static string Cobalt = "MyObjectBuilder_Ore/Cobalt";
                internal static string Magnesium = "MyObjectBuilder_Ore/Magnesium";
                internal static string Silicon = "MyObjectBuilder_Ore/Silicon";
                internal static string Silver = "MyObjectBuilder_Ore/Silver";
                internal static string Gold = "MyObjectBuilder_Ore/Gold";
                internal static string Platinum = "MyObjectBuilder_Ore/Platinum";
                internal static string Uranium = "MyObjectBuilder_Ore/Uranium";
            }
        };


        public static void sortOreOfType<TBlockType>(string typename, List<TBlockType> containers) where TBlockType : class, IMyTerminalBlock
        {
            int averageOreAmout = ((int)getSumAmountItemsOfType(typename, containers) / containers.Count);
            //Echo("" + averageOreAmout); 


            containers.Sort(delegate (TBlockType b1, TBlockType b2)
            {
                int result = (int)(getAmountItemsOfType(typename, b2) - getAmountItemsOfType(typename, b1));
                return result;
            });

            for (int i = 0; i < containers.Count - 1; i++)
            {
                IMyTerminalBlock containerSource = containers[i];
                IMyTerminalBlock containerDest = containers[i + 1];
                VRage.MyFixedPoint amountToMove = getAmountItemsOfType(typename, containerSource) - averageOreAmout;
                if (amountToMove > 0 && !containerSource.GetInventory(0).IsFull)
                {
                    int sourceItemIndex = getItemIndex(typename, containerSource);
                    int destItemIndex = getItemIndex(typename, containerDest);

                    containerSource.GetInventory(0).TransferItemTo(containerDest.GetInventory(0), sourceItemIndex,
                                destItemIndex, true, amountToMove);
                }
            }
        }

        static string getShortOreName(string name)
        {
            Dictionary<string, string> ores = new Dictionary<string, string>()
            {
                {InvetoryItems.Ores.Iron, "Fe"},
                {InvetoryItems.Ores.Nickel, "Ni"},
                {InvetoryItems.Ores.Cobalt, "Co"},
                {InvetoryItems.Ores.Magnesium, "Mg"},
                {InvetoryItems.Ores.Silicon, "Si"},
                {InvetoryItems.Ores.Silver, "Ag"},
                {InvetoryItems.Ores.Gold, "Au"},
                {InvetoryItems.Ores.Platinum, "Pt"},
                {InvetoryItems.Ores.Uranium, "Ur"}
            };

            string ret = getShortItemName(name);
            if (ores.ContainsKey(name))
                ret = ores[name];

            return ret;
        }

        static string getShortItemName(string name)
        {
            string[] splitted = name.Split('/');
            string ret = splitted.Length > 1 ? splitted[1] : name;
            return ret;
        }


        static void ApplyActionTo<TBlockType>(string action)
        where TBlockType : class, IMyTerminalBlock
        {
            List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
            GTS.GetBlocksOfType<TBlockType>(blocks, b => b.CubeGrid == Self.CubeGrid);
            blocks.ForEach(it =>
            {
                TBlockType block = (TBlockType)it;
                block.ApplyAction(action);
            });
        }

        static void SetValueTo<TBlockType, T>(string propertyName, T value)
        where TBlockType : class, IMyTerminalBlock
        {
            List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
            GTS.GetBlocksOfType<TBlockType>(blocks, b => b.CubeGrid == Self.CubeGrid);
            blocks.ForEach(it =>
            {
                TBlockType block = (TBlockType)it;
                block.SetValue(propertyName, value);
            });
        }

        static void getInventoryItems<TBlockType>(Dictionary<string, float> cargoItems)
        where TBlockType : class, IMyTerminalBlock
        {
            List<IMyInventoryItem> inventoryItems = new List<IMyInventoryItem>();

            List<IMyTerminalBlock> cargosList = new List<IMyTerminalBlock>();
            GTS.GetBlocksOfType<TBlockType>(cargosList, b => b.CubeGrid == Self.CubeGrid);
            cargosList.ForEach(it =>
            {
                TBlockType cargo = it as TBlockType;

                List<IMyInventoryItem> items = new List<IMyInventoryItem>();
                if (cargo.InventoryCount > 0)
                {
                    items = cargo.GetInventory().GetItems();

                    for (int j = 0; j < items.Count; ++j)
                    {
                        var item = items[j];
                        float amount = (float)item.Amount;
                        string typeName = item.GetDefinitionId().ToString();
                        if (cargoItems.ContainsKey(typeName))
                        {
                            cargoItems[typeName] += amount;
                        }
                        else
                        {
                            cargoItems.Add(typeName, amount);
                        }
                    }
                }
            });
        }

        static TBlockType GetBlockWithName<TBlockType>(string name)
        where TBlockType : class, IMyTerminalBlock
        {
            List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
            GTS.SearchBlocksOfName(name, blocks);
            foreach (var block in blocks)
            {
                if (block.CubeGrid == Self.CubeGrid)
                    return (TBlockType)block;
            }

            return null;
        }

        static List<TBlockType> GetBlocksOfType<TBlockType>(bool onlyThisGrid = true)
        where TBlockType : class, IMyTerminalBlock
        {
            List<TBlockType> blocks = new List<TBlockType>();
            GTS.GetBlocksOfType(blocks, b => onlyThisGrid ? b.CubeGrid == Self.CubeGrid  : true );

            return blocks;
        }

        static List<TBlockType>  GetBlocksOfTypeInGroup<TBlockType>(string groupName)
        where TBlockType : class, IMyTerminalBlock
        {
            List<TBlockType> blocks = new List<TBlockType>();
            var group = GTS.GetBlockGroupWithName(groupName);
            group?.GetBlocksOfType(blocks);
            return blocks;
        }

        public class Icons
        {
            static public string[] Energy = {
                            "█████████",
                            "██     ██",
                            "█       █",
                            "█ █████ █",
                            "█ ███ █ █",
                            "█ █   █ █",
                            "█ █ ███ █",
                            "█ █████ █",
                            "█       █",
                            "█████████"};

            static public string[] Hydrogen = {
                            "█████████",
                            "███   ███",
                            "█   █   █",
                            "█ █████ █",
                            "█ █ █ █ █",
                            "█ █   █ █",
                            "█ █ █ █ █",
                            "█ █████ █",
                            "█       █",
                            "█████████"};

            static public string[] Fuel = {
                            "█████████",
                            "█     ███",
                            "█ ███   █",
                            "█ █████ █",
                            "█ █ █ █ █",
                            "█ ██ ██ █",
                            "█ █ █ █ █",
                            "█ █████ █",
                            "█       █",
                            "█████████"};

            static public string[] Cargo = {
                            "█████████",
                            "█       █",
                            "█ █████ █",
                            "█  ███  █",
                            "█ █████ █",
                            "█ █████ █",
                            "█ █████ █",
                            "█ █████ █",
                            "█       █",
                            "█████████"};

        }

        public struct SColor
        {
            public byte r;
            public byte g;
            public byte b;

            public SColor(byte r, byte g, byte b)
            {
                this.r = r;
                this.g = g;
                this.b = b;
            }

            public static char getColor(byte r, byte g, byte b)
            {
                return (char)(0xe100 + (r << 6) + (g << 3) + b);
            }

            public char getColor()
            {
                return getColor(r, g, b);
            }
        }

        public class ProgressBar
        {

            int y = 0;
            int x = 0;
            char symbolToDraw;
            char backgroundSymbol;
            char colorToDrawPB;
            int pbSizeWithEdge;
            int pbSize;
            int height = 10;
            string[] icon;
            string[] drawBuffer;
            float percent = 1f;
            string pattern;
            string drawPattern;
            SColor colorFrom;
            SColor colorTo;
            bool dynamicPBColor = false;

            public ProgressBar(int x, int y, ref string[] drawBuffer, string[] icon, int size, string pattern = "*** ")
            {
                this.x = x + icon[0].Length;
                this.y = y;
                pbSizeWithEdge = size;
                pbSize = pbSizeWithEdge - 3;
                this.pattern = pattern;
                this.drawBuffer = drawBuffer;
                this.icon = icon;
                backgroundSymbol = SColor.getColor(0, 0, 0);
                setDrawColor(7, 7, 7);
            }

            public void setPercent(float percent)
            {
                this.percent = percent;
                draw();
            }

            void draw()
            {
                drawIcon();
                replacePBColor();
                StringBuilder upBottomline = new StringBuilder();
                StringBuilder gapLine = new StringBuilder();
                StringBuilder middleLine = new StringBuilder();

                upBottomline.Append(symbolToDraw, pbSizeWithEdge);
                gapLine.Append(backgroundSymbol, pbSize + 2);
                gapLine.Append(symbolToDraw);

                int position = (int)(pbSize * percent + 0.5f);

                middleLine.Append(backgroundSymbol);

                for (int i = 0; i < position / drawPattern.Length; i++)
                {
                    middleLine.Append(drawPattern);
                }

                int rest = position % drawPattern.Length;
                middleLine.Append(drawPattern.Substring(0, rest));
                middleLine.Append(backgroundSymbol, pbSize - position);
                middleLine.Append(backgroundSymbol);
                middleLine.Append(symbolToDraw);

                int py = y;
                drawBuffer[py] = drawBuffer[py].Remove(x, upBottomline.Length).Insert(x, upBottomline.ToString());
                py++;
                drawBuffer[py] = drawBuffer[py].Remove(x, gapLine.Length).Insert(x, gapLine.ToString());
                for (int j = 0; j < height - 4; j++)
                {
                    py++;
                    drawBuffer[py] = drawBuffer[py].Remove(x, middleLine.Length).Insert(x, middleLine.ToString());
                }
                py++;
                drawBuffer[py] = drawBuffer[py].Remove(x, gapLine.Length).Insert(x, gapLine.ToString());
                py++;
                drawBuffer[py] = drawBuffer[py].Remove(x, upBottomline.Length).Insert(x, upBottomline.ToString());

            }

            void drawIcon()
            {
                int py = y;
                int px = x - icon[0].Length;

                foreach (string line in icon)
                {
                    string coloredLine = line.Replace('█', symbolToDraw).Replace(' ', SColor.getColor(0, 0, 0));
                    drawBuffer[py] = drawBuffer[py].Remove(px, line.Length).Insert(px, coloredLine);
                    ++py;
                }
            }

            public void setDrawColor(byte r, byte g, byte b)
            {
                symbolToDraw = SColor.getColor(r, g, b);
            }

            public void setPBColor(SColor from, SColor to)
            {
                dynamicPBColor = true;
                colorFrom = from;
                colorTo = to;
            }


            void replacePBColor()
            {
                if (dynamicPBColor)
                {
                    char interpolatedColor = interpolateBetweenColors();
                    drawPattern = pattern.Replace('*', interpolatedColor);
                    drawPattern = drawPattern.Replace(' ', backgroundSymbol);
                }
                else
                {
                    drawPattern = pattern.Replace('*', symbolToDraw);
                    drawPattern = drawPattern.Replace(' ', backgroundSymbol);
                }
            }

            char interpolateBetweenColors()
            {
                SColor interpColor = new SColor(0, 0, 0);
                interpColor.r = (byte)interpolate(colorFrom.r, colorTo.r, percent);
                interpColor.g = (byte)interpolate(colorFrom.g, colorTo.g, percent);
                interpColor.b = (byte)interpolate(colorFrom.b, colorTo.b, percent);
                return interpColor.getColor();
            }

            public float interpolate(float p1, float p2, float fraction) { return p1 + (p2 - p1) * fraction; }

        }


        #endregion //Libraries
        #endregion
    }
}