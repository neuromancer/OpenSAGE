﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using OpenSage.Content.Util;
using OpenSage.Data;
using OpenSage.Data.Map;
using OpenSage.Data.Tga;
using OpenSage.Graphics.Cameras;
using OpenSage.Logic.Object;
using OpenSage.Mathematics;
using OpenSage.Scripting;
using OpenSage.Settings;
using OpenSage.Terrain;
using OpenSage.Utilities;
using OpenSage.Utilities.Extensions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Transforms;
using SixLabors.ImageSharp.Processing.Transforms.Resamplers;
using Veldrid;
using Veldrid.ImageSharp;
using Player = OpenSage.Logic.Player;
using Rectangle = OpenSage.Mathematics.Rectangle;
using Team = OpenSage.Logic.Team;

namespace OpenSage.Content
{
    internal sealed class MapLoader : ContentLoader<Scene3D>
    {
        private static readonly IResampler MapTextureResampler = new Lanczos2Resampler();

        protected override Scene3D LoadEntry(FileSystemEntry entry, ContentManager contentManager, Game game, LoadOptions loadOptions)
        {
            switch (contentManager.SageGame)
            {
                case SageGame.Ra3:
                case SageGame.Ra3Uprising:
                case SageGame.Cnc4:
                    // TODO
                    break;

                default:
                    contentManager.IniDataContext.LoadIniFile(@"Data\INI\Terrain.ini");
                    break;
            }

            var mapFile = MapFile.FromFileSystemEntry(entry);

            var heightMap = new HeightMap(mapFile.HeightMapData);

            var indexBufferCache = AddDisposable(new TerrainPatchIndexBufferCache(contentManager.GraphicsDevice));

            var tileDataTexture = AddDisposable(CreateTileDataTexture(
                contentManager.GraphicsDevice,
                mapFile,
                heightMap));

            var cliffDetailsBuffer = AddDisposable(CreateCliffDetails(
                contentManager.GraphicsDevice,
                mapFile));

            CreateTextures(
                contentManager,
                mapFile.BlendTileData,
                out var textureArray,
                out var textureDetails);

            var textureDetailsBuffer = AddDisposable(contentManager.GraphicsDevice.CreateStaticStructuredBuffer(textureDetails));

            var terrainMaterial = AddDisposable(new TerrainMaterial(contentManager, contentManager.EffectLibrary.Terrain));

            terrainMaterial.SetTileData(tileDataTexture);
            terrainMaterial.SetCliffDetails(cliffDetailsBuffer);
            terrainMaterial.SetTextureDetails(textureDetailsBuffer);
            terrainMaterial.SetTextureArray(textureArray);

            var terrainPatches = CreatePatches(
                contentManager.GraphicsDevice,
                heightMap,
                mapFile.BlendTileData,
                terrainMaterial,
                indexBufferCache);

            var cloudTextureName = mapFile.EnvironmentData?.CloudTexture ?? "tscloudmed.dds";
            var cloudTexture = contentManager.Load<Texture>(Path.Combine("Art", "Textures", cloudTextureName));

            var macroTextureName = mapFile.EnvironmentData?.MacroTexture ?? "tsnoiseurb.dds";
            var macroTexture = contentManager.Load<Texture>(Path.Combine("Art", "Textures", macroTextureName));

            var materialConstantsBuffer = AddDisposable(contentManager.GraphicsDevice.CreateStaticBuffer(
                new TerrainMaterial.TerrainMaterialConstants
                {
                    MapBorderWidth = new Vector2(mapFile.HeightMapData.BorderWidth, mapFile.HeightMapData.BorderWidth) * HeightMap.HorizontalScale,
                    MapSize = new Vector2(mapFile.HeightMapData.Width, mapFile.HeightMapData.Height) * HeightMap.HorizontalScale,
                    IsMacroTextureStretched = false // TODO: This must be one of the EnvironmentData unknown values.
                },
                BufferUsage.UniformBuffer));

            terrainMaterial.SetMaterialConstants(materialConstantsBuffer);

            var terrain = new Terrain.Terrain(
                heightMap,
                terrainPatches,
                cloudTexture,
                macroTexture,
                contentManager.SolidWhiteTexture);

            var players = Player.FromMapData(mapFile.SidesList.Players, contentManager).ToArray();

            var teams = (mapFile.SidesList.Teams ?? mapFile.Teams.Items)
                .Select(team => Team.FromMapData(team, players))
                .ToArray();

            LoadObjects(
                contentManager,
                heightMap,
                mapFile.ObjectsList.Objects,
                teams,
                out var waypoints,
                out var gameObjects);

            var lighting = new WorldLighting(
                mapFile.GlobalLighting.LightingConfigurations.ToLightSettingsDictionary(),
                mapFile.GlobalLighting.Time);

            var waypointPaths = new WaypointPathCollection(mapFile.WaypointsList.WaypointPaths
                .Select(path =>
                {
                    var start = waypoints[path.StartWaypointID];
                    var end = waypoints[path.EndWaypointID];
                    return new Settings.WaypointPath(start, end);
                }));

            // TODO: Don't hardcode this.
            // Perhaps add one ScriptComponent for the neutral player, 
            // and one for the active player.
            var scriptList = mapFile.GetPlayerScriptsList().ScriptLists[0];
            var mapScripts = CreateScripts(scriptList);

            var cameraController = new RtsCameraController(contentManager)
            {
                TerrainPosition = terrain.HeightMap.GetPosition(
                    terrain.HeightMap.Width / 2,
                    terrain.HeightMap.Height / 2)
            };

            contentManager.GraphicsDevice.WaitForIdle();

            return new Scene3D(
                game,
                cameraController,
                mapFile,
                terrain,
                mapScripts,
                gameObjects,
                waypoints,
                waypointPaths,
                lighting,
                players,
                teams);
        }

