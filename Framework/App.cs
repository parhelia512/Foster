using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

using static Foster.Framework.SDL3;
using static Foster.Framework.SDL3.Hints;

namespace Foster.Framework;

public static class App
{
	private static nint window;
	private static readonly List<Module> modules = [];
	private static readonly List<Func<Module>> registrations = [];
	private static readonly Stopwatch timer = new();
	private static bool started = false;
	private static TimeSpan lastTime;
	private static TimeSpan accumulator;
	private static string title = string.Empty;
	private static readonly Exception notRunningException = new("Foster is not Running");
	private static readonly List<(uint ID, nint Ptr)> openJoysticks = [];
	private static readonly List<(uint ID, nint Ptr)> openGamepads = [];
	
	/// <summary>
	/// Foster Version Number
	/// </summary>
	public static readonly Version Version = typeof(App).Assembly.GetName().Version!;

	/// <summary>
	/// If the Application is currently running
	/// </summary>
	public static bool Running { get; private set; } = false;

	/// <summary>
	/// If the Application is exiting. Call <see cref="Exit"/> to exit the Application.
	/// </summary>
	public static bool Exiting { get; private set; } = false;

	/// <summary>
	/// Gets the Stopwatch used to evaluate Application time.
	/// Note: Modifying this can break your update loop!
	/// </summary>
	public static Stopwatch Timer => timer;

	/// <summary>
	/// The Window Title
	/// </summary>
	public static string Title
	{
		get => title;
		set
		{
			if (!Running)
				throw notRunningException;
			
			if (title != value)
			{
				title = value;
				SDL_SetWindowTitle(window, value);
			}
		}
	}

	/// <summary>
	/// The Application Name, assigned on Run
	/// </summary>
	public static string Name { get; private set; } = string.Empty;

	/// <summary>
	/// Gets the path to the User Directory, which is the location where you should
	/// store application data like settings or save data.
	/// The location of this directory is platform and application dependent.
	/// For example on Windows this is usually in C:/User/{user}/AppData/Roaming/{App.Name}, 
	/// where as on Linux it's usually in ~/.local/share/{App.Name}
	/// </summary>
	public static string UserPath { get; private set; } = string.Empty;

	/// <summary>
	/// Returns whether the Application Window is currently Focused or not.
	/// </summary>
	public static bool Focused
	{
		get
		{
			if (!Running)
				throw notRunningException;
			var flags = SDL_WindowFlags.INPUT_FOCUS | SDL_WindowFlags.MOUSE_FOCUS;
			return (SDL_GetWindowFlags(window) & flags) != 0;
		}
	}

	/// <summary>
	/// The Window width, which isn't necessarily the size in Pixels depending on the Platform.
	/// Use WidthInPixels to get the drawable size.
	/// </summary>
	public static int Width
	{
		get => Size.X;
		set => Size = new(value, Height);
	}

	/// <summary>
	/// The Window height, which isn't necessarily the size in Pixels depending on the Platform.
	/// Use HeightInPixels to get the drawable size.
	/// </summary>
	public static int Height
	{
		get => Size.Y;
		set => Size = new(Width, value);
	}

	/// <summary>
	/// The Window size, which isn't necessarily the size in Pixels depending on the Platform.
	/// Use SizeInPixels to get the drawable size.
	/// </summary>
	public static Point2 Size
	{
		get
		{
			if (!Running)
				throw notRunningException;
			SDL_GetWindowSize(window, out int w, out int h);
			return new(w, h);
		}
		set
		{
			if (!Running)
				throw notRunningException;
			SDL_SetWindowSize(window, value.X, value.Y);
		}
	}

	/// <summary>
	/// The Width of the Window in Pixels
	/// </summary>
	public static int WidthInPixels => SizeInPixels.X;

	/// <summary>
	/// The Height of the Window in Pixels
	/// </summary>
	public static int HeightInPixels => SizeInPixels.Y;

