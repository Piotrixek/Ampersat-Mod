using MelonLoader;
using UnityEngine;
using Amp_Player;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;

namespace TestMod
{
    public static class BuildInfo
    {
        public const string Name = "Veni's Cheat Menu";
        public const string Description = "Mod for doing funny stuff";
        public const string Author = "Veni";
        public const string Company = null;
        public const string Version = "2.2.1";
        public const string DownloadLink = null;
    }

    public class TestMod : MelonMod
    {
        private PlayerController p_controller;
        private PlayerManager p_manager;
        private Rigidbody p_rigidbody;
        private Collider p_collider;
        private bool showMenu = true;
        private Rect menuRect = new Rect(20, 20, 400, 550);
        private int currentTab = 0;
        private string[] tabs = { "Player", "Combat", "World", "Spawner", "Misc" };

        // --- Player Cheats ---
        private bool godMode = false;
        private bool lastGodModeState = false;
        private bool infDash = false;
        private float speedMulti = 1.0f;
        private float originalSpeed = -1f;
        private float playerScale = 1.0f;
        private bool noclip = false;
        private bool originalGravity;

        // --- Combat Cheats ---
        private bool noCooldowns = false;
        private bool noOverheat = false;
        private bool alwaysCrit = false;
        private bool autoSwitchSpells = false;
        private float knockbackMulti = 1.0f;
        private float originalMKnockback = -1f;
        private float originalRKnockback = -1f;
        private bool infiniteRicochet = false;
        private bool homingProjectiles = false;
        private bool memeDamage = false;
        private bool reflectStatusEffects = false;

        // --- World Cheats ---
        private bool disableTraps = false;
        private bool trapsHitEnemies = false;
        private float timeScale = 1.0f;

        // --- Spawner Cheats ---
        private bool instantBurst = false;
        private AllEnemiesListSO allEnemiesList;
        private Dictionary<EnemyScriptableObject, GameObject> enemyPrefabMap;
        private int selectedEnemyIndex = 0;
        private Vector2 enemyScrollPos;

        // --- Misc Cheats ---
        private bool discoMode = false;

        // for reflection
        private MethodInfo resetOverheatMethod;
        private MethodInfo switchSpellMethod;
        private float spellSwitchTimer = 0f;
        private FieldInfo projectileRicochetCountField;
        private FieldInfo projectileDoesRicochetField;
        private FieldInfo timeSinceLastDashField;
        private FieldInfo primarySpellField;
        private FieldInfo alternateSpellField;

        public override void OnInitializeMelon()
        {
            HarmonyInstance.PatchAll(typeof(DamageIndicatorPatch));
            HarmonyInstance.PatchAll(typeof(PlayerManager_ApplyStatusEffects_Patch));
        }

        public override void OnSceneWasLoaded(int buildindex, string sceneName)
        {
            p_controller = null;

            var foundLists = Resources.FindObjectsOfTypeAll<AllEnemiesListSO>();
            if (foundLists.Length > 0)
            {
                allEnemiesList = foundLists[0];
                MapEnemyPrefabs();
            }

            HandleMenuState();
        }

        private void MapEnemyPrefabs()
        {
            enemyPrefabMap = new Dictionary<EnemyScriptableObject, GameObject>();
            var allEnemyControllers = Resources.FindObjectsOfTypeAll<EnemyController>();
            foreach (var controller in allEnemyControllers)
            {
                if (controller.gameObject.scene.name == null)
                {
                    if (controller.enemySO != null && !enemyPrefabMap.ContainsKey(controller.enemySO))
                    {
                        enemyPrefabMap.Add(controller.enemySO, controller.gameObject);
                    }
                }
            }
            MelonLogger.Msg($"Mapped {enemyPrefabMap.Count} enemy prefabs.");
        }

        private void HandleMenuState()
        {
            if (showMenu)
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
                if (p_controller != null) p_controller.enabled = false;
            }
            else
            {
                Cursor.visible = false;
                Cursor.lockState = CursorLockMode.Locked;
                if (p_controller != null)
                {
                    p_controller.enabled = true;
                    p_controller.ToggleGamepadControls();
                }
            }
        }