        private MapScriptCollection CreateScripts(ScriptList scriptList)
        {
            return new MapScriptCollection(
                CreateMapScriptGroups(scriptList.ScriptGroups),
                CreateMapScripts(scriptList.Scripts));
        }

        private static MapScriptGroup[] CreateMapScriptGroups(ScriptGroup[] scriptGroups)
        {
            var result = new MapScriptGroup[scriptGroups.Length];

            for (var i = 0; i < result.Length; i++)
            {
                result[i] = CreateMapScriptGroup(scriptGroups[i]);
            }

            return result;
        }

        private static MapScriptGroup CreateMapScriptGroup(ScriptGroup scriptGroup)
        {
            return new MapScriptGroup(
                scriptGroup.Name,
                CreateMapScripts(scriptGroup.Scripts),
                scriptGroup.IsActive,
                scriptGroup.IsSubroutine);
        }

        private static MapScript[] CreateMapScripts(Script[] scripts)
        {
            var result = new MapScript[scripts.Length];

            for (var i = 0; i < scripts.Length; i++)
            {
                result[i] = CreateMapScript(scripts[i]);
            }

            return result;
        }

        private static MapScript CreateMapScript(Script script)
        {
            var actionsIfTrue = script.ActionsIfTrue;
            var actionsIfFalse = script.ActionsIfFalse;

            return new MapScript(
                script.Name,
                script.OrConditions,
                actionsIfTrue,
                actionsIfFalse,
                script.IsActive,
                script.DeactivateUponSuccess,
                script.IsSubroutine,
                script.EvaluationInterval);
        }

        private static Waypoint CreateWaypoint(MapObject mapObject)
        {
            var waypointID = (uint) mapObject.Properties["waypointID"].Value;
            var waypointName = (string) mapObject.Properties["waypointName"].Value;

            string[] pathLabels = null;

            // It seems that if one of the label properties exists, all of them do
            if (mapObject.Properties.TryGetValue("waypointPathLabel1", out var label1))
            {
                pathLabels = new[]
                {
                    (string) label1.Value,
                    (string) mapObject.Properties["waypointPathLabel2"].Value,
                    (string) mapObject.Properties["waypointPathLabel3"].Value
                };
            }

            return new Waypoint(waypointID, waypointName, mapObject.Position, pathLabels);
        }