	/// <summary>
	/// The Size of the Window in Pixels
	/// </summary>
	public static Point2 SizeInPixels
	{
		get
		{
			if (!Running)
				throw notRunningException;
			SDL_GetWindowSizeInPixels(window, out int w, out int h);
			return new(w, h);
		}
	}

	/// <summary>
	/// Gets the Size of the Display that the Application Window is currently in.
	/// </summary>
	public static unsafe Point2 DisplaySize
	{
		get
		{
			if (!Running)
				throw notRunningException;
			var index = SDL_GetDisplayForWindow(window);
			var mode = SDL_GetCurrentDisplayMode(index);
			if (mode == null)
				return Point2.Zero;
			return new(mode->w, mode->h);
		}
	}

	/// <summary>
	/// Gets the Content Scale for the Application Window.
	/// </summary>
	public static Vector2 ContentScale
	{
		get
		{
			var index = SDL_GetDisplayForWindow(window);
			var scale = SDL_GetDisplayContentScale(index);
			
			if (scale <= 0)
			{
				Log.Warning($"SDL_GetDisplayForWindow failed: {Platform.GetErrorFromSDL()}");
				return new(WidthInPixels / Width, HeightInPixels / Height);
			}

			return Vector2.One * scale;
		}
	}

	/// <summary>
	/// Whether the Window is Fullscreen or not
	/// </summary>
	public static bool Fullscreen
	{
		get
		{
			if (!Running)
				throw notRunningException;
			return (SDL_GetWindowFlags(window) & SDL_WindowFlags.FULLSCREEN) != 0;
		}
		set
		{
			if (!Running)
				throw notRunningException;
			SDL_SetWindowFullscreen(window, value);
		}
	}

	/// <summary>
	/// Whether the Window is Resizable by the User
	/// </summary>
	public static bool Resizable
	{
		get
		{
			if (!Running)
				throw notRunningException;
			return (SDL_GetWindowFlags(window) & SDL_WindowFlags.RESIZABLE) != 0;
		}
		set
		{
			if (!Running)
				throw notRunningException;
			SDL_SetWindowResizable(window, value);
		}
	}

	/// <summary>
	/// If Vertical Synchronization is enabled
	/// </summary>
	[Obsolete("Use Graphics.VSync instead")]
	public static bool VSync
	{
		get => Graphics.VSync;
		set => Graphics.VSync = value;
	}

	/// <summary>
	/// If the Mouse is Hidden when over the Window
	/// </summary>
	public static bool MouseVisible
	{
		get
		{
			if (!Running)
				throw notRunningException;
			return SDL_CursorVisible();
		}
		set
		{
			if (!Running)
				throw notRunningException;
			if (value)
				SDL_ShowCursor();
			else
				SDL_HideCursor();
		}
	}

	/// <summary>
	/// What action to perform when the user requests for the Application to exit.
	/// If not assigned, the default behavior is to call <see cref="Exit"/>.
	/// </summary>
	public static Action? OnExitRequested;

	/// <summary>
	/// Called only in DEBUG builds when a hot reload occurs.
	/// Note that this may be called off-thread, depending on when the Hot Reload occurs.
	/// </summary>
	public static Action? OnHotReload;

	/// <summary>
	/// The Main Thread that the Application was Run on
	/// </summary>
	public static int MainThreadID { get; private set; }

	/// <summary>
	/// Registers a Module that will be run within the Application once it has started.
	/// If the Application is already running, the Module's Startup method will immediately be invoked.
	/// </summary>
	public static void Register<T>() where T : Module, new()
	{
		if (Exiting)
			throw new Exception("Cannot register new Modules while the Application is shutting down");

		if (!started)
		{
			registrations.Add(() => new T());
		}
		else
		{
			var it = new T();
			it.Startup();
			modules.Add(it);
		}
	}

	/// <summary>
	/// Runs the Application with the given Module automatically registered.
	/// Functionally the same as calling <see cref="Register{T}"/> followed by <see cref="Run(string, int, int, bool)"/>
	/// </summary>
	public static void Run<T>(string applicationName, int width, int height, bool fullscreen = false) where T : Module, new()
	{
		Register<T>();
		Run(applicationName, width, height, fullscreen);
	}