        public override void OnUpdate()
        {
            if (Input.GetKeyDown(KeyCode.Insert))
            {
                showMenu = !showMenu;
                HandleMenuState();
            }

            if (p_controller == null)
            {
                p_controller = GameObject.FindObjectOfType<PlayerController>();
                if (p_controller != null)
                {
                    p_manager = p_controller.GetComponent<PlayerManager>();
                    p_rigidbody = p_controller.GetComponent<Rigidbody>();
                    p_collider = p_controller.GetComponent<Collider>();

                    MelonLogger.Msg("found the player controller lets gooo");
                    var playerControllerType = typeof(PlayerController);
                    resetOverheatMethod = playerControllerType.GetMethod("ResetRangedOverheat", BindingFlags.NonPublic | BindingFlags.Instance);
                    switchSpellMethod = playerControllerType.GetMethod("SwitchSpell", BindingFlags.NonPublic | BindingFlags.Instance);
                    projectileRicochetCountField = typeof(Projectile).GetField("numberOfRicochets", BindingFlags.NonPublic | BindingFlags.Instance);
                    projectileDoesRicochetField = typeof(Projectile).GetField("doesRicochet", BindingFlags.NonPublic | BindingFlags.Instance);
                    timeSinceLastDashField = playerControllerType.GetField("timeSinceLastDash", BindingFlags.NonPublic | BindingFlags.Instance);
                    primarySpellField = playerControllerType.GetField("primarySpell", BindingFlags.NonPublic | BindingFlags.Instance);
                    alternateSpellField = playerControllerType.GetField("alternateSpell", BindingFlags.NonPublic | BindingFlags.Instance);


                    HandleMenuState();
                }
                return;
            }

            ApplyCheats();
            UpdateProjectiles();
            UpdateTraps();
            UpdateSpawners();
        }

        private void ApplyCheats()
        {
            if (p_controller == null) return;

            p_controller.transform.localScale = new Vector3(playerScale, playerScale, playerScale);
            Time.timeScale = timeScale;
            DamageIndicatorPatch.memeDamageEnabled = memeDamage;
            PlayerManager_ApplyStatusEffects_Patch.reflectEnabled = reflectStatusEffects;

            if (p_manager != null && godMode != lastGodModeState)
            {
                p_manager.SetInvulnerable(godMode);
                lastGodModeState = godMode;
            }

            if (showMenu || !p_controller.enabled) return;

            if (infDash && timeSinceLastDashField != null) timeSinceLastDashField.SetValue(p_controller, float.PositiveInfinity);
            if (noCooldowns)
            {
                p_controller.timeSinceLastMeleeAttack = float.PositiveInfinity;
                p_controller.timeSinceLastRangedAttack = float.PositiveInfinity;
                p_controller.timeSinceLastFairyAttack = float.PositiveInfinity;
            }
            if (noOverheat && resetOverheatMethod != null) resetOverheatMethod.Invoke(p_controller, null);
            if (alwaysCrit) p_controller.attributeManager.combinedMCriticalChance = 100f;
            if (discoMode) p_controller.transform.Rotate(0, 720 * Time.deltaTime, 0);

            if (autoSwitchSpells && switchSpellMethod != null && primarySpellField != null && alternateSpellField != null)
            {
                var primarySpell = primarySpellField.GetValue(p_controller);
                var alternateSpell = alternateSpellField.GetValue(p_controller);

                if (primarySpell != null && alternateSpell != null)
                {
                    spellSwitchTimer += Time.deltaTime;
                    if (spellSwitchTimer >= 0.5f)
                    {
                        switchSpellMethod.Invoke(p_controller, null);
                        spellSwitchTimer = 0f;
                    }
                }
            }

            if (p_controller.attributeManager != null)
            {
                if (originalSpeed < 0) originalSpeed = p_controller.attributeManager.combinedFootSpeed;
                p_controller.attributeManager.combinedFootSpeed = originalSpeed * speedMulti;

                if (originalMKnockback < 0) originalMKnockback = p_controller.attributeManager.combinedMKnockback;
                if (originalRKnockback < 0) originalRKnockback = p_controller.attributeManager.combinedRKnockback;
                p_controller.attributeManager.combinedMKnockback = originalMKnockback * knockbackMulti;
                p_controller.attributeManager.combinedRKnockback = originalRKnockback * knockbackMulti;
            }

            HandleNoclip();
        }

        private void HandleNoclip()
        {
            if (noclip && p_rigidbody != null)
            {
                float verticalMove = 0;
                if (Input.GetKey(KeyCode.Space)) verticalMove = 1;
                if (Input.GetKey(KeyCode.LeftControl)) verticalMove = -1;

                p_rigidbody.velocity = new Vector3(p_rigidbody.velocity.x, verticalMove * (originalSpeed * speedMulti), p_rigidbody.velocity.z);
            }
        }