        private static GameObject CreateGameObject(MapObject mapObject, Team[] teams, ContentManager contentManager)
        {
            var gameObject = contentManager.InstantiateObject(mapObject.TypeName);

            // TODO: Is there any valid case where we'd want to return null instead of throwing an exception?
            if (gameObject == null)
            {
                return null;
            }

            // TODO: If the object doesn't have a health value, how do we initialise it?
            if (gameObject.Definition.Body is ActiveBodyModuleData body)
            {
                var healthMultiplier = mapObject.Properties.TryGetValue("objectInitialHealth", out var health)
                    ? (uint) health.Value / 100.0f
                    : 1.0f;

                // TODO: Should we use InitialHealth or MaximumHealth here?
                var initialHealth = body.InitialHealth * healthMultiplier;
                gameObject.Health = (decimal) initialHealth;
            }

            if (mapObject.Properties.TryGetValue("originalOwner", out var teamName))
            {
                var team = teams.First(t => t.Name == (string) teamName.Value);
                gameObject.Owner = team;
            }

            if (mapObject.Properties.TryGetValue("objectSelectable", out var selectable))
            {
                gameObject.IsSelectable = (bool) selectable.Value;
            }

            return gameObject;
        }

        private static void LoadObjects(
            ContentManager contentManager,
            HeightMap heightMap,
            MapObject[] mapObjects,
            Team[] teams,
            out WaypointCollection waypointCollection,
            out GameObjectCollection gameObjects)
        {
            var waypoints = new List<Waypoint>();
            gameObjects = new GameObjectCollection(contentManager);

            foreach (var mapObject in mapObjects)
            {
                switch (mapObject.RoadType)
                {
                    case RoadType.None:
                        var position = mapObject.Position;

                        switch (mapObject.TypeName)
                        {
                            case "*Waypoints/Waypoint":
                                waypoints.Add(CreateWaypoint(mapObject));
                                break;

                            default:
                                // TODO: Handle locomotors when they're implemented.
                                position.Z += heightMap.GetHeight(position.X, position.Y);

                                var gameObject = CreateGameObject(mapObject, teams, contentManager);

                                if (gameObject != null)
                                {
                                    gameObject.Transform.Translation = position;
                                    gameObject.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, mapObject.Angle);

                                    gameObjects.Add(gameObject);
                                }

                                break;
                        }
                        break;

                    default:
                        // TODO: Roads.
                        break;
                }

                contentManager.GraphicsDevice.WaitForIdle();
            }

            waypointCollection = new WaypointCollection(waypoints);
        }

        private List<TerrainPatch> CreatePatches(
            GraphicsDevice graphicsDevice,
            HeightMap heightMap,
            BlendTileData blendTileData,
            TerrainMaterial terrainMaterial,
            TerrainPatchIndexBufferCache indexBufferCache)
        {
            const int numTilesPerPatch = Terrain.Terrain.PatchSize - 1;

            var heightMapWidthMinusOne = heightMap.Width - 1;
            var numPatchesX = heightMapWidthMinusOne / numTilesPerPatch;
            if (heightMapWidthMinusOne % numTilesPerPatch != 0)
            {
                numPatchesX += 1;
            }

            var heightMapHeightMinusOne = heightMap.Height - 1;
            var numPatchesY = heightMapHeightMinusOne / numTilesPerPatch;
            if (heightMapHeightMinusOne % numTilesPerPatch != 0)
            {
                numPatchesY += 1;
            }

            var patches = new List<TerrainPatch>();

            for (var y = 0; y < numPatchesY; y++)
            {
                for (var x = 0; x < numPatchesX; x++)
                {
                    var patchX = x * numTilesPerPatch;
                    var patchY = y * numTilesPerPatch;

                    var patchBounds = new Rectangle(
                        patchX,
                        patchY,
                        Math.Min(Terrain.Terrain.PatchSize, heightMap.Width - patchX),
                        Math.Min(Terrain.Terrain.PatchSize, heightMap.Height - patchY));

                    patches.Add(CreatePatch(
                        terrainMaterial,
                        heightMap,
                        blendTileData,
                        patchBounds,
                        graphicsDevice,
                        indexBufferCache));
                }
            }

            return patches;
        }