	/// <summary>
	/// Runs the Application
	/// </summary>
	public static unsafe void Run(string applicationName, int width, int height, bool fullscreen = false)
	{
		Debug.Assert(!Running, "Application is already running");
		Debug.Assert(!Exiting, "Application is still exiting");
		Debug.Assert(width > 0 && height > 0, "Width or height is <= 0");

		// log info
		{
			var sdlv = SDL_GetVersion();
			Log.Info($"Foster: v{Version.Major}.{Version.Minor}.{Version.Build}");
			Log.Info($"SDL: v{sdlv / 1000000}.{((sdlv) / 1000) % 1000}.{(sdlv) % 1000}");
			Log.Info($"Platform: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
			Log.Info($"Framework: {RuntimeInformation.FrameworkDescription}");
		}

		MainThreadID = Thread.CurrentThread.ManagedThreadId;

		// set SDL logging method
		SDL_SetLogOutputFunction(&Platform.HandleLogFromSDL, IntPtr.Zero);

		// by default allow controller presses while unfocused, 
		// let game decide if it should handle them
		SDL_SetHint(SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS, "1");

		// initialize SDL3
		{
			var initFlags = 
				SDL_InitFlags.VIDEO | SDL_InitFlags.TIMER | SDL_InitFlags.EVENTS |
				SDL_InitFlags.JOYSTICK | SDL_InitFlags.GAMEPAD;

			if (!SDL_Init(initFlags))
				throw Platform.CreateExceptionFromSDL(nameof(SDL_Init));

			// get the UserPath
			var name = Platform.ToUTF8(applicationName);
			UserPath = Platform.ParseUTF8(SDL_GetPrefPath(IntPtr.Zero, name));
			Platform.FreeUTF8(name);
		}

		// create the graphics device
		Renderer.CreateDevice();

		// create the window
		{
			var windowFlags = 
				SDL_WindowFlags.HIGH_PIXEL_DENSITY | SDL_WindowFlags.RESIZABLE | 
				SDL_WindowFlags.HIDDEN;

			if (fullscreen)
				windowFlags |= SDL_WindowFlags.FULLSCREEN;

			window = SDL_CreateWindow(applicationName, width, height, windowFlags);
			if (window == IntPtr.Zero)
				throw Platform.CreateExceptionFromSDL(nameof(SDL_CreateWindow));
		}

		Renderer.Startup(window);

		// toggle flags and show window
		SDL_StartTextInput(window);
		SDL_SetWindowFullscreenMode(window, null);
		SDL_SetWindowBordered(window, true);
		SDL_ShowCursor();

		// load default input mappings if they exist
		Input.AddDefaultSDLGamepadMappings(AppContext.BaseDirectory);

		// Clear Time
		Running = true;
		Time.Frame = 0;
		Time.Duration = new();
		lastTime = TimeSpan.Zero;
		accumulator = TimeSpan.Zero;
		timer.Restart();

		// poll events once, so input has controller state before Startup
		PollEvents();
		Input.Step();

		// register & startup all modules in order
		// this is in a loop in case a module registers more modules
		// from within its own constructor/startup call.
		while (registrations.Count > 0)
		{
			int from = modules.Count;

			// create all registered modules
			for (int i = 0; i < registrations.Count; i ++)
				modules.Add(registrations[i].Invoke());
			registrations.Clear();

			// notify all modules we're now running
			for (int i = from; i < modules.Count; i ++)
				modules[i].Startup();
		}
		
		// Display Window now that we're ready
		SDL_ShowWindow(window);

		// begin normal game loop
		started = true;
		while (!Exiting)
			Tick();

		// shutdown
		for (int i = modules.Count - 1; i >= 0; i --)
			modules[i].Shutdown();
		modules.Clear();
		Running = false;

		Renderer.Shutdown();
		SDL_StopTextInput(window);
		SDL_DestroyWindow(window);
		Renderer.DestroyDevice();
		SDL_Quit();

		window = IntPtr.Zero;
		started = false;
		Exiting = false;
	}

	/// <summary>
	/// Notifies the Application to Exit.
	/// The Application may finish the current frame before exiting.
	/// </summary>
	public static void Exit()
	{
		if (Running)
			Exiting = true;
	}

	private static void Tick()
	{
		static void Update(TimeSpan delta)
		{
			Time.Frame++;
			Time.Advance(delta);
			
			Input.Step();
			PollEvents();
			FramePool.NextFrame();

			for (int i = 0; i < modules.Count; i ++)
				modules[i].Update();
		}

		var currentTime = timer.Elapsed;
		var deltaTime = currentTime - lastTime;
		lastTime = currentTime;

		if (Time.FixedStep)
		{
			accumulator += deltaTime;

			// Do not let us run too fast
			while (accumulator < Time.FixedStepTarget)
			{
				int milliseconds = (int)(Time.FixedStepTarget - accumulator).TotalMilliseconds;
				Thread.Sleep(milliseconds);

				currentTime = timer.Elapsed;
				deltaTime = currentTime - lastTime;
				lastTime = currentTime;
				accumulator += deltaTime;
			}

			// Do not allow any update to take longer than our maximum.
			if (accumulator > Time.FixedStepMaxElapsedTime)
			{
				Time.Advance(accumulator - Time.FixedStepMaxElapsedTime);
				accumulator = Time.FixedStepMaxElapsedTime;
			}

			// Do as many fixed updates as we can
			while (accumulator >= Time.FixedStepTarget)
			{
				accumulator -= Time.FixedStepTarget;
				Update(Time.FixedStepTarget);
				if (Exiting)
					break;
			}
		}
		else
		{
			Update(deltaTime);
		}

		for (int i = 0; i < modules.Count; i ++)
			modules[i].Render();
		
		Renderer.Present();
	}

	private static unsafe void PollEvents()
	{
		// always perform a mouse-move event
		{
			SDL_GetMouseState(out float mouseX, out float mouseY);
			SDL_GetRelativeMouseState(out float deltaX, out float deltaY);
			Input.OnMouseMove(new Vector2(mouseX, mouseY), new Vector2(deltaX, deltaY));
		}

		SDL_Event ev = default;
		while (SDL_PollEvent(&ev) && ev.type != SDL_EventType.POLL_SENTINEL)
		{
			switch (ev.type)
			{
			case SDL_EventType.QUIT:
				if (started)
				{
					if (OnExitRequested != null)
						OnExitRequested();
					else
						Exit();
				}
				break;

			// mouse
			case SDL_EventType.MOUSE_BUTTON_DOWN:
				Input.OnMouseButton((int)Platform.GetMouseFromSDL(ev.button.button), true);
				break;
			case SDL_EventType.MOUSE_BUTTON_UP:
				Input.OnMouseButton((int)Platform.GetMouseFromSDL(ev.button.button), false);
				break;
			case SDL_EventType.MOUSE_WHEEL:
				Input.OnMouseWheel(new(ev.wheel.x, ev.wheel.y));
				break;

			// keyboard
			case SDL_EventType.KEY_DOWN:
				if (ev.key.repeat == 0)
					Input.OnKey((int)Platform.GetKeyFromSDL(ev.key.scancode), true);
				break;
			case SDL_EventType.KEY_UP:
				if (ev.key.repeat == 0)
					Input.OnKey((int)Platform.GetKeyFromSDL(ev.key.scancode), false);
				break;

			case SDL_EventType.TEXT_INPUT:
				Input.OnText(ev.text.text);
				break;

			// joystick
			case SDL_EventType.JOYSTICK_ADDED:
				{
					var id = ev.jdevice.which;
					if (SDL_IsGamepad(id))
						break;

					var ptr = SDL_OpenJoystick(id);
					openJoysticks.Add((id, ptr));

					Input.OnControllerConnect(
						id: new(id),
						name: Platform.ParseUTF8(SDL_GetJoystickName(ptr)),
						buttonCount: SDL_GetJoystickButtons(ptr),
						axisCount: SDL_GetJoystickAxes(ptr),
						isGamepad: false,
						type: GamepadTypes.Unknown,
						vendor: SDL_GetJoystickVendor(ptr),
						product: SDL_GetJoystickProduct(ptr),
						version: SDL_GetJoystickProductVersion(ptr)
					);
					break;
				}
			case SDL_EventType.JOYSTICK_REMOVED:
				{
					var id = ev.jdevice.which;
					if (SDL_IsGamepad(id))
						break;

					for (int i = 0; i < openJoysticks.Count; i ++)
						if (openJoysticks[i].ID == id)
						{
							SDL_CloseJoystick(openJoysticks[i].Ptr);
							openJoysticks.RemoveAt(i);
						}

					Input.OnControllerDisconnect(new(id));
					break;
				}
			case SDL_EventType.JOYSTICK_BUTTON_DOWN:
			case SDL_EventType.JOYSTICK_BUTTON_UP:
				{
					var id = ev.jbutton.which;
					if (SDL_IsGamepad(id))
						break;

					Input.OnControllerButton(
						id: new(id),
						button: ev.jbutton.button,
						pressed: ev.type == SDL_EventType.JOYSTICK_BUTTON_DOWN);

					break;
				}
			case SDL_EventType.JOYSTICK_AXIS_MOTION:
				{
					var id = ev.jaxis.which;
					if (SDL_IsGamepad(id))
						break;

					float value = ev.jaxis.value >= 0
						? ev.jaxis.value / 32767.0f
						: ev.jaxis.value / 32768.0f;

					Input.OnControllerAxis(
						id: new(id),
						axis: ev.jaxis.axis,
						value: value);

					break;
				}


			// gamepad
			case SDL_EventType.GAMEPAD_ADDED:
				{
					var id = ev.gdevice.which;
					var ptr = SDL_OpenGamepad(id);
					openGamepads.Add((id, ptr));

					Input.OnControllerConnect(
						id: new(id),
						name: Platform.ParseUTF8(SDL_GetGamepadName(ptr)),
						buttonCount: 15,
						axisCount: 6,
						isGamepad: true,
						type: (GamepadTypes)SDL_GetGamepadType(ptr),
						vendor: SDL_GetGamepadVendor(ptr),
						product: SDL_GetGamepadProduct(ptr),
						version: SDL_GetGamepadProductVersion(ptr)
					);
					break;
				}
			case SDL_EventType.GAMEPAD_REMOVED:
				{
					var id = ev.gdevice.which;
					for (int i = 0; i < openGamepads.Count; i ++)
						if (openGamepads[i].ID == id)
						{
							SDL_CloseGamepad(openGamepads[i].Ptr);
							openGamepads.RemoveAt(i);
						}

					Input.OnControllerDisconnect(new(id));
					break;
				}
			case SDL_EventType.GAMEPAD_BUTTON_DOWN:
			case SDL_EventType.GAMEPAD_BUTTON_UP:
				{
					var id = ev.gbutton.which;
					Input.OnControllerButton(
						id: new(id),
						button: (int)Platform.GetButtonFromSDL((SDL_GamepadButton)ev.gbutton.button),
						pressed: ev.type == SDL_EventType.GAMEPAD_BUTTON_DOWN);

					break;
				}
			case SDL_EventType.GAMEPAD_AXIS_MOTION:
				{
					var id = ev.gbutton.which;
					float value = ev.gaxis.value >= 0
						? ev.gaxis.value / 32767.0f
						: ev.gaxis.value / 32768.0f;

					Input.OnControllerAxis(
						id: new(id),
						axis: (int)Platform.GetAxisFromSDL((SDK_GamepadAxis)ev.gaxis.axis),
						value: value);
						
					break;
				}

			default:
				break;
			}
		}
	}
}