        private void ToggleNoclip(bool enabled)
        {
            if (p_collider == null || p_rigidbody == null) return;
            noclip = enabled;
            if (enabled)
            {
                originalGravity = p_rigidbody.useGravity;
                p_collider.isTrigger = true;
                p_rigidbody.useGravity = false;
            }
            else
            {
                p_collider.isTrigger = false;
                p_rigidbody.useGravity = originalGravity;
            }
        }

        private void UpdateProjectiles()
        {
            if (showMenu || (!infiniteRicochet && !homingProjectiles)) return;

            foreach (var proj in GameObject.FindObjectsOfType<Projectile>())
            {
                if (infiniteRicochet && projectileDoesRicochetField != null && projectileRicochetCountField != null)
                {
                    projectileDoesRicochetField.SetValue(proj, true);
                    projectileRicochetCountField.SetValue(proj, 99);
                }

                if (homingProjectiles)
                {
                    EnemyController closestEnemy = null;
                    float minDistance = float.MaxValue;

                    foreach (var enemyController in GameObject.FindObjectsOfType<EnemyController>())
                    {
                        if (!enemyController.isActiveAndEnabled) continue;
                        float dist = Vector3.Distance(proj.transform.position, enemyController.transform.position);
                        if (dist < minDistance)
                        {
                            minDistance = dist;
                            closestEnemy = enemyController;
                        }
                    }

                    if (closestEnemy != null)
                    {
                        var rb = proj.GetComponent<Rigidbody>();
                        if (rb != null)
                        {
                            Vector3 direction = (closestEnemy.transform.position - proj.transform.position).normalized;
                            rb.velocity = direction * 20f;
                            proj.transform.rotation = Quaternion.LookRotation(direction);
                        }
                    }
                }
            }
        }

        private void UpdateTraps()
        {
            if (showMenu) return;

            foreach (var trap in GameObject.FindObjectsOfType<ProjectileLauncher>())
            {
                var isActiveField = typeof(ProjectileLauncher).GetField("isActive", BindingFlags.NonPublic | BindingFlags.Instance);
                var damagesPlayerField = typeof(ProjectileLauncher).GetField("damagesPlayer", BindingFlags.NonPublic | BindingFlags.Instance);
                var damagesEnemyField = typeof(ProjectileLauncher).GetField("damagesEnemy", BindingFlags.NonPublic | BindingFlags.Instance);

                if (disableTraps)
                {
                    if ((bool)isActiveField.GetValue(trap)) isActiveField.SetValue(trap, false);
                }
                else if (trapsHitEnemies)
                {
                    if ((bool)damagesPlayerField.GetValue(trap)) damagesPlayerField.SetValue(trap, false);
                    if (!(bool)damagesEnemyField.GetValue(trap)) damagesEnemyField.SetValue(trap, true);
                }
            }
        }

        private void UpdateSpawners()
        {
            if (showMenu || !instantBurst) return;

            foreach (var burster in GameObject.FindObjectsOfType<EggBurster>())
            {
                burster.ExecuteTrigger();
            }
        }

        public override void OnGUI()
        {
            if (!showMenu) return;

            menuRect = GUILayout.Window(0, menuRect, (GUI.WindowFunction)MenuWindow, "Veni's Ultimate Fun Menu");
        }

        void MenuWindow(int windowID)
        {
            GUI.DragWindow(new Rect(0, 0, 10000, 20));

            currentTab = GUILayout.Toolbar(currentTab, tabs);

            if (p_controller == null)
            {
                GUILayout.Label("waiting for player...");
                return;
            }

            GUILayout.BeginVertical("box");

            switch (currentTab)
            {
                case 0: DrawPlayerTab(); break;
                case 1: DrawCombatTab(); break;
                case 2: DrawWorldTab(); break;
                case 3: DrawSpawnerTab(); break;
                case 4: DrawMiscTab(); break;
            }

            GUILayout.EndVertical();

            GUILayout.FlexibleSpace();
            GUILayout.Label("Press 'Insert' to hide menu");
        }

