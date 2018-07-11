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
        static Action<string> Log;
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
            virtual public void update(float dt) { }
            virtual public void update10() { }
            virtual public void update100() { }
            virtual public void update500() { }
            virtual public void incomeMessage(string message) { }
        }
        #endregion //BaseService

        #region NetworkService
        #region NetworkPacket
        public class NetworkPacket
        {
            const char delimiter = '#';
            public NetworkPacket(string rawData)
            {
                string[] splitData = rawData.Split(delimiter);
                if (splitData.Count() == 5)
                {
                    Enum.TryParse(splitData[0], out type);
                    to = splitData[1];
                    from = splitData[2];
                    Enum.TryParse(splitData[3], out code);
                    data = splitData[4];
                }
                else
                {
                    code = NetworkStatusCode.WRONG_PACKET;
                }
            }

            public NetworkPacket(NetworkPacketType type, string to, string from, NetworkStatusCode code, string data)
            {
                this.type = type;
                this.to = to;
                this.from = from;
                this.code = code;
                this.data = data;
            }

            public string serialize()
            {
                return type.ToString() + delimiter + to + delimiter + from + delimiter + code.ToString() + delimiter + data;
            }

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
        #region NetworkCommands
        public interface INetworkCommand
        {
            string serialize();
            void deserialize(string data);
        }
#endregion //NetworkCommands
        public class NetworkService : BaseShipService
        {
            IMyTextPanel debugTextPanel;
            IMyRadioAntenna antenna;
            IMyGridTerminalSystem GTS;
            string networkName;
            string groupName;
            bool isScanningNetwork = false;

            public List<string> network;

            public NetworkService(IMyGridTerminalSystem GTS, string networkName)
            {
                this.GTS = GTS;
                this.networkName = networkName;
                network = new List<string>();
                groupName = "[NetworkService]";
                init();
            }

            void init()
            {
                findRequiredBlocks();
                renameBlocks();
                //scanNetwork();
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
                    isScanningNetwork = false;
                    onScanNetworkComplete();
                }
            }

            public override void incomeMessage(string message)
            {
                NetworkPacket packet = new NetworkPacket(message);

                if (packet.to == networkName || packet.type == NetworkPacketType.BROADCAST)
                {
                    debugTextPanel?.WritePublicText(message + "\n", true);
                }

                if(packet.type == NetworkPacketType.BROADCAST)
                {
                    sendPacket(NetworkPacketType.BROADCAST_RESPONSE, packet.from);
                }

                if (packet.to == networkName)
                {
                    if (packet.type == NetworkPacketType.PING)
                    {
                        sendPacket(NetworkPacketType.PING_RESPONSE, packet.from);
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

                    }
                }
            }

            public void scanNetwork()
            {
                isScanningNetwork = true;
                sendPacket(NetworkPacketType.BROADCAST);
            }

            void onScanNetworkComplete()
            {
                foreach (string name in network)
                {
                    debugTextPanel?.WritePublicText(name + "\n", true);
                }
            }

            void sendPacket(NetworkPacketType packetType, string to = "", string data = "")
            {
                NetworkPacket packet = new NetworkPacket(packetType, to, networkName, NetworkStatusCode.OK, data);
                antenna.TransmitMessage(packet.serialize(), MyTransmitTarget.Everyone);
            }

            void findRequiredBlocks()
            {
                IMyBlockGroup groupBlocks = GTS.GetBlockGroupWithName(groupName);

                List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
                groupBlocks.GetBlocksOfType<IMyRadioAntenna>(blocks);

                blocks.ForEach(b => antenna = b as IMyRadioAntenna);
                if(antenna==null)
                {
                    Log(groupName + ": Can't find antenna");
                }

                groupBlocks.GetBlocksOfType<IMyTextPanel>(blocks);
                blocks.ForEach(b => debugTextPanel = b as IMyTextPanel);
                
            }

            void renameBlocks()
            {
                if (antenna != null)
                {
                    antenna.CustomName = groupName + " Antenna (" + networkName + ")";
                }
            }

            public void sendRequest(string to, string data, Action<string> responce)
            {

            }
        }
        #endregion //NetworkService



        IMyGridTerminalSystem GTS;

        List<IShipService> shipServices = new List<IShipService>();

        NetworkService networkService = null;

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update1 | UpdateFrequency.Update10 | UpdateFrequency.Update100;
            GTS = GridTerminalSystem;
            Log = Echo;
            networkService = new NetworkService(GTS, generateUniqueNetworkName());
            shipServices.Add(networkService);
        }

        public void Save()
        {
            
        }
        
        string generateUniqueNetworkName()
        {
            return "ship [" + Me.WorldMatrix.Translation.ToString().GetHashCode() + "]";
        }

        int executionCounter = 0;

        public void Main(string argument, UpdateType updateSource)
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

                if(executionCounter % 500 == 0)
                {
                    service.update500();
                }

                if (updateSource == UpdateType.Antenna)
                {
                    service.incomeMessage(argument);
                }

                if (updateSource == UpdateType.Trigger)
                {
                    networkService?.scanNetwork();
                }
            }
        }

#region Libraries

        VRage.MyFixedPoint getSumAmountItemsOfType<TBlockType>(string subtype, List<TBlockType> blocks) where TBlockType : class, IMyTerminalBlock
        {
            VRage.MyFixedPoint amount = 0;
            blocks.ForEach(block =>
            {
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
            });
            return amount;
        }

        VRage.MyFixedPoint getAmountItemsOfType(string subtype, IMyTerminalBlock block)
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

        int getItemIndex(string subtype, IMyTerminalBlock block)
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


        void sortOreOfType<TBlockType>(string typename, List<TBlockType> containers) where TBlockType : class, IMyTerminalBlock
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


        void ApplyActionTo<TBlockType>(string action)
        where TBlockType : class, IMyTerminalBlock
        {
            List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocksOfType<TBlockType>(blocks, b => b.CubeGrid == Me.CubeGrid);
            blocks.ForEach(it =>
            {
                TBlockType block = (TBlockType)it;
                block.ApplyAction(action);
            });
        }

        void SetValueTo<TBlockType, T>(string propertyName, T value)
        where TBlockType : class, IMyTerminalBlock
        {
            List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocksOfType<TBlockType>(blocks, b => b.CubeGrid == Me.CubeGrid);
            blocks.ForEach(it =>
            {
                TBlockType block = (TBlockType)it;
                block.SetValue<T>(propertyName, value);
            });
        }

        void getInventoryItems<TBlockType>(Dictionary<string, float> cargoItems)
        where TBlockType : class, IMyTerminalBlock
        {
            List<IMyInventoryItem> inventoryItems = new List<IMyInventoryItem>();

            List<IMyTerminalBlock> cargosList = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocksOfType<TBlockType>(cargosList, b => b.CubeGrid == Me.CubeGrid);
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

        TBlockType GetBlockWithName<TBlockType>(string name)
        where TBlockType : class, IMyTerminalBlock
        {
            List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
            GridTerminalSystem.SearchBlocksOfName(name, blocks);
            foreach (var block in blocks)
            {
                if (block.CubeGrid == Me.CubeGrid)
                    return (TBlockType)block;
            }

            return null;
        }

        #endregion //Libraries
        #endregion
    }
}