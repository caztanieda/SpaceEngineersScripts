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
    partial class ProgramForRocket : MyGridProgram
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
                    sortOreOfType("Ice", gasGenerators);
                }

                if (reactors != null && reactors.Count > 1)
                {
                    sortOreOfType("Uranium", reactors);
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

                    double elevation = getAngleBetweenVectors(forward, upForwardProjection, Vector3D.Up);
                    double azimuth = getAngleBetweenVectors(forward, rightForwardProjection, Vector3D.Right);

                    float force = 0.1f;

                    gyroscopes[0].Yaw = (float) azimuth * 2f * force;
                    gyroscopes[0].Pitch = (float) elevation * 2f * force;

                    Log("Elevation : " + (int)radiansToDegrees(elevation), false);
                    Log("Azimuth : " + (int)radiansToDegrees(azimuth));

                    Log("Yaw" + gyroscopes[0].Yaw);
                    Log("Pitch" + gyroscopes[0].Pitch);


                }
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

            Vector3D getForwardDirection()
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

            double getAngleBetweenVectors(Vector3D Va, Vector3D Vb, Vector3D Vn)
            {
                double angle = Math.Acos(Vector3D.Dot(Va, Vb));
                var cross = Vector3D.Cross(Va, Vb);
                if (Vector3D.Dot(Vn, cross) < 0)
                { // Or > 0
                    angle = -angle;
                }
                return angle;
            }

            double radiansToDegrees(double radians)
            {
                return radians * 180.0 / Math.PI;
            }

            Vector3D getAnglesBetweenDestinationAndForward()
            {
                return new Vector3D();
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
                networkService = new NetworkService(generateUniqueNetworkName());
                shipServices.Add(networkService);
                shipServices.Add(debugService);
                shipServices.Add(new FuelService());
                shipServices.Add(new ParkingService());
                var pilotingService = new PilotingService();
                pilotingService.SetDestination(new Vector3D(0, 5000, 5000));
                shipServices.Add(pilotingService);

                testListener = new TestListener(networkService);

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
                    string typeName = item.Content.SubtypeName;
                    if (typeName == subtype)
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
            items = block.GetInventory(0).GetItems();

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                string typeName = item.Content.SubtypeName;
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
                string typeName = item.Content.SubtypeName;
                if (typeName == subtype)
                {
                    return i;
                }
                //Echo(typeName); 
            }

            return 0;
        }


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
                {"Iron", "Fe"},
                {"Nickel", "Ni"},
                {"Cobalt", "Co"},
                {"Magnesium", "Mg"},
                {"Silicon", "Si"},
                {"Silver", "Ag"},
                {"Gold", "Au"},
                {"Platinum", "Pt"},
                {"Uranium", "Ur"}
            };

            string ret = name;
            if (ores.ContainsKey(name))
                ret = ores[name];

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

        void getInventoryItems<TBlockType>(Dictionary<string, float> cargoItems)
        where TBlockType : class, IMyTerminalBlock
        {
            List<IMyInventoryItem> inventoryItems = new List<IMyInventoryItem>();

            List<IMyTerminalBlock> cargosList = new List<IMyTerminalBlock>();
            GTS.GetBlocksOfType<TBlockType>(cargosList, b => b.CubeGrid == Me.CubeGrid);
            cargosList.ForEach(it =>
            {
                TBlockType cargo = (TBlockType)it;

                List<IMyInventoryItem> items = new List<IMyInventoryItem>();
                if (cargo.InventoryCount > 0)
                {
                    items = cargo.GetInventory(0).GetItems();

                    for (int j = 0; j < items.Count; ++j)
                    {
                        var item = items[j];
                        float amount = (float)item.Amount;
                        string typeName = item.Content.SubtypeName;
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

        static List<TBlockType> GetBlocksOfType<TBlockType>(string name, bool onlyThisGrid = true)
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

        #endregion //Libraries
        #endregion
    }
}