        void DrawPlayerTab()
        {
            GUILayout.Label("--- Player ---");
            godMode = GUILayout.Toggle(godMode, "God Mode (Invulnerable)");
            infDash = GUILayout.Toggle(infDash, "Infinite Dash");

            bool noclipToggle = GUILayout.Toggle(noclip, "Noclip (Space/Ctrl)");
            if (noclipToggle != noclip) ToggleNoclip(noclipToggle);

            GUILayout.Label("Speed Multiplier: " + speedMulti.ToString("F1") + "x");
            speedMulti = GUILayout.HorizontalSlider(speedMulti, 1.0f, 10.0f);

            GUILayout.Label("Player Size: " + playerScale.ToString("F1") + "x");
            playerScale = GUILayout.HorizontalSlider(playerScale, 0.1f, 5.0f);

            GUILayout.Space(10);
            GUILayout.Label("--- Actions ---");
            if (GUILayout.Button("Teleport to Mouse")) TeleportToMouse();
            if (GUILayout.Button("Add 1000 XP")) p_manager?.GainXP(1000);
            if (GUILayout.Button("Add 1000 GP")) p_manager?.ChangeCurrentGP(1000);
        }

        void DrawCombatTab()
        {
            GUILayout.Label("--- Attacks ---");
            noCooldowns = GUILayout.Toggle(noCooldowns, "No Attack Cooldowns");
            noOverheat = GUILayout.Toggle(noOverheat, "No Ranged Overheat");
            alwaysCrit = GUILayout.Toggle(alwaysCrit, "Always Critical Hits");
            autoSwitchSpells = GUILayout.Toggle(autoSwitchSpells, "Auto-Switch Spells");
            reflectStatusEffects = GUILayout.Toggle(reflectStatusEffects, "Reflect Status Effects");

            GUILayout.Label("Knockback: " + knockbackMulti.ToString("F1") + "x");
            knockbackMulti = GUILayout.HorizontalSlider(knockbackMulti, 1.0f, 20.0f);

            GUILayout.Space(10);
            GUILayout.Label("--- Projectiles ---");
            infiniteRicochet = GUILayout.Toggle(infiniteRicochet, "Infinite Ricochet");
            homingProjectiles = GUILayout.Toggle(homingProjectiles, "Homing Projectiles");
            memeDamage = GUILayout.Toggle(memeDamage, "Meme Damage Text");

            GUILayout.Space(10);
            GUILayout.Label("--- Actions ---");
            if (GUILayout.Button("Max Out Stats")) MaxOutStats();
            if (GUILayout.Button("Kill All Enemies")) KillAllEnemies();
        }

        void DrawWorldTab()
        {
            GUILayout.Label("--- Traps ---");
            disableTraps = GUILayout.Toggle(disableTraps, "Disable All Traps");
            if (disableTraps) trapsHitEnemies = false;
            trapsHitEnemies = GUILayout.Toggle(trapsHitEnemies, "Traps Hit Enemies");
            if (trapsHitEnemies) disableTraps = false;

            GUILayout.Space(10);
            GUILayout.Label("--- Time ---");
            GUILayout.Label("Time Scale: " + timeScale.ToString("F1") + "x");
            timeScale = GUILayout.HorizontalSlider(timeScale, 0.1f, 5.0f);
            if (GUILayout.Button("Reset Time")) timeScale = 1.0f;
        }

        void DrawSpawnerTab()
        {
            GUILayout.Label("--- Egg Bursters ---");
            instantBurst = GUILayout.Toggle(instantBurst, "Instantly Burst All Eggs");

            GUILayout.Space(10);
            GUILayout.Label("--- Enemy Spawning ---");

            if (allEnemiesList == null || enemyPrefabMap == null)
            {
                GUILayout.Label("enemy list not found yet");
                return;
            }

            enemyScrollPos = GUILayout.BeginScrollView(enemyScrollPos, GUILayout.Height(200));
            List<string> enemyNames = new List<string>();
            foreach (var enemySO in allEnemiesList.listOfAllEnemies)
            {
                enemyNames.Add(enemySO.enemyName);
            }
            selectedEnemyIndex = GUILayout.SelectionGrid(selectedEnemyIndex, enemyNames.ToArray(), 1);
            GUILayout.EndScrollView();

            if (GUILayout.Button("Spawn Selected Enemy"))
            {
                var enemySO = allEnemiesList.listOfAllEnemies[selectedEnemyIndex];
                if (enemyPrefabMap.TryGetValue(enemySO, out GameObject prefabToSpawn))
                {
                    GameObject newEnemy = GameObject.Instantiate(prefabToSpawn, p_controller.transform.position + p_controller.transform.forward * 2, Quaternion.identity);
                    newEnemy.SetActive(true);
                    MelonLogger.Msg("spawned a " + enemySO.enemyName);
                }
                else
                {
                    MelonLogger.Error("could not find prefab for " + enemySO.enemyName);
                }
            }
        }