        private TerrainPatch CreatePatch(
            TerrainMaterial terrainMaterial,
            HeightMap heightMap,
            BlendTileData blendTileData,
            Rectangle patchBounds,
            GraphicsDevice graphicsDevice,
            TerrainPatchIndexBufferCache indexBufferCache)
        {
            var indexBuffer = indexBufferCache.GetIndexBuffer(
                patchBounds.Width,
                patchBounds.Height,
                out var indices);

            var vertexBuffer = AddDisposable(CreateVertexBuffer(
                graphicsDevice,
                heightMap,
                patchBounds,
                indices,
                out var boundingBox,
                out var triangles));

            return new TerrainPatch(
                terrainMaterial,
                patchBounds,
                vertexBuffer,
                indexBuffer,
                (uint) indices.Length,
                triangles,
                boundingBox);
        }

        private static DeviceBuffer CreateVertexBuffer(
           GraphicsDevice graphicsDevice,
           HeightMap heightMap,
           Rectangle patchBounds,
           ushort[] indices,
           out BoundingBox boundingBox,
           out Triangle[] triangles)
        {
            var numVertices = patchBounds.Width * patchBounds.Height;

            var vertices = new TerrainVertex[numVertices];
            var points = new Vector3[numVertices];

            var vertexIndex = 0;
            for (var y = patchBounds.Y; y < patchBounds.Y + patchBounds.Height; y++)
            {
                for (var x = patchBounds.X; x < patchBounds.X + patchBounds.Width; x++)
                {
                    var position = heightMap.GetPosition(x, y);
                    points[vertexIndex] = position;
                    vertices[vertexIndex++] = new TerrainVertex
                    {
                        Position = position,
                        Normal = heightMap.Normals[x, y],
                        UV = new Vector2(x, y)
                    };
                }
            }

            boundingBox = BoundingBox.CreateFromPoints(points);

            triangles = new Triangle[(patchBounds.Width - 1) * (patchBounds.Height) * 2];

            var triangleIndex = 0;
            var indexIndex = 0;
            for (var y = 0; y < patchBounds.Height - 1; y++)
            {
                for (var x = 0; x < patchBounds.Width - 1; x++)
                {
                    // Triangle 1
                    triangles[triangleIndex++] = new Triangle
                    {
                        V0 = points[indices[indexIndex++]],
                        V1 = points[indices[indexIndex++]],
                        V2 = points[indices[indexIndex++]]
                    };

                    // Triangle 2
                    triangles[triangleIndex++] = new Triangle
                    {
                        V0 = points[indices[indexIndex++]],
                        V1 = points[indices[indexIndex++]],
                        V2 = points[indices[indexIndex++]]
                    };
                }
            }

            return graphicsDevice.CreateStaticBuffer(vertices, BufferUsage.VertexBuffer);
        }

        private static Texture CreateTileDataTexture(
            GraphicsDevice graphicsDevice,
            MapFile mapFile,
            HeightMap heightMap)
        {
            // TODO: Should be uint, once ShaderGen supports it.
            var tileData = new float[heightMap.Width * heightMap.Height * 4];

            var tileDataIndex = 0;
            for (var y = 0; y < heightMap.Height; y++)
            {
                for (var x = 0; x < heightMap.Width; x++)
                {
                    var baseTextureIndex = (byte) mapFile.BlendTileData.TextureIndices[mapFile.BlendTileData.Tiles[x, y]].TextureIndex;

                    var blendData1 = GetBlendData(mapFile, x, y, mapFile.BlendTileData.Blends[x, y], baseTextureIndex);
                    var blendData2 = GetBlendData(mapFile, x, y, mapFile.BlendTileData.ThreeWayBlends[x, y], baseTextureIndex);

                    uint packedTextureIndices = 0;
                    packedTextureIndices |= baseTextureIndex;
                    packedTextureIndices |= (uint) (blendData1.TextureIndex << 8);
                    packedTextureIndices |= (uint) (blendData2.TextureIndex << 16);

                    tileData[tileDataIndex++] = packedTextureIndices;

                    var packedBlendInfo = 0u;
                    packedBlendInfo |= blendData1.BlendDirection;
                    packedBlendInfo |= (uint) (blendData1.Flags << 8);
                    packedBlendInfo |= (uint) (blendData2.BlendDirection << 16);
                    packedBlendInfo |= (uint) (blendData2.Flags << 24);

                    tileData[tileDataIndex++] = packedBlendInfo;

                    tileData[tileDataIndex++] = mapFile.BlendTileData.CliffTextures[x, y];

                    tileData[tileDataIndex++] = 0;
                }
            }

            var textureIDsByteArray = new byte[tileData.Length * sizeof(float)];
            Buffer.BlockCopy(tileData, 0, textureIDsByteArray, 0, tileData.Length * sizeof(float));

            var rowPitch = (uint) heightMap.Width * sizeof(float) * 4;

            return graphicsDevice.CreateStaticTexture2D(
                (uint) heightMap.Width,
                (uint) heightMap.Height,
                new TextureMipMapData(
                    textureIDsByteArray,
                    rowPitch,
                    rowPitch * (uint) heightMap.Height,
                    (uint) heightMap.Width,
                    (uint) heightMap.Height),
                PixelFormat.R32_G32_B32_A32_Float);
        }

