﻿using Bannerlord.UIExtenderEx;
using HarmonyLib;
using RealisticWeather.GameModels;
using RealisticWeather.Logics;
using System;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.ComponentInterfaces;

namespace RealisticWeather
{
    // This mod adds rain/snow, fog and dust storms which affect movement speed, projectile speed, projectile accuracy and morale.
    public class RealisticWeatherSubModule : MBSubModuleBase
    {
        private Harmony _harmony;
        private Type _postureLogic;

        protected override void OnSubModuleLoad()
        {
            UIExtender uiExtender = new UIExtender("RealisticWeather");
            _harmony = new Harmony("mod.bannerlord.realisticweather");
            _harmony.PatchAll();
            uiExtender.Register(typeof(RealisticWeatherSubModule).Assembly);
            uiExtender.Enable();
        }

        // Check whether RBM is loaded.
        protected override void OnGameStart(Game game, IGameStarter gameStarter)
        {
            _postureLogic = AccessTools.TypeByName("RBMAI.PostureLogic+CreateMeleeBlowPatch");
            gameStarter.AddModel(new RealisticWeatherBattleMoraleModel((BattleMoraleModel)gameStarter.Models.ToList().FindLast(model => model is BattleMoraleModel)));
            if (_postureLogic != null)
            {
                _harmony.Patch(AccessTools.Method(_postureLogic, "calculateDefenderPostureDamage"), postfix: new HarmonyMethod(AccessTools.Method(typeof(RealisticWeatherPostureLogic), "Postfix")));
                _harmony.Patch(AccessTools.Method(_postureLogic, "calculateAttackerPostureDamage"), postfix: new HarmonyMethod(AccessTools.Method(typeof(RealisticWeatherPostureLogic), "Postfix")));
            }
        }

        public override void OnBeforeMissionBehaviorInitialize(Mission mission) => mission.AddMissionBehavior(new RealisticWeatherMissionBehavior());

        public override void OnGameEnd(Game game)
        {
            if (_postureLogic != null)
            {
                _harmony.Unpatch(AccessTools.Method(_postureLogic, "calculateDefenderPostureDamage"), AccessTools.Method(typeof(RealisticWeatherPostureLogic), "Postfix"));
                _harmony.Unpatch(AccessTools.Method(_postureLogic, "calculateAttackerPostureDamage"), AccessTools.Method(typeof(RealisticWeatherPostureLogic), "Postfix"));
            }
        }
    }
}