        void DrawMiscTab()
        {
            GUILayout.Label("--- Visuals ---");
            discoMode = GUILayout.Toggle(discoMode, "Disco Mode (Spin)");
        }

        void TeleportToMouse()
        {
            FieldInfo floorMaskField = typeof(PlayerController).GetField("floorMask", BindingFlags.NonPublic | BindingFlags.Instance);
            if (floorMaskField == null) return;
            int floorMask = (int)floorMaskField.GetValue(p_controller);

            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f, floorMask))
            {
                p_controller.transform.position = hit.point;
            }
        }

        void KillAllEnemies()
        {
            var enemies = GameObject.FindObjectsOfType<EnemyController>();
            foreach (var enemy in enemies) GameObject.Destroy(enemy.gameObject);
            MelonLogger.Msg($"Destroyed {enemies.Length} enemies. poof.");
        }

        void MaxOutStats()
        {
            var am = p_controller.attributeManager;
            if (am == null) return;

            am.combinedMCriticalMultiplier = 1000f;
            am.combinedMCriticalChance = 100f;
            am.combinedMKnockback = 1000f;
            am.combinedDmgMKinetic = 1000f;
            am.combinedDmgMFire = 1000f;
            am.combinedDmgMIce = 1000f;
            am.combinedDmgMChaos = 1000f;
            am.combinedMRate = 100f;
            am.combinedRRange = 1000f;
            am.combinedRSpeed = 1000f;
            am.combinedRKnockback = 1000f;
            am.combinedDmgRTotalKinetic = 1000f;
            am.combinedDmgRTotalFire = 1000f;
            am.combinedDmgRTotalIce = 1000f;
            am.combinedDmgRTotalChaos = 1000f;
            am.combinedRRate = 100f;
            am.combinedRMaxBurst = 99999f;
            am.combinedRCooldown = 999f;
            am.combinedDashCooldown = 999f;
            am.combinedDestructMultiplier = 1000f;

            originalSpeed = am.combinedFootSpeed;
            originalMKnockback = am.combinedMKnockback;
            originalRKnockback = am.combinedRKnockback;

            MelonLogger.Msg("stats are now ridiculous have fun");
        }
    }

    [HarmonyPatch(typeof(PlayerManager), "ApplyStatusEffects", new[] { typeof(StatusEffect[]) })]
    public static class PlayerManager_ApplyStatusEffects_Patch
    {
        public static bool reflectEnabled = false;

        [HarmonyPrefix]
        public static bool Prefix(PlayerManager __instance, StatusEffect[] statusEffects)
        {
            if (!reflectEnabled) return true;

            EnemyController closestEnemy = null;
            float minDistance = float.MaxValue;
            var playerTransform = __instance.transform;

            foreach (var enemyController in GameObject.FindObjectsOfType<EnemyController>())
            {
                if (!enemyController.isActiveAndEnabled) continue;
                float dist = Vector3.Distance(playerTransform.position, enemyController.transform.position);
                if (dist < minDistance)
                {
                    minDistance = dist;
                    closestEnemy = enemyController;
                }
            }

            if (closestEnemy != null)
            {
                var applyEffectsMethod = typeof(EnemyController).GetMethod("ApplyStatusEffects", BindingFlags.NonPublic | BindingFlags.Instance);
                if (applyEffectsMethod != null)
                {
                    applyEffectsMethod.Invoke(closestEnemy, new object[] { statusEffects });
                    MelonLogger.Msg($"Reflected status effects to {closestEnemy.name}");
                }
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(DamageIndicator), "PushDamageIndicator")]
    public static class DamageIndicatorPatch
    {
        public static bool memeDamageEnabled = false;

        [HarmonyPrefix]
        public static bool Prefix(DamageIndicator __instance, ref float amount, ref bool critical, ref StatusEffect statusEffect)
        {
            if (memeDamageEnabled && amount < 0)
            {
                var indicatorsField = typeof(DamageIndicator).GetField("indicators", BindingFlags.NonPublic | BindingFlags.Instance);
                var currentIndicatorField = typeof(DamageIndicator).GetField("currentIndicator", BindingFlags.NonPublic | BindingFlags.Instance);
                var indicators = (List<TMPro.TextMeshProUGUI>)indicatorsField.GetValue(__instance);
                int currentIndicator = (int)currentIndicatorField.GetValue(__instance);

                indicators[currentIndicator].text = "<color=#FF0000>pwned</color>";
                return true;
            }
            return true;
        }
    }
}
