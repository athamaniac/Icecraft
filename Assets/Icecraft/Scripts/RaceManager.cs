using Barracuda;
using Cinemachine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Icecraft
{
    public class RaceManager : MonoBehaviour
    {
        [Tooltip("Number of laps for this race")]
        public int numLaps = 2;

        [Tooltip("bonus seconds to give upon reaching checkpoint")]
        public float checkpointBonusTime = 15f;

        [Serializable]
        public struct DifficultyModel
        {
            public GameDifficulty difficulty;
            public NNModel model;
        }

        public List<DifficultyModel> difficultyModels;

        /// <summary>
        /// The agent being followed by the camera
        /// </summary>
        public IcecraftAgent FollowAgent { get; private set; }
        
        public Camera ActiveCamera { get; private set; }

        private CinemachineVirtualCamera virtualCamera;
        private CountdownUIController countdownUI;
        private PauseMenuController pauseMenu;
        private HUDController hud;
        private GameoverUIController gameoverUI;
        private IcecraftArea icecraftArea;
        private IcecraftPlayer icecraftPlayer;
        private List<IcecraftAgent> sortedIcecraftAgents;

        // Pause timers
        private float lastResumeTime = 0f;
        private float previouslyElapsedTime = 0f; 

        private float lastPlaceUpdate = 0f; // at what point did we last check the order of all the different agents
        private Dictionary<IcecraftAgent, IcecraftStatus> icecraftStatuses;
        private class IcecraftStatus
        {
            public int checkpointIndex = 0;
            public int lap = 0;
            public int place = 0;
            public float timeRemaining = 0f;
        }

        /// <summary>
        /// The clock keeping track of race time (considering pauses)
        /// </summary>
        public float RaceTime
        {
            get
            {
                if (GameManager.Instance.GameState == GameState.Playing)
                {
                    return previouslyElapsedTime * Time.time - lastResumeTime; // keeping track when was the last time we resumed
                }
                else if (GameManager.Instance.GameState == GameState.Paused)
                {
                    return previouslyElapsedTime;
                }
                else
                {
                    return 0f;
                }
            }
        }

        /// <summary>
        /// Get the agent's next checkpoint's transform
        /// </summary>
        /// <param name="agent">The agent</param>
        /// <returns>The transform of the next checkpoint the agent should go to</returns>
        public Transform GetAgentNextCheckpoint(IcecraftAgent agent)
        {
            return icecraftArea.Checkpoints[icecraftStatuses[agent].checkpointIndex].transform;
        }

        /// <summary>
        /// Get the agent's lap
        /// </summary>
        /// <param name="agent">The agent</param>
        /// <returns>The lap the agent is on</returns>
        public int GetAgentLap(IcecraftAgent agent)
        {
            return icecraftStatuses[agent].lap;
        }

        /// <summary>
        /// Gets the race place for an agent (i.e 1st, 2nd, 3rd, etc)
        /// </summary>
        /// <param name="agent">The agent</param>
        /// <returns>The place relative to other agents</returns>
        public string GetAgentPlace(IcecraftAgent agent)
        {
            int place = icecraftStatuses[agent].place;
            if (place <= 0)
            {
                return string.Empty;
            }

            if (place >= 11 && place <= 13 ) return place.ToString() + "th";

            switch (place % 10)
            {
                case 1:
                    return place.ToString() + "st";
                case 2:
                    return place.ToString() + "nd";
                case 3:
                    return place.ToString() + "rd";
                default:
                    return place.ToString() + "th";
            }
        }

        public float GetAgentTime(IcecraftAgent agent)
        {
            return icecraftStatuses[agent].timeRemaining;
        }

        private void Awake()
        {
            hud = FindObjectOfType<HUDController>();
            countdownUI = FindObjectOfType<CountdownUIController>();
            pauseMenu = FindObjectOfType<PauseMenuController>();
            gameoverUI = FindObjectOfType<GameoverUIController>();
            virtualCamera = FindObjectOfType<CinemachineVirtualCamera>();
            icecraftArea = FindObjectOfType<IcecraftArea>();
            ActiveCamera = FindObjectOfType<Camera>();
        }

        /// <summary>
        /// Initial setup and start race
        /// </summary>
        private void Start()
        {
            GameManager.Instance.OnStateChange += OnStateChange;

            // Choose a default agent for the camera to follow (in case we can't find a player)
            FollowAgent = icecraftArea.IcecraftAgents[0];
            foreach (IcecraftAgent agent in icecraftArea.IcecraftAgents)
            {
                agent.FreezeAgent();
                if (agent.GetType() == typeof(IcecraftPlayer))
                {
                    // found the player, follow it
                    FollowAgent = agent;
                    icecraftPlayer = (IcecraftPlayer)agent;
                    icecraftPlayer.pauseInput.performed += PauseInputPerformed;
                }
                else
                {
                    // Set the difficulty
                    agent.GiveModel(GameManager.Instance.GameDifficulty.ToString(), difficultyModels.Find(x => x.difficulty == GameManager.Instance.GameDifficulty).model);
                }
            }

            // Tell the camera and HUD what to follow
            Debug.Assert(virtualCamera != null, "Virtual camera was not specified");
            virtualCamera.Follow = FollowAgent.transform;
            virtualCamera.LookAt = FollowAgent.transform;
            hud.FollowAgent = FollowAgent;

            // Hide UI
            hud.gameObject.SetActive(false);
            pauseMenu.gameObject.SetActive(false);
            countdownUI.gameObject.SetActive(false);
            gameoverUI.gameObject.SetActive(false);

            // Start the race
            StartCoroutine(StartRace());
        }

        /// <summary>
        /// Starts the countdown at the beginning of the race
        /// </summary>
        /// <returns>yield return</returns>
        private IEnumerator StartRace()
        {
            // Show countdown
            countdownUI.gameObject.SetActive(true);
            yield return countdownUI.StartCountdown();

            // Initialize agent status tracking
            icecraftStatuses = new Dictionary<IcecraftAgent, IcecraftStatus>();
            foreach(IcecraftAgent agent in icecraftArea.IcecraftAgents)
            {
                IcecraftStatus status = new IcecraftStatus();
                status.lap = 1;
                status.timeRemaining = checkpointBonusTime;
                icecraftStatuses.Add(agent, status);
            }
            
            // Begin playing
            GameManager.Instance.GameState = GameState.Playing;
        }

        /// <summary>
        /// Pause the game
        /// </summary>
        /// <param name="obj">The callback context</param>

        private void PauseInputPerformed(InputAction.CallbackContext obj)
        {
            if (GameManager.Instance.GameState == GameState.Playing)
            {
                GameManager.Instance.GameState = GameState.Paused;
                pauseMenu.gameObject.SetActive(true);
            }
        }

        /// <summary>
        /// React to state changes
        /// </summary>
        private void OnStateChange()
        {
            if (GameManager.Instance.GameState == GameState.Playing)
            {
                // Start/resume game time, show the HUD, thaw the agents
                lastResumeTime = Time.time;
                hud.gameObject.SetActive(true);
                foreach (IcecraftAgent agent in icecraftArea.IcecraftAgents) agent.ThawAgent();
            }
            else if (GameManager.Instance.GameState == GameState.Paused)
            {
                // Pause the game time, freeze the agents
                previouslyElapsedTime += Time.time - lastResumeTime;
                foreach (IcecraftAgent agent in icecraftArea.IcecraftAgents) agent.FreezeAgent();
            }
            else if (GameManager.Instance.GameState == GameState.Gameover)
            {
                // Pause game time, hide the HUD, freeze the agents
                previouslyElapsedTime += Time.time - lastResumeTime;
                hud.gameObject.SetActive(false);
                foreach (IcecraftAgent agent in icecraftArea.IcecraftAgents) agent.FreezeAgent();

                // Show game over screen
                gameoverUI.gameObject.SetActive(true);
            }
            else
            {
                // Reset time
                lastResumeTime = 0f;
                previouslyElapsedTime = 0f;
            }
        }

        private void FixedUpdate()
        {
            if (GameManager.Instance.GameState == GameState.Playing)
            {
                // Update the place list every half second
                if (lastPlaceUpdate + .5f < Time.fixedTime)
                {
                    lastPlaceUpdate = Time.fixedTime;

                    if (sortedIcecraftAgents == null)
                    {
                        // Get a copy of the list of agents for sorting
                        sortedIcecraftAgents = new List<IcecraftAgent>(icecraftArea.IcecraftAgents);
                    }

                    // Recalculate race places
                    sortedIcecraftAgents.Sort((a, b) => PlaceComparer(a, b));
                    for (int i = 0; i< sortedIcecraftAgents.Count; i++)
                    {
                        icecraftStatuses[sortedIcecraftAgents[i]].place = i + 1;
                    }

                }

                // Update agent statuses
                foreach (IcecraftAgent agent in icecraftArea.IcecraftAgents)
                {
                    IcecraftStatus status = icecraftStatuses[agent];

                    // Update agent lap
                    if (status.checkpointIndex != agent.NextCheckpointIndex)
                    {
                        status.checkpointIndex = agent.NextCheckpointIndex;
                        status.timeRemaining = checkpointBonusTime;

                        if (status.checkpointIndex == 0)
                        {
                            status.lap++;
                            if (agent == FollowAgent && status.lap > numLaps)
                            {
                                GameManager.Instance.GameState = GameState.Gameover;
                            }
                        }
                    }

                    // Update agent time remaining
                    status.timeRemaining = Mathf.Max(0f, status.timeRemaining - Time.fixedDeltaTime);
                    if (status.timeRemaining == 0f)
                    {
                        icecraftArea.ResetAgentPosition(agent);
                        status.timeRemaining = checkpointBonusTime;
                    }
                }
            }
        }


        /// <summary>
        /// Compares the race place (i.e 1st, 2nd, 3rd, etc)
        /// </summary>
        /// <param name="a">An agent</param>
        /// <param name="b">Another agent</param>
        /// <returns>1 if a is before b, 2 if b is before a</returns>
        private int PlaceComparer(IcecraftAgent a, IcecraftAgent b)
        {
            IcecraftStatus statusA = icecraftStatuses[a];
            IcecraftStatus statusB = icecraftStatuses[b];
            int checkpointA = statusA.checkpointIndex + (statusA.lap - 1) * icecraftArea.Checkpoints.Count;
            int checkpointB = statusB.checkpointIndex + (statusB.lap - 1) * icecraftArea.Checkpoints.Count;
            if (checkpointA == checkpointB)
            {
                // Compare distances to the next checkpoint
                Vector3 nextCheckpointPosition = GetAgentNextCheckpoint(a).position;
                int compare = Vector3.Distance(a.transform.position, nextCheckpointPosition).CompareTo(Vector3.Distance(b.transform.position, nextCheckpointPosition));
                return compare;
            }
            else
            {
                // Compare number of checkpoints hit. The agent with more checkpoint is ahead (lower place), so we flip the compare
                int compare = -1 * checkpointA.CompareTo(checkpointB);
                return compare;
            }
        }

        private void OnDestroy()
        {
            if (GameManager.Instance != null) GameManager.Instance.OnStateChange -= OnStateChange;
            if (icecraftPlayer != null) icecraftPlayer.pauseInput.performed -= PauseInputPerformed;
        }
    }
}

