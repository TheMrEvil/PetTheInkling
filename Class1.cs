using HarmonyLib;
using System;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;
using System.Reflection;

namespace PetTheInkling
{
    [HarmonyPatch(typeof(AIDiageticInteraction), "Select")]
    public static class DiageticInteractionPatch
    {
        public static void Postfix(AIDiageticInteraction __instance)
        {
            __instance.OnInteract?.Invoke();
        }
    }

    public class Class1 : MelonMod
    {
        private bool modInitialized = false;
        private FieldInfo petOwnerIDField = null;
        private MethodInfo hasTagMethod = null;
        private PropertyInfo viewIDProperty = null;
        private float lastPetCheckTime = 0f;
        private const float PET_CHECK_INTERVAL = 2f; // Check every 2 seconds

        public override void OnApplicationStart()
        {
            // Get the required fields/methods using reflection
            try
            {
                // Get PetOwnerID field
                petOwnerIDField = typeof(AIControl).GetField("PetOwnerID", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (petOwnerIDField != null)
                {
                    MelonLogger.Msg("Successfully found PetOwnerID field");
                }
                else
                {
                    MelonLogger.Warning("PetOwnerID field not found");
                }

                // Get HasTag method
                hasTagMethod = typeof(AIControl).GetMethod("HasTag", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (hasTagMethod != null)
                {
                    MelonLogger.Msg("Successfully found HasTag method");
                }
                else
                {
                    MelonLogger.Warning("HasTag method not found");
                }

                // Get ViewID property from PlayerControl
                viewIDProperty = typeof(PlayerControl).GetProperty("ViewID", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (viewIDProperty == null)
                {
                    // Try alternative names
                    viewIDProperty = typeof(PlayerControl).GetProperty("viewID", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                }
                if (viewIDProperty != null)
                {
                    MelonLogger.Msg("Successfully found ViewID property");
                }
                else
                {
                    MelonLogger.Warning("ViewID property not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error finding required members: {ex.Message}");
            }
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            MelonLogger.Msg($"Scene loaded: {sceneName} (Build Index: {buildIndex})");
            // Reset timer to check immediately in any new scene
            lastPetCheckTime = 0f;
        }

        public override void OnUpdate()
        {
            // Continuously check for pets in ALL scenes
            if (Time.time - lastPetCheckTime >= PET_CHECK_INTERVAL)
            {
                lastPetCheckTime = Time.time;
                CheckAndSetupPets();
            }
        }

        private void CheckAndSetupPets()
        {
            try
            {
                var player = PlayerControl.myInstance;
                if (player == null) return; // No player yet, skip this check

                // Find all AI controls that could be Inklings (pets with owners)
                var aiControls = GameObject.FindObjectsOfType<AIControl>();
                
                // Only log when we find AI controls to reduce spam
                if (aiControls.Length > 0)
                {
                    MelonLogger.Msg($"Found {aiControls.Length} AIControl objects, checking for pets...");
                }

                int petInteractionsAdded = 0;
                int playerViewID = GetPlayerViewID(player);

                foreach (var aiControl in aiControls)
                {
                    try
                    {
                        // Check if this AI already has a pet interaction component
                        var existingInteraction = aiControl.GetComponentInChildren<AIDiageticInteraction>();
                        if (existingInteraction != null && existingInteraction.Label == "Pet Inkling")
                        {
                            continue; // Already has pet interaction, skip
                        }

                        // Debug info for each AI
                        MelonLogger.Msg($"Checking AI: {aiControl.name} (AIName: {aiControl.AIName})");

                        // Check if this AI is a pet using reflection or alternative methods
                        bool isPet = false;
                        int petOwnerID = -1;

                        if (petOwnerIDField != null)
                        {
                            try
                            {
                                petOwnerID = (int)petOwnerIDField.GetValue(aiControl);
                                isPet = petOwnerID != -1;
                                //MelonLogger.Msg($"  PetOwnerID: {petOwnerID}, isPet: {isPet}");
                            }
                            catch (Exception ex)
                            {
                                MelonLogger.Error($"Error accessing PetOwnerID for {aiControl.name}: {ex.Message}");
                            }
                        }

                        if (!isPet)
                        {
                            // Alternative detection: check if AI has certain characteristics of pets
                            bool nameMatch = aiControl.name.ToLower().Contains("inkling") || aiControl.AIName.ToLower().Contains("inkling");
                            bool hasInklingTag = HasTagSafe(aiControl, "Inkling");
                            bool hasPetTag = HasTagSafe(aiControl, "Pet");

                            isPet = nameMatch || hasInklingTag || hasPetTag;
                            
                            //MelonLogger.Msg($"  Alternative detection - Name match: {nameMatch}, Inkling tag: {hasInklingTag}, Pet tag: {hasPetTag}, Final isPet: {isPet}");
                        }

                        if (isPet)
                        {
                            MelonLogger.Msg($"Found pet/Inkling: {aiControl.name} (Owner: {petOwnerID})");

                            // Add pet interaction component
                            var petInteraction = aiControl.transform.GetChild(0).gameObject.AddComponent<AIDiageticInteraction>();

                            // Configure the interaction similar to Dawn and Dusk
                            petInteraction.Label = "Pet Inkling";
                            petInteraction.InteractDistance = 5f;
                            petInteraction.Interactivity = AIDiageticInteraction.RepeatType.OwnerCooldown;
                            petInteraction.Cooldown = 3f; // 3 second cooldown between pets
                            petInteraction.OwnerOnly = true; // Only the owner can pet
                            petInteraction.Act = AIDiageticInteraction.InteractType.Action;

                            // Create a simple petting action
                            SetupPettingAction(petInteraction, aiControl);

                            petInteractionsAdded++;
                            MelonLogger.Msg($"Added pet interaction to Inkling owned by player {petOwnerID}");
                        }
                    }
                    catch (System.Exception ex)
                    {
                        MelonLogger.Error($"Error processing AI {aiControl.name}: {ex.Message}");
                    }
                }

                if (petInteractionsAdded > 0)
                {
                    MelonLogger.Msg($"Added {petInteractionsAdded} new pet interactions this check.");
                }

                // Also check for nearby pet interactions for debugging
                CheckForNearbyPetInteractions();
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in CheckAndSetupPets: {ex.Message}");
            }
        }

        private bool HasTagSafe(AIControl aiControl, string tag)
        {
            try
            {
                if (hasTagMethod != null)
                {
                    bool result = (bool)hasTagMethod.Invoke(aiControl, new object[] { tag });
                    MelonLogger.Msg($"    HasTag({tag}) = {result}");
                    return result;
                }
                else
                {
                    // Fallback: check the name for common pet indicators
                    bool result = aiControl.name.ToLower().Contains(tag.ToLower()) ||
                                 aiControl.AIName.ToLower().Contains(tag.ToLower());
                    MelonLogger.Msg($"    HasTag({tag}) fallback = {result}");
                    return result;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error checking tag {tag}: {ex.Message}");
                return false;
            }
        }

        private int GetPlayerViewID(PlayerControl player)
        {
            try
            {
                if (viewIDProperty != null)
                {
                    return (int)viewIDProperty.GetValue(player);
                }
                else
                {
                    // Fallback: use reflection to find any property that might be the ViewID
                    var possibleProperties = new string[] { "ViewID", "viewID", "ID", "ActorNr", "NetworkId" };
                    foreach (string propName in possibleProperties)
                    {
                        var prop = typeof(PlayerControl).GetProperty(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (prop != null && prop.PropertyType == typeof(int))
                        {
                            return (int)prop.GetValue(player);
                        }
                    }
                    return -1;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error getting player ViewID: {ex.Message}");
                return -1;
            }
        }

        private void SetupPettingAction(AIDiageticInteraction interaction, AIControl aiControl)
        {
            try
            {
                interaction.OnInteract += () => {
                    MelonLogger.Msg($"{aiControl.name} has been pet!");
                };
                // Create a simple effect that could play an animation or sound
                // This is a basic implementation - you might want to add more sophisticated effects
                
                // You can customize this to trigger specific animations, sounds, or effects
                // For now, we'll set up a basic interaction that logs the petting action
                
                MelonLogger.Msg($"Pet interaction configured for Inkling: {aiControl.name}");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error setting up petting action: {ex.Message}");
            }
        }

        private void CheckForNearbyPetInteractions()
        {
            try
            {
                var player = PlayerControl.myInstance;
                if (player == null) return;

                var interactions = GameObject.FindObjectsOfType<AIDiageticInteraction>();
                var nearbyPetInteractions = interactions.Where(i => 
                    i.Label == "Pet Inkling" && 
                    Vector3.Distance(i.transform.position, player.transform.position) < 10f).ToList();

                if (nearbyPetInteractions.Count > 0)
                {
                    MelonLogger.Msg($"Found {nearbyPetInteractions.Count} nearby pet interactions:");
                    foreach (var interaction in nearbyPetInteractions)
                    {
                        float distance = Vector3.Distance(interaction.transform.position, player.transform.position);
                        MelonLogger.Msg($"  - {interaction.name} at distance {distance:F2}");
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error checking nearby interactions: {ex.Message}");
            }
        }

        private bool IsOwnedByPlayer(AIControl ai, PlayerControl player)
        {
            try
            {
                if (petOwnerIDField != null)
                {
                    int petOwnerID = (int)petOwnerIDField.GetValue(ai);
                    int playerViewID = GetPlayerViewID(player);
                    return petOwnerID == playerViewID;
                }
                else
                {
                    // Alternative: check if the AI is following the player or has other pet characteristics
                    // This is a fallback method that might need adjustment
                    return Vector3.Distance(ai.transform.position, player.transform.position) < 15f &&
                           (ai.name.ToLower().Contains("inkling") || ai.AIName.ToLower().Contains("inkling"));
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error checking pet ownership: {ex.Message}");
                return false;
            }
        }
    }
}