        private static BlendData GetBlendData(
            MapFile mapFile, 
            int x, int y, 
            ushort blendIndex, 
            byte baseTextureIndex)
        {
            if (blendIndex > 0)
            {
                var blendDescription = mapFile.BlendTileData.BlendDescriptions[blendIndex - 1];
                var flipped = blendDescription.Flags.HasFlag(BlendFlags.Flipped);
                var flags = (byte) (flipped ? 1 : 0);
                if (blendDescription.TwoSided)
                {
                    flags |= 2;
                }
                return new BlendData
                {
                    TextureIndex = (byte) mapFile.BlendTileData.TextureIndices[(int) blendDescription.SecondaryTextureTile].TextureIndex,
                    BlendDirection = (byte) blendDescription.BlendDirection,
                    Flags = flags
                };
            }
            else
            {
                return new BlendData
                {
                    TextureIndex = baseTextureIndex
                };
            }
        }

        private struct BlendData
        {
            public byte TextureIndex;
            public byte BlendDirection;
            public byte Flags;
        }

        private static DeviceBuffer CreateCliffDetails(
            GraphicsDevice graphicsDevice,
            MapFile mapFile)
        {
            var cliffDetails = new CliffInfo[mapFile.BlendTileData.CliffTextureMappings.Length];

            const int cliffScalingFactor = 64;
            for (var i = 0; i < cliffDetails.Length; i++)
            {
                var cliffMapping = mapFile.BlendTileData.CliffTextureMappings[i];
                cliffDetails[i] = new CliffInfo
                {
                    BottomLeftUV = cliffMapping.BottomLeftCoords * cliffScalingFactor,
                    BottomRightUV = cliffMapping.BottomRightCoords * cliffScalingFactor,
                    TopLeftUV = cliffMapping.TopLeftCoords * cliffScalingFactor,
                    TopRightUV = cliffMapping.TopRightCoords * cliffScalingFactor
                };
            }

            return cliffDetails.Length > 0
                ? graphicsDevice.CreateStaticStructuredBuffer(cliffDetails)
                : null;
        }

