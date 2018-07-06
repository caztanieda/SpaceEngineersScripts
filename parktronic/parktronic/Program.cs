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
        static Action<string> Log;

        public interface IShipService
        {
            void update(float dt);
        }

        public class Parking : IShipService
        {
            float maxSensorDistance = 10f;
            IMyGridTerminalSystem GTS;
            string groupName;
            IMyLightingBlock lightingBlock = null;
            IMyCameraBlock cameraSensor = null;

            public Parking(IMyGridTerminalSystem GTS)
            {
                this.GTS = GTS;
                groupName = "[Service Parking]";
            }

            void init()
            {
                findRequiredBlocks();

                cameraSensor.EnableRaycast = true;
                float.TryParse(cameraSensor.CustomData, out maxSensorDistance);
            }

            public void update(float dt)
            {

                init();

                if (cameraSensor != null)
                {
                    MyDetectedEntityInfo lastDetectedEntityInfo = cameraSensor.Raycast(maxSensorDistance);
                    Log(lastDetectedEntityInfo.Type.ToString());
                    if (lastDetectedEntityInfo.Type != MyDetectedEntityType.None)
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

                    Log("Distance: " + distance);
                }
            }

            void findRequiredBlocks()
            {
                IMyBlockGroup groupBlocks = GTS.GetBlockGroupWithName(groupName);

                List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
                groupBlocks.GetBlocksOfType<IMyCameraBlock>(blocks);

                blocks.ForEach(b => cameraSensor = b as IMyCameraBlock);

                if(cameraSensor == null)
                {
                    Log("[Service Parking] Can' find a camera");
                }

                groupBlocks.GetBlocksOfType<IMyLightingBlock>(blocks);
                blocks.ForEach(b => lightingBlock = b as IMyLightingBlock);

                if (cameraSensor == null)
                {
                    Log("[Service Parking] Can' find a light block");
                }
            }

            void changeLightColor(float distance)
            {
                Vector3 redColor = new Vector3(1f, 0f, 0f);
                Vector3 greenColor = new Vector3(0f, 1f, 0f);
                Vector3 color = Vector3.Lerp(redColor, greenColor, distance / maxSensorDistance);
                lightingBlock.Color = new Color(color);
            }
        }

        IMyGridTerminalSystem GTS;

        List<IShipService> shipServices = new List<IShipService>();

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
            GTS = GridTerminalSystem;
            Log = Echo;

            shipServices.Add(new Parking(GTS));
        }

        public void Save()
        {
            
        }
        
        public void Main(string argument, UpdateType updateSource)
        {
            foreach(IShipService service in shipServices)
            {
                service.update((float)Runtime.TimeSinceLastRun.TotalSeconds);
            }
        }
#endregion
    }
}