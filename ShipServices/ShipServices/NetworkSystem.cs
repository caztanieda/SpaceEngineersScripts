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
            void incomeMessage(string message);
        }

        public class BaseShipService : IShipService
        {
            virtual public void update(float dt) { }
            virtual public void update10() { }
            virtual public void update100() { }
            virtual public void incomeMessage(string message) { }
        }
        #endregion //BaseService

        #region NetworkService
        #region NetworkPacket
        public class NetworkPacket
        {
            public NetworkPacket(string rawData)
            {
                string[] splitData = rawData.Split(',');
                if (splitData.Count() == 5)
                {
                    type = splitData[0];
                    to = splitData[1];
                    from = splitData[2];
                    code = splitData[3];
                    data = splitData[4];
                }
                else
                {
                    code = NetworkStatusCode.WRONG_PACKET;
                }
            }

            public NetworkPacket(string type, string to, string from, string code, string data)
            {
                this.type = type;
                this.to = to;
                this.from = from;
                this.code = code;
                this.data = data;
            }

            public string deserialize()
            {
                return type + "," + to + "," + from + "," + code + "," + data;
            }

            public string type;
            public string to;
            public string from;
            public string code;
            public string data;
        }

        public class NetworkStatusCode
        {
            public static string OK = "OK";
            public static string TIMEOUT = "TIMEOUT";
            public static string WRONG_PACKET = "WRONG_PACKET";
        }
        #endregion //#NetworkPacket

        public class NetworkService : BaseShipService
        {
            IMyTextPanel debugTextPanel;
            IMyRadioAntenna antenna;
            IMyGridTerminalSystem GTS;
            string networkName;
            List<string> network;
            string groupName;

            public NetworkService(IMyGridTerminalSystem GTS, string networkName)
            {
                this.GTS = GTS;
                this.networkName = networkName;
                network = new List<string>();
                groupName = "[NetworkService]";
            }

            void init()
            {
                findRequiredBlocks();
                renameBlocks();
                scanNetwork();
            }

            override public void update100()
            {
                //reinit
                init();
            }

            public override void incomeMessage(string message)
            {
                debugTextPanel?.WritePublicText(message + "\n", true);
                Log("Raw message:" + message);
                NetworkPacket packet = new NetworkPacket(message);
                Log("Message from" + packet.from + " to "+ packet.to + " data" + packet.data);
            }

            void scanNetwork()
            {
                NetworkPacket packet = new NetworkPacket("PING", "", networkName, NetworkStatusCode.OK, "Test data");
                antenna.TransmitMessage(packet.deserialize(), MyTransmitTarget.Everyone);
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
                    antenna.CustomName = groupName + " Antenna";
                }
            }
        }
        #endregion //NetworkService



        IMyGridTerminalSystem GTS;

        List<IShipService> shipServices = new List<IShipService>();

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update1 | UpdateFrequency.Update10 | UpdateFrequency.Update100;
            GTS = GridTerminalSystem;
            Log = Echo;
            shipServices.Add(new NetworkService(GTS, "rocket"));
        }

        public void Save()
        {
            
        }
        
        struct TestSerialization
        {
            public int a;
            public string str;
        }

        public void Main(string argument, UpdateType updateSource)
        {
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

                if (updateSource == UpdateType.Antenna)
                {
                    service.incomeMessage(argument);
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