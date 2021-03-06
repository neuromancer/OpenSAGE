﻿using System;
using System.Collections.Generic;
using OpenSage.Input;
using OpenSage.Mathematics;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;
using Rectangle = OpenSage.Mathematics.Rectangle;

namespace OpenSage
{
    public sealed class GameWindow : DisposableBase
    {
        public event EventHandler ClientSizeChanged;

        private readonly Sdl2Window _window;

        private readonly Queue<InputMessage> _messageQueue = new Queue<InputMessage>();

        private bool _closing;
        private int _lastMouseX;
        private int _lastMouseY;

        private void RaiseClientSizeChanged()
        {
            ClientSizeChanged?.Invoke(this, EventArgs.Empty);
        }

        public GraphicsDevice GraphicsDevice { get; private set; }

        public Rectangle ClientBounds
        {
            get
            {
                var result = _window.Bounds;
                return new Rectangle(0, 0, result.Width, result.Height);
            }
        }

        public event EventHandler<InputMessageEventArgs> InputMessageReceived;

        private void RaiseInputMessageReceived(InputMessageEventArgs args)
        {
            InputMessageReceived?.Invoke(this, args);
        }

        public bool IsMouseVisible
        {
            get => _window.CursorVisible;
            set => _window.CursorVisible = value;
        }

        // TODO: Remove this once we switch to Veldrid.
        public IntPtr NativeWindowHandle => _window.Handle;

        public GameWindow(IntPtr windowsWindowHandle)
        {
            _window = new Sdl2Window(windowsWindowHandle, false);
            AfterWindowCreated(null);
        }

        public GameWindow(string title, int x, int y, int width, int height, GraphicsBackend? preferredBackend)
        {
            _window = new Sdl2Window(title, x, y, width, height, SDL_WindowFlags.OpenGL, false);
            AfterWindowCreated(preferredBackend);
        }

        private void AfterWindowCreated(GraphicsBackend? preferredBackend)
        {
            _window.KeyDown += HandleKeyDown;
            _window.KeyUp += HandleKeyUp;

            _window.MouseDown += HandleMouseDown;
            _window.MouseUp += HandleMouseUp;
            _window.MouseMove += HandleMouseMove;
            _window.MouseWheel += HandleMouseWheel;

            _window.Resized += HandleResized;

            _window.Closing += HandleClosing;

#if DEBUG
            const bool debug = true;
#else
            const bool debug = false;
#endif

            var graphicsDeviceOptions = new GraphicsDeviceOptions(debug, PixelFormat.D32_Float_S8_UInt, true)
            {
                ResourceBindingModel = ResourceBindingModel.Improved
            };

            if (preferredBackend != null)
            {
                GraphicsDevice = AddDisposable(VeldridStartup.CreateGraphicsDevice(
                    _window,
                    graphicsDeviceOptions,
                    preferredBackend.Value));
            }
            else
            {
                GraphicsDevice = AddDisposable(VeldridStartup.CreateGraphicsDevice(
                    _window,
                    graphicsDeviceOptions));
            }
        }

        private void HandleClosing()
        {
            _closing = true;
        }

        private void HandleResized()
        {
            GraphicsDevice.ResizeMainWindow(
                (uint) _window.Bounds.Width,
                (uint) _window.Bounds.Height);

            RaiseClientSizeChanged();
        }

        private void HandleKeyDown(KeyEvent evt)
        {
            var message = InputMessage.CreateKeyDown(evt.Key);
            _messageQueue.Enqueue(message);
        }

        private void HandleKeyUp(KeyEvent evt)
        {
            var message = InputMessage.CreateKeyUp(evt.Key);
            _messageQueue.Enqueue(message);
        }

        private void HandleMouseDown(MouseEvent evt)
        {
            InputMessageType? getMessageType()
            {
                switch (evt.MouseButton)
                {
                    case MouseButton.Left:
                        return InputMessageType.MouseLeftButtonDown;
                    case MouseButton.Middle:
                        return InputMessageType.MouseMiddleButtonDown;
                    case MouseButton.Right:
                        return InputMessageType.MouseRightButtonDown;
                    default:
                        return null;
                }
            }

            var messageType = getMessageType();
            if (messageType == null)
            {
                return;
            }

            var message = InputMessage.CreateMouseButton(messageType.Value, new Point2D(_lastMouseX, _lastMouseY));
            _messageQueue.Enqueue(message);
        }

        private void HandleMouseUp(MouseEvent evt)
        {
            InputMessageType? getMessageType()
            {
                switch (evt.MouseButton)
                {
                    case MouseButton.Left:
                        return InputMessageType.MouseLeftButtonUp;
                    case MouseButton.Middle:
                        return InputMessageType.MouseMiddleButtonUp;
                    case MouseButton.Right:
                        return InputMessageType.MouseRightButtonUp;
                    default:
                        return null;
                }
            }

            var messageType = getMessageType();
            if (messageType == null)
            {
                return;
            }

            var message = InputMessage.CreateMouseButton(messageType.Value, new Point2D(_lastMouseX, _lastMouseY));
            _messageQueue.Enqueue(message);
        }

        private void HandleMouseMove(MouseMoveEventArgs args)
        {
            _lastMouseX = args.State.X;
            _lastMouseY = args.State.Y;

            var message = InputMessage.CreateMouseMove(new Point2D(args.State.X, args.State.Y));
            _messageQueue.Enqueue(message);
        }

        private void HandleMouseWheel(MouseWheelEventArgs args)
        {
            var message = InputMessage.CreateMouseWheel((int) (args.WheelDelta * 100));
            _messageQueue.Enqueue(message);
        }

        public void SetCursor(Cursor cursor)
        {
            // TODO
        }

        public bool PumpEvents()
        {
            _window.PumpEvents();

            if (_closing)
            {
                return false;
            }

            while (_messageQueue.Count > 0)
            {
                RaiseInputMessageReceived(new InputMessageEventArgs(_messageQueue.Dequeue()));
            }

            return true;
        }

        protected override void Dispose(bool disposeManagedResources)
        {
            // TODO: This isn't right.
            _window.Close();

            GraphicsDevice.WaitForIdle();

            base.Dispose(disposeManagedResources);
        }
    }
}
