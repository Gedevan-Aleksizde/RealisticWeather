﻿using HarmonyLib;
using SandBox.Missions.MissionLogics.Arena;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.CustomBattle;

namespace RealisticWeather.Behaviors
{
    public class RealisticWeatherMissionBehavior : MissionBehavior
    {
        private bool _hasTicked;
        private SoundEvent _rainSound, _dustSound;

        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

        public override void AfterStart()
        {
            Scene scene = Mission.Scene;

            RealisticWeatherManager.Current.SetDust(false);

            _hasTicked = false;

            if (!scene.IsAtmosphereIndoor)
            {
                float rainDensity = -1f, fogDensity = 0f;
                bool hasDust = false, isArenaMission = Mission.HasMissionBehavior<ArenaAgentStateDeciderLogic>();

                if (Game.Current.GameType is Campaign)
                {
                    RealisticWeatherSettings settings = RealisticWeatherSettings.Instance;

                    // Find the position of the weather prefab within 25km of the main party.
                    Vec3 prefabPosition = RealisticWeatherManager.Current.PrefabPositions.FirstOrDefault(p => p.AsVec2.Distance(MobileParty.MainParty.Position2D) <= 25f);

                    if (prefabPosition.z == 1)
                    {
                        // If the position has z as 1, spawn a dust storm in the mission.
                        hasDust = true;
                    }
                    else if (prefabPosition.z == 2)
                    {
                        // If the position has z as 2, spawn fog in the mission.
                        fogDensity = MBRandom.RandomInt(1, 32);
                    }

                    if (settings.ShouldOverrideRainDensity)
                    {
                        rainDensity = settings.OverriddenRainDensity;
                    }

                    if (settings.ShouldOverrideFogDensity)
                    {
                        fogDensity = settings.OverriddenFogDensity;
                    }

                    if (isArenaMission && !settings.CanArenaHaveRain)
                    {
                        rainDensity = 0f;
                    }

                    if (isArenaMission && !settings.CanArenaHaveFog)
                    {
                        fogDensity = 1f;
                    }

                    if (isArenaMission && !settings.CanArenaHaveDust)
                    {
                        hasDust = false;
                    }
                }
                else if (Game.Current.GameType is CustomGame)
                {
                    if (RealisticWeatherMixin.MixinWeakReference != null && RealisticWeatherMixin.MixinWeakReference.TryGetTarget(out RealisticWeatherMixin mixin))
                    {
                        if (mixin.SelectedRainDensity > 0f)
                        {
                            rainDensity = mixin.SelectedRainDensity;
                        }

                        if (mixin.SelectedFogDensity > 1f)
                        {
                            fogDensity = mixin.SelectedFogDensity;
                        }

                        hasDust = mixin.SelectedFogDensity == 0f;
                    }
                }

                if (rainDensity > -1f)
                {
                    Vec3 sunColor = new Vec3(255, 255, 255, 255);
                    float sunAltitude = (50 * MathF.Cos(MathF.PI * scene.TimeOfDay / 6)) + 50, sunIntensity = (1 - rainDensity) / 1000;

                    scene.SetRainDensity(rainDensity);
                    // Decrease the sun intensity according to rain density.
                    scene.SetSun(ref sunColor, sunAltitude, 0, sunIntensity);
                }

                if (fogDensity > 0f)
                {
                    Vec3 fogColor = new Vec3(1, 1, 1, 1);
                    float fogFalloff = 0.5f * MathF.Sin(MathF.PI * scene.TimeOfDay / 24);

                    scene.SetFog(fogDensity, ref fogColor, fogFalloff);
                    scene.SetFogAdvanced(0, 0, -40);
                }

                if (hasDust && rainDensity == -1f)
                {
                    Vec2 terrainSize = new Vec2(1f, 1f);

                    scene.GetTerrainData(out Vec2i nodeDimension, out float nodeSize, out _, out _);
                    terrainSize.x = nodeDimension.X * nodeSize;
                    terrainSize.y = nodeDimension.Y * nodeSize;
                    GameEntity.Instantiate(scene, "dust_prefab_entity", new MatrixFrame(Mat3.Identity, (terrainSize / 2).ToVec3()));
                    // Decrease the sky brightness.
                    scene.SetSkyBrightness(scene.TimeOfDay < 12 ? ((MathF.Pow(2, scene.TimeOfDay) - 1) / 10) : ((MathF.Pow(2, 24 - scene.TimeOfDay) - 1) / 10));
                    // Apply the "Aserai Harsh" color grade.
                    scene.SetSceneColorGradeIndex(23);
                    RealisticWeatherManager.Current.SetDust(true);

                    _dustSound = SoundEvent.CreateEvent(SoundEvent.GetEventIdFromString("dust_storm"), scene);
                    // Play the dust storm ambient sound.
                    _dustSound.Play();
                }
            }
        }

