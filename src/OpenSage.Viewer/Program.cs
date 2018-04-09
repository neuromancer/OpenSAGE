﻿using OpenSage.Viewer.UI;
using Veldrid;
using Veldrid.Sdl2;

namespace OpenSage.Viewer
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            Platform.Start();

            const int initialWidth = 1024;
            const int initialHeight = 768;

            using (var window = new GameWindow("OpenSAGE Viewer", 100, 100, initialWidth, initialHeight, SDL_WindowFlags.Resizable))
            using (var commandList = window.GraphicsDevice.ResourceFactory.CreateCommandList())
            using (var imGuiRenderer = new ImGuiRenderer(window.GraphicsDevice, window.GraphicsDevice.MainSwapchain.Framebuffer.OutputDescription, initialWidth, initialHeight))
            using (var gameTimer = new GameTimer())
            {
                window.ClientSizeChanged += (sender, e) =>
                {
                    imGuiRenderer.WindowResized(window.ClientBounds.Width, window.ClientBounds.Height);
                };

                gameTimer.Start();

                using (var mainForm = new MainForm(window, imGuiRenderer))
                {
                    while (true)
                    {
                        commandList.Begin();

                        gameTimer.Update();

                        if (!window.PumpEvents())
                        {
                            break;
                        }

                        commandList.SetFramebuffer(window.GraphicsDevice.MainSwapchain.Framebuffer);

                        commandList.ClearColorTarget(0, RgbaFloat.Clear);

                        imGuiRenderer.Update(
                            (float) gameTimer.CurrentGameTime.ElapsedGameTime.TotalSeconds,
                            window.CurrentInputSnapshot);

                        mainForm.Draw();

                        imGuiRenderer.Render(window.GraphicsDevice, commandList);

                        commandList.End();

                        window.GraphicsDevice.SubmitCommands(commandList);

                        window.GraphicsDevice.SwapBuffers();
                    }
                }
            }

            Platform.Stop();
        }
    }
}