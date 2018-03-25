﻿using System;
using System.Collections.Generic;
using System.Numerics;
using OpenSage.Data.Ini;
using OpenSage.DataViewer.Controls;
using OpenSage.Graphics.Cameras.Controllers;
using OpenSage.Graphics.ParticleSystems;
using OpenSage.Logic.Object;
using OpenSage.Settings;

namespace OpenSage.DataViewer.UI.Viewers.Ini
{
    public sealed class ParticleSystemView : GameControl
    {
        // We need to copy the identity matrix so that we can pass it by reference.
        private static readonly Matrix4x4 WorldIdentity = Matrix4x4.Identity;

        public ParticleSystemView(Func<IntPtr, Game> createGame, ParticleSystemDefinition particleSystemDefinition)
        {
            CreateGame = h =>
            {
                var game = createGame(h);

                var particleSystem = new ParticleSystem(
                    game.ContentManager,
                    particleSystemDefinition,
                    () => ref WorldIdentity);

                game.Updating += (sender, e) =>
                {
                    particleSystem.Update(e.GameTime);
                };

                game.BuildingRenderList += (sender, e) =>
                {
                    particleSystem.BuildRenderList(e.RenderList, Matrix4x4.Identity);
                };

                game.Scene3D = new Scene3D(
                    game,
                    new ArcballCameraController(Vector3.Zero, 200),
                    null,
                    null,
                    new List<Terrain.Road>(),
                    null,
                    new GameObjectCollection(game.ContentManager),
                    new WaypointCollection(),
                    new WaypointPathCollection(),
                    WorldLighting.CreateDefault());

                return game;
            };
        }
    }
}