        public override void OnMissionTick(float dt)
        {
            if (!_hasTicked)
            {
                Scene scene = Mission.Scene;
                float rainDensity = scene.GetRainDensity();

                if (rainDensity > 0f)
                {
                    GameEntity rainPrefab = scene.GetFirstEntityWithName("rain_prefab_entity") ?? scene.GetFirstEntityWithName("snow_prefab_entity");
                    int season = ((MissionInitializerRecord)AccessTools.Property(typeof(Mission), "InitializerRecord").GetValue(Mission)).AtmosphereOnCampaign.TimeInfo.Season;

                    if (rainPrefab != null)
                    {
                        Mesh skyboxMesh = scene.GetSkyboxMesh();
                        Material skyboxMaterial = skyboxMesh.GetMaterial().CreateCopy();

                        // Change the skybox texture to an overcast sky.
                        skyboxMaterial.SetTexture(Material.MBTextureType.DiffuseMap, Texture.GetFromResource("sky_photo_overcast_01"));
                        skyboxMesh.SetMaterial(skyboxMaterial);

                        foreach (GameEntity entity in rainPrefab.GetChildren())
                        {
                            if (entity.Name != "rain_far")
                            {
                                // Multiply the rain particle emission rate according to rain density.
                                entity.SetRuntimeEmissionRateMultiplier((20 * (rainDensity - 0.7f)) + 1);
                            }
                            else
                            {
                                for (int i = 1; i < (40 * (rainDensity - 0.85f)) + 1; i++)
                                {
                                    Mesh rainMesh = entity.GetFirstMesh().CreateCopy();
                                    MatrixFrame rainFrame = rainMesh.GetLocalFrame();

                                    rainFrame.Advance(MBRandom.RandomFloat * 10);
                                    rainFrame.Elevate(MBRandom.RandomFloat * 10);
                                    rainFrame.Strafe(MBRandom.RandomFloat * 10);
                                    rainMesh.SetLocalFrame(rainFrame);
                                    // Add copies of the background rain mesh to the scene according to rain density.
                                    entity.AddMesh(rainMesh);
                                }
                            }
                        }
                    }

                    if (rainDensity >= 0.7f && rainDensity < 0.775f)
                    {
                        _rainSound = SoundEvent.CreateEvent(SoundEvent.GetEventIdFromString(season != 3 ? "rain_light" : "snow_light"), scene);
                    }
                    else if (rainDensity >= 0.775f && rainDensity < 0.925f)
                    {
                        _rainSound = SoundEvent.CreateEvent(SoundEvent.GetEventIdFromString(season != 3 ? "rain_moderate" : "snow_moderate"), scene);
                    }
                    else if (rainDensity >= 0.925f)
                    {
                        _rainSound = SoundEvent.CreateEvent(SoundEvent.GetEventIdFromString(season != 3 ? "rain_heavy" : "snow_heavy"), scene);
                    }

                    // Play the rain ambient sound.
                    _rainSound?.Play();
                }

                _hasTicked = true;
            }
        }

        protected override void OnEndMission()
        {
            // Stop the ambient sounds.
            _rainSound?.Stop();
            _dustSound?.Stop();
        }
    }
}
