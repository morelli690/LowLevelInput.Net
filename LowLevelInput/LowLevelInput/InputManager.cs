﻿using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using LowLevelInput.Converters;
using LowLevelInput.Hooks;

namespace LowLevelInput
{
    /// <summary>
    /// Provides methods to manage keyboard and mouse hooks
    /// </summary>
    /// <seealso cref="System.IDisposable" />
    public class InputManager : IDisposable
    {
        private object _lockObject;

        private LowLevelKeyboardHook _keyboardHook;
        private LowLevelMouseHook _mouseHook;

        private Dictionary<VirtualKeyCode, KeyState> _keyStates;
        private Dictionary<VirtualKeyCode, List<KeyStateChangedEventHandler>> _keyStateChangedCallbacks;

        /// <summary>
        /// Gets a value indicating whether this instance is initialized.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this instance is initialized; otherwise, <c>false</c>.
        /// </value>
        public bool IsInitialized { get; private set; }

        /// <summary>
        /// Gets or sets a value indicating whether [capture mouse move].
        /// </summary>
        /// <value><c>true</c> if [capture mouse move]; otherwise, <c>false</c>.</value>
        public bool CaptureMouseMove
        {
            get
            {
                var tmp = _mouseHook;

                if (tmp == null) throw new InvalidOperationException("The " + nameof(InputManager) + " is not initialized.");

                return tmp.CaptureMouseMove;
            }
            set
            {
                var tmp = _mouseHook;

                if (tmp == null) throw new InvalidOperationException("The " + nameof(InputManager) + " is not initialized.");

                tmp.CaptureMouseMove = value;
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether [clear injected flag].
        /// </summary>
        /// <value><c>true</c> if [clear injected flag]; otherwise, <c>false</c>.</value>
        public bool ClearInjectedFlag
        {
            get
            {
                var tmp_keyboard = _keyboardHook;
                var tmp_mouse = _mouseHook;

                if(tmp_keyboard == null || tmp_mouse == null) throw new InvalidOperationException("The " + nameof(InputManager) + " is not initialized.");

                return tmp_keyboard.ClearInjectedFlag;
            }
            set
            {
                var tmp_keyboard = _keyboardHook;
                var tmp_mouse = _mouseHook;

                if (tmp_keyboard == null || tmp_mouse == null) throw new InvalidOperationException("The " + nameof(InputManager) + " is not initialized.");

                tmp_keyboard.ClearInjectedFlag = value;
                tmp_mouse.ClearInjectedFlag = value;
            }
        }

        /// <summary>
        /// A callback for key state changed events
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="state">The state.</param>
        public delegate void KeyStateChangedEventHandler(VirtualKeyCode key, KeyState state);

        /// <summary>
        /// Occurs when a key on the keyboard changed it's state.
        /// </summary>
        public event LowLevelKeyboardHook.KeyboardEventHandler OnKeyboardEvent;
        /// <summary>
        /// Occurs when a key on the mouse changed it's state.
        /// </summary>
        public event LowLevelMouseHook.MouseEventHandler OnMouseEvent;

        /// <summary>
        /// Initializes a new instance of the <see cref="InputManager"/> class.
        /// </summary>
        public InputManager()
        {
            _lockObject = new object();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="InputManager"/> class and it's hooks.
        /// </summary>
        /// <param name="captureMouseMove">if set to <c>true</c> [capture mouse move].</param>
        public InputManager(bool captureMouseMove)
        {
            _lockObject = new object();

            Initialize(captureMouseMove, false);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="InputManager"/> class and it's hooks.
        /// </summary>
        /// <param name="captureMouseMove">if set to <c>true</c> [capture mouse move].</param>
        /// <param name="clearInjectedFlag">if set to <c>true</c> [clear injected flag].</param>
        public InputManager(bool captureMouseMove, bool clearInjectedFlag)
        {
            _lockObject = new object();

            Initialize(captureMouseMove, clearInjectedFlag);
        }

        /// <summary>
        /// Finalizes an instance of the <see cref="InputManager"/> class.
        /// </summary>
        ~InputManager()
        {
            Dispose(false);
        }

        /// <summary>
        /// Initializes this instance.
        /// </summary>
        public void Initialize()
        {
            Initialize(true, false);
        }

        /// <summary>
        /// Initializes the specified capture mouse move.
        /// </summary>
        /// <param name="captureMouseMove">if set to <c>true</c> [capture mouse move].</param>
        /// <param name="clearInjectedFlag">if set to <c>true</c> [clear injected flag].</param>
        /// <exception cref="InvalidOperationException">The " + nameof(InputManager) + " is already initialized.</exception>
        public void Initialize(bool captureMouseMove, bool clearInjectedFlag)
        {
            lock (_lockObject)
            {
                if (IsInitialized) throw new InvalidOperationException("The " + nameof(InputManager) + " is already initialized.");

                _keyStateChangedCallbacks = new Dictionary<VirtualKeyCode, List<KeyStateChangedEventHandler>>();
                _keyStates = new Dictionary<VirtualKeyCode, KeyState>();

                foreach(var pair in KeyCodeConverter.EnumerateVirtualKeyCodes())
                {
                    _keyStateChangedCallbacks.Add(pair.Key, new List<KeyStateChangedEventHandler>());
                    _keyStates.Add(pair.Key, KeyState.None);
                }

                _keyboardHook = new LowLevelKeyboardHook(clearInjectedFlag);
                _mouseHook = new LowLevelMouseHook(captureMouseMove, clearInjectedFlag);

                _keyboardHook.OnKeyboardEvent += _keyboardHook_OnKeyboardEvent;
                _mouseHook.OnMouseEvent += _mouseHook_OnMouseEvent;

                _keyboardHook.InstallHook();
                _mouseHook.InstallHook();

                IsInitialized = true;
            }
        }

        private void _mouseHook_OnMouseEvent(VirtualKeyCode key, KeyState state, int x, int y)
        {
            if (key == VirtualKeyCode.INVALID) return;

            var mouseEvents = OnMouseEvent;

            if(mouseEvents != null)
            {
                Task.Factory.StartNew(() =>
                {
                    mouseEvents.Invoke(key, state, x, y);
                });
            }

            _keyStates[key] = state == KeyState.Up && GetState(key) == KeyState.Down
                    ? KeyState.Pressed
                    : state;

            Task.Factory.StartNew(() =>
            {
                var curCallbacks = _keyStateChangedCallbacks[key];

                if (curCallbacks == null) return;
                if (curCallbacks.Count == 0) return;

                foreach (var callback in curCallbacks)
                    callback(key, state);

            });
        }

        private void _keyboardHook_OnKeyboardEvent(VirtualKeyCode key, KeyState state)
        {
            if (key == VirtualKeyCode.INVALID) return;

            var keyboardEvents = OnKeyboardEvent;

            if (keyboardEvents != null)
            {
                Task.Factory.StartNew(() =>
                {
                    keyboardEvents.Invoke(key, state);
                });
            }

            _keyStates[key] = state == KeyState.Up && GetState(key) == KeyState.Down
                    ? KeyState.Pressed
                    : state;

            Task.Factory.StartNew(() =>
            {
                var curCallbacks = _keyStateChangedCallbacks[key];

                if (curCallbacks == null) return;
                if (curCallbacks.Count == 0) return;

                foreach (var callback in curCallbacks)
                    callback(key, state);

            });
        }

        /// <summary>
        /// Terminates this instance.
        /// </summary>
        /// <exception cref="InvalidOperationException">The " + nameof(InputManager) + " needs to be initialized before it can be terminated.</exception>
        public void Terminate()
        {
            lock (_lockObject)
            {
                if (!IsInitialized) throw new InvalidOperationException("The " + nameof(InputManager) + " needs to be initialized before it can be terminated.");

                if (_keyboardHook != null)
                {
                    _keyboardHook.Dispose();
                    _keyboardHook = null;
                }

                if (_mouseHook != null)
                {
                    _mouseHook.Dispose();
                    _mouseHook = null;
                }

                OnKeyboardEvent = null;
                OnMouseEvent = null;

                _keyStateChangedCallbacks = null;
                _keyStates = null;

                IsInitialized = false;
            }
        }

        /// <summary>
        /// Gets the state of this key.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException">The " + nameof(InputManager) + " needs to be initialized before it can execute this method.</exception>
        public KeyState GetState(VirtualKeyCode key)
        {
            if (!IsInitialized) throw new InvalidOperationException("The " + nameof(InputManager) + " needs to be initialized before it can execute this method.");

            if (key == VirtualKeyCode.INVALID) return KeyState.None;

            return _keyStates[key];
        }

        /// <summary>
        /// Sets the internal state of this key.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="state">The state.</param>
        /// <exception cref="InvalidOperationException">The " + nameof(InputManager) + " needs to be initialized before it can execute this method.</exception>
        public void SetState(VirtualKeyCode key, KeyState state)
        {
            if (!IsInitialized) throw new InvalidOperationException("The " + nameof(InputManager) + " needs to be initialized before it can execute this method.");

            if (key == VirtualKeyCode.INVALID) return;

            _keyStates[key] = state;
        }

        /// <summary>
        /// Determines whether the specified key is pressed.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>
        ///   <c>true</c> if the specified key is pressed; otherwise, <c>false</c>.
        /// </returns>
        /// <exception cref="InvalidOperationException">The " + nameof(InputManager) + " needs to be initialized before it can execute this method.</exception>
        public bool IsPressed(VirtualKeyCode key)
        {
            if (!IsInitialized) throw new InvalidOperationException("The " + nameof(InputManager) + " needs to be initialized before it can execute this method.");

            if (key == VirtualKeyCode.INVALID) return false;

            return GetState(key) == KeyState.Down;
        }

        /// <summary>
        /// Determines whether the specified key was pressed.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException">The " + nameof(InputManager) + " needs to be initialized before it can execute this method.</exception>
        public bool WasPressed(VirtualKeyCode key)
        {
            if (!IsInitialized) throw new InvalidOperationException("The " + nameof(InputManager) + " needs to be initialized before it can execute this method.");

            if (key == VirtualKeyCode.INVALID) return false;

            if(GetState(key) == KeyState.Pressed)
            {
                SetState(key, KeyState.Up);

                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Registers an event (callback) for certain keys.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="handler">The handler.</param>
        /// <exception cref="InvalidOperationException">The " + nameof(InputManager) + " needs to be initialized before it can execute this method.</exception>
        /// <exception cref="ArgumentException">VirtualKeyCode.INVALID is not supported by this method. - key</exception>
        /// <exception cref="ArgumentNullException">handler</exception>
        public void RegisterEvent(VirtualKeyCode key, KeyStateChangedEventHandler handler)
        {
            if (!IsInitialized) throw new InvalidOperationException("The " + nameof(InputManager) + " needs to be initialized before it can execute this method.");

            if (key == VirtualKeyCode.INVALID) throw new ArgumentException("VirtualKeyCode.INVALID is not supported by this method.", nameof(key));
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            lock (_lockObject)
            {
                _keyStateChangedCallbacks[key].Add(handler);
            }

            return;
        }

        /// <summary>
        /// Unregisters an event (callback) for certain keys.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="handler">The handler.</param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException">The " + nameof(InputManager) + " needs to be initialized before it can execute this method.</exception>
        /// <exception cref="ArgumentException">VirtualKeyCode.INVALID is not supported by this method. - key</exception>
        /// <exception cref="ArgumentNullException">handler</exception>
        public bool UnregisterEvent(VirtualKeyCode key, KeyStateChangedEventHandler handler)
        {
            if (!IsInitialized) throw new InvalidOperationException("The " + nameof(InputManager) + " needs to be initialized before it can execute this method.");

            if (key == VirtualKeyCode.INVALID) throw new ArgumentException("VirtualKeyCode.INVALID is not supported by this method.", nameof(key));
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            lock(_lockObject)
            {
                return _keyStateChangedCallbacks[key].Remove(handler);
            }
        }

        /// <summary>
        /// Waits until a given event on a key occurs.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="state">The state. KeyState.None indicates any state</param>
        /// <param name="timeout">The timeout.</param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException">The " + nameof(InputManager) + " needs to be initialized before it can execute this method.</exception>
        /// <exception cref="ArgumentException">VirtualKeyCode.INVALID is not supported by this method. - key</exception>
        public bool WaitForEvent(VirtualKeyCode key, KeyState state = KeyState.None, int timeout = -1)
        {
            if (!IsInitialized) throw new InvalidOperationException("The " + nameof(InputManager) + " needs to be initialized before it can execute this method.");

            if (key == VirtualKeyCode.INVALID) throw new ArgumentException("VirtualKeyCode.INVALID is not supported by this method.", nameof(key));

            object threadLock = new object();

            KeyStateChangedEventHandler handler = (VirtualKeyCode curKey, KeyState curState) =>
            {
                if (curKey != key) return;

                if (curState != state && state != KeyState.None) return;

                if (Monitor.TryEnter(threadLock))
                {
                    // someone else has the lock
                    Monitor.PulseAll(threadLock);
                    Monitor.Exit(threadLock);
                }
            };

            bool result = false;

            RegisterEvent(key, handler);

            Monitor.Enter(threadLock);

            if(timeout < 0)
            {
                Monitor.Wait(threadLock);
                result = true;
            }
            else
            {
                result = Monitor.Wait(threadLock, timeout);
            }

            UnregisterEvent(key, handler);

            return result;
        }

        #region IDisposable Support
        private bool disposedValue = false;

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    
                }

                try
                {
                    Terminate();
                }
                catch
                {
                    // NotInitialized
                }
                

                disposedValue = true;
            }
        }
        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