        private void CreateTextures(
            ContentManager contentManager,
            BlendTileData blendTileData,
            out Texture textureArray,
            out TextureInfo[] textureDetails)
        {
            var graphicsDevice = contentManager.GraphicsDevice;

            var numTextures = (uint) blendTileData.Textures.Length;

            var textureInfo = new(uint size, FileSystemEntry entry)[numTextures];
            var largestTextureSize = uint.MinValue;

            textureDetails = new TextureInfo[numTextures];

            for (var i = 0; i < numTextures; i++)
            {
                var mapTexture = blendTileData.Textures[i];

                var terrainType = contentManager.IniDataContext.TerrainTextures.First(x => x.Name == mapTexture.Name);
                var texturePath = Path.Combine("Art", "Terrain", terrainType.Texture);
                var entry = contentManager.FileSystem.GetFile(texturePath);

                var size = (uint) TgaFile.GetSquareTextureSize(entry);

                textureInfo[i] = (size, entry);

                if (size > largestTextureSize)
                {
                    largestTextureSize = size;
                }

                textureDetails[i] = new TextureInfo
                {
                    TextureIndex = (uint) i,
                    CellSize = mapTexture.CellSize * 2
                };
            }

            textureArray = AddDisposable(graphicsDevice.ResourceFactory.CreateTexture(
                TextureDescription.Texture2D(
                    largestTextureSize,
                    largestTextureSize,
                    CalculateMipMapCount(largestTextureSize, largestTextureSize),
                    numTextures,
                    PixelFormat.R8_G8_B8_A8_UNorm,
                    TextureUsage.Sampled)));

            var commandList = graphicsDevice.ResourceFactory.CreateCommandList();
            commandList.Begin();

            var texturesToDispose = new List<Texture>();

            for (var i = 0u; i < numTextures; i++)
            {
                var tgaFile = TgaFile.FromFileSystemEntry(textureInfo[i].entry);
                var originalData = TgaFile.ConvertPixelsToRgba8(tgaFile);

                using (var tgaImage = Image.LoadPixelData<Rgba32>(
                    originalData,
                    tgaFile.Header.Width,
                    tgaFile.Header.Height))
                {
                    if (tgaFile.Header.Width != largestTextureSize)
                    {
                        tgaImage.Mutate(x => x
                            .Resize((int) largestTextureSize, (int) largestTextureSize, MapTextureResampler));
                    }

                    var imageSharpTexture = new ImageSharpTexture(tgaImage);

                    var sourceTexture = CreateTextureViaStaging(
                        imageSharpTexture,
                        graphicsDevice,
                        graphicsDevice.ResourceFactory);

                    texturesToDispose.Add(sourceTexture);

                    for (var mipLevel = 0u; mipLevel < imageSharpTexture.MipLevels; mipLevel++)
                    {
                        commandList.CopyTexture(
                            sourceTexture,
                            0, 0, 0,
                            mipLevel,
                            0,
                            textureArray,
                            0, 0, 0,
                            mipLevel,
                            i,
                            (uint) imageSharpTexture.Images[mipLevel].Width,
                            (uint) imageSharpTexture.Images[mipLevel].Height,
                            1,
                            1);
                    }
                }
            }

            commandList.End();

            graphicsDevice.SubmitCommands(commandList);

            foreach (var texture in texturesToDispose)
            {
                graphicsDevice.DisposeWhenIdle(texture);
            }

            graphicsDevice.DisposeWhenIdle(commandList);

            graphicsDevice.WaitForIdle();
        }

        private unsafe Texture CreateTextureViaStaging(ImageSharpTexture texture, GraphicsDevice gd, ResourceFactory factory)
        {
            Texture staging = factory.CreateTexture(
                TextureDescription.Texture2D(texture.Width, texture.Height, texture.MipLevels, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Staging));

            CommandList cl = gd.ResourceFactory.CreateCommandList();
            cl.Begin();
            for (uint level = 0; level < texture.MipLevels; level++)
            {
                Image<Rgba32> image = texture.Images[level];
                fixed (void* pin = &image.DangerousGetPinnableReferenceToPixelBuffer())
                {
                    MappedResource map = gd.Map(staging, MapMode.Write, level);
                    uint rowWidth = (uint)(image.Width * 4);
                    if (rowWidth == map.RowPitch)
                    {
                        Unsafe.CopyBlock(map.Data.ToPointer(), pin, (uint)(image.Width * image.Height * 4));
                    }
                    else
                    {
                        for (uint y = 0; y < image.Height; y++)
                        {
                            byte* dstStart = (byte*)map.Data.ToPointer() + y * map.RowPitch;
                            byte* srcStart = (byte*)pin + y * rowWidth;
                            Unsafe.CopyBlock(dstStart, srcStart, rowWidth);
                        }
                    }
                    gd.Unmap(staging, level);
                }
            }
            cl.End();

            gd.SubmitCommands(cl);
            gd.DisposeWhenIdle(cl);

            return staging;
        }

        private static uint CalculateMipMapCount(uint width, uint height)
        {
            return 1u + (uint) Math.Floor(Math.Log(Math.Max(width, height), 2));
        }
    }
}
