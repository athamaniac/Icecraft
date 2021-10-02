using Cinemachine;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Icecraft
{

    public class IcecraftArea : MonoBehaviour
    {
        [Tooltip("The path the race will take")]
        public CinemachineSmoothPath racePath;

        [Tooltip("The prefab to use for checkpoints")]
        public GameObject checkpointPrefab;

        [Tooltip("The prefab to use for the start/end checkpoint")]
        public GameObject finishCheckpointPrefab;

        [Tooltip("If true, enable training mode")]
        public bool trainingMode;

        
        public List<IcecraftAgent> IcecraftAgents { get; private set; }
        public List<GameObject> Checkpoints { get; private set; }
        public IcecraftAcademy IcecraftAcademy { get; private set; }

        /// <summary>
        /// Actions to perform when the script wakes up
        /// </summary>
        private void Awake()
        {
            //Find all icecraft agents in the area
            IcecraftAgents = transform.GetComponentsInChildren<IcecraftAgent>().ToList();
            Debug.Assert(IcecraftAgents.Count > 0, "No IcecraftAgents found");

            IcecraftAcademy = FindObjectOfType<IcecraftAcademy>();
        }

        /// <summary>
        /// Set up the area
        /// </summary>
        private void Start()
        {
            //Create checkpoints along the race path
            Debug.Assert(racePath != null, "Race path was not set");
            Checkpoints = new List<GameObject>();
            int numCheckpoints = (int)racePath.MaxUnit(CinemachinePathBase.PositionUnits.PathUnits);
            for (int i = 0; i < numCheckpoints; i++)
            {
                // Instantiate either a checkpoint or finish line checkpoint
                GameObject checkpoint;
                if (i == numCheckpoints - 1) checkpoint = Instantiate<GameObject>(finishCheckpointPrefab);
                else checkpoint = Instantiate<GameObject>(checkpointPrefab);

                // Set the parent, position and rotation
                checkpoint.transform.SetParent(racePath.transform);
                checkpoint.transform.localPosition = racePath.m_Waypoints[i].position; //gets the position of the current checkpoint
                checkpoint.transform.rotation = racePath.EvaluateOrientationAtUnit(i, CinemachinePathBase.PositionUnits.PathUnits);

                // Add the checkpoint to the list
                Checkpoints.Add(checkpoint);

            }

        }

        /// <summary>
        /// Resets the position of an agent using its current NextCheckpointIndex, unless randomize = true,
        /// then will pick a new random checkpoint
        /// </summary>
        /// <param name="agent">The agent to reset</param>
        /// <param name="randomize">If true, will pick a new NextCheckpointIndex before reset</param>
        public void ResetAgentPosition(IcecraftAgent agent, bool randomize = false) 
        {
            if (randomize)
            {
                // Pick a new next checkpoint at random
                agent.NextCheckpointIndex = Random.Range(0, Checkpoints.Count);
            }

            // Set start position to the previous checkpoint
            int previousCheckpointIndex = agent.NextCheckpointIndex - 1;
            if (previousCheckpointIndex == -1) previousCheckpointIndex = Checkpoints.Count - 1;

            float startPosition = racePath.FromPathNativeUnits(previousCheckpointIndex, CinemachinePathBase.PositionUnits.PathUnits);

            // Convert the position on the race path to a position in 3D space
            Vector3 basePosition = racePath.EvaluatePosition(startPosition);

            // Get the orienatation at that position on the race path
            Quaternion orientation = racePath.EvaluateOrientation(startPosition);

            // Calculate a horizontal offset so that agents are spread out (by 10 meters each)
            Vector3 positionOffset = Vector3.right * (IcecraftAgents.IndexOf(agent) - IcecraftAgents.Count / 2f) * UnityEngine.Random.Range(9f,10f);

            // Set the icecraft position and rotation
            agent.transform.position = basePosition + orientation * positionOffset;
            agent.transform.rotation = orientation;

        }

    }
}
